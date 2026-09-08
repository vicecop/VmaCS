namespace VmaCS;

/// <summary>
/// Flags to be passed as VmaAllocationCreateInfo::flags.
/// </summary>
[Flags]
public enum AllocationCreateFlags
{
    /// <summary>
    /// Set this flag if the allocation should have its own memory block.
    /// Use it for special, big resources, like fullscreen images used as attachments.
    /// If you use this flag while creating a buffer or an image, VkMemoryDedicatedAllocateInfo structure is applied if possible.
    /// </summary>
    DedicatedMemory = 0x0001,

    /// <summary>
    /// Set this flag to only try to allocate from existing VkDeviceMemory blocks and never create new such block.
    /// If new allocation cannot be placed in any of the existing blocks, allocation fails with VK_ERROR_OUT_OF_DEVICE_MEMORY error.
    /// You should not use DedicatedMemory and NeverAllocate at the same time.
    /// </summary>
    NeverAllocate = 0x0002,

    /// <summary>
    /// Set this flag to use a memory that will be persistently mapped and retrieve pointer to it.
    /// Pointer to mapped memory will be returned through VmaAllocationInfo::pMappedData.
    /// It is valid to use this flag for allocation made from memory type that is not HOST_VISIBLE.
    /// This flag is then ignored and memory is not mapped. This is useful if you need an allocation
    /// that is efficient to use on GPU (DEVICE_LOCAL) and still want to map it directly if possible on platforms that support it (e.g. Intel GPU).
    /// </summary>
    Mapped = 0x0004,

    /// <summary>
    /// Allocation will be created from upper stack in a double stack pool.
    /// This flag is only allowed for custom pools created with VMA_POOL_CREATE_LINEAR_ALGORITHM_BIT flag.
    /// </summary>
    UpperAddress = 0x0040,

    /// <summary>
    /// Create both buffer/image and allocation, but don't bind them together.
    /// It is useful when you want to bind yourself to do some more advanced binding, e.g. using some extensions.
    /// The flag is meaningful only with functions that bind by default: vmaCreateBuffer(), vmaCreateImage(). Otherwise it is ignored.
    /// If you want to make sure the new buffer/image is not tied to the new memory allocation through VkMemoryDedicatedAllocateInfoKHR structure
    /// in case the allocation ends up in its own memory block, use also flag CAN_ALIAS_BIT.
    /// </summary>
    DontBind = 0x0080,

    /// <summary>
    /// Create allocation only if additional device memory required for it, if any, won't exceed memory budget.
    /// Otherwise return VK_ERROR_OUT_OF_DEVICE_MEMORY.
    /// </summary>
    WithinBudget = 0x0100,

    /// <summary>
    /// Requests possibility to map the allocation (using vmaMapMemory() or VMA_ALLOCATION_CREATE_MAPPED_BIT).
    /// Declares that mapped memory will only be written sequentially, e.g. using memcpy() or a loop writing number-by-number,
    /// never read or accessed randomly, so a memory type can be selected that is uncached and write-combined.
    /// If you use VMA_MEMORY_USAGE_AUTO or other VMA_MEMORY_USAGE_AUTO* value, you must use this flag to be able to map the allocation.
    /// </summary>
    HostAccessSequentialWrite = 0x0400,

    /// <summary>
    /// Requests possibility to map the allocation. Declares that mapped memory can be read, written, and accessed in random order,
    /// so a HOST_CACHED memory type is preferred.
    /// If you use VMA_MEMORY_USAGE_AUTO or other VMA_MEMORY_USAGE_AUTO* value, you must use this flag to be able to map the allocation.
    /// </summary>
    HostAccessRandom = 0x0800,

    /// <summary>
    /// Set this flag if the allocated memory will have aliasing resources.
    /// Usage of this flag prevents supplying VkMemoryDedicatedAllocateInfoKHR when DEDICATED_MEMORY_BIT is specified.
    /// Otherwise created dedicated memory will not be suitable for aliasing resources, resulting in Vulkan Validation Layer errors.
    /// </summary>
    CanAlias = 0x00000200,

    /// <summary>
    /// Together with HOST_ACCESS_SEQUENTIAL_WRITE_BIT or HOST_ACCESS_RANDOM_BIT, it says that despite request for host access,
    /// a not-HOST_VISIBLE memory type can be selected if it may improve performance.
    /// By using this flag, you declare that you will check if the allocation ended up in a HOST_VISIBLE memory type
    /// and if not, you will create some "staging" buffer and issue an explicit transfer to write/read your data.
    /// </summary>
    HostAccessAllowTransferInstead = 0x1000
}
