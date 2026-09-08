using System.Diagnostics;
using Silk.NET.Vulkan;

namespace VmaCS;

internal sealed class BlockMetadataTlsf : IBlockMetadata
{
    public long DebugMargin { get; set; }
    private const byte SECOND_LEVEL_INDEX = 5;
    private const long SMALL_BUFFER_SIZE = 256;
    private const byte MEMORY_CLASS_SHIFT = 7;
    private const int MAX_MEMORY_CLASSES = 65 - MEMORY_CLASS_SHIFT;

    private BlockBufferImageGranularity _granularityHandler;
    private long _bufferImageGranularity = 1;

    public long BufferImageGranularity
    {
        get => _bufferImageGranularity;
        set
        {
            if (_bufferImageGranularity != value)
            {
                _bufferImageGranularity = value;
                _granularityHandler = new BlockBufferImageGranularity(value);
                _granularityHandler.Init(Size);
            }
        }
    }

    private sealed class Block
    {
        public long Offset;
        public long Size;
        public Block? PrevPhysical;
        public Block? NextPhysical;
        public Block? PrevFree;
        public Block? NextFree;
        public bool IsFree;
        public object? UserData;
    }

    public long Size { get; private set; }

    private long _allocCount;
    private long _blocksFreeCount;
    private long _blocksFreeSize;
    private uint _isFreeBitmap;
    private uint[] _innerIsFreeBitmap = new uint[MAX_MEMORY_CLASSES];
    private int _listsCount;
    private Block?[] _freeList = [];
    private Block _nullBlock = null!;

    public int AllocationCount => (int)_allocCount;

    public long SumFreeSize => _blocksFreeSize + _nullBlock.Size;

    public long UnusedRangeSizeMax
    {
        get
        {
            long max = 0;

            for (var b = _nullBlock; b != null; b = b.PrevPhysical)
            {
                if (b.IsFree && b.Size > max)
                {
                    max = b.Size;
                }
            }

            return max;
        }
    }

    public bool IsEmpty => _allocCount == 0;

    public BlockMetadataTlsf(long blockSize, long debugMargin)
    {
        Size = blockSize;
        DebugMargin = debugMargin;
        _granularityHandler = new BlockBufferImageGranularity(BufferImageGranularity);
        Init(blockSize);
    }

    private void Init(long size)
    {
        _nullBlock = new Block
        {
            Offset = 0,
            Size = size,
            PrevPhysical = null,
            NextPhysical = null,
            PrevFree = null,
            NextFree = null,
            IsFree = true,
            UserData = null
        };

        var memoryClass = SizeToMemoryClass(size);
        var sli = SizeToSecondIndex(size, memoryClass);

        _listsCount = (memoryClass == 0 ? 0 : (memoryClass - 1) * (1 << SECOND_LEVEL_INDEX) + sli) + 1;
        _listsCount += 4;

        _innerIsFreeBitmap = new uint[MAX_MEMORY_CLASSES];
        _freeList = new Block?[_listsCount];

        _granularityHandler.Init(size);
    }

    public void Validate()
    {
        Helpers.Validate(SumFreeSize <= Size);

        var calculatedSize = _nullBlock.Size;
        var calculatedFreeSize = _nullBlock.Size;
        long allocCount = 0;
        long freeCount = 0;

        for (var list = 0; list < _listsCount; ++list)
        {
            var block = _freeList[list];

            if (block != null)
            {
                Helpers.Validate(block.IsFree);
                Helpers.Validate(block.PrevFree == null);

                while (block.NextFree != null)
                {
                    Helpers.Validate(block.NextFree.IsFree);
                    Helpers.Validate(block.NextFree.PrevFree == block);
                    block = block.NextFree;
                }
            }
        }

        var nextOffset = _nullBlock.Offset;

        Helpers.Validate(_nullBlock.NextPhysical == null);

        if (_nullBlock.PrevPhysical != null)
        {
            Helpers.Validate(_nullBlock.PrevPhysical.NextPhysical == _nullBlock);
        }

        for (var prev = _nullBlock.PrevPhysical; prev != null; prev = prev.PrevPhysical)
        {
            Helpers.Validate(prev.Offset + prev.Size == nextOffset);
            nextOffset = prev.Offset;
            calculatedSize += prev.Size;

            var listIndex = GetListIndex(prev.Size);

            if (prev.IsFree)
            {
                ++freeCount;

                var freeBlock = _freeList[listIndex];

                Helpers.Validate(freeBlock != null);

                var found = false;

                do
                {
                    if (freeBlock == prev)
                    {
                        found = true;
                    }

                    freeBlock = freeBlock.NextFree;
                } while (!found && freeBlock != null);

                Helpers.Validate(found);
                calculatedFreeSize += prev.Size;
            }
            else
            {
                ++allocCount;

                var freeBlock = _freeList[listIndex];

                while (freeBlock != null)
                {
                    Helpers.Validate(freeBlock != prev);
                    freeBlock = freeBlock.NextFree;
                }
            }

            if (prev.PrevPhysical != null)
            {
                Helpers.Validate(prev.PrevPhysical.NextPhysical == prev);
            }
        }

        Helpers.Validate(nextOffset == 0);
        Helpers.Validate(calculatedSize == Size);
        Helpers.Validate(calculatedFreeSize == SumFreeSize);
        Helpers.Validate(allocCount == _allocCount);
        Helpers.Validate(freeCount == _blocksFreeCount);
    }

    public void CalcAllocationStatInfo(out StatInfo outInfo)
    {
        outInfo = default;
        outInfo.BlockCount = 1;

        var nullBlockFree = _nullBlock.Size > 0 ? 1 : 0;
        var totalRanges = _allocCount + _blocksFreeCount + nullBlockFree;
        var freeRanges = _blocksFreeCount + nullBlockFree;

        outInfo.AllocationCount = (int)(totalRanges - freeRanges);
        outInfo.UnusedRangeCount = (int)freeRanges;

        outInfo.UnusedBytes = SumFreeSize;
        outInfo.UsedBytes = Size - outInfo.UnusedBytes;

        outInfo.AllocationSizeMin = long.MaxValue;
        outInfo.AllocationSizeMax = 0;
        outInfo.UnusedRangeSizeMin = long.MaxValue;
        outInfo.UnusedRangeSizeMax = 0;

        for (var b = _nullBlock; b != null; b = b.PrevPhysical)
        {
            if (b.IsFree)
            {
                if (b.Size < outInfo.UnusedRangeSizeMin)
                {
                    outInfo.UnusedRangeSizeMin = b.Size;
                }

                if (b.Size > outInfo.UnusedRangeSizeMax)
                {
                    outInfo.UnusedRangeSizeMax = b.Size;
                }
            }
            else
            {
                if (b.Size < outInfo.AllocationSizeMin)
                {
                    outInfo.AllocationSizeMin = b.Size;
                }

                if (b.Size > outInfo.AllocationSizeMax)
                {
                    outInfo.AllocationSizeMax = b.Size;
                }
            }
        }
    }

    public void AddPoolStats(ref PoolStats stats)
    {
        stats.Size += Size;
        stats.UnusedSize += SumFreeSize;
        stats.AllocationBytes += Size - SumFreeSize;
        stats.AllocationCount += (int)_allocCount;
        stats.UnusedRangeCount += (int)(_blocksFreeCount + (_nullBlock.Size > 0 ? 1 : 0));

        var tmp = UnusedRangeSizeMax;

        if (tmp > stats.UnusedRangeSizeMax)
        {
            stats.UnusedRangeSizeMax = tmp;
        }

        for (var b = _nullBlock.PrevPhysical; b != null; b = b.PrevPhysical)
        {
            if (b.IsFree || b.UserData is not IAllocation alloc)
            {
                continue;
            }

            if (alloc.Size < stats.AllocationSizeMin)
            {
                stats.AllocationSizeMin = alloc.Size;
            }

            if (alloc.Size > stats.AllocationSizeMax)
            {
                stats.AllocationSizeMax = alloc.Size;
            }
        }
    }

    public bool IsBufferImageGranularityConflictPossible(long bufferImageGranularity,
                                                         ref SuballocationType type)
    {
        if (bufferImageGranularity == 1 || IsEmpty)
        {
            return false;
        }

        var minAlignment = long.MaxValue;
        var typeConflict = false;

        for (var b = _nullBlock.PrevPhysical; b != null; b = b.PrevPhysical)
        {
            var thisType = GetBlockType(b);

            if (thisType != SuballocationType.Free)
            {
                minAlignment = Math.Min(minAlignment, ((IAllocation)b.UserData!).Alignment);

                if (Helpers.IsBufferImageGranularityConflict(type, thisType))
                {
                    typeConflict = true;
                }

                type = thisType;
            }
        }

        return typeConflict || minAlignment >= bufferImageGranularity;
    }

    public bool TryCreateAllocationRequest(in AllocationContext context,
                                           out AllocationRequest request)
    {
        request = default;
        request.Type = AllocationRequestType.Normal;

        if (context.AllocationSize <= 0)
        {
            return false;
        }

        var allocSize = context.AllocationSize;
        var allocAlignment = context.AllocationAlignment;
        _granularityHandler.RoundupAllocRequest(context.SuballocationType, ref allocSize, ref allocAlignment);
        allocSize += DebugMargin;

        if (allocSize > SumFreeSize)
        {
            return false;
        }

        if (_blocksFreeCount == 0)
        {
            return CheckBlock(_nullBlock, (uint)_listsCount, in context, ref request);
        }

        var sizeForNextList = allocSize;
        var smallSizeStep = SMALL_BUFFER_SIZE / 4;

        if (allocSize > SMALL_BUFFER_SIZE)
        {
            sizeForNextList += 1L << (BitScanMSB(allocSize) - SECOND_LEVEL_INDEX);
        }
        else if (allocSize > SMALL_BUFFER_SIZE - smallSizeStep)
        {
            sizeForNextList = SMALL_BUFFER_SIZE + 1;
        }
        else
        {
            sizeForNextList += smallSizeStep;
        }

        uint nextListIndex;
        uint prevListIndex;
        Block? nextListBlock;
        Block? prevListBlock;

        if ((context.Strategy & AllocationStrategyFlags.FirstFit) != 0)
        {
            nextListBlock = FindFreeBlock(sizeForNextList, out nextListIndex);

            if (nextListBlock != null && CheckBlock(nextListBlock, nextListIndex, in context, ref request))
            {
                return true;
            }

            if (CheckBlock(_nullBlock, (uint)_listsCount, in context, ref request))
            {
                return true;
            }

            while (nextListBlock != null)
            {
                if (CheckBlock(nextListBlock, nextListIndex, in context, ref request))
                {
                    return true;
                }

                nextListBlock = nextListBlock.NextFree;
            }

            prevListBlock = FindFreeBlock(allocSize, out prevListIndex);

            while (prevListBlock != null)
            {
                if (CheckBlock(prevListBlock, prevListIndex, in context, ref request))
                {
                    return true;
                }

                prevListBlock = prevListBlock.NextFree;
            }
        }
        else if ((context.Strategy & AllocationStrategyFlags.BestFit) != 0)
        {
            prevListBlock = FindFreeBlock(allocSize, out prevListIndex);

            while (prevListBlock != null)
            {
                if (CheckBlock(prevListBlock, prevListIndex, in context, ref request))
                {
                    return true;
                }

                prevListBlock = prevListBlock.NextFree;
            }

            if (CheckBlock(_nullBlock, (uint)_listsCount, in context, ref request))
            {
                return true;
            }

            nextListBlock = FindFreeBlock(sizeForNextList, out nextListIndex);

            while (nextListBlock != null)
            {
                if (CheckBlock(nextListBlock, nextListIndex, in context, ref request))
                {
                    return true;
                }

                nextListBlock = nextListBlock.NextFree;
            }
        }
        else if ((context.Strategy & Helpers.INTERNAL_ALLOCATION_STRATEGY_MIN_OFFSET) != 0)
        {
            var blockList = new List<Block>();

            for (var b = _nullBlock.PrevPhysical; b != null; b = b.PrevPhysical)
            {
                if (b.IsFree && b.Size >= allocSize)
                {
                    blockList.Add(b);
                }
            }

            for (var i = blockList.Count - 1; i >= 0; --i)
            {
                if (CheckBlock(blockList[i], GetListIndex(blockList[i].Size), in context, ref request))
                {
                    return true;
                }
            }

            if (CheckBlock(_nullBlock, (uint)_listsCount, in context, ref request))
            {
                return true;
            }

            return false;
        }
        else
        {
            nextListBlock = FindFreeBlock(sizeForNextList, out nextListIndex);

            while (nextListBlock != null)
            {
                if (CheckBlock(nextListBlock, nextListIndex, in context, ref request))
                {
                    return true;
                }

                nextListBlock = nextListBlock.NextFree;
            }

            if (CheckBlock(_nullBlock, (uint)_listsCount, in context, ref request))
            {
                return true;
            }

            prevListBlock = FindFreeBlock(allocSize, out prevListIndex);

            while (prevListBlock != null)
            {
                if (CheckBlock(prevListBlock, prevListIndex, in context, ref request))
                {
                    return true;
                }

                prevListBlock = prevListBlock.NextFree;
            }
        }

        while (++nextListIndex < _listsCount)
        {
            nextListBlock = _freeList[nextListIndex];

            while (nextListBlock != null)
            {
                if (CheckBlock(nextListBlock, nextListIndex, in context, ref request))
                {
                    return true;
                }

                nextListBlock = nextListBlock.NextFree;
            }
        }

        return false;
    }

    public void Alloc(in AllocationRequest request,
                      SuballocationType type,
                      long allocSize,
                      IAllocation allocation)
    {
        var currentBlock = (Block)request.Item!;
        var offset = request.Offset;

        Debug.Assert(currentBlock.Offset <= offset);

        if (currentBlock != _nullBlock)
        {
            RemoveFreeBlock(currentBlock);
        }

        var debugMargin = DebugMargin;
        var missingAlignment = offset - currentBlock.Offset;

        if (missingAlignment != 0)
        {
            var prevBlock = currentBlock.PrevPhysical!;

            if (prevBlock.IsFree && prevBlock.Size != debugMargin)
            {
                var oldList = GetListIndex(prevBlock.Size);
                prevBlock.Size += missingAlignment;

                if (oldList != GetListIndex(prevBlock.Size))
                {
                    prevBlock.Size -= missingAlignment;
                    RemoveFreeBlock(prevBlock);
                    prevBlock.Size += missingAlignment;
                    InsertFreeBlock(prevBlock);
                }
                else
                {
                    _blocksFreeSize += missingAlignment;
                }
            }
            else
            {
                var newBlock = new Block
                {
                    Offset = currentBlock.Offset,
                    Size = missingAlignment,
                    PrevPhysical = prevBlock,
                    NextPhysical = currentBlock,
                    PrevFree = null,
                    NextFree = null,
                    IsFree = false,
                    UserData = null
                };

                currentBlock.PrevPhysical = newBlock;
                prevBlock.NextPhysical = newBlock;
                InsertFreeBlock(newBlock);
            }

            currentBlock.Size -= missingAlignment;
            currentBlock.Offset += missingAlignment;
        }

        var size = allocSize + debugMargin;

        if (currentBlock.Size == size)
        {
            if (currentBlock == _nullBlock)
            {
                var newNull = new Block
                {
                    Offset = currentBlock.Offset + size,
                    Size = 0,
                    PrevPhysical = currentBlock,
                    NextPhysical = null,
                    PrevFree = null,
                    NextFree = null,
                    IsFree = true,
                    UserData = null
                };

                currentBlock.NextPhysical = newNull;
                currentBlock.IsFree = false;
                _nullBlock = newNull;
            }
        }
        else
        {
            Debug.Assert(currentBlock.Size > size, "Proper block already found, shouldn't find smaller one!");

            var newBlock = new Block
            {
                Offset = currentBlock.Offset + size,
                Size = currentBlock.Size - size,
                PrevPhysical = currentBlock,
                NextPhysical = currentBlock.NextPhysical,
                PrevFree = null,
                NextFree = null,
                IsFree = false,
                UserData = null
            };

            currentBlock.NextPhysical = newBlock;
            currentBlock.Size = size;

            if (currentBlock == _nullBlock)
            {
                _nullBlock = newBlock;
                _nullBlock.IsFree = true;
                currentBlock.IsFree = false;
            }
            else
            {
                newBlock.NextPhysical!.PrevPhysical = newBlock;
                InsertFreeBlock(newBlock);
            }
        }

        currentBlock.UserData = allocation;

        _allocCount += 1;

        _granularityHandler.AllocPages(type, request.Offset, size);
    }

    public void Free(IAllocation allocation)
    {
        for (var b = _nullBlock.PrevPhysical; b != null; b = b.PrevPhysical)
        {
            if (!b.IsFree && ReferenceEquals(b.UserData, allocation))
            {
                _granularityHandler.FreePages(b.Offset, b.Size);
                FreeBlock(b);
                return;
            }
        }

        throw new InvalidOperationException("Allocation not found!");
    }

    public void FreeAtOffset(long offset)
    {
        for (var b = _nullBlock.PrevPhysical; b != null; b = b.PrevPhysical)
        {
            if (!b.IsFree && b.Offset == offset)
            {
                _granularityHandler.FreePages(b.Offset, b.Size);
                FreeBlock(b);
                return;
            }
        }

        throw new InvalidOperationException("Allocation not found!");
    }

    public void Clear()
    {
        _allocCount = 0;
        _blocksFreeCount = 0;
        _blocksFreeSize = 0;
        _isFreeBitmap = 0;
        _granularityHandler.Clear();
        Init(Size);
    }

    public IEnumerable<IAllocation> GetAllocations()
    {
        for (var b = _nullBlock.PrevPhysical; b != null; b = b.PrevPhysical)
        {
            if (!b.IsFree && b.UserData is IAllocation alloc)
            {
                yield return alloc;
            }
        }
    }

    public void SetAllocationUserData(IAllocation from, IAllocation to)
    {
        for (var b = _nullBlock.PrevPhysical; b != null; b = b.PrevPhysical)
        {
            if (!b.IsFree && ReferenceEquals(b.UserData, from))
            {
                b.UserData = to;
                return;
            }
        }

        throw new InvalidOperationException("Allocation not found!");
    }

    public void SetAllocationUserData(long offset, IAllocation to)
    {
        for (var b = _nullBlock.PrevPhysical; b != null; b = b.PrevPhysical)
        {
            if (!b.IsFree && b.Offset == offset)
            {
                b.UserData = to;
                return;
            }
        }

        throw new InvalidOperationException("Allocation not found!");
    }

    public long GetNextFreeRegionSize(IAllocation allocation)
    {
        for (var b = _nullBlock.PrevPhysical; b != null; b = b.PrevPhysical)
        {
            if (!b.IsFree && ReferenceEquals(b.UserData, allocation))
            {
                var next = b.NextPhysical;
                return next != null && next.IsFree ? next.Size : 0;
            }
        }

        return 0;
    }

    public Result CheckCorruption(nuint blockDataPointer)
        => Helpers.CheckBlockCorruption(blockDataPointer, Size, DebugMargin, GetAllocations());

    private static SuballocationType GetBlockType(Block b) =>
        b.IsFree ? SuballocationType.Free : ((IAllocation)b.UserData!).SuballocationType;

    private void FreeBlock(Block block)
    {
        var next = block.NextPhysical!;

        Debug.Assert(!block.IsFree, "Block is already free!");

        var prev = block.PrevPhysical;

        if (prev != null && prev.IsFree)
        {
            RemoveFreeBlock(prev);
            MergeBlock(block, prev);
        }

        if (!next.IsFree)
        {
            InsertFreeBlock(block);
        }
        else if (next == _nullBlock)
        {
            MergeBlock(_nullBlock, block);
        }
        else
        {
            RemoveFreeBlock(next);
            MergeBlock(next, block);
            InsertFreeBlock(next);
        }

        _allocCount -= 1;
    }

    private static int BitScanMSB(long mask)
    {
        if (mask == 0)
        {
            return 255;
        }

        return 63 - BitOps.LeadingZeroCount((ulong)mask);
    }

    private static int BitScanLSB(long mask)
    {
        if (mask == 0)
        {
            return 255;
        }

        return BitOps.TrailingZeroCount((ulong)mask);
    }

    private static byte SizeToMemoryClass(long size)
    {
        if (size > SMALL_BUFFER_SIZE)
        {
            return (byte)(BitScanMSB(size) - MEMORY_CLASS_SHIFT);
        }

        return 0;
    }

    private static ushort SizeToSecondIndex(long size, byte memoryClass)
    {
        if (memoryClass == 0)
        {
            return (ushort)((size - 1) / 64);
        }

        return (ushort)((size >> (memoryClass + MEMORY_CLASS_SHIFT - SECOND_LEVEL_INDEX)) ^ (1 << SECOND_LEVEL_INDEX));
    }

    private static uint GetListIndex(byte memoryClass, ushort secondIndex)
    {
        if (memoryClass == 0)
        {
            return secondIndex;
        }

        var index = (uint)(memoryClass - 1) * (1u << SECOND_LEVEL_INDEX) + secondIndex;

        return index + 4;
    }

    private static uint GetListIndex(long size)
    {
        var mc = SizeToMemoryClass(size);

        return GetListIndex(mc, SizeToSecondIndex(size, mc));
    }

    private void RemoveFreeBlock(Block block)
    {
        Debug.Assert(block != _nullBlock, "Cannot remove null block from free list!");
        Debug.Assert(block.IsFree, "Block is already taken!");

        if (block.NextFree != null)
        {
            block.NextFree.PrevFree = block.PrevFree;
        }

        if (block.PrevFree != null)
        {
            block.PrevFree.NextFree = block.NextFree;
        }
        else
        {
            var memClass = SizeToMemoryClass(block.Size);
            var secondIndex = SizeToSecondIndex(block.Size, memClass);
            var index = GetListIndex(memClass, secondIndex);

            Debug.Assert(_freeList[index] == block);

            _freeList[index] = block.NextFree;

            if (block.NextFree == null)
            {
                _innerIsFreeBitmap[memClass] &= ~(1u << secondIndex);

                if (_innerIsFreeBitmap[memClass] == 0)
                {
                    _isFreeBitmap &= memClass >= 32 ? 0 : ~(1u << memClass);
                }
            }
        }

        block.IsFree = false;
        block.UserData = null;
        _blocksFreeCount -= 1;
        _blocksFreeSize -= block.Size;
    }

    private void InsertFreeBlock(Block block)
    {
        Debug.Assert(block != _nullBlock, "Cannot insert null block into free list!");
        Debug.Assert(!block.IsFree, "Cannot insert block twice!");

        var memClass = SizeToMemoryClass(block.Size);
        var secondIndex = SizeToSecondIndex(block.Size, memClass);
        var index = GetListIndex(memClass, secondIndex);

        Debug.Assert(index < _listsCount);

        block.IsFree = true;
        block.UserData = null;
        block.PrevFree = null;
        block.NextFree = _freeList[index];
        _freeList[index] = block;

        if (block.NextFree != null)
        {
            block.NextFree.PrevFree = block;
        }
        else
        {
            _innerIsFreeBitmap[memClass] |= 1u << secondIndex;

            if (memClass < 32)
            {
                _isFreeBitmap |= 1u << memClass;
            }
        }

        _blocksFreeCount += 1;
        _blocksFreeSize += block.Size;
    }

    private static void MergeBlock(Block absorber, Block absorbed)
    {
        Debug.Assert(absorber.PrevPhysical == absorbed, "Cannot merge separate physical regions!");
        Debug.Assert(!absorbed.IsFree, "Cannot merge block that belongs to free list!");

        absorber.Offset = absorbed.Offset;
        absorber.Size += absorbed.Size;
        absorber.PrevPhysical = absorbed.PrevPhysical;

        if (absorber.PrevPhysical != null)
        {
            absorber.PrevPhysical.NextPhysical = absorber;
        }
    }

    private Block? FindFreeBlock(long size, out uint listIndex)
    {
        listIndex = (uint)_listsCount;

        var memoryClass = SizeToMemoryClass(size);
        var innerFreeMap = _innerIsFreeBitmap[memoryClass] & (~0u << SizeToSecondIndex(size, memoryClass));

        if (innerFreeMap == 0)
        {
            uint freeMap;

            if (memoryClass + 1 >= 32)
            {
                freeMap = 0;
            }
            else
            {
                freeMap = _isFreeBitmap & (~0u << (memoryClass + 1));
            }

            if (freeMap == 0)
            {
                return null;
            }

            memoryClass = (byte)BitScanLSB(freeMap);
            innerFreeMap = _innerIsFreeBitmap[memoryClass];
        }

        listIndex = GetListIndex(memoryClass, (ushort)BitScanLSB(innerFreeMap));

        return _freeList[listIndex];
    }

    private bool CheckBlock(Block block,
                            uint listIndex,
                            in AllocationContext context,
                            ref AllocationRequest request)
    {
        if (!block.IsFree)
        {
            return false;
        }

        var allocSize = context.AllocationSize + DebugMargin;
        var alignedOffset = Helpers.AlignUp(block.Offset, context.AllocationAlignment);

        if (block.Size < allocSize + alignedOffset - block.Offset)
        {
            return false;
        }

        if (_granularityHandler.CheckConflictAndAlignUp(ref alignedOffset, allocSize, block.Offset, block.Size, context.SuballocationType))
        {
            return false;
        }

        var paddingBegin = alignedOffset - block.Offset;

        if (paddingBegin + allocSize > block.Size)
        {
            return false;
        }

        request.Type = AllocationRequestType.Normal;
        request.Item = block;
        request.Offset = alignedOffset;
        request.CustomData = context.SuballocationType;
        request.SumFreeSize = 0;
        request.SumItemSize = 0;

        if (listIndex != _listsCount && block.PrevFree != null)
        {
            block.PrevFree.NextFree = block.NextFree;

            block.NextFree?.PrevFree = block.PrevFree;

            block.PrevFree = null;
            block.NextFree = _freeList[listIndex];
            _freeList[listIndex] = block;

            block.NextFree?.PrevFree = block;
        }

        return true;
    }
}
