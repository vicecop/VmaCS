namespace VmaCS;

/// <summary>
/// Parameters for a defragmentation operation.
/// </summary>
public struct DefragmentationInfo
{
    /// <summary>
    /// Allocations to consider for defragmentation. Optional.
    /// </summary>
    public Allocation[]? Allocations;

    /// <summary>
    /// Custom pool to defragment. Optional.
    /// </summary>
    public VulkanMemoryPool? Pool;

    /// <summary>
    /// Memory types to consider for defragmentation. Optional.
    /// </summary>
    public int[]? MemoryTypes;

    /// <summary>
    /// Use combination of <see cref="DefragmentationFlags"/>.
    /// </summary>
    public DefragmentationFlags Flags;

    /// <summary>
    /// Maximum number of defragmentation passes. Optional.
    /// </summary>
    public uint MaxPassCount;

    /// <summary>
    /// Maximum number of bytes to move on the GPU per pass. Optional.
    /// </summary>
    public uint MaxGpuBytesToMove;

    /// <summary>
    /// Maximum number of bytes to move on the CPU per pass. Optional.
    /// </summary>
    public uint MaxCpuBytesToMove;

    /// <summary>
    /// Maximum number of allocations to move per pass. Optional.
    /// </summary>
    public uint MaxAllocationsPerPass;

    /// <summary>
    /// Command buffer used for GPU-side memory copies during defragmentation. Optional.
    /// </summary>
    public Silk.NET.Vulkan.CommandBuffer CommandBuffer;

    /// <summary>
    /// Scratch buffer used for GPU-side memory copies during defragmentation. Optional.
    /// </summary>
    public Silk.NET.Vulkan.Buffer ScratchBuffer;
}
