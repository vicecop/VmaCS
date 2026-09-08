using System.Diagnostics;
using Silk.NET.Vulkan;

namespace VmaCS;

internal class BlockList(VulkanMemoryAllocator allocator,
                         VulkanMemoryPool? pool,
                         int memoryTypeIndex,
                         long preferredBlockSize,
                         int minBlockCount,
                         int maxBlockCount,
                         long bufferImageGranularity,
                         bool explicitBlockSize,
                         long minAllocationAlignment,
                         Func<long, IBlockMetadata> algorithm) : IDisposable
{
    private readonly List<VulkanMemoryBlock> _blocks = [];
    private readonly ReaderWriterLockSlim _rwLock = new(LockRecursionPolicy.NoRecursion);

    private readonly int _minBlockCount = minBlockCount, _maxBlockCount = maxBlockCount;
    private readonly bool _explicitBlockSize = explicitBlockSize;

    private readonly long _minAllocationAlignment = minAllocationAlignment;

    private readonly Func<long, IBlockMetadata> _metaObjectCreate = algorithm;

    private bool _hasEmptyBlock;
    private uint _nextBlockID;

    /// <summary>
    /// Disposes the block list and all underlying Vulkan memory blocks.
    /// </summary>
    public void Dispose()
    {
        foreach (var block in _blocks)
        {
            block.Dispose();
        }

        _rwLock.Dispose();
    }

    /// <summary>
    /// Back-reference to the allocator that owns this block list.
    /// </summary>
    public VulkanMemoryAllocator Allocator { get; } = allocator;

    /// <summary>
    /// The pool this block list belongs to, if any.
    /// </summary>
    public VulkanMemoryPool? ParentPool { get; } = pool;

    /// <summary>
    /// True if this block list belongs to a custom pool; false if it belongs to the allocator's default block vector.
    /// </summary>
    public bool IsCustomPool => ParentPool != null;

    public int MemoryTypeIndex { get; } = memoryTypeIndex;

    public long PreferredBlockSize { get; } = preferredBlockSize;

    public long BufferImageGranularity { get; } = bufferImageGranularity;

    public bool IsEmpty
    {
        get
        {
            _rwLock.EnterReadLock(Allocator.UseLock);

            try
            {
                return _blocks.Count == 0;
            }
            finally
            {
                _rwLock.ExitReadLock(Allocator.UseLock);
            }
        }
    }

    /// <summary>
    /// Corruption detection flag. Currently always false; set to true when corruption detection is implemented.
    /// </summary>
    public static bool IsCorruptedDetectionEnabled => false;

    /// <summary>
    /// Number of blocks currently managed by this block list.
    /// </summary>
    public int BlockCount => _blocks.Count;

    /// <summary>
    /// Enumerates all active allocations across all blocks in this list.
    /// </summary>
    /// <returns>An enumerable of <see cref="BlockAllocation"/> objects.</returns>
    public IEnumerable<BlockAllocation> EnumerateAllocations()
    {
        _rwLock.EnterReadLock(Allocator.UseLock);

        try
        {
            foreach (var block in _blocks)
            {
                foreach (var alloc in block.MetaData.GetAllocations())
                {
                    yield return (BlockAllocation)alloc;
                }
            }
        }
        finally
        {
            _rwLock.ExitReadLock(Allocator.UseLock);
        }
    }

    public VulkanMemoryBlock this[int index] => _blocks[index];

    private IEnumerable<VulkanMemoryBlock> EnumerateBlocksInReverse()
    {
        var localList = _blocks;

        for (var index = localList.Count - 1; index >= 0; --index)
        {
            yield return localList[index];
        }
    }

    /// <summary>
    /// Creates the minimum number of blocks required by this list's configuration.
    /// </summary>
    /// <returns>The Vulkan Result of the block creation operations.</returns>
    public Result CreateMinBlocks()
    {
        if (_blocks.Count > 0)
        {
            throw new InvalidOperationException("Block list not empty");
        }

        for (var i = 0; i < _minBlockCount; ++i)
        {
            var res = CreateBlock(PreferredBlockSize, out _);

            if (res.IsError())
            {
                return res.ThrowOrReturn<AllocationException>(Allocator.ThrowOnError, "Unable to allocate device memory block");
            }
        }

        return Result.Success;
    }

    /// <summary>
    /// Retrieves statistics for this block list, including block count, allocation count, and memory usage.
    /// </summary>
    /// <returns>Aggregated pool statistics.</returns>
    public PoolStats GetPoolStats()
    {
        _rwLock.EnterReadLock(Allocator.UseLock);

        try
        {
            var stats = new PoolStats
            {
                BlockCount = _blocks.Count,
                AllocationSizeMin = long.MaxValue,
                AllocationSizeMax = 0,
            };

            foreach (var block in _blocks)
            {
                block.Validate();

                block.MetaData.AddPoolStats(ref stats);
            }

            // C++ counts a dedicated allocation created through a custom pool as one of that pool's
            // blocks, so its size, block count and allocation statistics must be folded into the pool.
            // Dedicated allocations are tracked in the allocator-global handler, so scan it and keep
            // only the ones attributed to this pool.
            if (ParentPool != null)
            {
                ref var handler = ref Allocator.DedicatedAllocations[MemoryTypeIndex];

                handler.Lock.EnterReadLock(Allocator.UseLock);

                try
                {
                    foreach (var allocation in handler.Allocations)
                    {
                        if (((DedicatedAllocation)allocation).Pool != ParentPool)
                        {
                            continue;
                        }

                        stats.BlockCount += 1;
                        stats.Size += allocation.Size;
                        stats.AllocationCount += 1;
                        stats.AllocationBytes += allocation.Size;

                        if (allocation.Size < stats.AllocationSizeMin)
                        {
                            stats.AllocationSizeMin = allocation.Size;
                        }

                        if (allocation.Size > stats.AllocationSizeMax)
                        {
                            stats.AllocationSizeMax = allocation.Size;
                        }
                    }
                }
                finally
                {
                    handler.Lock.ExitReadLock(Allocator.UseLock);
                }
            }

            return stats;
        }
        finally
        {
            _rwLock.ExitReadLock(Allocator.UseLock);
        }
    }

    /// <summary>
    /// Allocates memory from this block list.
    /// </summary>
    /// <param name="size">Size of the allocation, in bytes.</param>
    /// <param name="alignment">Alignment requirement, in bytes.</param>
    /// <param name="allocInfo">Allocation creation parameters.</param>
    /// <param name="suballocType">Type of suballocation.</param>
    /// <param name="allocation">Receives the created <see cref="BlockAllocation"/> if successful.</param>
    /// <returns>The Vulkan Result of the allocation.</returns>
    public Result Allocate(long size,
                           long alignment,
                           in AllocationCreateInfo allocInfo,
                           SuballocationType suballocType,
                           out BlockAllocation? allocation)
    {
        _rwLock.EnterWriteLock(Allocator.UseLock);

        try
        {
            return AllocatePage(size, alignment, allocInfo, suballocType, out allocation);
        }
        finally
        {
            _rwLock.ExitWriteLock(Allocator.UseLock);
        }
    }

    /// <summary>
    /// Frees an allocation back to this block list.
    /// </summary>
    /// <param name="allocation">The allocation to free.</param>
    public void Free(Allocation allocation)
    {
        VulkanMemoryBlock? blockToDelete = null;

        bool budgetExceeded;

        {
            var heapIndex = Allocator.MemoryTypeIndexToHeapIndex(MemoryTypeIndex);
            var budget = Allocator.GetBudget(heapIndex);
            budgetExceeded = budget.Usage >= budget.Budget;
        }

        _rwLock.EnterWriteLock(Allocator.UseLock);

        try
        {
            var blockAlloc = (BlockAllocation)allocation;

            if (blockAlloc.FreedByDefrag)
            {
                return;
            }

            var block = blockAlloc.Block;

            if (allocation.IsPersistantMapped)
            {
                block.Unmap(1);
            }

            if (Allocator.DebugDetectCorruption)
            {
                // C++ VmaBlockVector::Free: validate the freed allocation's magic value
                // immediately before freeing, for immediate (not just on-demand) overrun detection.
                if (block.ValidateAllocationMagic(blockAlloc).IsError())
                {
                    throw new ValidationFailedException();
                }
            }

            block.MetaData.Free(blockAlloc);

            if (Allocator.DebugDetectCorruption)
            {
                // C++ VmaBlockVector::Free -> WriteMagicValueAfterAllocation: re-write the
                // trailing margin's magic value after the allocation is freed (offset + size, DebugMargin bytes).
                block.FillWithMagic(blockAlloc.Offset + blockAlloc.Size, Allocator.DebugMargin);
            }

            block.NotifyFree();

            block.Validate();

            var canDeleteBlock = _blocks.Count > _minBlockCount;

            if (block.MetaData.IsEmpty)
            {
                if ((_hasEmptyBlock || budgetExceeded) && canDeleteBlock)
                {
                    blockToDelete = block;
                    Remove(block);
                }
            }
            else if (_hasEmptyBlock && canDeleteBlock)
            {
                block = _blocks[_blocks.Count - 1];

                if (block.MetaData.IsEmpty)
                {
                    blockToDelete = block;
                    _blocks.RemoveAt(_blocks.Count - 1);
                }
            }

            UpdateHasEmptyBlock();
            IncrementallySortBlocks();
        }
        finally
        {
            _rwLock.ExitWriteLock(Allocator.UseLock);
        }

        blockToDelete?.Dispose();
    }

    /// <summary>
    /// Accumulates statistics from all blocks in this list into the provided <see cref="Stats"/> accumulator.
    /// </summary>
    /// <param name="stats">The statistics accumulator to update.</param>
    public void AddStats(Stats stats)
    {
        var memTypeIndex = MemoryTypeIndex;
        var memHeapIndex = Allocator.MemoryTypeIndexToHeapIndex(memTypeIndex);

        _rwLock.EnterReadLock(Allocator.UseLock);

        try
        {
            foreach (var block in _blocks)
            {
                block.Validate();

                block.MetaData.CalcAllocationStatInfo(out var info);
                StatInfo.Add(ref stats.TotalRef, info);
                StatInfo.Add(ref stats.MemoryTypeArray[memTypeIndex], info);
                StatInfo.Add(ref stats.MemoryHeapArray[memHeapIndex], info);
            }
        }
        finally
        {
            _rwLock.ExitReadLock(Allocator.UseLock);
        }
    }

    /// <summary>
    /// Checks for memory corruption in all blocks of this list.
    /// </summary>
    /// <returns>The Vulkan Result of the corruption check.</returns>
    public Result CheckCorruption()
    {
        if (!Allocator.DebugDetectCorruption)
        {
            return Result.Success;
        }

        var result = Result.Success;

        _rwLock.EnterReadLock(Allocator.UseLock);

        try
        {
            foreach (var block in _blocks)
            {
                var r = block.CheckCorruption();

                if (r.IsError())
                {
                    result = r;
                }
            }
        }
        finally
        {
            _rwLock.ExitReadLock(Allocator.UseLock);
        }

        return result;
    }

    /// <summary>
    /// Calculates the total number of active allocations across all blocks in this list.
    /// </summary>
    /// <returns>Total allocation count.</returns>
    public int CalcAllocationCount()
    {
        var res = 0;

        foreach (var block in _blocks)
        {
            res += block.MetaData.AllocationCount;
        }

        return res;
    }

    /// <summary>
    /// Checks whether buffer-image granularity conflicts are possible in any block of this list.
    /// </summary>
    /// <returns>True if a conflict is possible; otherwise false.</returns>
    public bool IsBufferImageGranularityConflictPossible()
    {
        if (BufferImageGranularity == 1)
        {
            return false;
        }

        var lastSuballocType = SuballocationType.Free;

        foreach (var block in _blocks)
        {
            if (block.MetaData.IsBufferImageGranularityConflictPossible(BufferImageGranularity, ref lastSuballocType))
            {
                return true;
            }
        }

        return false;
    }

    private long CalcMaxBlockSize()
    {
        long result = 0;

        for (var i = _blocks.Count - 1; i >= 0; --i)
        {
            var blockSize = _blocks[i].MetaData.Size;

            if (result < blockSize)
            {
                result = blockSize;
            }

            if (result >= PreferredBlockSize)
            {
                break;
            }
        }

        return result;
    }

    private Result AllocatePage(long size,
                                long alignment,
                                in AllocationCreateInfo createInfo,
                                SuballocationType suballocType,
                                out BlockAllocation? allocation)
    {
        var mapped = (createInfo.Flags & AllocationCreateFlags.Mapped) != 0;

        // Mirror VmaBlockVector::Allocate: every allocation is aligned up to at least the block
        // list's minimum allocation alignment (pool minAllocationAlignment / memory type min alignment).
        alignment = Math.Max(alignment, _minAllocationAlignment);

        long freeMemory;

        {
            var heapIndex = Allocator.MemoryTypeIndexToHeapIndex(MemoryTypeIndex);

            var heapBudget = Allocator.GetBudget(heapIndex);

            freeMemory = (heapBudget.Usage < heapBudget.Budget) ? (heapBudget.Budget - heapBudget.Usage) : 0;
        }

        var canFallbackToDedicated = !IsCustomPool;
        var canCreateNewBlock = ((createInfo.Flags & AllocationCreateFlags.NeverAllocate) == 0) && (_blocks.Count < _maxBlockCount) && (freeMemory >= size || !canFallbackToDedicated);

        var strategy = createInfo.Strategy;

        switch (strategy)
        {
            case 0:
                strategy = AllocationStrategyFlags.BestFit;
                break;
            case AllocationStrategyFlags.BestFit:
            case AllocationStrategyFlags.WorstFit:
            case AllocationStrategyFlags.FirstFit:
                break;
            default:
                allocation = null;
                return Result.ErrorFeatureNotPresent.ThrowOrReturn<AllocationException>(Allocator.ThrowOnError, "Invalid allocation strategy");
        }

        if (size + 2 * Allocator.DebugMargin > PreferredBlockSize)
        {
            allocation = null;
            return Result.ErrorOutOfDeviceMemory.ThrowOrReturn<AllocationException>(Allocator.ThrowOnError, "Allocation size larger than block size");
        }

        var context = new AllocationContext
        {
            BufferImageGranularity = BufferImageGranularity,
            AllocationSize = size,
            AllocationAlignment = alignment,
            Strategy = strategy,
            SuballocationType = suballocType,
            UpperAddress = (createInfo.Flags & AllocationCreateFlags.UpperAddress) != 0
        };

        BlockAllocation? alloc;
        {
            if (strategy == AllocationStrategyFlags.BestFit)
            {
                foreach (var block in _blocks)
                {
                    alloc = AllocateFromBlock(block, in context, mapped, createInfo.UserData);

                    if (alloc != null)
                    {
                        allocation = alloc;
                        return Result.Success;
                    }
                }
            }
            else
            {
                foreach (var curBlock in EnumerateBlocksInReverse())
                {
                    alloc = AllocateFromBlock(curBlock, in context, mapped, createInfo.UserData);

                    if (alloc != null)
                    {
                        allocation = alloc;
                        return Result.Success;
                    }
                }
            }
        }

        if (canCreateNewBlock)
        {
            var newBlockSize = PreferredBlockSize;
            var newBlockSizeShift = 0;
            const int NEW_BLOCK_SIZE_SHIFT_MAX = 3;

            if (!_explicitBlockSize)
            {
                var maxExistingBlockSize = CalcMaxBlockSize();

                for (var i = 0; i < NEW_BLOCK_SIZE_SHIFT_MAX; ++i)
                {
                    var smallerNewBlockSize = newBlockSize / 2;
                    if (smallerNewBlockSize > maxExistingBlockSize && smallerNewBlockSize >= size * 2)
                    {
                        newBlockSize = smallerNewBlockSize;
                        newBlockSizeShift += 1;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            var newBlockIndex = 0;

            var res = (newBlockSize <= freeMemory || !canFallbackToDedicated) ? CreateBlock(newBlockSize, out newBlockIndex) : Result.ErrorOutOfDeviceMemory;

            if (!_explicitBlockSize)
            {
                while (res < 0 && newBlockSizeShift < NEW_BLOCK_SIZE_SHIFT_MAX)
                {
                    var smallerNewBlockSize = newBlockSize / 2;

                    if (smallerNewBlockSize >= size)
                    {
                        newBlockSize = smallerNewBlockSize;
                        newBlockSizeShift += 1;
                        res = (newBlockSize <= freeMemory || !canFallbackToDedicated) ? CreateBlock(newBlockSize, out newBlockIndex) : Result.ErrorOutOfDeviceMemory;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            if (res.IsSuccess())
            {
                var block = _blocks[newBlockIndex];

                alloc = AllocateFromBlock(block, in context, mapped, createInfo.UserData);

                if (alloc != null)
                {
                    allocation = alloc;
                    return Result.Success;
                }
            }
        }

        allocation = null;
        return Result.ErrorOutOfDeviceMemory.ThrowOrReturn<AllocationException>(Allocator.ThrowOnError, "Unable to allocate memory");
    }

    private unsafe BlockAllocation? AllocateFromBlock(VulkanMemoryBlock block,
                                                      in AllocationContext context,
                                                      bool mapped,
                                                      object? userData)
    {
        if (!block.MetaData.TryCreateAllocationRequest(in context, out var request))
        {
            return null;
        }

        if (mapped)
        {
            block.Map(1, out var _);
        }

        var allocation = new BlockAllocation(Allocator);

        block.MetaData.Alloc(in request, context.SuballocationType, context.AllocationSize, allocation);

        allocation.InitBlockAllocation(block, request.Offset, context.AllocationAlignment, context.AllocationSize, MemoryTypeIndex,
                                       context.SuballocationType, mapped);

        block.NotifyAlloc();

        UpdateHasEmptyBlock();

        block.Validate();

        allocation.UserData = userData;

        Allocator.Budget.AddAllocation(Allocator.MemoryTypeIndexToHeapIndex(MemoryTypeIndex), context.AllocationSize);

        return allocation;
    }

    private unsafe Result CreateBlock(long blockSize, out int newBlockIndex)
    {
        newBlockIndex = -1;

        var info = new MemoryAllocateInfo
        {
            SType = StructureType.MemoryAllocateInfo,
            MemoryTypeIndex = (uint)MemoryTypeIndex,
            AllocationSize = (ulong)blockSize
        };

        // Every standalone block can potentially contain a buffer with BufferUsageFlags.ShaderDeviceAddressBitKhr - always enable the feature
        var allocFlagsInfo = new MemoryAllocateFlagsInfoKHR(StructureType.MemoryAllocateFlagsInfoKhr);
        if (Allocator.UseKhrBufferDeviceAddress)
        {
            allocFlagsInfo.Flags = MemoryAllocateFlags.DeviceAddressBitKhr;
            info.PNext = &allocFlagsInfo;
        }

        // Custom pNext chain for the pool's device memory blocks (e.g. VK_KHR_external_memory export),
        // mirroring VmaPool_T::m_ParentDeviceMemoryBlock allocation. Chained after the BDA flags info.
        void* poolNext = default;
        if (ParentPool != null)
        {
            poolNext = ParentPool.MemoryAllocateNext;
        }
        if (poolNext != default)
        {
            if (Allocator.UseKhrBufferDeviceAddress)
            {
                allocFlagsInfo.PNext = poolNext;
            }
            else
            {
                info.PNext = poolNext;
            }
        }

        var res = Allocator.AllocateVulkanMemory(in info, out var mem);

        if (res < 0)
        {
            return res;
        }

        var meta = _metaObjectCreate(blockSize);
        meta.BufferImageGranularity = BufferImageGranularity;
        meta.DebugMargin = Allocator.DebugMargin;


        if (meta.Size != blockSize)
        {
            throw new InvalidOperationException("Returned Metadata object reports incorrect block size");
        }

        var block = new VulkanMemoryBlock(Allocator, ParentPool, MemoryTypeIndex, mem, _nextBlockID++, meta);

        if (Allocator.DebugDetectCorruption)
        {
            block.FillWithMagic(0, block.MetaData.Size);
        }

        _blocks.Add(block);

        newBlockIndex = _blocks.Count - 1;

        return Result.Success;
    }

    private void UpdateHasEmptyBlock()
    {
        _hasEmptyBlock = false;

        foreach (var block in _blocks)
        {
            if (block.MetaData.IsEmpty)
            {
                _hasEmptyBlock = true;
                break;
            }
        }
    }

    private void Remove(VulkanMemoryBlock block)
    {
        var res = _blocks.Remove(block);
        Debug.Assert(res, "");
    }

    private void IncrementallySortBlocks()
    {
        if ((uint)_blocks.Count <= 1)
        {
            return;
        }

        var prevBlock = _blocks[0];
        var i = 1;

        do
        {
            var curBlock = _blocks[i];

            if (prevBlock.MetaData.SumFreeSize > curBlock.MetaData.SumFreeSize)
            {
                _blocks[i - 1] = curBlock;
                _blocks[i] = prevBlock;
                return;
            }

            prevBlock = curBlock;
            i += 1;
        }
        while (i < _blocks.Count);
    }

    internal struct MoveData
    {
        public long Size;
        public long Alignment;
        public SuballocationType Type;
        public AllocationStrategyFlags Strategy;
        public BlockAllocation Source;
    }

    internal void DefragmentPass(DefragmentationContext ctx, int index, out bool moved)
    {
        moved = false;

        _rwLock.EnterWriteLock(Allocator.UseLock);

        try
        {
            if (_blocks.Count > 1)
            {
                moved = ctx.ComputeDefragmentation(this, index);
            }
            else if (_blocks.Count == 1)
            {
                moved = ctx.ReallocWithinBlock(this, _blocks[0]);
            }
        }
        finally
        {
            _rwLock.ExitWriteLock(Allocator.UseLock);
        }
    }

    internal bool ReallocWithinBlock(VulkanMemoryBlock block, DefragmentationContext ctx)
    {
        var metadata = block.MetaData;

        foreach (BlockAllocation src in metadata.GetAllocations())
        {
            if (ReferenceEquals(src.UserData, ctx))
            {
                continue;
            }

            if (ctx.AllocSet != null && !ctx.AllocSet.Contains(src))
            {
                continue;
            }

            switch (ctx.CheckCounters(src.Size))
            {
                case DefragmentationContext.CounterStatus.Ignore:
                    continue;
                case DefragmentationContext.CounterStatus.End:
                    return true;
            }

            if (src.Offset == 0 || metadata.SumFreeSize < src.Size)
            {
                continue;
            }

            var context = new AllocationContext
            {
                BufferImageGranularity = BufferImageGranularity,
                AllocationSize = src.Size,
                AllocationAlignment = src.Alignment,
                Strategy = Helpers.INTERNAL_ALLOCATION_STRATEGY_MIN_OFFSET,
                SuballocationType = src.SuballocationType
            };

            if (metadata.TryCreateAllocationRequest(in context, out var request)
                && request.Offset < src.Offset
                && CommitTemp(block, in request, context, ctx, out var temp))
            {
                ctx.AddMove(new DefragmentationMove
                {
                    Operation = DefragmentationMoveOperation.Copy,
                    Source = src,
                    Destination = temp
                });

                if (ctx.IncrementCounters(src.Size))
                {
                    return true;
                }
            }
        }

        return false;
    }

    internal bool AllocInOtherBlock(long start,
                                    long end,
                                    ref MoveData data,
                                    DefragmentationContext ctx)
    {
        for (var i = start; i < end; i++)
        {
            var dstBlock = _blocks[(int)i];

            if (dstBlock.MetaData.SumFreeSize < data.Size)
            {
                continue;
            }

            var context = new AllocationContext
            {
                BufferImageGranularity = BufferImageGranularity,
                AllocationSize = data.Size,
                AllocationAlignment = data.Alignment,
                Strategy = data.Strategy,
                SuballocationType = data.Type
            };

            if (dstBlock.MetaData.TryCreateAllocationRequest(in context, out var request)
                && CommitTemp(dstBlock, in request, context, ctx, out var temp))
            {
                ctx.AddMove(new DefragmentationMove
                {
                    Operation = DefragmentationMoveOperation.Copy,
                    Source = data.Source,
                    Destination = temp
                });

                if (ctx.IncrementCounters(data.Size))
                {
                    return true;
                }

                break;
            }
        }

        return false;
    }

    internal static void SwapAllocations(BlockAllocation src, BlockAllocation dst)
    {
        var srcOffset = src.Offset;
        var srcBlock = src.Block;
        var dstOffset = dst.Offset;
        var dstBlock = dst.Block;

        // Reassign the two metadata suballocation nodes. Using offsets (not the
        // allocation objects) keeps the swap unambiguous even when both nodes live
        // in the same block.
        srcBlock.MetaData.SetAllocationUserData(srcOffset, dst);
        dstBlock.MetaData.SetAllocationUserData(dstOffset, src);

        // Update the allocation objects, transferring map ref-counts for any
        // persistently mapped allocations so the cached mapped pointer stays valid.
        src.ChangeAllocation(dstBlock, dstOffset);
        dst.ChangeAllocation(srcBlock, srcOffset);
    }

    internal void SortByFreeSize() =>
        _blocks.Sort((a, b) => b.MetaData.SumFreeSize.CompareTo(a.MetaData.SumFreeSize));

    internal bool CommitTemp(VulkanMemoryBlock block,
                             in AllocationRequest request,
                             in AllocationContext context,
                             DefragmentationContext ctx,
                             out BlockAllocation temp)
    {
        temp = new BlockAllocation(Allocator);

        block.MetaData.Alloc(in request, context.SuballocationType, context.AllocationSize, temp);
        temp.InitBlockAllocation(block,
                                 request.Offset,
                                 context.AllocationAlignment,
                                 context.AllocationSize,
                                 MemoryTypeIndex,
                                 context.SuballocationType,
                                 false);
        temp.UserData = ctx;

        block.NotifyAlloc();

        Allocator.Budget.AddAllocation(Allocator.MemoryTypeIndexToHeapIndex(MemoryTypeIndex), context.AllocationSize);

        return true;
    }
}

