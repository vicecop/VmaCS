using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class AllocationPropertyTests
{
    [Fact]
    public void Allocation_BlockSize_ReturnsBlockSizeForBlockAllocation()
    {
        using var ctx = new VulkanContext();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 128,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var allocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? allocation = null;

        try
        {
            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out allocation);
            Assert.NotNull(allocation);

            Assert.True(allocation.NonNull().BlockSize >= 128);
            Assert.Equal(allocation.NonNull().BlockSize, allocation.NonNull().BlockSize);
        }
        finally
        {
            if (allocation is not null)
            {
                allocation.NonNull().Dispose();
            }
            if (buffer.Handle != 0)
            {
                ctx.Vk.DestroyBuffer(ctx.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }

    [Fact]
    public void Allocation_MemoryPropertyFlags_ReturnsFlagsFromMemoryType()
    {
        using var ctx = new VulkanContext();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var allocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? allocation = null;

        try
        {
            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out allocation);
            Assert.NotNull(allocation);

            var flags = allocation.NonNull().MemoryPropertyFlags;
            Assert.NotEqual(0u, (uint)flags);
        }
        finally
        {
            if (allocation is not null)
            {
                allocation.NonNull().Dispose();
            }
            if (buffer.Handle != 0)
            {
                ctx.Vk.DestroyBuffer(ctx.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }

    [Fact]
    public void Allocation_Name_Getter_ReturnsNullByDefault()
    {
        using var ctx = new VulkanContext();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var allocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? allocation = null;

        try
        {
            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out allocation);
            Assert.NotNull(allocation);

            Assert.Null(allocation.NonNull().Name);
        }
        finally
        {
            if (allocation is not null)
            {
                allocation.NonNull().Dispose();
            }
            if (buffer.Handle != 0)
            {
                ctx.Vk.DestroyBuffer(ctx.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }

    [Fact]
    public void Allocation_Dispose_CalledTwice_DoesNotThrow()
    {
        using var ctx = new VulkanContext();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var allocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? allocation = null;

        try
        {
            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out allocation);
            Assert.NotNull(allocation);

            allocation.NonNull().Dispose();
            allocation.NonNull().Dispose();
        }
        finally
        {
            if (allocation is not null)
            {
                allocation.NonNull().Dispose();
            }
            if (buffer.Handle != 0)
            {
                ctx.Vk.DestroyBuffer(ctx.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }

    [Fact]
    public void Allocation_UserData_Getter_ReturnsNullByDefault()
    {
        using var ctx = new VulkanContext();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var allocInfo = new AllocationCreateInfo { Usage = MemoryUsage.GpuOnly };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? allocation = null;

        try
        {
            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out allocation);
            Assert.NotNull(allocation);

            Assert.Null(allocation.NonNull().UserData);
        }
        finally
        {
            if (allocation is not null)
            {
                allocation.NonNull().Dispose();
            }
            if (buffer.Handle != 0)
            {
                ctx.Vk.DestroyBuffer(ctx.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }
}
