namespace VmaCS.Tests.Unit;

public class BlockBufferImageGranularityTests : IDisposable
{
    private const long DEFAULT_BLOCK_SIZE = 4096;

    public void Dispose()
    {
    }

    private static BlockBufferImageGranularity Create(long granularity)
    {
        var handler = new BlockBufferImageGranularity(granularity);
        handler.Init(DEFAULT_BLOCK_SIZE);
        return handler;
    }

    [Fact]
    public void RoundupAllocRequest_SmallGranularity_UnknownType_RoundsUp()
    {
        var handler = Create(64);

        var size = 128L;
        var alignment = 4L;
        handler.RoundupAllocRequest(SuballocationType.Unknown, ref size, ref alignment);

        Assert.Equal(128, size);
        Assert.Equal(64, alignment);
    }

    [Fact]
    public void SuballocationType_Values_AreAsExpected()
    {
        Assert.Equal(0, (int)SuballocationType.Free);
        Assert.Equal(1, (int)SuballocationType.Unknown);
        Assert.Equal(2, (int)SuballocationType.Buffer);
        Assert.Equal(3, (int)SuballocationType.ImageUnknown);
        Assert.Equal(4, (int)SuballocationType.ImageLinear);
        Assert.Equal(5, (int)SuballocationType.ImageOptimal);
    }

    [Fact]
    public void RoundupAllocRequest_SmallGranularity_FreeType_NotRounded()
    {
        var handler = Create(64);

        var size = 128L;
        var alignment = 4L;
        handler.RoundupAllocRequest(SuballocationType.Free, ref size, ref alignment);

        Assert.Equal(128, size);
        Assert.Equal(4, alignment);
    }

    [Fact]
    public void RoundupAllocRequest_LargeGranularity_NoOp()
    {
        var handler = Create(4096);

        var size = 128L;
        var alignment = 4L;
        handler.RoundupAllocRequest(SuballocationType.ImageOptimal, ref size, ref alignment);

        Assert.Equal(128, size);
        Assert.Equal(4, alignment);
    }

    [Fact]
    public void CheckConflictAndAlignUp_DisabledGranularity_ReturnsFalse()
    {
        var handler = Create(64);

        long offset = 0;
        var result = handler.CheckConflictAndAlignUp(ref offset, 128, 0, DEFAULT_BLOCK_SIZE, SuballocationType.Buffer);

        Assert.False(result);
        Assert.Equal(0, offset);
    }

    [Fact]
    public void CheckConflictAndAlignUp_EnabledNoConflict_ReturnsFalse()
    {
        var handler = Create(4096);

        long offset = 0;
        var result = handler.CheckConflictAndAlignUp(ref offset, 128, 0, DEFAULT_BLOCK_SIZE, SuballocationType.Buffer);

        Assert.False(result);
        Assert.Equal(0, offset);
    }

    [Fact]
    public void CheckConflictAndAlignUp_StartPageConflict_ReturnsTrue()
    {
        var handler = Create(4096);

        handler.AllocPages(SuballocationType.Buffer, 0, 4096);

        long offset = 0;
        var result = handler.CheckConflictAndAlignUp(ref offset, 128, 0, DEFAULT_BLOCK_SIZE, SuballocationType.ImageOptimal);

        Assert.True(result);
    }

    [Fact]
    public void AllocPages_SinglePage_Succeeds()
    {
        var handler = Create(4096);

        handler.AllocPages(SuballocationType.Buffer, 0, 128);
        handler.FreePages(0, 128);
        handler.Clear();
    }

    [Fact]
    public void AllocPages_CrossesPageBoundary_Succeeds()
    {
        var handler = Create(8192);

        handler.AllocPages(SuballocationType.ImageOptimal, 4096 - 32, 128);
        handler.FreePages(4096 - 32, 128);
        handler.Clear();
    }

    [Fact]
    public void Clear_ResetsState()
    {
        var handler = Create(4096);

        handler.AllocPages(SuballocationType.Buffer, 0, 4096);
        handler.Clear();

        handler.AllocPages(SuballocationType.Buffer, 0, 128);
        handler.Clear();
    }

    [Theory]
    [InlineData("tlsf")]
    [InlineData("linear")]
    public void Metadata_AllocationRespectsGranularity(string kind)
    {
        const long blockSize = 16384;
        const long granularity = 4096;

        Func<long, IBlockMetadata> create = kind switch
        {
            "tlsf" => s => new BlockMetadataTlsf(s, 0),
            "linear" => s => new BlockMetadataLinear(s, 0),
            _ => throw new ArgumentException(kind)
        };

        var meta = create(blockSize);
        meta.BufferImageGranularity = granularity;

        var ctx1 = new AllocationContext
        {
            BufferImageGranularity = granularity,
            AllocationSize = 128,
            AllocationAlignment = 1,
            Strategy = 0,
            SuballocationType = SuballocationType.Buffer
        };
        Assert.True(meta.TryCreateAllocationRequest(in ctx1, out var req1));
        var alloc1 = new BlockAllocation(null!);
        meta.Alloc(in req1, SuballocationType.Buffer, 128, alloc1);

        var ctx2 = new AllocationContext
        {
            BufferImageGranularity = granularity,
            AllocationSize = 128,
            AllocationAlignment = 1,
            Strategy = 0,
            SuballocationType = SuballocationType.ImageOptimal
        };
        Assert.True(meta.TryCreateAllocationRequest(in ctx2, out var req2));
        var alloc2 = new BlockAllocation(null!);
        meta.Alloc(in req2, SuballocationType.ImageOptimal, 128, alloc2);

        Assert.True(req2.Offset >= 4096, $"Expected offset >= 4096 due to granularity, got {req2.Offset}");
    }

    [Theory]
    [InlineData("tlsf")]
    [InlineData("linear")]
    public void Metadata_FreeAndReallocateRespectsGranularity(string kind)
    {
        const long blockSize = 16384;
        const long granularity = 4096;

        Action<IBlockMetadata, long> setGranularity = kind switch
        {
            "tlsf" => (m, g) => ((BlockMetadataTlsf)m).BufferImageGranularity = g,
            "linear" => (m, g) => ((BlockMetadataLinear)m).BufferImageGranularity = g,
            _ => throw new ArgumentException(kind)
        };

        var meta = kind switch
        {
            "tlsf" => (IBlockMetadata)new BlockMetadataTlsf(blockSize, 0),
            "linear" => new BlockMetadataLinear(blockSize, 0),
            _ => throw new ArgumentException(kind)
        };
        setGranularity(meta, granularity);

        var ctx1 = new AllocationContext
        {
            BufferImageGranularity = granularity,
            AllocationSize = 128,
            AllocationAlignment = 1,
            Strategy = 0,
            SuballocationType = SuballocationType.Buffer
        };
        Assert.True(meta.TryCreateAllocationRequest(in ctx1, out var req1));
        var alloc1 = new BlockAllocation(null!);
        alloc1.InitBlockAllocation(null, req1.Offset, 1, 128, 0, SuballocationType.Buffer, false);
        meta.Alloc(in req1, SuballocationType.Buffer, 128, alloc1);

        var ctx2 = new AllocationContext
        {
            BufferImageGranularity = granularity,
            AllocationSize = 128,
            AllocationAlignment = 1,
            Strategy = 0,
            SuballocationType = SuballocationType.ImageOptimal
        };
        Assert.True(meta.TryCreateAllocationRequest(in ctx2, out var req2));
        var alloc2 = new BlockAllocation(null!);
        alloc2.InitBlockAllocation(null, req2.Offset, granularity, 128, 0, SuballocationType.ImageOptimal, false);
        meta.Alloc(in req2, SuballocationType.ImageOptimal, 128, alloc2);

        meta.Free(alloc2);

        var ctx3 = new AllocationContext
        {
            BufferImageGranularity = granularity,
            AllocationSize = 128,
            AllocationAlignment = 1,
            Strategy = 0,
            SuballocationType = SuballocationType.Buffer
        };
        Assert.True(meta.TryCreateAllocationRequest(in ctx3, out var req3));
        Assert.True(req3.Offset >= 128);
    }
}
