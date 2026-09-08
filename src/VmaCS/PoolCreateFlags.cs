namespace VmaCS;

/// <summary>
/// Flags to be passed as VmaPoolCreateInfo::flags.
/// </summary>
[Flags]
public enum PoolCreateFlags
{
    /// <summary>
    /// Use this flag if you always allocate only buffers and linear images or only optimal images out of this pool and so Buffer-Image Granularity can be ignored.
    /// This is an optional optimization flag.
    /// If you always allocate using vmaCreateBuffer(), vmaCreateImage(), vmaAllocateMemoryForBuffer(), then you don't need to use it because allocator
    /// knows exact type of your allocations so it can handle Buffer-Image Granularity in the optimal way.
    /// If you also allocate using vmaAllocateMemoryForImage() or vmaAllocateMemory(), exact type of such allocations is not known, so allocator must be conservative
    /// in handling Buffer-Image Granularity, which can lead to suboptimal allocation (wasted memory). In that case, if you can make sure you always allocate only
    /// buffers and linear images or only optimal images out of this pool, use this flag to make allocator disregard Buffer-Image Granularity and so make allocations
    /// faster and more optimal.
    /// </summary>
    IgnoreBufferImageGranularity = 0x0002,

    /// <summary>
    /// Enables alternative, linear allocation algorithm in this pool.
    /// Specify this flag to enable linear allocation algorithm, which always creates new allocations after last one and doesn't reuse space from allocations freed in
    /// between. It trades memory consumption for simplified algorithm and data structure, which has better performance and uses less memory for metadata.
    /// By using this flag, you can achieve behavior of free-at-once, stack, ring buffer, and double stack.
    /// </summary>
    LinearAlgorithm = 0x0004
}
