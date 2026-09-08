using Silk.NET.Vulkan;

namespace VmaCS.Tests.Unit;

public class Maintenance4Tests : IDisposable
{
    private readonly VulkanContext _ctx = new();

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public void FindMemoryTypeIndexForBufferInfo_WhenMaintenance4Supported_ReturnsValidTypeIndex()
    {
        if (!_ctx.Maintenance4Supported)
        {
            return;
        }

        using var allocator = new VulkanMemoryAllocator(new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = Vk.Version11,
            VulkanAPIObject = _ctx.Vk,
            Instance = _ctx.Instance,
            PhysicalDevice = _ctx.PhysicalDevice,
            LogicalDevice = _ctx.Device
        });

        var bufferInfo = new BufferCreateInfo
        {
            SType = StructureType.BufferCreateInfo,
            Size = 4096,
            Usage = BufferUsageFlags.VertexBufferBit
        };
        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.GpuOnly
        };

        var result = allocator.FindMemoryTypeIndexForBufferInfo(in bufferInfo, in allocInfo, out var typeIndex);

        Assert.True(result.IsSuccess());
        Assert.NotNull(typeIndex);
        Assert.True(typeIndex.Value >= 0);
        Assert.True(typeIndex.Value < allocator.MemoryTypeCount);
    }

    [Fact]
    public void FindMemoryTypeIndexForImageInfo_WhenMaintenance4Supported_ReturnsValidTypeIndex()
    {
        if (!_ctx.Maintenance4Supported)
        {
            return;
        }

        using var allocator = new VulkanMemoryAllocator(new VulkanMemoryAllocatorCreateInfo
        {
            VulkanAPIVersion = Vk.Version11,
            VulkanAPIObject = _ctx.Vk,
            Instance = _ctx.Instance,
            PhysicalDevice = _ctx.PhysicalDevice,
            LogicalDevice = _ctx.Device
        });

        var imageInfo = new ImageCreateInfo
        {
            SType = StructureType.ImageCreateInfo,
            ImageType = ImageType.Type2D,
            Format = Format.R8G8B8A8Unorm,
            Extent = new Extent3D { Width = 256, Height = 256, Depth = 1 },
            MipLevels = 1,
            ArrayLayers = 1,
            Samples = SampleCountFlags.Count1Bit,
            Tiling = ImageTiling.Optimal,
            Usage = ImageUsageFlags.SampledBit,
            InitialLayout = ImageLayout.Undefined
        };
        var allocInfo = new AllocationCreateInfo
        {
            Usage = MemoryUsage.GpuOnly
        };

        var result = allocator.FindMemoryTypeIndexForImageInfo(in imageInfo, in allocInfo, out var typeIndex);

        Assert.True(result.IsSuccess());
        Assert.NotNull(typeIndex);
        Assert.True(typeIndex.Value >= 0);
        Assert.True(typeIndex.Value < allocator.MemoryTypeCount);
    }

    [Fact]
    public void IsMaintenance4Supported_MatchesContextDetection() => Assert.Equal(_ctx.Maintenance4Supported, _ctx.Allocator.IsMaintenance4Supported);

    [Fact]
    public void IsMaintenance5Supported_MatchesContextDetection()
    {
        var supported = _ctx.Allocator.IsMaintenance5Supported;
        Assert.Equal(_ctx.Maintenance4Supported, supported);
    }
}
