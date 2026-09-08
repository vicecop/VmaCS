using Silk.NET.Vulkan;

namespace VmaCS;

/// <summary>
/// Numeric statistics for the allocator, broken down by memory type, memory heap, and total.
/// </summary>
public class Stats
{
    private readonly StatInfo[] _memoryType;
    private readonly StatInfo[] _memoryHeap;
    private StatInfo _total;

    /// <summary>
    /// Statistics per Vulkan memory type.
    /// </summary>
    public IReadOnlyList<StatInfo> MemoryType => _memoryType;

    /// <summary>
    /// Statistics per Vulkan memory heap.
    /// </summary>
    public IReadOnlyList<StatInfo> MemoryHeap => _memoryHeap;

    /// <summary>
    /// Total statistics across all memory types and heaps.
    /// </summary>
    public StatInfo Total => _total;

    // Internal mutation accessors: the allocator fills these via ref/out methods on StatInfo.
    internal ref StatInfo TotalRef => ref _total;
    internal StatInfo[] MemoryTypeArray => _memoryType;
    internal StatInfo[] MemoryHeapArray => _memoryHeap;

    /// <summary>
    /// Initializes a new instance of the <see cref="Stats"/> class with empty statistics.
    /// </summary>
    internal Stats()
    {
        _memoryType = new StatInfo[Vk.MaxMemoryTypes];
        _memoryHeap = new StatInfo[Vk.MaxMemoryHeaps];

        StatInfo.Init(out _total);

        for (var i = 0; i < Vk.MaxMemoryTypes; ++i)
        {
            StatInfo.Init(out _memoryType[i]);
        }

        for (var i = 0; i < Vk.MaxMemoryHeaps; ++i)
        {
            StatInfo.Init(out _memoryHeap[i]);
        }
    }

    /// <summary>
    /// Post-processes the statistics to calculate averages.
    /// </summary>
    internal void PostProcess()
    {
        StatInfo.PostProcessCalcStatInfo(ref _total);

        for (var i = 0; i < Vk.MaxMemoryTypes; ++i)
        {
            StatInfo.PostProcessCalcStatInfo(ref _memoryType[i]);
        }

        for (var i = 0; i < Vk.MaxMemoryHeaps; ++i)
        {
            StatInfo.PostProcessCalcStatInfo(ref _memoryHeap[i]);
        }
    }
}
