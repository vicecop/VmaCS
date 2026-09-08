using System.Runtime.InteropServices;

namespace VmaCS;

internal static class Win32Native
{
    [DllImport("kernel32", SetLastError = true)]
    public static extern bool CloseHandle(nint handle);

    [DllImport("kernel32", SetLastError = true)]
    public static extern nint GetCurrentProcess();

    [DllImport("kernel32", SetLastError = true)]
    public static extern bool DuplicateHandle(
        nint hSourceProcessHandle,
        nint hSourceHandle,
        nint hTargetProcessHandle,
        out nint lpTargetHandle,
        uint dwDesiredAccess,
        bool bInheritHandle,
        uint dwOptions);
}
