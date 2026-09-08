namespace VmaCS;

/// <summary>
/// Information about moves in a single defragmentation pass.
/// </summary>
public struct DefragmentationPassMoveInfo
{
    /// <summary>
    /// Array of moves to be performed in this pass.
    /// </summary>
    public DefragmentationMove[]? Moves;
}
