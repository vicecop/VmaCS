using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public unsafe class DedicatedAllocationTests
{
    [Fact]
    public void TestCreateDedicatedImage()
    {
        using var ctx = new VulkanContext();

        var imgInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D(64, 64, 1),
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            InitialLayout = ImageLayout.Preinitialized,
            Usage = ImageUsageFlags.SampledBit,
        };

        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.DedicatedMemory,
        };

        Image image = default;
        Allocation? alloc = null;

        try
        {
            ctx.Allocator.CreateDedicatedImage(in imgInfo, in allocInfo, null, out image, out alloc);

            Assert.NotEqual(0u, image.Handle);
            Assert.NotNull(alloc);
            Assert.True(alloc.NonNull().IsDedicated);
            Assert.Equal(0, alloc.Offset);
        }
        finally
        {
            if (image.Handle != 0)
            {
                ctx.Vk.DestroyImage(ctx.Device, image, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
            alloc?.Dispose();
        }
    }

    [Fact]
    public void TestDedicatedAllocation_MapUnmapAndStats()
    {
        using var ctx = new VulkanContext();

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.TransferSrcBit,
        };

        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.CpuOnly,
            Flags = AllocationCreateFlags.DedicatedMemory,
        };

        Silk.NET.Vulkan.Buffer buffer = default;
        Allocation? alloc = null;

        try
        {
            ctx.Allocator.CreateDedicatedBuffer(in bufferInfo, in allocInfo, null, out buffer, out alloc);
            Assert.NotNull(alloc);
            Assert.True(alloc.NonNull().IsDedicated);

            var mapRes = alloc.Map(out var ptr);
            Assert.True(mapRes.IsSuccess());
            Assert.NotEqual(0, (nint)ptr);

            alloc.Unmap();

            Assert.True(alloc.Size >= 1024);
            Assert.Equal(alloc.Size, alloc.NonNull().BlockSize);
        }
        finally
        {
            if (buffer.Handle != 0)
            {
                ctx.Vk.DestroyBuffer(ctx.Device, buffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
            alloc?.Dispose();
        }
    }
}
