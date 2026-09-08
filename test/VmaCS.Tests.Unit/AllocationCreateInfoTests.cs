using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public unsafe class PriorityMinAlignmentMemoryNextTests : IDisposable
{
    private readonly VulkanContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    private static MemoryRequirements Req(long size) => new()
    {
        Size = (ulong)size,
        Alignment = 1,
        MemoryTypeBits = uint.MaxValue,
    };

    // VmaAllocationCreateInfo::minAlignment is applied as max(vkMemReq.alignment, minAlignment).
    // A block suballocation must then be aligned to that value.
    [Fact]
    public void MinAlignment_AppliedToSuballocationOffset()
    {
        const long minAlign = 4096;
        var req = Req(256);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly, MinAlignment = minAlign };

        _ctx.Allocator.AllocateMemory(in req, in ai, out var alloc);

        Assert.NotNull(alloc);

        try
        {
            Assert.IsType<BlockAllocation>(alloc);
            Assert.Equal(0L, alloc.Offset % minAlign);
        }
        finally
        {
            alloc.Dispose();
        }
    }

    [Fact]
    public void MinAlignment_DefaultKeepsAllocationWorking()
    {
        var req = Req(256);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        _ctx.Allocator.AllocateMemory(in req, in ai, out var alloc);

        Assert.NotNull(alloc);

        try
        {
            Assert.NotNull(alloc);
        }
        finally
        {
            alloc.Dispose();
        }
    }

    // VmaAllocationCreateInfo::pMemoryAllocateNext is a raw pNext chain for VkMemoryAllocateInfo and
    // forces a dedicated allocation. A valid (Flags=0) MemoryAllocateFlagsInfoKHR pointer is used so
    // the chain is well-formed; the resulting allocation must be dedicated (not a block suballocation).
    [Fact]
    public void MemoryAllocateNext_ForcesDedicatedAllocation()
    {
        var req = Req(4096);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        MemoryAllocateFlagsInfoKHR flagsInfo = new()
        {
            SType = StructureType.MemoryAllocateFlagsInfoKhr,
            Flags = 0,
        };

        var pFlagsInfo = &flagsInfo;
        ai.MemoryAllocateNext = pFlagsInfo;

        _ctx.Allocator.AllocateMemory(in req, in ai, out var alloc);

        Assert.NotNull(alloc);

        try
        {
            Assert.False(alloc is BlockAllocation);
        }
        finally
        {
            alloc.Dispose();
        }
    }

    // VmaAllocationCreateInfo::priority is plumbed through; without the allocator's
    // EXT_MEMORY_PRIORITY flag it is ignored (no GPU prioritization), but must not break allocation.
    [Fact]
    public void Priority_FieldPlumbed_IgnoredWithoutExtension()
    {
        var req = Req(256);
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly, Priority = 0.25f };

        _ctx.Allocator.AllocateMemory(in req, in ai, out var alloc);

        Assert.NotNull(alloc);

        try
        {
            Assert.NotNull(alloc);
        }
        finally
        {
            alloc.Dispose();
        }
    }

    [Fact]
    public void Priority_DefaultIsHalf()
    {
        var ai = new AllocationCreateInfo();
        Assert.Equal(0.5f, ai.Priority);
    }
}
