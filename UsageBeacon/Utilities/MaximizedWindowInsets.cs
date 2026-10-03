using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace UsageBeacon.Utilities;

/// <summary>
/// Measures how far a maximized borderless window extends past its monitor's work area.
/// </summary>
public static class MaximizedWindowInsets
{
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    /// <summary>
    /// Returns the overhang in DIPs, or zero when the window is not maximized or cannot be measured.
    /// </summary>
    public static Thickness Get(Window window)
    {
        if (window.WindowState != WindowState.Maximized) return new Thickness(0);
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var bounds)) return new Thickness(0);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        var monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return new Thickness(0);

        var dpi = VisualTreeHelper.GetDpi(window);
        return Compute(
            new Int32Rect(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top),
            new Int32Rect(info.Work.Left, info.Work.Top,
                info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top),
            dpi.DpiScaleX, dpi.DpiScaleY);
    }

    /// <summary>
    /// Converts the physical-pixel distance from window bounds to the work area into DIPs.
    /// </summary>
    public static Thickness Compute(Int32Rect window, Int32Rect work, double scaleX, double scaleY)
        => new(
            Math.Max(0, work.X - window.X) / scaleX,
            Math.Max(0, work.Y - window.Y) / scaleY,
            Math.Max(0, window.X + window.Width - (work.X + work.Width)) / scaleX,
            Math.Max(0, window.Y + window.Height - (work.Y + work.Height)) / scaleY);
}
