using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public unsafe class AliasingTests
{
    [Fact]
    public void TestCreateAliasingBuffer2()
    {
        using var ctx = new VulkanContext();

        var bufInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 64 * 1024 * 1024,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.StorageTexelBufferBit,
        };

        ctx.Allocator.CreateBuffer(
            in bufInfo,
            new AllocationCreateInfo { Usage = MemoryUsage.Auto, Flags = AllocationCreateFlags.DedicatedMemory | AllocationCreateFlags.CanAlias },
            out var origBuffer, out var bufAlloc);

        Assert.NotNull(bufAlloc);

        Silk.NET.Vulkan.Buffer aliasBuffer = default;

        try
        {
            Assert.Equal(0, bufAlloc.Offset);
            Assert.True(bufAlloc.Size >= 64 * 1024 * 1024);

            var aliasInfo = bufInfo;
            aliasInfo.Size = 1024;
            Assert.Equal(Result.Success, ctx.Allocator.CreateAliasingBuffer(bufAlloc, 0, in aliasInfo, out aliasBuffer));
            Assert.NotEqual(0u, aliasBuffer.Handle);

            var zeroInfo = bufInfo;
            zeroInfo.Size = 0;
            Assert.Equal(Result.ErrorInitializationFailed, ctx.Allocator.CreateAliasingBuffer(bufAlloc, 0, in zeroInfo, out _));

            var tooBig = bufInfo;
            tooBig.Size = (ulong)(bufAlloc.Size + 1);
            Assert.Equal(Result.ErrorInitializationFailed, ctx.Allocator.CreateAliasingBuffer(bufAlloc, 0, in tooBig, out _));

            var overInfo = bufInfo;
            overInfo.Size = 1024;
            Assert.Equal(Result.ErrorInitializationFailed, ctx.Allocator.CreateAliasingBuffer(bufAlloc, bufAlloc.Size, in overInfo, out _));
        }
        finally
        {
            if (aliasBuffer.Handle != 0)
            {
                ctx.Vk.DestroyBuffer(ctx.Device, aliasBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
            }

            bufAlloc.Dispose();
            ctx.Vk.DestroyBuffer(ctx.Device, origBuffer, ReadOnlySpan<AllocationCallbacks>.Empty);
        }
    }

    [Fact]
    public void TestCreateAliasingImage2()
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

        Image img1 = default;
        Allocation? alloc = null;
        Image img2 = default;

        try
        {
            ctx.Allocator.CreateDedicatedImage(
                in imgInfo,
                new AllocationCreateInfo { Usage = MemoryUsage.Auto, Flags = AllocationCreateFlags.DedicatedMemory | AllocationCreateFlags.CanAlias },
                null,
                out img1, out alloc);

            Assert.NotNull(alloc);
            Assert.Equal(0, alloc.Offset);

            Assert.Equal(Result.Success, ctx.Allocator.CreateAliasingImage(alloc, 0, in imgInfo, out img2));

            Assert.NotEqual(0u, img2.Handle);

            var zeroImg = imgInfo;
            zeroImg.Extent = new Extent3D(0, 0, 0);
            Assert.Equal(Result.ErrorInitializationFailed, ctx.Allocator.CreateAliasingImage(alloc, 0, in zeroImg, out _));
        }
        finally
        {
            if (img2.Handle != 0)
            {
                ctx.Vk.DestroyImage(ctx.Device, img2, ReadOnlySpan<AllocationCallbacks>.Empty);
            }

            if (alloc != null)
            {
                alloc.Dispose();
            }

            if (img1.Handle != 0)
            {
                ctx.Vk.DestroyImage(ctx.Device, img1, ReadOnlySpan<AllocationCallbacks>.Empty);
            }
        }
    }
}
