using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Parameters for creating an allocation.
/// </summary>
public unsafe struct AllocationCreateInfo
{
    /// <summary>
    /// Use combination of <see cref="AllocationCreateFlags"/>.
    /// </summary>
    public AllocationCreateFlags Flags;

    /// <summary>
    /// Allocation strategy to be used. See <see cref="AllocationStrategyFlags"/>.
    /// </summary>
    public AllocationStrategyFlags Strategy;

    /// <summary>
    /// Intended usage of the allocated memory. See <see cref="MemoryUsage"/>.
    /// </summary>
    public MemoryUsage Usage;

    /// <summary>
    /// Required memory property flags for the memory type.
    /// </summary>
    public MemoryPropertyFlags RequiredFlags;

    /// <summary>
    /// Preferred memory property flags for the memory type.
    /// </summary>
    public MemoryPropertyFlags PreferredFlags;

    /// <summary>
    /// Bit mask of allowed memory type indices. Optional, leave 0 for no restriction.
    /// </summary>
    public uint MemoryTypeBits;

    /// <summary>
    /// Custom pool to allocate from. Optional.
    /// </summary>
    public VulkanMemoryPool? Pool;

    /// <summary>
    /// User data pointer or string to associate with the allocation. Optional.
    /// </summary>
    public object? UserData;

    /// <summary>
    /// Minimum alignment for the allocation. Optional, leave 0 for default.
    /// </summary>
    public long MinAlignment;

    /// <summary>
    /// Priority of the allocation relative to other memory allocations.
    /// A floating-point value between 0 and 1. Larger values are higher priority.
    /// Only used when <see cref="AllocatorCreateFlags.ExtMemoryPriority"/> is set.
    /// Default value is 0.5.
    /// </summary>
    public float Priority = 0.5f;

    /// <summary>
    /// Additional pNext chain to be attached to VkMemoryAllocateInfo for this allocation.
    /// Optional.
    /// </summary>
    public void* MemoryAllocateNext;

    /// <summary>
    /// Initializes a new instance of the <see cref="AllocationCreateInfo"/> struct with default values.
    /// </summary>
    public AllocationCreateInfo()
    {
        Priority = 0.5f;
    }
}
