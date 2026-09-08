namespace VmaCS;

/// <summary>
/// Represents a single allocation move during defragmentation.
/// </summary>
public sealed class DefragmentationMove
{
    /// <summary>
    /// Operation to perform for this move. The consumer must set this to <see cref="DefragmentationMoveOperation.Ignore"/>
    /// or <see cref="DefragmentationMoveOperation.Destroy"/> on returned move objects to tell PassEnd which moves to skip.
    /// Because PassBegin hands back the same <see cref="DefragmentationMove"/> references the context keeps internally,
    /// this mutation propagates into the context (mirrors C++ VmaDefragmentationMove.operation).
    /// </summary>
    public DefragmentationMoveOperation Operation { get; set; }

    /// <summary>
    /// Source allocation being moved from. Null if no source.
    /// </summary>
    public BlockAllocation? Source { get; set; }

    /// <summary>
    /// Destination allocation being moved to. Null if no destination.
    /// </summary>
    public BlockAllocation? Destination { get; set; }
}

