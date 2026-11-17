using System.Runtime.InteropServices;

namespace ComicArchiveConverter;

/// <summary>Small Windows API calls that WinForms doesn't expose directly.</summary>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    private const uint FLASHW_ALL = 3;           // flash the caption and the taskbar button
    private const uint FLASHW_TIMERNOFG = 12;    // keep flashing until the window comes to the front

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO info);

    /// <summary>Makes the taskbar button flash orange until the user switches back to the window.</summary>
    public static void FlashTaskbar(IntPtr windowHandle)
    {
        var info = new FLASHWINFO
        {
            cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
            hwnd = windowHandle,
            dwFlags = FLASHW_ALL | FLASHW_TIMERNOFG,
            uCount = uint.MaxValue,
            dwTimeout = 0,
        };
        FlashWindowEx(ref info);
    }
}
