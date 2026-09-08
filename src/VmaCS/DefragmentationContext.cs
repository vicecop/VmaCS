namespace VmaCS;

/// <summary>
/// Manages defragmentation passes.
/// </summary>
public sealed class DefragmentationContext
{
    internal const int MAX_ALLOCS_TO_IGNORE = 16;

    internal enum CounterStatus
    {
        Pass,
        Ignore,
        End
    }

    private readonly VulkanMemoryAllocator _allocator;
    private readonly uint _algorithm;
    private readonly ulong _maxPassBytes;
    private readonly uint _maxPassAllocations;
    private readonly HashSet<Allocation>? _allocSet;

    private readonly List<BlockList> _vectors = [];
    private readonly bool _isPool;

    private readonly List<DefragmentationMove> _moves = [];
    private DefragmentationStats _passStats;
    private DefragmentationStats _globalStats;
    private int _ignoredAllocs;

    private sealed class BalancedState
    {
        public long AvgFreeSize;
        public long AvgAllocSize = long.MaxValue;
    }

    private sealed class ExtensiveState
    {
        public int Operation; // 0 FindFreeBlock, 4 Cleanup, 5 Done
        public long FirstFreeBlock = long.MaxValue;
    }

    private readonly BalancedState[]? _balanced;
    private readonly ExtensiveState[]? _extensive;

    private bool _ended;

    internal DefragmentationContext(VulkanMemoryAllocator allocator, in DefragmentationInfo info)
    {
        _allocator = allocator;

        var flags = (uint)(info.Flags & DefragmentationFlags.AlgorithmMask);
        _algorithm = flags == 0 ? (uint)DefragmentationFlags.AlgorithmBalanced : flags;

        _maxPassBytes = info.MaxCpuBytesToMove != 0 ? info.MaxCpuBytesToMove
            : info.MaxGpuBytesToMove != 0 ? info.MaxGpuBytesToMove : ulong.MaxValue;
        _maxPassAllocations = info.MaxAllocationsPerPass != 0 ? info.MaxAllocationsPerPass : uint.MaxValue;

        if (info.Allocations != null)
        {
            _allocSet = [.. info.Allocations];

            var seen = new HashSet<BlockList>();

            foreach (var a in info.Allocations)
            {
                if (a is BlockAllocation ba)
                {
                    var list = ba.Block.ParentPool?.BlockList ?? _allocator.BlockLists[a.MemoryTypeIndex];

                    if (seen.Add(list))
                    {
                        _vectors.Add(list);
                    }
                }
            }
        }
        else if (info.Pool != null)
        {
            _vectors.Add(info.Pool.BlockList);
            _isPool = true;
        }
        else if (info.MemoryTypes != null)
        {
            foreach (var t in info.MemoryTypes)
            {
                _vectors.Add(_allocator.BlockLists[t]);
            }
        }
        else
        {
            for (var i = 0; i < _allocator.MemoryTypeCount; i++)
            {
                _vectors.Add(_allocator.BlockLists[i]);
            }
        }

        switch (_algorithm)
        {
            case (uint)DefragmentationFlags.AlgorithmBalanced:
                _balanced = new BalancedState[_vectors.Count];
                for (var i = 0; i < _vectors.Count; i++)
                {
                    _balanced[i] = new BalancedState();
                }
                break;
            case (uint)DefragmentationFlags.AlgorithmExtensive:
                _extensive = new ExtensiveState[_vectors.Count];
                for (var i = 0; i < _vectors.Count; i++)
                {
                    _extensive[i] = new ExtensiveState();
                }
                break;
        }
    }

    internal HashSet<Allocation>? AllocSet => _allocSet;

    internal CounterStatus CheckCounters(long bytes)
    {
        if (_passStats.BytesMoved + (ulong)bytes > _maxPassBytes)
        {
            if (++_ignoredAllocs < MAX_ALLOCS_TO_IGNORE)
            {
                return CounterStatus.Ignore;
            }

            return CounterStatus.End;
        }

        _ignoredAllocs = 0;
        return CounterStatus.Pass;
    }

    internal bool IncrementCounters(long bytes)
    {
        _passStats.BytesMoved += (ulong)bytes;
        _passStats.AllocationsMoved += 1;

        return _passStats.AllocationsMoved >= _maxPassAllocations || _passStats.BytesMoved >= _maxPassBytes;
    }

    internal void AddMove(DefragmentationMove move)
    {
        // GPU defragmentation copies the buffer into itself at a new offset, so the temporary
        // destination allocation belongs to the same VkBuffer as the source.
        if (move.Operation == DefragmentationMoveOperation.Copy && move.Source is { } src)
        {
            move.Destination!.Buffer = src.Buffer;
        }

        _moves.Add(move);
    }

    /// <summary>
    /// Starts a single defragmentation pass.
    /// </summary>
    /// <returns>Information about the moves to be performed in this pass.</returns>
    public DefragmentationPassMoveInfo PassBegin()
    {
        if (_ended)
        {
            throw new InvalidOperationException("Defragmentation context has already ended.");
        }

        _moves.Clear();
        _passStats = default;
        _ignoredAllocs = 0;
        if (_isPool)
        {
            _vectors[0].DefragmentPass(this, 0, out _);
        }
        else
        {
            for (var i = 0; i < _vectors.Count; i++)
            {
                _vectors[i].DefragmentPass(this, i, out var vm);

                if (vm)
                {
                    break;
                }
            }
        }

        return new DefragmentationPassMoveInfo { Moves = _moves.ToArray() };
    }

    /// <summary>
    /// Ends a single defragmentation pass and applies the moves.
    /// </summary>
    /// <returns>True if any moves were applied; false otherwise.</returns>
    public bool PassEnd()
    {
        if (_ended)
        {
            throw new InvalidOperationException("Defragmentation context has already ended.");
        }

        var applied = 0;

        foreach (var move in _moves)
        {
            var src = move.Source!;
            var dst = move.Destination!;
            var list = src.Block.ParentPool?.BlockList ?? _allocator.BlockLists[src.MemoryTypeIndex];

            switch (move.Operation)
            {
                case DefragmentationMoveOperation.Copy:
                    {
                        var beforeCount = list.BlockCount;
                        BlockList.SwapAllocations(src, dst);

                        // GPU defragmentation: the data was copied by the caller between PassBegin
                        // and PassEnd (old buffer -> new buffer bound to the destination). Nothing to
                        // rebind here; the new buffer already references the destination memory.
                        var freedBlockSize = (ulong)dst.Block.MetaData.Size;
                        list.Free(dst);

                        if (list.BlockCount < beforeCount)
                        {
                            _passStats.BytesFreed += freedBlockSize;
                            _passStats.DeviceMemoryBlocksFreed += 1;
                        }

                        applied++;
                        break;
                    }
                case DefragmentationMoveOperation.Ignore:
                    {
                        _passStats.BytesMoved -= (ulong)src.Size;
                        _passStats.AllocationsMoved -= 1;
                        list.Free(dst);
                        break;
                    }
                case DefragmentationMoveOperation.Destroy:
                    {
                        _passStats.BytesMoved -= (ulong)src.Size;
                        _passStats.AllocationsMoved -= 1;

                        var bc = list.BlockCount;
                        var fbs = (ulong)src.Block.MetaData.Size;
                        list.Free(src);
                        if (list.BlockCount < bc)
                        {
                            _passStats.BytesFreed += fbs;
                            _passStats.DeviceMemoryBlocksFreed += 1;
                        }

                        var bc2 = list.BlockCount;
                        var fbs2 = (ulong)dst.Block.MetaData.Size;
                        list.Free(dst);
                        if (list.BlockCount < bc2)
                        {
                            _passStats.BytesFreed += fbs2;
                            _passStats.DeviceMemoryBlocksFreed += 1;
                        }

                        applied++;
                        break;
                    }
            }
        }

        _globalStats.BytesMoved += _passStats.BytesMoved;
        _globalStats.AllocationsMoved += _passStats.AllocationsMoved;
        _globalStats.BytesFreed += _passStats.BytesFreed;
        _globalStats.DeviceMemoryBlocksFreed += _passStats.DeviceMemoryBlocksFreed;

        _passStats = default;
        _moves.Clear();

        return applied > 0;
    }

    /// <summary>
    /// Ends the defragmentation process and returns final statistics.
    /// </summary>
    /// <returns>Final defragmentation statistics.</returns>
    public DefragmentationStats End()
    {
        _ended = true;
        return _globalStats;
    }

    internal bool ComputeDefragmentation(BlockList list, int index) => _algorithm switch
    {
        (uint)DefragmentationFlags.AlgorithmFast => ComputeFast(list),
        (uint)DefragmentationFlags.AlgorithmBalanced => ComputeBalanced(list, index, true),
        (uint)DefragmentationFlags.AlgorithmFull => ComputeFull(list),
        (uint)DefragmentationFlags.AlgorithmExtensive => ComputeExtensive(list, index),
        _ => ComputeBalanced(list, index, true),
    };

    internal bool ReallocWithinBlock(BlockList list, VulkanMemoryBlock block) => list.ReallocWithinBlock(block, this);

    private static BlockList.MoveData GetMoveData(BlockAllocation src) => new()
    {
        Size = src.Size,
        Alignment = src.Alignment,
        Type = src.SuballocationType,
        Strategy = AllocationStrategyFlags.BestFit,
        Source = src
    };

    private bool ComputeFast(BlockList list)
    {
        for (var i = list.BlockCount - 1; i >= 0; i--)
        {
            var block = list[i];
            var metadata = block.MetaData;

            foreach (BlockAllocation src in metadata.GetAllocations())
            {
                if (ReferenceEquals(src.UserData, this))
                {
                    continue;
                }

                if (_allocSet != null && !_allocSet.Contains(src))
                {
                    continue;
                }

                switch (CheckCounters(src.Size))
                {
                    case CounterStatus.Ignore:
                        continue;
                    case CounterStatus.End:
                        return true;
                }

                var data = GetMoveData(src);

                if (list.AllocInOtherBlock(0, i, ref data, this))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool ComputeBalanced(BlockList list, int index, bool update)
    {
        var state = _balanced![index];

        if (update && state.AvgAllocSize == long.MaxValue)
        {
            UpdateVectorStatistics(list, state);
        }

        var startMoveCount = _moves.Count;
        var minimalFreeRegion = state.AvgFreeSize / 2;

        for (var i = list.BlockCount - 1; i >= 0; i--)
        {
            var block = list[i];
            var metadata = block.MetaData;
            long prevFreeRegionSize = 0;

            foreach (BlockAllocation src in metadata.GetAllocations())
            {
                if (ReferenceEquals(src.UserData, this))
                {
                    continue;
                }

                if (_allocSet != null && !_allocSet.Contains(src))
                {
                    continue;
                }

                switch (CheckCounters(src.Size))
                {
                    case CounterStatus.Ignore:
                        continue;
                    case CounterStatus.End:
                        return true;
                }

                var data = GetMoveData(src);
                var prevMoveCount = _moves.Count;

                if (list.AllocInOtherBlock(0, i, ref data, this))
                {
                    return true;
                }

                var nextFreeRegionSize = metadata.GetNextFreeRegionSize(src);

                if (prevMoveCount == _moves.Count && src.Offset != 0 && metadata.SumFreeSize >= src.Size)
                {
                    if (prevFreeRegionSize >= minimalFreeRegion
                        || nextFreeRegionSize >= minimalFreeRegion
                        || src.Size <= state.AvgFreeSize
                        || src.Size <= state.AvgAllocSize)
                    {
                        var context = new AllocationContext
                        {
                            BufferImageGranularity = list.BufferImageGranularity,
                            AllocationSize = src.Size,
                            AllocationAlignment = src.Alignment,
                            Strategy = Helpers.INTERNAL_ALLOCATION_STRATEGY_MIN_OFFSET,
                            SuballocationType = src.SuballocationType
                        };

                        if (metadata.TryCreateAllocationRequest(in context, out var request))
                        {
                            if (request.Offset < src.Offset)
                            {
                                if (list.CommitTemp(block, in request, context, this, out var temp))
                                {
                                    AddMove(new DefragmentationMove
                                    {
                                        Operation = DefragmentationMoveOperation.Copy,
                                        Source = src,
                                        Destination = temp
                                    });

                                    if (IncrementCounters(src.Size))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }

                prevFreeRegionSize = nextFreeRegionSize;
            }
        }

        if (startMoveCount == _moves.Count && !update)
        {
            state.AvgAllocSize = long.MaxValue;
            return ComputeBalanced(list, index, false);
        }

        return false;
    }

    private bool ComputeFull(BlockList list)
    {
        for (var i = list.BlockCount - 1; i >= 0; i--)
        {
            var block = list[i];
            var metadata = block.MetaData;

            foreach (BlockAllocation src in metadata.GetAllocations())
            {
                if (ReferenceEquals(src.UserData, this))
                {
                    continue;
                }

                if (_allocSet != null && !_allocSet.Contains(src))
                {
                    continue;
                }

                switch (CheckCounters(src.Size))
                {
                    case CounterStatus.Ignore:
                        continue;
                    case CounterStatus.End:
                        return true;
                }

                var data = GetMoveData(src);
                var prevMoveCount = _moves.Count;

                if (list.AllocInOtherBlock(0, i, ref data, this))
                {
                    return true;
                }

                if (prevMoveCount == _moves.Count && src.Offset != 0 && metadata.SumFreeSize >= src.Size)
                {
                    var context = new AllocationContext
                    {
                        BufferImageGranularity = list.BufferImageGranularity,
                        AllocationSize = src.Size,
                        AllocationAlignment = src.Alignment,
                        Strategy = Helpers.INTERNAL_ALLOCATION_STRATEGY_MIN_OFFSET,
                        SuballocationType = src.SuballocationType
                    };

                    if (metadata.TryCreateAllocationRequest(in context, out var request))
                    {
                        if (request.Offset < src.Offset)
                        {
                            if (list.CommitTemp(block, in request, context, this, out var temp))
                            {
                                AddMove(new DefragmentationMove
                                {
                                    Operation = DefragmentationMoveOperation.Copy,
                                    Source = src,
                                    Destination = temp
                                });

                                if (IncrementCounters(src.Size))
                                {
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }

        return false;
    }

    private bool ComputeExtensive(BlockList list, int index)
    {
        if (list.BufferImageGranularity == 1)
        {
            return ComputeFull(list);
        }

        var state = _extensive![index];

        switch (state.Operation)
        {
            case 0: // Move allocations out of the last block to free it.
                {
                    var last = (state.FirstFreeBlock == long.MaxValue ? list.BlockCount : state.FirstFreeBlock) - 1;

                    if (last >= 0)
                    {
                        var block = list[(int)last];

                        foreach (BlockAllocation src in block.MetaData.GetAllocations())
                        {
                            if (ReferenceEquals(src.UserData, this))
                            {
                                continue;
                            }

                            if (_allocSet != null && !_allocSet.Contains(src))
                            {
                                continue;
                            }

                            switch (CheckCounters(src.Size))
                            {
                                case CounterStatus.Ignore:
                                    continue;
                                case CounterStatus.End:
                                    return true;
                            }

                            var data = GetMoveData(src);

                            if (list.AllocInOtherBlock(0, last, ref data, this))
                            {
                                state.FirstFreeBlock = last;
                                return true;
                            }
                        }
                    }

                    state.Operation = 4; // Cleanup
                    return false;
                }
            case 4: // Pack data in blocks even tighter.
                {
                    for (var i = 0; i < list.BlockCount; i++)
                    {
                        if (list.ReallocWithinBlock(list[i], this))
                        {
                            return true;
                        }
                    }

                    state.Operation = 5; // Done
                    return false;
                }
            default:
                return false;
        }
    }

    private static void UpdateVectorStatistics(BlockList list, BalancedState state)
    {
        long allocCount = 0;
        long freeCount = 0;
        long totalSize = 0;
        long sumFree = 0;

        for (var i = 0; i < list.BlockCount; i++)
        {
            var m = list[i].MetaData;
            m.CalcAllocationStatInfo(out var info);

            allocCount += info.AllocationCount;
            freeCount += info.UnusedRangeCount;
            totalSize += m.Size;
            sumFree += info.UnusedBytes;
        }

        if (allocCount > 0)
        {
            state.AvgAllocSize = (totalSize - sumFree) / allocCount;
        }

        state.AvgFreeSize = sumFree / System.Math.Max(1, freeCount);
    }
}

