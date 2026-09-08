using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class BatchAllocationTests : IDisposable
{
    private readonly VulkanContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    private static MemoryRequirements Req(long size) => new()
    {
        Size = (ulong)size,
        Alignment = 1,
        MemoryTypeBits = uint.MaxValue,
    };

    private long TotalAllocationCount()
    {
        long total = 0;
        var budgets = new AllocationBudget[_ctx.Allocator.MemoryHeapCount];
        _ctx.Allocator.GetBudget(budgets);
        foreach (var b in budgets)
        {
            total += b.AllocationCount;
        }
        return total;
    }

    // vmaAllocateMemoryPages: allocates N identical allocations in one call.
    [Fact]
    public void AllocateMemoryPages_ReturnsRequestedCount()
    {
        var req = Req(4096);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        _ctx.Allocator.AllocateMemoryPages(in req, in ai, 4, out var allocations);

        try
        {
            Assert.Equal(4, allocations.Length);
            Assert.All(allocations, a => Assert.NotNull(a));
            // All pages share the same memory type (C++ makes every allocation identical).
            Assert.All(allocations, a => Assert.Equal(allocations[0].MemoryTypeIndex, a.MemoryTypeIndex));
        }
        finally
        {
            foreach (var a in allocations)
            {
                a.Dispose();
            }
        }
    }

    // No leak: every logical allocation is accounted for in the budget and is released on free.
    [Fact]
    public void AllocateMemoryPages_AllocationCountBalances()
    {
        var req = Req(8192);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        var before = TotalAllocationCount();

        _ctx.Allocator.AllocateMemoryPages(in req, in ai, 5, out var allocations);

        try
        {
            Assert.Equal(before + 5, TotalAllocationCount());
        }
        finally
        {
            foreach (var a in allocations)
            {
                a.Dispose();
            }
        }

        Assert.Equal(before, TotalAllocationCount());
    }

    // Atomicity: if a page allocation fails, already-made pages are freed (all-or-nothing).
    [Fact]
    public void AllocateMemoryPages_AtomicOnFailure_LeavesNoLeak()
    {
        _ = Req(8192);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        var before = TotalAllocationCount();

        // A dedicated allocation that is far too large to ever succeed forces a failure.
        var badReq = Req(long.MaxValue / 2);
        var res = _ctx.Allocator.AllocateMemoryPages(in badReq, in ai, 3, out var _);

        // With ThrowOnError off (the default, mirroring C++ VMA), the failure is reported as a Result code.
        Assert.NotEqual(Result.Success, res);

        // Nothing leaked from the (attempted) partial work.
        Assert.Equal(before, TotalAllocationCount());
    }

    [Fact]
    public void AllocateMemoryPages_ZeroCount_ReturnsEmpty()
    {
        var req = Req(4096);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        _ctx.Allocator.AllocateMemoryPages(in req, in ai, 0, out var allocations);

        Assert.Empty(allocations);
    }

    [Fact]
    public void FreeMemoryPages_FreesAllPages()
    {
        var req = Req(4096);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        var before = TotalAllocationCount();
        _ctx.Allocator.AllocateMemoryPages(in req, in ai, 3, out var allocations);

        foreach (var a in allocations)
        {
            a.Dispose();
        }

        Assert.Equal(before, TotalAllocationCount());
    }
}
