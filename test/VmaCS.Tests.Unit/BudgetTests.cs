using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class BudgetTests : IDisposable
{
    private readonly VulkanContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    private static long TotalAllocationBytes(VulkanMemoryAllocator allocator)
    {
        var heapCount = allocator.MemoryHeapCount;
        long total = 0;

        for (var i = 0; i < heapCount; ++i)
        {
            var budget = allocator.GetBudget(i);
            total += budget.AllocationBytes;
        }

        return total;
    }

    // Mirrors vmaGetHeapBudget: public per-heap query returns sane, non-negative values.
    [Fact]
    public void GetBudget_SingleHeap_ReturnsSaneValues()
    {
        Assert.True(_ctx.Allocator.MemoryHeapCount > 0);

        var budget = _ctx.Allocator.GetBudget(0);

        Assert.True(budget.Budget > 0, "Budget should be derived from a non-zero heap size.");
        Assert.True(budget.Usage >= 0);
        Assert.True(budget.BlockBytes >= 0);
        Assert.True(budget.AllocationBytes >= 0);
    }

    // Mirrors vmaGetHeapBudgets: array overload fills every heap and matches per-heap queries.
    [Fact]
    public void GetBudget_Array_FillsAllHeaps()
    {
        var heapCount = _ctx.Allocator.MemoryHeapCount;
        var budgets = new AllocationBudget[heapCount];

        _ctx.Allocator.GetBudget(budgets);

        for (var i = 0; i < heapCount; ++i)
        {
            var single = _ctx.Allocator.GetBudget(i);

            Assert.Equal(single.BlockBytes, budgets[i].BlockBytes);
            Assert.Equal(single.AllocationBytes, budgets[i].AllocationBytes);
            Assert.Equal(single.Usage, budgets[i].Usage);
            Assert.Equal(single.Budget, budgets[i].Budget);
        }
    }

    [Fact]
    public void GetBudget_ArrayTooSmall_Throws()
    {
        var tooSmall = new AllocationBudget[_ctx.Allocator.MemoryHeapCount - 1];
        Assert.Throws<ArgumentException>(() => _ctx.Allocator.GetBudget(tooSmall));
    }

    [Fact]
    public void GetBudget_InvalidHeapIndex_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ctx.Allocator.GetBudget(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ctx.Allocator.GetBudget(_ctx.Allocator.MemoryHeapCount));
    }

    // The public budget reflects live allocations: a dedicated allocation of size S
    // increments tracked AllocationBytes by exactly S, and returns to 0 after free.
    [Fact]
    public void GetBudget_ReflectsDedicatedAllocation()
    {
        const long size = 1024;
        var bi = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = size,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
        };
        var ai = new AllocationCreateInfo { Flags = AllocationCreateFlags.DedicatedMemory };

        Assert.Equal(0, TotalAllocationBytes(_ctx.Allocator));

        _ctx.Allocator.CreateBuffer(in bi, in ai, out var buf, out var alloc);
        Assert.NotNull(alloc);

        try
        {
            Assert.Equal(size, TotalAllocationBytes(_ctx.Allocator));
        }
        finally
        {
            _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            alloc.Dispose();
        }

        Assert.Equal(0, TotalAllocationBytes(_ctx.Allocator));
    }

    // Mirrors VmaBudget.statistics: a dedicated allocation accounts for exactly one
    // VkDeviceMemory block (BlockCount) and one logical allocation (AllocationCount).
    [Fact]
    public void GetBudget_ReflectsBlockAndAllocationCounts()
    {
        var heapCount = _ctx.Allocator.MemoryHeapCount;

        long TotalBlocks() { long t = 0; var b = new AllocationBudget[heapCount]; _ctx.Allocator.GetBudget(b); foreach (var x in b) { t += x.BlockCount; } return t; }
        long TotalAllocs() { long t = 0; var b = new AllocationBudget[heapCount]; _ctx.Allocator.GetBudget(b); foreach (var x in b) { t += x.AllocationCount; } return t; }

        Assert.Equal(0, TotalBlocks());
        Assert.Equal(0, TotalAllocs());

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
            Assert.Equal(1, TotalBlocks());
            Assert.Equal(1, TotalAllocs());
        }
        finally
        {
            _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            alloc.Dispose();
        }

        Assert.Equal(0, TotalBlocks());
        Assert.Equal(0, TotalAllocs());
    }
}
