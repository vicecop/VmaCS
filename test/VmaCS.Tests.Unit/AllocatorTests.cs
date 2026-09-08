using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public unsafe class AllocatorTests
{
    [Fact]
    public void CreateBuffer_AllocatesAndBindsGpuBuffer()
    {
        using var ctx = new VulkanContext();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.GpuOnly,
        };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? allocated = null;
        try
        {
            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out var allocation);
            allocated = allocation;

            Assert.NotEqual(0u, buffer.Handle);
            Assert.NotNull(allocation);
            Assert.True(allocation.Size >= 1024);
            Assert.True(allocation.Offset >= 0);
        }
        finally
        {
            if (allocated != null)
            {
                allocated.Dispose();
            }

            if (buffer.Handle != 0)
            {
                ctx.Vk.DestroyBuffer(ctx.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }

        }
    }

    [Fact]
    public void CreateBuffer_CpuToGpu_IsMappable()
    {
        using var ctx = new VulkanContext();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 256,
            Usage = BufferUsageFlags.VertexBufferBit,
        };

        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.CpuToGpu,
        };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? allocated = null;

        try
        {
            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out var allocation);
            allocated = allocation;

            Assert.NotNull(allocation);
            Assert.True(allocation.Size >= 256);

            allocation.Map(out var ptr);
            Assert.NotEqual(0, (nint)ptr);
            allocation.Unmap();
        }
        finally
        {
            if (allocated != null)
            {
                allocated.Dispose();
            }

            if (buffer.Handle != 0)
            {
                ctx.Vk.DestroyBuffer(ctx.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }

    [Fact]
    public void CreatePool_AllocatesBufferFromPool()
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
        try
        {
            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out var allocation);
            allocated = allocation;

            Assert.NotEqual(0u, buffer.Handle);
            Assert.NotNull(allocation);
        }
        finally
        {
            if (allocated != null)
            {
                allocated.Dispose();
            }

            if (buffer.Handle != 0)
            {
                ctx.Vk.DestroyBuffer(ctx.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }
}
