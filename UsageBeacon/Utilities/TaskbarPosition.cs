using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;

namespace UsageBeacon.Utilities;

/// <summary>
/// Reads taskbar and notification-area geometry through Windows APIs.
/// </summary>
public static class TaskbarPosition
{
    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string cls, string? win);
    [DllImport("user32.dll")] private static extern IntPtr FindWindowEx(IntPtr p, IntPtr a, string cls, string? win);
    [DllImport("user32.dll")] private static extern bool   EnumWindows(EnumWindowsProc proc, IntPtr parameter);
    [DllImport("user32.dll")] private static extern int    GetClassName(IntPtr hwnd, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool   GetWindowRect(IntPtr hwnd, out RECT r);

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    public record Info(
        double TaskbarTop,    double TaskbarLeft,
        double TaskbarBottom, double TaskbarRight,
        double TaskbarHeight,
        double NotifyLeft,     // Notification area's left edge in WPF logical pixels.
        double? WidgetsRight,  // Right edge of the weather/widgets area.
        double? ContentLeft,   // Left edge of centered taskbar items.
        double? ContentRight); // Right edge of centered taskbar items.

    public record CurrentBounds(Rectangle Taskbar, Rectangle? Notification);

    // This read is intentionally independent of Get(): the widget checks it
    // frequently, while Get() may run a cross-process UI Automation scan.
    public static CurrentBounds? ReadCurrentBounds(int screenIndex)
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0) return null;
        var target = screens[Math.Clamp(screenIndex, 0, screens.Length - 1)];
        var taskbar = FindTaskbar(target.Bounds);
        if (taskbar == IntPtr.Zero || !TryReadRectangle(taskbar, out var taskbarRect) ||
            taskbarRect.Width <= 4 || taskbarRect.Height <= 4)
            return null;

        var notify = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        Rectangle? notifyRect = notify != IntPtr.Zero &&
            TryReadRectangle(notify, out var measured) &&
            measured.Width > 0 && measured.Height > 0
            ? measured : null;
        return new CurrentBounds(taskbarRect, notifyRect);
    }

    public static bool TryReadRectangle(IntPtr hwnd, out Rectangle rectangle)
    {
        rectangle = default;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect)) return false;
        rectangle = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return rectangle.Width > 0 && rectangle.Height > 0;
    }

    internal static bool HasNotificationClearance(
        Rectangle widget, Rectangle notification, int clearance)
        => widget.Bottom <= notification.Top || widget.Top >= notification.Bottom ||
           widget.Right <= notification.Left - clearance;

    internal static int RequiredNotificationShift(
        Rectangle widget, Rectangle notification, int clearance)
        => Math.Max(0, widget.Right - (notification.Left - clearance));

    internal static bool IsOutsideTaskbar(Rectangle widget, Rectangle taskbar)
        => !widget.IntersectsWith(taskbar);

    internal static bool IsWithinTaskbar(Rectangle widget, Rectangle taskbar)
        => widget.Left >= taskbar.Left && widget.Top >= taskbar.Top &&
           widget.Right <= taskbar.Right && widget.Bottom <= taskbar.Bottom;

    // UI Automation scans of the taskbar are expensive cross-process queries,
    // so their results are cached and refreshed only when the cheap window
    // rectangles change, the cache is invalidated, or the entry grows stale.
    private static readonly TimeSpan UiaRescanInterval = TimeSpan.FromSeconds(5);
    private static readonly object CacheLock = new();
    private static readonly Dictionary<IntPtr, UiaCacheEntry> UiaCache = new();

    private sealed record UiaMeasurements(
        double? WidgetsRight,
        double? ClockLeft,
        double? ContentLeft,
        double? ContentRight);

    private sealed record UiaCacheEntry(
        RECT TaskbarRect,
        RECT NotifyRect,
        bool HasNotify,
        DateTime ScannedAtUtc,
        UiaMeasurements Measurements);

    /// <summary>Drops cached taskbar measurements, e.g. after a display change.</summary>
    public static void Invalidate()
    {
        lock (CacheLock) UiaCache.Clear();
    }

    internal static bool ShouldRescan(
        DateTime nowUtc,
        DateTime lastScanUtc,
        bool geometryChanged,
        TimeSpan rescanInterval)
        => geometryChanged || nowUtc - lastScanUtc >= rescanInterval;

    public static Info? Get(int screenIndex = 0)
    {
        var screens = Screen.AllScreens;
        var target = screenIndex < screens.Length ? screens[screenIndex] : screens[0];
        var taskbar = FindTaskbar(target.Bounds);
        if (taskbar == IntPtr.Zero) return null;

        if (!GetWindowRect(taskbar, out var tb)) return null;

        // An auto-hidden taskbar can be four pixels or smaller.
        // Return null so the caller can fall back to screen-edge placement.
        if (tb.Bottom - tb.Top <= 4 || tb.Right - tb.Left <= 4) return null;

        // Place the widget to the left of the notification area.
        var notify = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        var notifyRect = default(RECT);
        var hasNotify = notify != IntPtr.Zero && GetWindowRect(notify, out notifyRect);

        using var g = Graphics.FromHwnd(taskbar);
        var dpi = g.DpiX / 96.0;
        var measurements = GetUiaMeasurements(taskbar, tb, hasNotify, notifyRect, dpi);
        var rightAnchorLeft = hasNotify
            ? notifyRect.Left / dpi
            : measurements.ClockLeft ?? tb.Right / dpi;

        return new Info(
            TaskbarTop:    tb.Top    / dpi,
            TaskbarLeft:   tb.Left   / dpi,
            TaskbarBottom: tb.Bottom / dpi,
            TaskbarRight:  tb.Right  / dpi,
            TaskbarHeight: (tb.Bottom - tb.Top) / dpi,
            NotifyLeft:    rightAnchorLeft,
            WidgetsRight:  measurements.WidgetsRight,
            ContentLeft:   measurements.ContentLeft,
            ContentRight:  measurements.ContentRight);
    }

    private static UiaMeasurements GetUiaMeasurements(
        IntPtr taskbar,
        RECT taskbarRect,
        bool hasNotify,
        RECT notifyRect,
        double dpi)
    {
        var nowUtc = DateTime.UtcNow;
        lock (CacheLock)
        {
            if (UiaCache.TryGetValue(taskbar, out var entry))
            {
                var geometryChanged =
                    !entry.TaskbarRect.Equals(taskbarRect) ||
                    entry.HasNotify != hasNotify ||
                    !entry.NotifyRect.Equals(notifyRect);
                if (!ShouldRescan(nowUtc, entry.ScannedAtUtc, geometryChanged, UiaRescanInterval))
                    return entry.Measurements;
            }
        }

        var measurements = ScanUia(taskbar, taskbarRect, hasNotify, notifyRect, dpi);
        lock (CacheLock)
        {
            UiaCache[taskbar] = new UiaCacheEntry(
                taskbarRect, notifyRect, hasNotify, nowUtc, measurements);
        }
        return measurements;
    }

    private static IntPtr FindTaskbar(Rectangle targetBounds)
    {
        var match = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            var className = new StringBuilder(64);
            GetClassName(hwnd, className, className.Capacity);
            if (className.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd") ||
                !GetWindowRect(hwnd, out var rect))
                return true;

            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            if (!bounds.IntersectsWith(targetBounds)) return true;

            match = hwnd;
            return false;
        }, IntPtr.Zero);
        return match;
    }

    // One UI Automation query collects every taskbar button rectangle; the
    // three measurements are then derived from that in-memory list. The
    // content pass needs the final widgets-right value, so the passes cannot
    // be merged into a single loop.
    private static UiaMeasurements ScanUia(
        IntPtr taskbar,
        RECT taskbarRect,
        bool hasNotify,
        RECT notifyRect,
        double dpi)
    {
        try
        {
            var root = AutomationElement.FromHandle(taskbar);
            var buttons = root.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));

            var rects = new List<System.Windows.Rect>(buttons.Count);
            foreach (AutomationElement button in buttons)
            {
                var rect = button.Current.BoundingRectangle;
                if (!rect.IsEmpty &&
                    rect.Top >= taskbarRect.Top &&
                    rect.Bottom <= taskbarRect.Bottom)
                    rects.Add(rect);
            }

            // Right edge of the weather/widgets area at the taskbar's left end.
            double? widgetsRight = null;
            foreach (var rect in rects)
            {
                if (rect.Left > taskbarRect.Left + 250) continue;
                widgetsRight = widgetsRight is null
                    ? rect.Right / dpi
                    : Math.Max(widgetsRight.Value, rect.Right / dpi);
            }

            // Left edge of the rightmost button in the taskbar's right half.
            double? clockLeft = null;
            double? clockEdge = null;
            var middle = taskbarRect.Left + (taskbarRect.Right - taskbarRect.Left) / 2.0;
            foreach (var rect in rects)
            {
                if (rect.Left < middle) continue;
                if (clockEdge is null || rect.Right > clockEdge)
                {
                    clockEdge = rect.Right;
                    clockLeft = rect.Left / dpi;
                }
            }

            // Bounds of the centered taskbar items between both anchors.
            var rightAnchorLeft = hasNotify
                ? notifyRect.Left / dpi
                : clockLeft ?? taskbarRect.Right / dpi;
            var afterWidgets = (widgetsRight ?? taskbarRect.Left / dpi) + 1;
            double? contentLeft = null;
            double? contentRight = null;
            foreach (var rect in rects)
            {
                var rectLeft = rect.Left / dpi;
                var rectRight = rect.Right / dpi;
                if (rectLeft <= afterWidgets || rectRight >= rightAnchorLeft) continue;
                contentLeft = contentLeft is null
                    ? rectLeft
                    : Math.Min(contentLeft.Value, rectLeft);
                contentRight = contentRight is null
                    ? rectRight
                    : Math.Max(contentRight.Value, rectRight);
            }

            return new UiaMeasurements(widgetsRight, clockLeft, contentLeft, contentRight);
        }
        catch { return new UiaMeasurements(null, null, null, null); }
    }
}
