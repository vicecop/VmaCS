using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public unsafe class MappingHysteresisIntegrationTests
{
    private static int GetMappableMemoryType(VulkanMemoryAllocator allocator)
    {
        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var baseAllocInfo = new AllocationCreateInfo { Usage = MemoryUsage.CpuToGpu };

        allocator.FindMemoryTypeIndexForBufferInfo(in bufferInfo, in baseAllocInfo, out var memoryType);
        Assert.True(memoryType.HasValue, "Failed to find a memory type for the buffer.");

        return memoryType.Value;
    }

    [Fact]
    public void Block_StaysMapped_AfterRepeatedMapUnmapCycles()
    {
        using var ctx = new VulkanContext();
        var memoryType = GetMappableMemoryType(ctx.Allocator);

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memoryType,
            MinBlockCount = 1,
            MaxBlockCount = 1,
            BlockSize = 64 * 1024,
        });

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.CpuToGpu,
            Pool = pool,
        };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? allocated = null;
        try
        {
            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out var allocation);
            allocated = allocation;

            var blockAlloc = Assert.IsType<BlockAllocation>(allocation);

            for (var i = 0; i < 7; i++)
            {
                allocation.Map(out _);
                allocation.Unmap();
            }

            // Hysteresis keeps the underlying VkDeviceMemory block mapped even
            // though the allocation itself is currently unmapped.
            Assert.NotEqual(0, (nint)blockAlloc.Block.MappedData);
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
    public void Block_IsUnmapped_AfterSingleMapUnmapCycle()
    {
        using var ctx = new VulkanContext();
        var memoryType = GetMappableMemoryType(ctx.Allocator);

        using var pool = ctx.Allocator.CreatePool(new AllocationPoolCreateInfo
        {
            MemoryTypeIndex = memoryType,
            MinBlockCount = 1,
            MaxBlockCount = 1,
            BlockSize = 64 * 1024,
        });

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64,
            Usage = BufferUsageFlags.VertexBufferBit,
        };
        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.CpuToGpu,
            Pool = pool,
        };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? allocated = null;
        try
        {
            ctx.Allocator.CreateBuffer(in bufferInfo, in allocInfo, out buffer, out var allocation);
            allocated = allocation;

            var blockAlloc = Assert.IsType<BlockAllocation>(allocation);

            allocation.Map(out _);
            allocation.Unmap();

            // A single cycle is below the hysteresis threshold, so the block
            // must be fully unmapped.
            Assert.Equal(0, (nint)blockAlloc.Block.MappedData);
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
