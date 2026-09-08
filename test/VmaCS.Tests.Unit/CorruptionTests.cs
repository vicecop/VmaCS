using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class CorruptionTests : IDisposable
{
    private readonly VulkanContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    private static (IBlockMetadata meta, long offset, long size) AllocateOne(
        Func<long, IBlockMetadata> create, long blockSize, long margin, VulkanMemoryAllocator allocator)
    {
        var meta = create(blockSize);
        meta.BufferImageGranularity = margin;

        var ctx = new AllocationContext
        {
            BufferImageGranularity = margin,
            AllocationSize = 128,
            AllocationAlignment = 1,
            Strategy = 0,
            SuballocationType = SuballocationType.Buffer
        };
        Assert.True(meta.TryCreateAllocationRequest(in ctx, out var req));

        var alloc = new BlockAllocation(allocator);
        meta.Alloc(in req, SuballocationType.Buffer, 128, alloc);
        alloc.InitBlockAllocation(null, req.Offset, 1, 128, 0, SuballocationType.Buffer, false);

        return (meta, req.Offset, 128);
    }

    private static byte[] FillWithMagic(long blockSize)
    {
        var buf = new byte[blockSize];
        var magic = BitConverter.GetBytes(Helpers.CORRUPTION_DETECTION_MAGIC_VALUE);

        for (long k = 0; k < blockSize; ++k)
        {
            buf[k] = magic[k & 3];
        }

        return buf;
    }

    [Theory]
    [InlineData("tlsf")]
    [InlineData("linear")]
    public void CheckCorruption_DetectsMarginOverrun(string kind)
    {
        const long blockSize = 1024;
        const long margin = 64;

        Func<long, IBlockMetadata> create = kind switch
        {
            "tlsf" => s => new BlockMetadataTlsf(s, margin),
            "linear" => s => new BlockMetadataLinear(s, margin),
            _ => throw new ArgumentException(kind)
        };

        var (meta, offset, size) = AllocateOne(create, blockSize, margin, _ctx.Allocator);
        var buf = FillWithMagic(blockSize);

        // Valid: the trailing margin (VMA_DEBUG_MARGIN bytes after the allocation) is all magic.
        unsafe
        {
            fixed (byte* p = buf)
            {
                Assert.Equal(Result.Success, meta.CheckCorruption((nuint)p));
            }
        }

        // Overrun the allocation by one byte into its trailing margin.
        buf[offset + size] = (byte)(buf[offset + size] ^ 0xFF);

        unsafe
        {
            fixed (byte* p = buf)
            {
                Assert.Equal(Result.ErrorUnknown, meta.CheckCorruption((nuint)p));
            }
        }
    }

    [Theory]
    [InlineData("tlsf")]
    [InlineData("linear")]
    public void CheckCorruption_NoCorruptionWhenMarginsIntact(string kind)
    {
        const long blockSize = 1024;
        const long margin = 64;

        Func<long, IBlockMetadata> create = kind switch
        {
            "tlsf" => s => new BlockMetadataTlsf(s, margin),
            "linear" => s => new BlockMetadataLinear(s, margin),
            _ => throw new ArgumentException(kind)
        };

        var (meta, _, _) = AllocateOne(create, blockSize, margin, _ctx.Allocator);
        var buf = FillWithMagic(blockSize);

        unsafe
        {
            fixed (byte* p = buf)
            {
                Assert.Equal(Result.Success, meta.CheckCorruption((nuint)p));
            }
        }
    }

    [Fact]
    public void CheckCorruption_DisabledByDefaultReturnsFeatureNotPresent()
    {
        // Mirrors C++ VmaBlockVector::CheckCorruption: when corruption detection is disabled
        // (default), the public API returns VK_ERROR_FEATURE_NOT_PRESENT, not VK_SUCCESS.
        Assert.False(_ctx.Allocator.DebugDetectCorruption);

        var result = _ctx.Allocator.CheckCorruption(0);

        Assert.Equal(Result.ErrorFeatureNotPresent, result);
    }
}
