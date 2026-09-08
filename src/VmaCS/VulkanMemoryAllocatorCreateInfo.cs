using Silk.NET.Core;
using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Parameters for creating a <see cref="VulkanMemoryAllocator"/>.
/// </summary>
public struct VulkanMemoryAllocatorCreateInfo
{
    /// <summary>
    /// Use combination of <see cref="AllocatorCreateFlags"/>.
    /// </summary>
    public AllocatorCreateFlags Flags;

    /// <summary>
    /// Vulkan API version used by the application.
    /// </summary>
    public required Version32 VulkanAPIVersion;

    /// <summary>
    /// Vulkan API object used for dispatching Vulkan calls.
    /// </summary>
    public required Vk VulkanAPIObject;

    /// <summary>
    /// Vulkan instance used to query physical device properties.
    /// </summary>
    public required Instance Instance;

    /// <summary>
    /// Vulkan physical device to allocate memory from.
    /// </summary>
    public required PhysicalDevice PhysicalDevice;

    /// <summary>
    /// Vulkan logical device to allocate memory from.
    /// </summary>
    public required Device LogicalDevice;

    /// <summary>
    /// Preferred size of a single VkDeviceMemory block to be allocated, in bytes.
    /// Leave 0 for default.
    /// </summary>
    public long PreferredLargeHeapBlockSize;

    /// <summary>
    /// Optional limits on the size of individual Vulkan memory heaps, in bytes.
    /// </summary>
    public long[]? HeapSizeLimits;

    /// <summary>
    /// Optional allocation callbacks for custom host memory allocation.
    /// </summary>
    public AllocationCallbacks? AllocationCallbacks;

    /// <summary>
    /// Optional callbacks for device memory allocation.
    /// </summary>
    public DeviceMemoryCallbacks? DeviceMemoryCallbacks;

    /// <summary>
    /// If true, exceptions are thrown on errors instead of returning error codes.
    /// </summary>
    public bool ThrowOnError;
}
