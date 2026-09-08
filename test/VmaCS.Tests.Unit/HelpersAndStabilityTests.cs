using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class HelpersTests
{
    [Fact]
    public void PrevPow_ReturnsZeroForZero()
    {
        Assert.Equal(0, Helpers.PrevPow(0));
        Assert.Equal(0L, Helpers.PrevPow(0L));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(5, 4)]
    [InlineData(8, 8)]
    [InlineData(1023, 512)]
    [InlineData(1024, 1024)]
    [InlineData(int.MaxValue, 1 << 30)]
    public void PrevPow_ReturnsLargestPowerOfTwoNotExceeding(int value, int expected) => Assert.Equal(expected, Helpers.PrevPow(value));

    [Theory]
    [InlineData(1L, 1L)]
    [InlineData(5L, 4L)]
    [InlineData(1000L, 512L)]
    [InlineData(1024L, 1024L)]
    [InlineData(long.MaxValue, 1L << 62)]
    public void PrevPowLong_ReturnsLargestPowerOfTwoNotExceeding(long value, long expected) => Assert.Equal(expected, Helpers.PrevPow(value));
}

public class AllocatorBehaviorTests : IDisposable
{
    private readonly VulkanContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public void DedicatedAllocation_CalculateStats_Succeeds()
    {
        var bi = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };
        var ai = new AllocationCreateInfo { Flags = AllocationCreateFlags.DedicatedMemory };

        _ctx.Allocator.CreateBuffer(in bi, in ai, out var buf, out var alloc);
        Assert.NotNull(alloc);

        try
        {
            var stats = _ctx.Allocator.CalculateStats();
            Assert.NotNull(stats);
        }
        finally
        {
            _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            alloc.Dispose();
        }
    }

    [Fact]
    public void DedicatedAllocation_BudgetCounterMatchesCpp()
    {
        var bi = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };
        var ai = new AllocationCreateInfo { Flags = AllocationCreateFlags.DedicatedMemory };

        var before = _ctx.Allocator.Budget.OperationsSinceBudgetFetch;

        _ctx.Allocator.CreateBuffer(in bi, in ai, out var buf, out var alloc);
        Assert.NotNull(alloc);

        try
        {
            Assert.Equal(before + 2, _ctx.Allocator.Budget.OperationsSinceBudgetFetch);

            _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            alloc.Dispose();
            alloc = null;

            Assert.Equal(before + 3, _ctx.Allocator.Budget.OperationsSinceBudgetFetch);
        }
        finally
        {
            if (alloc != null)
            {
                try { _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty); } catch { }
                try { alloc.Dispose(); } catch { }
            }
        }
    }
}
