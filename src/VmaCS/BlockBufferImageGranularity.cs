namespace VmaCS;

internal class BlockBufferImageGranularity
{
    private const uint MAX_LOW_BUFFER_IMAGE_GRANULARITY = 256;

    private readonly long _bufferImageGranularity;
    private uint _regionCount;
    private RegionInfo[] _regionInfo = [];

    public BlockBufferImageGranularity(long bufferImageGranularity)
    {
        _bufferImageGranularity = bufferImageGranularity;
    }

    public void Init(long size)
    {
        if (IsEnabled())
        {
            _regionCount = (uint)((size + _bufferImageGranularity - 1) / _bufferImageGranularity);
            _regionInfo = new RegionInfo[_regionCount];
        }
    }

    public void Destroy()
    {
        _regionInfo = [];
        _regionCount = 0;
    }

    public void RoundupAllocRequest(SuballocationType allocType, ref long inOutAllocSize, ref long inOutAllocAlignment)
    {
        if (_bufferImageGranularity > 1 && _bufferImageGranularity <= MAX_LOW_BUFFER_IMAGE_GRANULARITY)
        {
            if (allocType == SuballocationType.Unknown ||
                allocType == SuballocationType.ImageUnknown ||
                allocType == SuballocationType.ImageOptimal)
            {
                inOutAllocAlignment = Math.Max(inOutAllocAlignment, _bufferImageGranularity);
                inOutAllocSize = AlignUp(inOutAllocSize, _bufferImageGranularity);
            }
        }
    }

    public bool CheckConflictAndAlignUp(ref long inOutAllocOffset, long allocSize, long blockOffset, long blockSize, SuballocationType allocType)
    {
        if (!IsEnabled())
        {
            return false;
        }

        var startPage = GetStartPage(inOutAllocOffset);

        if (_regionInfo[startPage].AllocCount > 0 &&
            Helpers.IsBufferImageGranularityConflict((SuballocationType)_regionInfo[startPage].AllocType, allocType))
        {
            inOutAllocOffset = AlignUp(inOutAllocOffset, _bufferImageGranularity);
            if (blockSize < allocSize + inOutAllocOffset - blockOffset)
            {
                return true;
            }

            startPage = GetStartPage(inOutAllocOffset);
            if (_regionInfo[startPage].AllocCount > 0 &&
                Helpers.IsBufferImageGranularityConflict((SuballocationType)_regionInfo[startPage].AllocType, allocType))
            {
                return true;
            }
        }

        var endPage = GetEndPage(inOutAllocOffset, allocSize);

        if (endPage != startPage &&
            _regionInfo[endPage].AllocCount > 0 &&
            Helpers.IsBufferImageGranularityConflict((SuballocationType)_regionInfo[endPage].AllocType, allocType))
        {
            return true;
        }

        return false;
    }

    public void AllocPages(SuballocationType allocType, long offset, long size)
    {
        if (!IsEnabled())
        {
            return;
        }


        var startPage = GetStartPage(offset);
        AllocPage(startPage, allocType);

        var endPage = GetEndPage(offset, size);
        if (startPage != endPage)
        {

            AllocPage(endPage, allocType);
        }

    }

    public void FreePages(long offset, long size)
    {
        if (!IsEnabled())
        {
            return;
        }

        var startPage = GetStartPage(offset);
        --_regionInfo[startPage].AllocCount;

        if (_regionInfo[startPage].AllocCount == 0)
        {
            _regionInfo[startPage].AllocType = (byte)SuballocationType.Free;
        }

        var endPage = GetEndPage(offset, size);

        if (startPage != endPage)
        {
            --_regionInfo[endPage].AllocCount;
            if (_regionInfo[endPage].AllocCount == 0)
            {
                _regionInfo[endPage].AllocType = (byte)SuballocationType.Free;
            }

        }
    }

    public void Clear()
    {
        if (_regionInfo.Length > 0)
        {
            Array.Clear(_regionInfo, 0, _regionInfo.Length);
        }
    }

    private bool IsEnabled() => _bufferImageGranularity > MAX_LOW_BUFFER_IMAGE_GRANULARITY;

    private uint GetStartPage(long offset) => (uint)((offset & ~(_bufferImageGranularity - 1)) / _bufferImageGranularity);

    private uint GetEndPage(long offset, long size) => (uint)(((offset + size - 1) & ~(_bufferImageGranularity - 1)) / _bufferImageGranularity);

    private void AllocPage(uint pageIndex, SuballocationType allocType)
    {
        _regionInfo[pageIndex].AllocCount++;
        _regionInfo[pageIndex].AllocType = (byte)allocType;
    }

    private static long AlignUp(long value, long alignment) => (value + alignment - 1) / alignment * alignment;

    private struct RegionInfo
    {
        public byte AllocType;
        public ushort AllocCount;
    }
}
