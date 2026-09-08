namespace VmaCS;

/// <summary>
/// Intended usage of the allocated memory.
/// </summary>
/// <remarks>
/// The <see cref="Auto"/>, <see cref="AutoPreferDevice"/>, and <see cref="AutoPreferHost"/> values are recommended for most use cases.
/// The older values (<see cref="GpuOnly"/>, <see cref="CpuOnly"/>, <see cref="CpuToGpu"/>, <see cref="GpuToCpu"/>, <see cref="CpuCopy"/>) are obsolete
/// because they hardcode specific memory property flags and do not adapt to the actual memory architecture of the GPU.
/// </remarks>
public enum MemoryUsage
{
    /// <summary>
    /// No intended memory usage specified. Use other members of <see cref="AllocationCreateInfo"/> to specify your requirements.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Obsolete. Prefers <c>VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT</c>.
    /// </summary>
    GpuOnly,

    /// <summary>
    /// Obsolete. Guarantees <c>VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT</c> and <c>VK_MEMORY_PROPERTY_HOST_COHERENT_BIT</c>.
    /// </summary>
    CpuOnly,

    /// <summary>
    /// Obsolete. Guarantees <c>VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT</c>, prefers <c>VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT</c>.
    /// </summary>
    CpuToGpu,

    /// <summary>
    /// Obsolete. Guarantees <c>VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT</c>, prefers <c>VK_MEMORY_PROPERTY_HOST_CACHED_BIT</c>.
    /// </summary>
    GpuToCpu = 4,

    /// <summary>
    /// Lazily allocated GPU memory having <c>VK_MEMORY_PROPERTY_LAZILY_ALLOCATED_BIT</c>. Exists mostly on mobile platforms.
    /// Using it on desktop PC or other GPUs with no such memory type present will fail the allocation.
    /// Usage: Memory for transient attachment images (color attachments, depth attachments etc.), created with <c>VK_IMAGE_USAGE_TRANSIENT_ATTACHMENT_BIT</c>.
    /// Allocations with this usage are always created as dedicated - it implies <c>VMA_ALLOCATION_CREATE_DEDICATED_MEMORY_BIT</c>.
    /// </summary>
    GpuLazilyAllocated = 5,

    /// <summary>
    /// Obsolete. Prefers not <c>VK_MEMORY_PROPERTY_DEVICE_LOCAL_BIT</c>.
    /// </summary>
    CpuCopy = 6,

    /// <summary>
    /// Selects the best memory type automatically.
    /// This is the recommended value for most common use cases.
    ///
    /// When using this value, if you want to map the allocation, you must pass one of the host access flags:
    /// <c>AllocationCreateFlags.HostAccessSequentialWrite</c> or <c>AllocationCreateFlags.HostAccessRandom</c>.
    ///
    /// Can only be used with functions that provide buffer/image create info, e.g. <see cref="VulkanMemoryAllocator.CreateBuffer"/>,
    /// <see cref="VulkanMemoryAllocator.CreateImage"/>, <see cref="VulkanMemoryAllocator.FindMemoryTypeIndexForBufferInfo"/>,
    /// <see cref="VulkanMemoryAllocator.FindMemoryTypeIndexForImageInfo"/>.
    /// </summary>
    Auto = 7,

    /// <summary>
    /// Selects the best memory type automatically with preference for GPU (device) memory.
    ///
    /// When using this value, if you want to map the allocation, you must pass one of the host access flags:
    /// <c>AllocationCreateFlags.HostAccessSequentialWrite</c> or <c>AllocationCreateFlags.HostAccessRandom</c>.
    ///
    /// Can only be used with functions that provide buffer/image create info.
    /// </summary>
    AutoPreferDevice = 8,

    /// <summary>
    /// Selects the best memory type automatically with preference for CPU (host) memory.
    ///
    /// When using this value, if you want to map the allocation, you must pass one of the host access flags:
    /// <c>AllocationCreateFlags.HostAccessSequentialWrite</c> or <c>AllocationCreateFlags.HostAccessRandom</c>.
    ///
    /// Can only be used with functions that provide buffer/image create info.
    /// </summary>
    AutoPreferHost = 9
}
