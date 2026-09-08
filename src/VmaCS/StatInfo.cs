namespace VmaCS;

/// <summary>
/// Internal statistics accumulator.
/// </summary>
public struct StatInfo
{
    /// <summary>
    /// Total number of VkDeviceMemory blocks.
    /// </summary>
    public int BlockCount;

    /// <summary>
    /// Total number of unused ranges.
    /// </summary>
    public int UnusedRangeCount;

    /// <summary>
    /// Total number of allocations.
    /// </summary>
    public int AllocationCount;

    /// <summary>
    /// Total size of used bytes.
    /// </summary>
    public long UsedBytes;

    /// <summary>
    /// Total size of unused bytes.
    /// </summary>
    public long UnusedBytes;

    /// <summary>
    /// Size of the smallest allocation, in bytes.
    /// </summary>
    public long AllocationSizeMin;

    /// <summary>
    /// Average size of allocations, in bytes.
    /// </summary>
    public long AllocationSizeAvg;

    /// <summary>
    /// Size of the largest allocation, in bytes.
    /// </summary>
    public long AllocationSizeMax;

    /// <summary>
    /// Size of the smallest unused range, in bytes.
    /// </summary>
    public long UnusedRangeSizeMin;

    /// <summary>
    /// Average size of unused ranges, in bytes.
    /// </summary>
    public long UnusedRangeSizeAvg;

    /// <summary>
    /// Size of the largest unused range, in bytes.
    /// </summary>
    public long UnusedRangeSizeMax;

    /// <summary>
    /// Initializes a <see cref="StatInfo"/> with default values, seeding min sizes to long.MaxValue.
    /// </summary>
    /// <param name="info">Receives the initialized statistics.</param>
    internal static void Init(out StatInfo info)
    {
        info = default;
        info.AllocationSizeMin = long.MaxValue;
        info.UnusedRangeSizeMin = long.MaxValue;
    }

    /// <summary>
    /// Adds the statistics from another <see cref="StatInfo"/> into this one.
    /// </summary>
    /// <param name="info">The accumulator to update.</param>
    /// <param name="other">The statistics to add.</param>
    internal static void Add(ref StatInfo info, in StatInfo other)
    {
        info.BlockCount += other.BlockCount;
        info.AllocationCount += other.AllocationCount;
        info.UnusedRangeCount += other.UnusedRangeCount;
        info.UsedBytes += other.UsedBytes;
        info.UnusedBytes += other.UnusedBytes;

        if (info.AllocationSizeMin > other.AllocationSizeMin)
        {
            info.AllocationSizeMin = other.AllocationSizeMin;
        }

        if (info.AllocationSizeMax < other.AllocationSizeMax)
        {
            info.AllocationSizeMax = other.AllocationSizeMax;
        }

        if (info.UnusedRangeSizeMin > other.UnusedRangeSizeMin)
        {
            info.UnusedRangeSizeMin = other.UnusedRangeSizeMin;
        }

        if (info.UnusedRangeSizeMax < other.UnusedRangeSizeMax)
        {
            info.UnusedRangeSizeMax = other.UnusedRangeSizeMax;
        }
    }

    /// <summary>
    /// Calculates average sizes from totals.
    /// </summary>
    /// <param name="info">The statistics to post-process.</param>
    internal static void PostProcessCalcStatInfo(ref StatInfo info)
    {
        info.AllocationSizeAvg = info.AllocationCount != 0 ? info.UsedBytes / info.AllocationCount : 0;
        info.UnusedRangeSizeAvg = info.UnusedRangeCount != 0 ? info.UnusedBytes / info.UnusedRangeCount : 0;
    }
}
