using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class AutoMemoryUsageTests : IDisposable
{
    private readonly VulkanContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    private static bool IsHostVisible(VulkanMemoryAllocator a, int typeIndex) =>
        (a.GetMemoryTypeProperties(typeIndex) & MemoryPropertyFlags.HostVisibleBit) != 0;

    private static bool IsDeviceLocal(VulkanMemoryAllocator a, int typeIndex) =>
        (a.GetMemoryTypeProperties(typeIndex) & MemoryPropertyFlags.DeviceLocalBit) != 0;

    // VMA_MEMORY_USAGE_AUTO with HOST_ACCESS_SEQUENTIAL_WRITE on an upload buffer
    // (vertex buffer written by host) -> must resolve to HOST_VISIBLE memory.
    [Fact]
    public void Auto_SequentialWriteUploadBuffer_IsHostVisible()
    {
        var bi = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 4096,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
        };
        var ai = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.HostAccessSequentialWrite,
        };

        _ctx.Allocator.CreateBuffer(in bi, in ai, out var buf, out var alloc);
        Assert.NotNull(alloc);

        try
        {
            Assert.True(IsHostVisible(_ctx.Allocator, alloc.MemoryTypeIndex));
        }
        finally
        {
            _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            alloc.Dispose();
        }
    }

    // VMA_MEMORY_USAGE_AUTO with HOST_ACCESS_RANDOM on a read-back buffer
    // (index buffer read by host) -> must resolve to HOST_VISIBLE memory.
    [Fact]
    public void Auto_RandomReadbackBuffer_IsHostVisible()
    {
        var bi = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 4096,
            Usage = BufferUsageFlags.TransferSrcBit | BufferUsageFlags.IndexBufferBit,
        };
        var ai = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.HostAccessRandom,
        };

        _ctx.Allocator.CreateBuffer(in bi, in ai, out var buf, out var alloc);
        Assert.NotNull(alloc);

        try
        {
            Assert.True(IsHostVisible(_ctx.Allocator, alloc.MemoryTypeIndex));
        }
        finally
        {
            _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            alloc.Dispose();
        }
    }

    // VMA_MEMORY_USAGE_AUTO_PREFER_HOST keeps the buffer on the host side.
    [Fact]
    public void AutoPreferHost_SequentialWrite_IsHostVisible()
    {
        var bi = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 4096,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.UniformBufferBit,
        };
        var ai = new AllocationCreateInfo
        {
            Usage = MemoryUsage.AutoPreferHost,
            Flags = AllocationCreateFlags.HostAccessSequentialWrite,
        };

        _ctx.Allocator.CreateBuffer(in bi, in ai, out var buf, out var alloc);
        Assert.NotNull(alloc);

        try
        {
            Assert.True(IsHostVisible(_ctx.Allocator, alloc.MemoryTypeIndex));
        }
        finally
        {
            _ctx.Vk.DestroyBuffer(_ctx.Device, buf, ReadOnlySpan<AllocationCallbacks>.Empty);
            alloc.Dispose();
        }
    }

    // VMA_MEMORY_USAGE_AUTO on a GPU-only image (sampled) -> prefers DEVICE_LOCAL memory.
    [Fact]
    public void Auto_GpuImage_PicksDeviceLocal()
    {
        var ii = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Srgb,
            Extent = new Extent3D { Width = 16, Height = 16, Depth = 1 },
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.SampledBit,
            SharingMode = SharingMode.Exclusive,
            InitialLayout = ImageLayout.Undefined,
        };
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.Auto };

        _ctx.Allocator.CreateImage(in ii, in ai, out var img, out var alloc);
        Assert.NotNull(alloc);

        try
        {
            Assert.True(IsDeviceLocal(_ctx.Allocator, alloc.MemoryTypeIndex));
        }
        finally
        {
            _ctx.Vk.DestroyImage(_ctx.Device, img, ReadOnlySpan<AllocationCallbacks>.Empty);
            alloc.Dispose();
        }
    }

    // FindMemoryTypeIndexForBufferInfo carries the resource usage, so AUTO is legal there.
    [Fact]
    public void FindMemoryTypeIndexForBufferInfo_Auto_ReturnsType()
    {
        var bi = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 1024,
            Usage = BufferUsageFlags.TransferDstBit | BufferUsageFlags.VertexBufferBit,
        };
        var ai = new AllocationCreateInfo
        {
            Usage = MemoryUsage.Auto,
            Flags = AllocationCreateFlags.HostAccessSequentialWrite,
        };

        _ctx.Allocator.FindMemoryTypeIndexForBufferInfo(in bi, in ai, out var typeIndex);

        Assert.NotNull(typeIndex);
        Assert.True(IsHostVisible(_ctx.Allocator, typeIndex.Value));
    }

    // AUTO without resource information (low-level FindMemoryTypeIndex) cannot resolve a type,
    // mirroring C++ where VMA_MEMORY_USAGE_AUTO* requires vmaCreateBuffer/vmaCreateImage.
    [Fact]
    public void FindMemoryTypeIndex_Auto_WithoutResourceInfo_ReturnsNull()
    {
        var ai = new AllocationCreateInfo { Usage = MemoryUsage.Auto };
        _ = _ctx.Allocator.FindMemoryTypeIndex(uint.MaxValue, in ai, out var typeIndex);

        Assert.Null(typeIndex);
    }
}
