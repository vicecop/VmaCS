namespace VmaCS;

internal static class ReaderWriterLockSlimExtensions
{
    public static void EnterReadLock(this ReaderWriterLockSlim l, bool useLock)
    {
        if (useLock)
        {
            l.EnterReadLock();
        }
    }

    public static void ExitReadLock(this ReaderWriterLockSlim l, bool useLock)
    {
        if (useLock)
        {
            l.ExitReadLock();
        }
    }

    public static void EnterWriteLock(this ReaderWriterLockSlim l, bool useLock)
    {
        if (useLock)
        {
            l.EnterWriteLock();
        }
    }

    public static void ExitWriteLock(this ReaderWriterLockSlim l, bool useLock)
    {
        if (useLock)
        {
            l.ExitWriteLock();
        }
    }
}
