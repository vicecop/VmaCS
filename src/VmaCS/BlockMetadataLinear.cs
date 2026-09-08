using System.Diagnostics;
using Silk.NET.Vulkan;

namespace VmaCS;

internal sealed class BlockMetadataLinear : IBlockMetadata
{
    public long DebugMargin { get; set; }

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

    private const int COMPACT_THRESHOLD = 32;

    private readonly List<Suballocation> _suballocations0 = new();
    private readonly List<Suballocation> _suballocations1 = new();

    private int _1stVectorIndex;
    private SecondVectorMode _2ndVectorMode;

    private int _1stNullItemsBeginCount;
    private int _1stNullItemsMiddleCount;
    private int _2ndNullItemsCount;

    private long _sumFreeSize;

    public long Size { get; private set; }

    public BlockMetadataLinear(long blockSize, long debugMargin)
    {
        Debug.Assert(blockSize > 0);

        Size = blockSize;
        DebugMargin = debugMargin;
        _sumFreeSize = blockSize;
        _granularityHandler = new BlockBufferImageGranularity(BufferImageGranularity);
        _granularityHandler.Init(blockSize);
    }

    private List<Suballocation> _suballocations1st =>
        _1stVectorIndex != 0 ? _suballocations1 : _suballocations0;

    private List<Suballocation> _suballocations2nd =>
        _1stVectorIndex != 0 ? _suballocations0 : _suballocations1;

    public int AllocationCount =>
        _suballocations1st.Count - _1stNullItemsBeginCount - _1stNullItemsMiddleCount +
        _suballocations2nd.Count - _2ndNullItemsCount;

    public long SumFreeSize => _sumFreeSize;

    public long UnusedRangeSizeMax
    {
        get
        {
            CalcFreeRanges(out _, out _, out var sizeMax);

            return sizeMax;
        }
    }

    public bool IsEmpty => AllocationCount == 0;

    public bool TryCreateAllocationRequest(in AllocationContext context, out AllocationRequest request)
    {
        request = default;

        request.Type = AllocationRequestType.Normal;
        request.Item = this;
        request.SumItemSize = 0;

        if (context.AllocationSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context.AllocationSize));
        }

        if (context.SuballocationType == SuballocationType.Free)
        {
            throw new ArgumentException("Invalid Allocation Type", nameof(context.SuballocationType));
        }

        if (context.BufferImageGranularity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(context.BufferImageGranularity));
        }

        if (context.AllocationSize > Size)
        {
            return false;
        }

        return context.UpperAddress
            ? CreateAllocationRequestUpperAddress(in context, out request)
            : CreateAllocationRequestLowerAddress(in context, out request);
    }

    public void Clear()
    {
        _sumFreeSize = Size;
        _suballocations0.Clear();
        _suballocations1.Clear();
        _1stVectorIndex = 0;
        _2ndVectorMode = SecondVectorMode.Empty;
        _1stNullItemsBeginCount = 0;
        _1stNullItemsMiddleCount = 0;
        _2ndNullItemsCount = 0;
        _granularityHandler.Clear();
    }

    public void Alloc(in AllocationRequest request,
                      SuballocationType type,
                      long allocSize,
                      IAllocation allocation)
    {
        Debug.Assert(type != SuballocationType.Free);
        Debug.Assert(allocSize > 0);
        Debug.Assert(allocation != null);
        Debug.Assert(request.Offset >= 0);

        var offset = request.Offset;
        var newSuballoc = new Suballocation
        {
            Offset = offset,
            Size = allocSize,
            Type = type,
            Allocation = allocation
        };

        switch (request.Type)
        {
            case AllocationRequestType.UpperAddress:
                {
                    Debug.Assert(_2ndVectorMode != SecondVectorMode.RingBuffer,
                        "Trying to use linear allocator as double stack while it was already used as ring buffer.");

                    var suballocations2nd = _suballocations2nd;
                    suballocations2nd.Add(newSuballoc);
                    _2ndVectorMode = SecondVectorMode.DoubleStack;
                    break;
                }
            case AllocationRequestType.EndOfList1:
                {
                    var suballocations1st = _suballocations1st;

                    Debug.Assert(suballocations1st.Count == 0 ||
                        offset >= suballocations1st[suballocations1st.Count - 1].Offset + suballocations1st[suballocations1st.Count - 1].Size);
                    Debug.Assert(offset + allocSize <= Size);

                    suballocations1st.Add(newSuballoc);
                    break;
                }
            case AllocationRequestType.EndOfList2:
                {
                    var suballocations1st = _suballocations1st;
                    Debug.Assert(suballocations1st.Count == 0 ||
                        offset + allocSize <= suballocations1st[_1stNullItemsBeginCount].Offset);

                    var suballocations2nd = _suballocations2nd;

                    switch (_2ndVectorMode)
                    {
                        case SecondVectorMode.Empty:
                            Debug.Assert(suballocations2nd.Count == 0);
                            _2ndVectorMode = SecondVectorMode.RingBuffer;
                            break;

                        case SecondVectorMode.RingBuffer:
                            Debug.Assert(suballocations2nd.Count > 0);
                            break;

                        case SecondVectorMode.DoubleStack:
                            Debug.Assert(false, "Trying to use linear allocator as ring buffer while it was already used as double stack.");
                            break;
                    }

                    suballocations2nd.Add(newSuballoc);
                    break;
                }
            default:
                Debug.Assert(false, "CRITICAL INTERNAL ERROR.");
                break;
        }

        _sumFreeSize -= newSuballoc.Size;

        _granularityHandler.AllocPages(type, request.Offset, allocSize);
    }

    public void Free(IAllocation allocation)
    {
        foreach (var suballoc in _suballocations1st)
        {
            if (ReferenceEquals(suballoc.Allocation, allocation))
            {
                FreeInternal(suballoc.Offset);
                return;
            }
        }

        foreach (var suballoc in _suballocations2nd)
        {
            if (ReferenceEquals(suballoc.Allocation, allocation))
            {
                FreeInternal(suballoc.Offset);
                return;
            }
        }

        throw new InvalidOperationException("Allocation not found!");
    }

    public void FreeAtOffset(long offset) => FreeInternal(offset);

    public IEnumerable<IAllocation> GetAllocations()
    {
        var suballocations1st = _suballocations1st;
        for (int i = _1stNullItemsBeginCount, count = suballocations1st.Count; i < count; ++i)
        {
            var suballoc = suballocations1st[i];
            if (suballoc.Type != SuballocationType.Free && suballoc.Allocation is { } alloc)
            {
                yield return alloc;
            }
        }

        var suballocations2nd = _suballocations2nd;
        if (_2ndVectorMode == SecondVectorMode.DoubleStack)
        {
            for (var i = suballocations2nd.Count - 1; i >= 0; --i)
            {
                var suballoc = suballocations2nd[i];
                if (suballoc.Type != SuballocationType.Free && suballoc.Allocation is { } alloc)
                {
                    yield return alloc;
                }
            }
        }
        else
        {
            for (int i = 0, count = suballocations2nd.Count; i < count; ++i)
            {
                var suballoc = suballocations2nd[i];
                if (suballoc.Type != SuballocationType.Free && suballoc.Allocation is { } alloc)
                {
                    yield return alloc;
                }
            }
        }
    }

    public void SetAllocationUserData(IAllocation from, IAllocation to)
    {
        var suballocations1st = _suballocations1st;
        for (var i = _1stNullItemsBeginCount; i < suballocations1st.Count; ++i)
        {
            if (ReferenceEquals(suballocations1st[i].Allocation, from))
            {
                var s = suballocations1st[i];
                s.Allocation = to;
                suballocations1st[i] = s;
                return;
            }
        }

        var suballocations2nd = _suballocations2nd;
        for (var i = 0; i < suballocations2nd.Count; ++i)
        {
            if (ReferenceEquals(suballocations2nd[i].Allocation, from))
            {
                var s = suballocations2nd[i];
                s.Allocation = to;
                suballocations2nd[i] = s;
                return;
            }
        }

        throw new InvalidOperationException("Allocation not found!");
    }

    public void SetAllocationUserData(long offset, IAllocation to)
    {
        var suballocations1st = _suballocations1st;
        for (var i = _1stNullItemsBeginCount; i < suballocations1st.Count; ++i)
        {
            if (suballocations1st[i].Offset == offset && suballocations1st[i].Allocation is { })
            {
                var s = suballocations1st[i];
                s.Allocation = to;
                suballocations1st[i] = s;
                return;
            }
        }

        var suballocations2nd = _suballocations2nd;
        for (var i = 0; i < suballocations2nd.Count; ++i)
        {
            if (suballocations2nd[i].Offset == offset && suballocations2nd[i].Allocation is { })
            {
                var s = suballocations2nd[i];
                s.Allocation = to;
                suballocations2nd[i] = s;
                return;
            }
        }

        throw new InvalidOperationException("Allocation not found!");
    }

    public long GetNextFreeRegionSize(IAllocation allocation)
    {
        var suballocations1st = _suballocations1st;
        for (var i = _1stNullItemsBeginCount; i < suballocations1st.Count; ++i)
        {
            if (ReferenceEquals(suballocations1st[i].Allocation, allocation))
            {
                return NextFreeRegionSizeAfter(suballocations1st,
                                               i,
                                               suballocations1st[i].Offset + suballocations1st[i].Size,
                                               true);
            }
        }

        var suballocations2nd = _suballocations2nd;
        for (var i = 0; i < suballocations2nd.Count; ++i)
        {
            if (ReferenceEquals(suballocations2nd[i].Allocation, allocation))
            {
                return NextFreeRegionSizeAfter(suballocations2nd,
                                               i,
                                               suballocations2nd[i].Offset + suballocations2nd[i].Size,
                                               _2ndVectorMode == SecondVectorMode.RingBuffer);
            }
        }

        return 0;
    }

    public Result CheckCorruption(nuint blockDataPointer)
        => Helpers.CheckBlockCorruption(blockDataPointer, Size, DebugMargin, GetAllocations());

    public void Validate()
    {
        var suballocations1st = _suballocations1st;
        var suballocations2nd = _suballocations2nd;

        Helpers.Validate(suballocations2nd.Count == 0 || _2ndVectorMode != SecondVectorMode.Empty);
        Helpers.Validate(suballocations1st.Count > 0 ||
            suballocations2nd.Count == 0 ||
            _2ndVectorMode != SecondVectorMode.RingBuffer);

        if (suballocations1st.Count > 0)
        {
            Helpers.Validate(suballocations1st[_1stNullItemsBeginCount].Type != SuballocationType.Free);
            Helpers.Validate(suballocations1st[suballocations1st.Count - 1].Type != SuballocationType.Free);
        }

        if (suballocations2nd.Count > 0)
        {
            Helpers.Validate(suballocations2nd[suballocations2nd.Count - 1].Type != SuballocationType.Free);
        }

        Helpers.Validate(_1stNullItemsBeginCount + _1stNullItemsMiddleCount <= suballocations1st.Count);
        Helpers.Validate(_2ndNullItemsCount <= suballocations2nd.Count);

        long sumUsedSize = 0;
        long offset = 0;
        var debugMargin = DebugMargin;

        if (_2ndVectorMode == SecondVectorMode.RingBuffer)
        {
            var nullItem2ndCount = 0;
            for (var i = 0; i < suballocations2nd.Count; ++i)
            {
                var suballoc = suballocations2nd[i];
                var currFree = suballoc.Type == SuballocationType.Free;

                Helpers.Validate(suballoc.Offset >= offset);

                if (!currFree)
                {
                    Helpers.Validate(suballoc.Allocation!.Offset == suballoc.Offset);
                    Helpers.Validate(suballoc.Allocation.Size == suballoc.Size);
                    sumUsedSize += suballoc.Size;
                }
                else
                {
                    ++nullItem2ndCount;
                }

                offset = suballoc.Offset + suballoc.Size + debugMargin;
            }

            Helpers.Validate(nullItem2ndCount == _2ndNullItemsCount);
        }

        for (var i = 0; i < _1stNullItemsBeginCount; ++i)
        {
            Helpers.Validate(suballocations1st[i].Type == SuballocationType.Free &&
                suballocations1st[i].Allocation == null);
        }

        var nullItem1stCount = _1stNullItemsBeginCount;

        for (var i = _1stNullItemsBeginCount; i < suballocations1st.Count; ++i)
        {
            var suballoc = suballocations1st[i];
            var currFree = suballoc.Type == SuballocationType.Free;

            Helpers.Validate(suballoc.Offset >= offset);
            Helpers.Validate(i >= _1stNullItemsBeginCount || currFree);

            if (!currFree)
            {
                Helpers.Validate(suballoc.Allocation!.Offset == suballoc.Offset);
                Helpers.Validate(suballoc.Allocation.Size == suballoc.Size);
                sumUsedSize += suballoc.Size;
            }
            else
            {
                ++nullItem1stCount;
            }

            offset = suballoc.Offset + suballoc.Size + debugMargin;
        }

        Helpers.Validate(nullItem1stCount == _1stNullItemsBeginCount + _1stNullItemsMiddleCount);

        if (_2ndVectorMode == SecondVectorMode.DoubleStack)
        {
            var nullItem2ndCount = 0;
            for (var i = suballocations2nd.Count; i-- > 0;)
            {
                var suballoc = suballocations2nd[i];
                var currFree = suballoc.Type == SuballocationType.Free;

                Helpers.Validate(suballoc.Offset >= offset);

                if (!currFree)
                {
                    Helpers.Validate(suballoc.Allocation!.Offset == suballoc.Offset);
                    Helpers.Validate(suballoc.Allocation.Size == suballoc.Size);
                    sumUsedSize += suballoc.Size;
                }
                else
                {
                    ++nullItem2ndCount;
                }

                offset = suballoc.Offset + suballoc.Size + debugMargin;
            }

            Helpers.Validate(nullItem2ndCount == _2ndNullItemsCount);
        }

        Helpers.Validate(offset <= Size);
        Helpers.Validate(_sumFreeSize == Size - sumUsedSize);
    }

    public void CalcAllocationStatInfo(out StatInfo outInfo)
    {
        outInfo = default;
        StatInfo.Init(out outInfo);

        outInfo.BlockCount = 1;
        outInfo.UnusedBytes = _sumFreeSize;
        outInfo.UsedBytes = Size - _sumFreeSize;

        var rangeCount = 0;
        var sizeMin = long.MaxValue;
        long sizeMax = 0;
        long lastOffset = 0;
        long maxAllocSize = 0;
        var minAllocSize = long.MaxValue;
        long allocCount = 0;

        var suballocations1st = _suballocations1st;
        var suballocations2nd = _suballocations2nd;
        var size = Size;
        _ = DebugMargin;

        if (_2ndVectorMode == SecondVectorMode.RingBuffer)
        {
            var freeSpace2ndTo1stEnd = suballocations1st[_1stNullItemsBeginCount].Offset;
            var nextIndex = 0;
            var count2nd = suballocations2nd.Count;
            while (lastOffset < freeSpace2ndTo1stEnd)
            {
                while (nextIndex < count2nd && suballocations2nd[nextIndex].Type == SuballocationType.Free)
                {
                    ++nextIndex;
                }

                if (nextIndex < count2nd)
                {
                    var s = suballocations2nd[nextIndex];
                    if (lastOffset < s.Offset)
                    {
                        AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, s.Offset - lastOffset);
                    }

                    maxAllocSize = Math.Max(maxAllocSize, s.Size);
                    minAllocSize = Math.Min(minAllocSize, s.Size);
                    allocCount += 1;
                    lastOffset = s.Offset + s.Size;
                    ++nextIndex;
                }
                else
                {
                    if (lastOffset < freeSpace2ndTo1stEnd)
                    {
                        AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, freeSpace2ndTo1stEnd - lastOffset);
                    }

                    lastOffset = freeSpace2ndTo1stEnd;
                }
            }
        }

        var next1st = _1stNullItemsBeginCount;
        var count1st = suballocations1st.Count;
        var freeSpace1stTo2ndEnd = _2ndVectorMode == SecondVectorMode.DoubleStack
            ? suballocations2nd[suballocations2nd.Count - 1].Offset
            : size;

        while (lastOffset < freeSpace1stTo2ndEnd)
        {
            while (next1st < count1st && suballocations1st[next1st].Type == SuballocationType.Free)
            {
                ++next1st;
            }

            if (next1st < count1st)
            {
                var s = suballocations1st[next1st];
                if (lastOffset < s.Offset)
                {
                    AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, s.Offset - lastOffset);
                }

                maxAllocSize = Math.Max(maxAllocSize, s.Size);
                minAllocSize = Math.Min(minAllocSize, s.Size);
                allocCount += 1;
                lastOffset = s.Offset + s.Size;
                ++next1st;
            }
            else
            {
                if (lastOffset < freeSpace1stTo2ndEnd)
                {
                    AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, freeSpace1stTo2ndEnd - lastOffset);
                }

                lastOffset = freeSpace1stTo2ndEnd;
            }
        }

        if (_2ndVectorMode == SecondVectorMode.DoubleStack)
        {
            var next2nd = suballocations2nd.Count - 1;
            while (lastOffset < size)
            {
                while (next2nd != -1 && suballocations2nd[next2nd].Type == SuballocationType.Free)
                {
                    --next2nd;
                }

                if (next2nd != -1)
                {
                    var s = suballocations2nd[next2nd];
                    if (lastOffset < s.Offset)
                    {
                        AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, s.Offset - lastOffset);
                    }

                    maxAllocSize = Math.Max(maxAllocSize, s.Size);
                    minAllocSize = Math.Min(minAllocSize, s.Size);
                    allocCount += 1;
                    lastOffset = s.Offset + s.Size;
                    --next2nd;
                }
                else
                {
                    if (lastOffset < size)
                    {
                        AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, size - lastOffset);
                    }

                    lastOffset = size;
                }
            }
        }

        outInfo.AllocationCount = (int)allocCount;
        if (allocCount > 0)
        {
            outInfo.AllocationSizeMin = minAllocSize;
            outInfo.AllocationSizeMax = maxAllocSize;
        }

        outInfo.UnusedRangeCount = rangeCount;
        if (rangeCount > 0)
        {
            outInfo.UnusedRangeSizeMin = sizeMin;
            outInfo.UnusedRangeSizeMax = sizeMax;
        }
    }

    public void AddPoolStats(ref PoolStats stats)
    {
        CalcFreeRanges(out var unusedRangeCount, out _, out var unusedRangeSizeMax);

        stats.Size += Size;
        stats.UnusedSize += _sumFreeSize;
        stats.AllocationBytes += Size - _sumFreeSize;
        stats.AllocationCount += AllocationCount;
        stats.UnusedRangeCount += unusedRangeCount;

        if (unusedRangeSizeMax > stats.UnusedRangeSizeMax)
        {
            stats.UnusedRangeSizeMax = unusedRangeSizeMax;
        }

        foreach (var sub in _suballocations1st)
        {
            if (sub.Type == SuballocationType.Free)
            {
                continue;
            }

            var sz = sub.Allocation!.Size;

            if (sz < stats.AllocationSizeMin)
            {
                stats.AllocationSizeMin = sz;
            }

            if (sz > stats.AllocationSizeMax)
            {
                stats.AllocationSizeMax = sz;
            }
        }

        foreach (var sub in _suballocations2nd)
        {
            if (sub.Type == SuballocationType.Free)
            {
                continue;
            }

            var sz = sub.Allocation!.Size;

            if (sz < stats.AllocationSizeMin)
            {
                stats.AllocationSizeMin = sz;
            }

            if (sz > stats.AllocationSizeMax)
            {
                stats.AllocationSizeMax = sz;
            }
        }
    }

    public bool IsBufferImageGranularityConflictPossible(long bufferImageGranularity, ref SuballocationType type)
    {
        if (bufferImageGranularity == 1 || IsEmpty)
        {
            return false;
        }

        var minAlignment = long.MaxValue;
        var typeConflict = false;

        var suballocations1st = _suballocations1st;
        for (int i = _1stNullItemsBeginCount, count = suballocations1st.Count; i < count; ++i)
        {
            var suballoc = suballocations1st[i];
            if (suballoc.Type != SuballocationType.Free)
            {
                minAlignment = Math.Min(minAlignment, suballoc.Allocation!.Alignment);
                if (Helpers.IsBufferImageGranularityConflict(type, suballoc.Type))
                {
                    typeConflict = true;
                }

                type = suballoc.Type;
            }
        }

        var suballocations2nd = _suballocations2nd;
        for (int i = 0, count = suballocations2nd.Count; i < count; ++i)
        {
            var suballoc = suballocations2nd[i];
            if (suballoc.Type != SuballocationType.Free)
            {
                minAlignment = Math.Min(minAlignment, suballoc.Allocation!.Alignment);
                if (Helpers.IsBufferImageGranularityConflict(type, suballoc.Type))
                {
                    typeConflict = true;
                }

                type = suballoc.Type;
            }
        }

        return typeConflict || minAlignment >= bufferImageGranularity;
    }

    private void CalcFreeRanges(out int rangeCount, out long sizeMin, out long sizeMax)
    {
        rangeCount = 0;
        sizeMin = long.MaxValue;
        sizeMax = 0;

        long lastOffset = 0;

        var suballocations1st = _suballocations1st;
        var suballocations2nd = _suballocations2nd;
        var size = Size;

        if (_2ndVectorMode == SecondVectorMode.RingBuffer)
        {
            var freeSpace2ndTo1stEnd = suballocations1st[_1stNullItemsBeginCount].Offset;
            var nextIndex = 0;
            var count2nd = suballocations2nd.Count;
            while (lastOffset < freeSpace2ndTo1stEnd)
            {
                while (nextIndex < count2nd && suballocations2nd[nextIndex].Type == SuballocationType.Free)
                {
                    ++nextIndex;
                }

                if (nextIndex < count2nd)
                {
                    var s = suballocations2nd[nextIndex];
                    if (lastOffset < s.Offset)
                    {
                        AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, s.Offset - lastOffset);
                    }

                    lastOffset = s.Offset + s.Size;
                    ++nextIndex;
                }
                else
                {
                    if (lastOffset < freeSpace2ndTo1stEnd)
                    {
                        AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, freeSpace2ndTo1stEnd - lastOffset);
                    }

                    lastOffset = freeSpace2ndTo1stEnd;
                }
            }
        }

        var next1st = _1stNullItemsBeginCount;
        var count1st = suballocations1st.Count;
        var freeSpace1stTo2ndEnd = _2ndVectorMode == SecondVectorMode.DoubleStack
            ? suballocations2nd[suballocations2nd.Count - 1].Offset
            : size;

        while (lastOffset < freeSpace1stTo2ndEnd)
        {
            while (next1st < count1st && suballocations1st[next1st].Type == SuballocationType.Free)
            {
                ++next1st;
            }

            if (next1st < count1st)
            {
                var s = suballocations1st[next1st];
                if (lastOffset < s.Offset)
                {
                    AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, s.Offset - lastOffset);
                }

                lastOffset = s.Offset + s.Size;
                ++next1st;
            }
            else
            {
                if (lastOffset < freeSpace1stTo2ndEnd)
                {
                    AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, freeSpace1stTo2ndEnd - lastOffset);
                }

                lastOffset = freeSpace1stTo2ndEnd;
            }
        }

        if (_2ndVectorMode == SecondVectorMode.DoubleStack)
        {
            var next2nd = suballocations2nd.Count - 1;
            while (lastOffset < size)
            {
                while (next2nd != -1 && suballocations2nd[next2nd].Type == SuballocationType.Free)
                {
                    --next2nd;
                }

                if (next2nd != -1)
                {
                    var s = suballocations2nd[next2nd];
                    if (lastOffset < s.Offset)
                    {
                        AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, s.Offset - lastOffset);
                    }

                    lastOffset = s.Offset + s.Size;
                    --next2nd;
                }
                else
                {
                    if (lastOffset < size)
                    {
                        AddUnusedRange(ref rangeCount, ref sizeMin, ref sizeMax, size - lastOffset);
                    }

                    lastOffset = size;
                }
            }
        }
    }

    private void FreeInternal(long offset)
    {
        var suballocations1st = _suballocations1st;
        var suballocations2nd = _suballocations2nd;

        if (suballocations1st.Count > 0)
        {
            var firstSuballoc = suballocations1st[_1stNullItemsBeginCount];
            if (firstSuballoc.Offset == offset)
            {
                firstSuballoc.Type = SuballocationType.Free;
                firstSuballoc.Allocation = null;
                suballocations1st[_1stNullItemsBeginCount] = firstSuballoc;
                _sumFreeSize += firstSuballoc.Size;
                ++_1stNullItemsBeginCount;
                CleanupAfterFree();
                return;
            }
        }

        if (_2ndVectorMode == SecondVectorMode.RingBuffer || _2ndVectorMode == SecondVectorMode.DoubleStack)
        {
            if (suballocations2nd.Count > 0)
            {
                var lastSuballoc = suballocations2nd[suballocations2nd.Count - 1];
                if (lastSuballoc.Offset == offset)
                {
                    _sumFreeSize += lastSuballoc.Size;
                    suballocations2nd.RemoveAt(suballocations2nd.Count - 1);
                    CleanupAfterFree();
                    return;
                }
            }
        }
        else if (_2ndVectorMode == SecondVectorMode.Empty)
        {
            if (suballocations1st.Count > 0)
            {
                var lastSuballoc = suballocations1st[suballocations1st.Count - 1];
                if (lastSuballoc.Offset == offset)
                {
                    _sumFreeSize += lastSuballoc.Size;
                    suballocations1st.RemoveAt(suballocations1st.Count - 1);
                    CleanupAfterFree();
                    return;
                }
            }
        }

        {
            var it = BinaryFindByOffset(suballocations1st,
                                        _1stNullItemsBeginCount,
                                        suballocations1st.Count - 1,
                                        offset,
                                        true);
            if (it >= 0)
            {
                var s = suballocations1st[it];
                s.Type = SuballocationType.Free;
                s.Allocation = null;
                suballocations1st[it] = s;
                ++_1stNullItemsMiddleCount;
                _sumFreeSize += s.Size;
                CleanupAfterFree();
                return;
            }
        }

        if (_2ndVectorMode != SecondVectorMode.Empty)
        {
            var it = BinaryFindByOffset(suballocations2nd, 0, suballocations2nd.Count - 1, offset,
                _2ndVectorMode == SecondVectorMode.RingBuffer);
            if (it >= 0)
            {
                var s = suballocations2nd[it];
                s.Type = SuballocationType.Free;
                s.Allocation = null;
                suballocations2nd[it] = s;
                ++_2ndNullItemsCount;
                _sumFreeSize += s.Size;
                CleanupAfterFree();
                return;
            }
        }

        Debug.Assert(false, "Allocation to free not found in linear allocator!");
    }

    private void CleanupAfterFree()
    {
        var suballocations1st = _suballocations1st;
        var suballocations2nd = _suballocations2nd;

        if (IsEmpty)
        {
            suballocations1st.Clear();
            suballocations2nd.Clear();
            _1stNullItemsBeginCount = 0;
            _1stNullItemsMiddleCount = 0;
            _2ndNullItemsCount = 0;
            _2ndVectorMode = SecondVectorMode.Empty;
        }
        else
        {
            var suballoc1stCount = suballocations1st.Count;
            var nullItem1stCount = _1stNullItemsBeginCount + _1stNullItemsMiddleCount;
            Debug.Assert(nullItem1stCount <= suballoc1stCount);

            while (_1stNullItemsBeginCount < suballoc1stCount &&
                suballocations1st[_1stNullItemsBeginCount].Type == SuballocationType.Free)
            {
                ++_1stNullItemsBeginCount;
                --_1stNullItemsMiddleCount;
            }

            while (_1stNullItemsMiddleCount > 0
                && suballocations1st.Count > 0
                && suballocations1st[suballocations1st.Count - 1].Type == SuballocationType.Free)
            {
                --_1stNullItemsMiddleCount;
                suballocations1st.RemoveAt(suballocations1st.Count - 1);
            }

            while (_2ndNullItemsCount > 0
                && suballocations2nd.Count > 0
                && suballocations2nd[suballocations2nd.Count - 1].Type == SuballocationType.Free)
            {
                --_2ndNullItemsCount;
                suballocations2nd.RemoveAt(suballocations2nd.Count - 1);
            }

            while (_2ndNullItemsCount > 0
                && suballocations2nd.Count > 0
                && suballocations2nd[0].Type == SuballocationType.Free)
            {
                --_2ndNullItemsCount;
                suballocations2nd.RemoveAt(0);
            }

            if (ShouldCompact1st())
            {
                var nonNullItemCount = suballoc1stCount - nullItem1stCount;
                var srcIndex = _1stNullItemsBeginCount;
                for (var dstIndex = 0; dstIndex < nonNullItemCount; ++dstIndex)
                {
                    while (suballocations1st[srcIndex].Type == SuballocationType.Free)
                    {
                        ++srcIndex;
                    }

                    if (dstIndex != srcIndex)
                    {
                        suballocations1st[dstIndex] = suballocations1st[srcIndex];
                    }

                    ++srcIndex;
                }

                suballocations1st.RemoveRange(nonNullItemCount, suballocations1st.Count - nonNullItemCount);
                _1stNullItemsBeginCount = 0;
                _1stNullItemsMiddleCount = 0;
            }

            if (suballocations2nd.Count == 0)
            {
                _2ndVectorMode = SecondVectorMode.Empty;
            }

            if (suballocations1st.Count - _1stNullItemsBeginCount == 0)
            {
                suballocations1st.Clear();
                _1stNullItemsBeginCount = 0;

                if (suballocations2nd.Count > 0 && _2ndVectorMode == SecondVectorMode.RingBuffer)
                {
                    _2ndVectorMode = SecondVectorMode.Empty;
                    _1stNullItemsMiddleCount = _2ndNullItemsCount;
                    while (_1stNullItemsBeginCount < suballocations2nd.Count &&
                        suballocations2nd[_1stNullItemsBeginCount].Type == SuballocationType.Free)
                    {
                        ++_1stNullItemsBeginCount;
                        --_1stNullItemsMiddleCount;
                    }

                    _2ndNullItemsCount = 0;
                    _1stVectorIndex ^= 1;
                }
            }
        }

        DebugValidate();
    }

    private bool CreateAllocationRequestLowerAddress(in AllocationContext context, out AllocationRequest request)
    {
        request = default;

        var blockSize = Size;
        var debugMargin = DebugMargin;
        var bufferImageGranularity = context.BufferImageGranularity;
        var allocSize = context.AllocationSize;
        var allocAlignment = context.AllocationAlignment;
        var allocType = context.SuballocationType;

        _granularityHandler.RoundupAllocRequest(allocType, ref allocSize, ref allocAlignment);

        var suballocations1st = _suballocations1st;
        var suballocations2nd = _suballocations2nd;

        if (_2ndVectorMode == SecondVectorMode.Empty || _2ndVectorMode == SecondVectorMode.DoubleStack)
        {
            long resultBaseOffset = 0;
            if (suballocations1st.Count > 0)
            {
                var lastSuballoc = suballocations1st[suballocations1st.Count - 1];
                resultBaseOffset = lastSuballoc.Offset + lastSuballoc.Size + debugMargin;
            }

            var resultOffset = Helpers.AlignUp(resultBaseOffset, allocAlignment);

            if (bufferImageGranularity > 1 && bufferImageGranularity != allocAlignment && suballocations1st.Count > 0)
            {
                var bufferImageGranularityConflict = false;
                for (var prevSuballocIndex = suballocations1st.Count; prevSuballocIndex-- > 0;)
                {
                    var prevSuballoc = suballocations1st[prevSuballocIndex];
                    if (Helpers.BlocksOnSamePage(prevSuballoc.Offset, prevSuballoc.Size, resultOffset, bufferImageGranularity))
                    {
                        if (Helpers.IsBufferImageGranularityConflict(prevSuballoc.Type, allocType))
                        {
                            bufferImageGranularityConflict = true;
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                if (bufferImageGranularityConflict)
                {
                    resultOffset = Helpers.AlignUp(resultOffset, bufferImageGranularity);
                }
            }

            var freeSpaceEnd = _2ndVectorMode == SecondVectorMode.DoubleStack
                ? suballocations2nd[suballocations2nd.Count - 1].Offset
                : blockSize;

            if (resultOffset + allocSize + debugMargin <= freeSpaceEnd)
            {
                if ((allocSize % bufferImageGranularity != 0 || resultOffset % bufferImageGranularity != 0) &&
                    _2ndVectorMode == SecondVectorMode.DoubleStack)
                {
                    for (var nextSuballocIndex = suballocations2nd.Count; nextSuballocIndex-- > 0;)
                    {
                        var nextSuballoc = suballocations2nd[nextSuballocIndex];
                        if (Helpers.BlocksOnSamePage(resultOffset, allocSize, nextSuballoc.Offset, bufferImageGranularity))
                        {
                            if (Helpers.IsBufferImageGranularityConflict(allocType, nextSuballoc.Type))
                            {
                                return false;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                request.Offset = resultOffset;
                request.Type = AllocationRequestType.EndOfList1;
                return true;
            }
        }

        if (_2ndVectorMode == SecondVectorMode.Empty || _2ndVectorMode == SecondVectorMode.RingBuffer)
        {
            Debug.Assert(suballocations1st.Count > 0);

            long resultBaseOffset = 0;
            if (suballocations2nd.Count > 0)
            {
                var lastSuballoc = suballocations2nd[suballocations2nd.Count - 1];
                resultBaseOffset = lastSuballoc.Offset + lastSuballoc.Size + debugMargin;
            }

            var resultOffset = Helpers.AlignUp(resultBaseOffset, allocAlignment);

            if (bufferImageGranularity > 1 && bufferImageGranularity != allocAlignment && suballocations2nd.Count > 0)
            {
                var bufferImageGranularityConflict = false;
                for (var prevSuballocIndex = suballocations2nd.Count; prevSuballocIndex-- > 0;)
                {
                    var prevSuballoc = suballocations2nd[prevSuballocIndex];
                    if (Helpers.BlocksOnSamePage(prevSuballoc.Offset, prevSuballoc.Size, resultOffset, bufferImageGranularity))
                    {
                        if (Helpers.IsBufferImageGranularityConflict(prevSuballoc.Type, allocType))
                        {
                            bufferImageGranularityConflict = true;
                            break;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                if (bufferImageGranularityConflict)
                {
                    resultOffset = Helpers.AlignUp(resultOffset, bufferImageGranularity);
                }
            }

            var index1st = _1stNullItemsBeginCount;

            if ((index1st == suballocations1st.Count && resultOffset + allocSize + debugMargin <= blockSize) ||
                (index1st < suballocations1st.Count && resultOffset + allocSize + debugMargin <= suballocations1st[index1st].Offset))
            {
                if (allocSize % bufferImageGranularity != 0 || resultOffset % bufferImageGranularity != 0)
                {
                    for (var nextSuballocIndex = index1st; nextSuballocIndex < suballocations1st.Count; ++nextSuballocIndex)
                    {
                        var nextSuballoc = suballocations1st[nextSuballocIndex];
                        if (Helpers.BlocksOnSamePage(resultOffset, allocSize, nextSuballoc.Offset, bufferImageGranularity))
                        {
                            if (Helpers.IsBufferImageGranularityConflict(allocType, nextSuballoc.Type))
                            {
                                return false;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                request.Offset = resultOffset;
                request.Type = AllocationRequestType.EndOfList2;
                return true;
            }
        }

        return false;
    }

    private bool CreateAllocationRequestUpperAddress(in AllocationContext context, out AllocationRequest request)
    {
        request = default;

        var blockSize = Size;
        var bufferImageGranularity = context.BufferImageGranularity;
        var allocSize = context.AllocationSize;
        var allocAlignment = context.AllocationAlignment;
        var allocType = context.SuballocationType;

        _granularityHandler.RoundupAllocRequest(allocType, ref allocSize, ref allocAlignment);

        var suballocations1st = _suballocations1st;
        var suballocations2nd = _suballocations2nd;

        if (_2ndVectorMode == SecondVectorMode.RingBuffer)
        {
            Debug.Assert(false, "Trying to use pool with linear algorithm as double stack, while it is already being used as ring buffer.");
            return false;
        }

        if (allocSize > blockSize)
        {
            return false;
        }

        var resultBaseOffset = blockSize - allocSize;
        if (suballocations2nd.Count > 0)
        {
            var lastSuballoc = suballocations2nd[suballocations2nd.Count - 1];
            resultBaseOffset = lastSuballoc.Offset - allocSize;
            if (allocSize > lastSuballoc.Offset)
            {
                return false;
            }
        }

        var resultOffset = resultBaseOffset;

        var debugMargin = DebugMargin;
        if (debugMargin > 0)
        {
            if (resultOffset < debugMargin)
            {
                return false;
            }

            resultOffset -= debugMargin;
        }

        resultOffset = Helpers.AlignDown(resultOffset, allocAlignment);

        if (bufferImageGranularity > 1 && bufferImageGranularity != allocAlignment && suballocations2nd.Count > 0)
        {
            var bufferImageGranularityConflict = false;
            for (var nextSuballocIndex = suballocations2nd.Count; nextSuballocIndex-- > 0;)
            {
                var nextSuballoc = suballocations2nd[nextSuballocIndex];
                if (Helpers.BlocksOnSamePage(resultOffset, allocSize, nextSuballoc.Offset, bufferImageGranularity))
                {
                    if (Helpers.IsBufferImageGranularityConflict(nextSuballoc.Type, allocType))
                    {
                        bufferImageGranularityConflict = true;
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            if (bufferImageGranularityConflict)
            {
                resultOffset = Helpers.AlignDown(resultOffset, bufferImageGranularity);
            }
        }

        var endOf1st = suballocations1st.Count > 0
            ? suballocations1st[suballocations1st.Count - 1].Offset + suballocations1st[suballocations1st.Count - 1].Size
            : 0;

        if (endOf1st + debugMargin <= resultOffset)
        {
            if (bufferImageGranularity > 1)
            {
                for (var prevSuballocIndex = suballocations1st.Count; prevSuballocIndex-- > 0;)
                {
                    var prevSuballoc = suballocations1st[prevSuballocIndex];
                    if (Helpers.BlocksOnSamePage(prevSuballoc.Offset, prevSuballoc.Size, resultOffset, bufferImageGranularity))
                    {
                        if (Helpers.IsBufferImageGranularityConflict(allocType, prevSuballoc.Type))
                        {
                            return false;
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            request.Offset = resultOffset;
            request.Type = AllocationRequestType.UpperAddress;
            return true;
        }

        return false;
    }

    private static int BinaryFindByOffset(List<Suballocation> list,
                                          int start,
                                          int end,
                                          long offset,
                                          bool ascending)
    {
        while (start <= end)
        {
            var mid = start + ((end - start) >> 1);
            var midOffset = list[mid].Offset;
            var cmp = ascending ? midOffset.CompareTo(offset) : offset.CompareTo(midOffset);

            if (cmp == 0)
            {
                return mid;
            }

            if (cmp < 0)
            {
                start = mid + 1;
            }
            else
            {
                end = mid - 1;
            }
        }

        return -1;
    }

    private static void AddUnusedRange(ref int rangeCount,
                                       ref long sizeMin,
                                       ref long sizeMax,
                                       long size)
    {
        rangeCount += 1;

        if (size < sizeMin)
        {
            sizeMin = size;
        }

        if (size > sizeMax)
        {
            sizeMax = size;
        }
    }

    private long NextFreeRegionSizeAfter(List<Suballocation> list,
                                         int index,
                                         long nextOffset,
                                         bool ascending)
    {
        if (ascending)
        {
            for (var i = index + 1; i < list.Count; ++i)
            {
                if (list[i].Type != SuballocationType.Free)
                {
                    return list[i].Offset - nextOffset;
                }
            }
        }
        else
        {
            for (var i = index - 1; i >= 0; --i)
            {
                if (list[i].Type != SuballocationType.Free)
                {
                    return list[i].Offset - nextOffset;
                }
            }
        }

        return Size - nextOffset;
    }

    private bool ShouldCompact1st()
    {
        var nullItemCount = _1stNullItemsBeginCount + _1stNullItemsMiddleCount;
        var suballocCount = _suballocations1st.Count;
        return suballocCount > COMPACT_THRESHOLD && nullItemCount * 2 >= (suballocCount - nullItemCount) * 3;
    }

    [Conditional("DEBUG")]
    private void DebugValidate() => Validate();

    private enum SecondVectorMode
    {
        Empty,
        RingBuffer,
        DoubleStack
    }
}
