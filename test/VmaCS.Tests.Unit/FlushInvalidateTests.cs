using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public unsafe class FlushInvalidateTests
{
    [Fact]
    public void TestFlushAllocations()
    {
        using var ctx = new VulkanContext();

        const int count = 3;
        var allocs = new Allocation?[count];
        var buffers = new Silk.NET.Vulkan.Buffer[count];

        try
        {
            for (var i = 0; i < count; ++i)
            {
                var bufInfo = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = 1024,
                    Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
                };

                ctx.Allocator.CreateBuffer(
                    in bufInfo,
                    new AllocationCreateInfo { Usage = MemoryUsage.Auto, Flags = AllocationCreateFlags.HostAccessSequentialWrite | AllocationCreateFlags.Mapped },
                    out buffers[i], out var alloc);
                Assert.NotNull(alloc);
                allocs[i] = alloc;

                Assert.NotEqual(0, (nint)alloc.MappedData);

                var span = new Span<byte>(alloc.MappedData, 1024);
                for (var j = 0; j < span.Length; ++j)
                {
                    span[j] = (byte)(i * 7 + j);
                }
            }

            var allocsFlat = new Allocation[count];
            for (var i = 0; i < count; i++) { allocsFlat[i] = allocs[i].NonNull(); }

            Assert.Equal(Result.Success, ctx.Allocator.FlushAllocations(allocsFlat));

            var offsets = new long[count];
            var sizes = new long[count];
            for (var i = 0; i < count; ++i)
            {
                offsets[i] = 0;
                sizes[i] = 512;
            }
            Assert.Equal(Result.Success, ctx.Allocator.FlushAllocations(allocsFlat, offsets, sizes));

            Assert.Equal(Result.Success, ctx.Allocator.InvalidateAllocations(allocsFlat));
            Assert.Equal(Result.Success, ctx.Allocator.InvalidateAllocations(allocsFlat, offsets, sizes));

            for (var i = 0; i < count; ++i)
            {
                var span = new Span<byte>(allocs[i].NonNull().MappedData, 1024);
                for (var j = 0; j < span.Length; ++j)
                {
                    Assert.Equal((byte)(i * 7 + j), span[j]);
                }
            }
        }
        finally
        {
            for (var i = 0; i < count; ++i)
            {
                if (allocs[i] != null)
                {
                    allocs[i].NonNull().Dispose();
                }

                if (buffers[i].Handle != 0)
                {
                    ctx.Vk.DestroyBuffer(ctx.Device, buffers[i], ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }
        }
    }

    [Fact]
    public void TestInvalidateAllocations()
    {
        using var ctx = new VulkanContext();

        Assert.Equal(Result.Success, ctx.Allocator.FlushAllocations(Array.Empty<Allocation>()));
        Assert.Equal(Result.Success, ctx.Allocator.InvalidateAllocations(Array.Empty<Allocation>()));

        const int count = 2;
        var allocs = new Allocation?[count];
        var buffers = new Silk.NET.Vulkan.Buffer[count];

        try
        {
            for (var i = 0; i < count; ++i)
            {
                var bufInfo = new BufferCreateInfo
                {
                    SType = StructureType.BufferCreateInfo,
                    Size = 2048,
                    Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.TransferDstBit,
                };

                ctx.Allocator.CreateBuffer(
                    in bufInfo,
                    new AllocationCreateInfo { Usage = MemoryUsage.Auto, Flags = AllocationCreateFlags.HostAccessSequentialWrite | AllocationCreateFlags.Mapped },
                    out buffers[i], out var alloc);
                Assert.NotNull(alloc);
                allocs[i] = alloc;

                Assert.NotEqual(0, (nint)alloc.MappedData);
            }

            var allocsFlat = new Allocation[count];
            for (var i = 0; i < count; i++) { allocsFlat[i] = allocs[i].NonNull(); }

            Assert.Equal(Result.Success, ctx.Allocator.InvalidateAllocations(allocsFlat));
        }
        finally
        {
            for (var i = 0; i < count; ++i)
            {
                if (allocs[i] != null)
                {
                    allocs[i].NonNull().Dispose();
                }

                if (buffers[i].Handle != 0)
                {
                    ctx.Vk.DestroyBuffer(ctx.Device, buffers[i], ReadOnlySpan<AllocationCallbacks>.Empty);
                }
            }
        }
    }
}
