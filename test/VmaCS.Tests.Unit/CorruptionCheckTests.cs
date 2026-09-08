using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class CorruptionCheckTests
{
    [Fact]
    public void TestCheckPoolCorruption_DisabledByDefaultIsNoOp()
    {
        using var ctx = new VulkanContext();

        Assert.False(ctx.Allocator.DebugDetectCorruption);

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var baseAllocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        ctx.Allocator.FindMemoryTypeIndexForBufferInfo(in bufferInfo, in baseAllocInfo, out var memoryType);
        Assert.True(memoryType.HasValue, "Failed to find a memory type for the buffer.");

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memoryType.Value,
            MinBlockCount = 1,
            MaxBlockCount = 1,
        });

        Assert.Equal(Result.ErrorFeatureNotPresent, pool.CheckForCorruption());
    }

    [Fact]
    public void TestCheckPoolCorruption_NoCorruptionWhenIntact()
    {
        using var ctx = new VulkanContext();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var baseAllocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        ctx.Allocator.FindMemoryTypeIndexForBufferInfo(in bufferInfo, in baseAllocInfo, out var memoryType);
        Assert.True(memoryType.HasValue, "Failed to find a memory type for the buffer.");

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memoryType.Value,
            MinBlockCount = 1,
            MaxBlockCount = 1,
        });

        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.GpuOnly,
            Pool = pool,
        };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? allocated = null;

        var prevDetect = ctx.Allocator.DebugDetectCorruption;
        var prevMargin = ctx.Allocator.DebugMargin;

        try
        {
            ctx.Allocator.DebugDetectCorruption = true;
            ctx.Allocator.DebugMargin = 16;

            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out var allocation);
            allocated = allocation;

            Assert.NotEqual(0u, buffer.Handle);
            Assert.NotNull(allocated);

            Assert.Equal(Result.Success, pool.CheckForCorruption());
        }
        finally
        {
            if (allocated != null)
            {
                allocated.Dispose();
            }

            ctx.Allocator.DebugDetectCorruption = prevDetect;
            ctx.Allocator.DebugMargin = prevMargin;
        }
    }

    [Fact(Skip = "VMA recording not yet implemented in VmaCS (VmaRecordSettings / vmaBeginRecording)")]
    public void TestRecording() => Assert.Skip("Implement VMA recording and add a unit test (VMA does not test it).");
}
