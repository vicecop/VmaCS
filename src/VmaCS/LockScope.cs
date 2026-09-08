namespace VmaCS;

/// <summary>
/// Scoped lock holder that optionally acquires a lock for the duration of the scope. Internal use only.
/// </summary>
internal readonly ref struct LockScope : IDisposable
{
    private readonly object? _gate;
    private readonly bool _entered;

    /// <summary>
    /// Initializes a new instance of the <see cref="LockScope"/> struct.
    /// </summary>
    /// <param name="gate">The lock gate object to acquire.</param>
    /// <param name="useLock">If true, acquire the lock; otherwise, do nothing.</param>
    public LockScope(object gate, bool useLock)
    {
        _gate = useLock ? gate : null;
        _entered = useLock;
        if (useLock)
        {
            Monitor.Enter(gate);
        }
    }

    /// <summary>
    /// Releases the lock if it was acquired.
    /// </summary>
    public void Dispose()
    {
        if (_entered)
        {
            Monitor.Exit(_gate!);
        }
    }
}