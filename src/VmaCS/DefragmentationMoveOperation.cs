namespace VmaCS;

/// <summary>
/// Operation to be performed for a single allocation during defragmentation.
/// </summary>
public enum DefragmentationMoveOperation
{
    /// <summary>
    /// Buffer/image has been recreated at dstTmpAllocation, data has been copied, old buffer/image has been destroyed.
    /// srcAllocation should be changed to point to the new place.
    /// </summary>
    Copy = 0,

    /// <summary>
    /// Set this value if you cannot move the allocation. New place reserved at dstTmpAllocation will be freed.
    /// srcAllocation will remain unchanged.
    /// </summary>
    Ignore = 1,

    /// <summary>
    /// Set this value if you want to destroy the allocation.
    /// </summary>
    Destroy = 2
}
