namespace VmaCS;

/// <summary>
/// Flags for creating a <see cref="VulkanMemoryAllocator"/>.
/// </summary>
[Flags]
public enum AllocatorCreateFlags
{
    /// <summary>
    /// Allocator and all objects created from it will not be synchronized internally,
    /// so you must guarantee they are used from only one thread at a time or synchronized externally by you.
    /// Using this flag may increase performance because internal locks are not used.
    /// </summary>
    ExternallySyncronized = 0x00000001,

    /// <summary>
    /// Enables usage of VK_EXT_memory_budget extension. The extension provides query for
    /// current memory usage and budget, which will probably be more accurate than an
    /// estimation used by the library otherwise.
    /// </summary>
    ExtMemoryBudget = 0x00000008,

    /// <summary>
    /// Enables usage of VK_AMD_device_coherent_memory extension. The extension and
    /// accompanying device feature provide access to memory types with
    /// VK_MEMORY_PROPERTY_DEVICE_COHERENT_BIT_AMD and VK_MEMORY_PROPERTY_DEVICE_UNCACHED_BIT_AMD flags.
    /// They are useful mostly for writing breadcrumb markers - a common method for debugging GPU crash/hang/TDR.
    /// When the extension is not enabled, such memory types are still enumerated, but their usage is illegal.
    /// To protect from this error, if you don't create the allocator with this flag, it will refuse
    /// to allocate any memory or create a custom pool in such memory type, returning VK_ERROR_FEATURE_NOT_PRESENT.
    /// </summary>
    AMDDeviceCoherentMemory = 0x00000010,

    /// <summary>
    /// Enables usage of "buffer device address" feature. When this flag is set, you can create
    /// buffers with VK_BUFFER_USAGE_SHADER_DEVICE_ADDRESS_BIT using VMA. The library automatically
    /// adds VK_MEMORY_ALLOCATE_DEVICE_ADDRESS_BIT to allocated memory blocks wherever it might be needed.
    /// </summary>
    BufferDeviceAddress = 0x00000020,

    /// <summary>
    /// Enables usage of VK_EXT_memory_priority extension. When this flag is used,
    /// VmaAllocationCreateInfo::priority and VmaPoolCreateInfo::priority are used to set
    /// priorities of allocated Vulkan memory. Without it, these variables are ignored.
    /// A priority must be a floating-point value between 0 and 1, indicating the priority
    /// of the allocation relative to other memory allocations. Larger values are higher priority.
    /// The value to be used for default priority is 0.5.
    /// </summary>
    ExtMemoryPriority = 0x00000040
}
