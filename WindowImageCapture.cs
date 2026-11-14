using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text;

namespace UltimateMacro;

internal sealed record WindowImageFrame(nint Window, Rectangle ClientBounds, Rectangle ScreenBounds, Bitmap Bitmap) : IDisposable
{
    public void Dispose() => Bitmap.Dispose();
}

internal static class WindowImageCapture
{
    internal static ImageClickTarget? DescribeForeground()
    {
        var window = Native.GetForegroundWindow();
        if (!TryDescribe(window, out var title, out var windowClass, out var process)) return null;
        return new() { WindowTitle = title, WindowClass = windowClass, ProcessName = process, SelectedWindow = window };
    }

    internal static nint ResolveForeground(ImageClickTarget target)
    {
        var window = Native.GetForegroundWindow();
        if (window == 0 || target.SelectedWindow != 0 && target.SelectedWindow != window || !MatchesIdentity(window, target)) return 0;
        if (target.SelectedWindow != 0) return window;

        var duplicate = false;
        var enumerated = Native.EnumWindows((candidate, _) =>
        {
            if (candidate == window || !MatchesIdentity(candidate, target)) return true;
            duplicate = true;
            return false;
        }, 0);
        return enumerated && !duplicate ? window : 0;
    }

    internal static nint ResolveBackground(ImageClickTarget target)
    {
        if (target.SelectedWindow != 0)
            return MatchesIdentity(target.SelectedWindow, target) ? target.SelectedWindow : 0;

        nint window = 0;
        var unique = Native.EnumWindows((candidate, _) =>
        {
            if (!MatchesIdentity(candidate, target)) return true;
            if (window != 0) return false;
            window = candidate;
            return true;
        }, 0);
        return unique ? window : 0;
    }

    internal static bool TryClientArea(nint window, out Rectangle area)
    {
        area = Rectangle.Empty;
        if (window == 0 || !Native.IsWindowVisible(window) || Native.IsIconic(window) || !Native.GetClientRect(window, out var rect)) return false;
        area = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
        return area.Width > 0 && area.Height > 0;
    }

    internal static WindowImageFrame? Capture(nint window)
    {
        if (window == 0 || Native.GetForegroundWindow() != window || !TryClientBounds(window, out var client)) return null;
        var desktop = new Rectangle(Native.GetSystemMetrics(76), Native.GetSystemMetrics(77), Native.GetSystemMetrics(78), Native.GetSystemMetrics(79));
        var screen = Rectangle.Intersect(client, desktop);
        if (screen.Width < ImageClickTarget.MinimumSide || screen.Height < ImageClickTarget.MinimumSide || (long)screen.Width * screen.Height > 20_000_000) return null;

        var bitmap = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppRgb);
        var retained = false;
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(screen.Location, Point.Empty, screen.Size, CopyPixelOperation.SourceCopy);
            if (Native.GetForegroundWindow() == window && TryClientBounds(window, out var after) && after == client)
            {
                retained = true;
                return new(window, client, screen, bitmap);
            }
        }
        catch (Win32Exception) { }
        catch (ArgumentException) { }
        finally { if (!retained) bitmap.Dispose(); }
        return null;
    }

    internal static bool CanClick(nint window, Rectangle client, Point point) => Native.GetForegroundWindow() == window &&
        TryClientBounds(window, out var current) && current == client && current.Contains(point) && BelongsTo(window, point);

    internal static bool RegionIsVisible(WindowImageFrame frame, Rectangle region, Point click)
    {
        var screenRegion = region;
        screenRegion.Offset(frame.ScreenBounds.Location);
        return CanClick(frame.Window, frame.ClientBounds, click) &&
            BelongsTo(frame.Window, screenRegion.Location) && BelongsTo(frame.Window, new Point(screenRegion.Right - 1, screenRegion.Top)) &&
            BelongsTo(frame.Window, new Point(screenRegion.Left, screenRegion.Bottom - 1)) && BelongsTo(frame.Window, new Point(screenRegion.Right - 1, screenRegion.Bottom - 1)) &&
            BelongsTo(frame.Window, new Point(screenRegion.Left + screenRegion.Width / 2, screenRegion.Top + screenRegion.Height / 2));
    }

    internal static bool MatchesIdentity(nint window, ImageClickTarget target) =>
        TryDescribe(window, out _, out _, out var process, target) &&
        string.Equals(process, target.ProcessName, StringComparison.OrdinalIgnoreCase);

    private static bool BelongsTo(nint window, Point point) => Native.GetAncestor(Native.WindowFromPoint(new Native.Point { X = point.X, Y = point.Y }), 2) == window;

    private static bool TryClientBounds(nint window, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!TryClientArea(window, out var rect)) return false;
        var topLeft = new Native.Point { X = rect.Left, Y = rect.Top };
        var bottomRight = new Native.Point { X = rect.Right, Y = rect.Bottom };
        if (!Native.ClientToScreen(window, ref topLeft) || !Native.ClientToScreen(window, ref bottomRight)) return false;
        bounds = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool TryDescribe(nint window, out string title, out string windowClass, out string processName, ImageClickTarget? expected = null)
    {
        title = windowClass = processName = "";
        if (window == 0 || !Native.IsWindowVisible(window) || Native.GetWindowThreadProcessId(window, out var processId) == 0 || processId == Environment.ProcessId) return false;
        var titleBuffer = new StringBuilder(1025);
        if (Native.GetWindowText(window, titleBuffer, titleBuffer.Capacity) == 0) return false;
        title = titleBuffer.ToString();
        if (expected is not null && !string.Equals(title, expected.WindowTitle, StringComparison.Ordinal)) return false;
        var classBuffer = new StringBuilder(257);
        if (Native.GetClassName(window, classBuffer, classBuffer.Capacity) == 0) return false;
        windowClass = classBuffer.ToString();
        if (expected is not null && !string.Equals(windowClass, expected.WindowClass, StringComparison.Ordinal)) return false;
        try
        {
            using var process = Process.GetProcessById((int)processId);
            processName = process.ProcessName;
            return !string.IsNullOrWhiteSpace(processName);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException) { return false; }
    }
}

internal readonly record struct ImageClickSearch(ImageMatchStatus Status, Point Point = default, nint Window = default, Rectangle ClientBounds = default);

internal interface IImageClickFinder : IDisposable
{
    ImageClickSearch Find(CancellationToken token);
    bool CanClick(ImageClickSearch hit);
    void IDisposable.Dispose() { }
}

internal sealed class WindowImageClickFinder : IImageClickFinder
{
    private readonly ImageClickTarget target;
    private readonly ImagePattern pattern;
    private BackgroundImageCapture? background;

    internal WindowImageClickFinder(ImageClickTarget target)
    {
        this.target = target;
        using var template = target.OpenTemplate();
        pattern = new ImagePattern(template);
    }

    public ImageClickSearch Find(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (target.Background)
        {
            var selected = WindowImageCapture.ResolveBackground(target);
            if (!WindowImageCapture.TryClientArea(selected, out var area)) return new(ImageMatchStatus.BackgroundUnavailable);
            background ??= new BackgroundImageCapture(target);
            using var result = background.Search(selected, area.Size, preview: false, token);
            if (result.Match.Status != ImageMatchStatus.Found) return new(result.Match.Status);
            var click = new Point(result.Match.Bounds.X + target.ClickX, result.Match.Bounds.Y + target.ClickY);
            return new(ImageMatchStatus.Found, click, selected, area);
        }
        var window = WindowImageCapture.ResolveForeground(target);
        using var frame = WindowImageCapture.Capture(window);
        if (frame is null) return new(ImageMatchStatus.WindowUnavailable);
        var match = Match(frame, token);
        if (match.Status != ImageMatchStatus.Found) return new(match.Status);
        var point = new Point(frame.ScreenBounds.X + match.Bounds.X + target.ClickX, frame.ScreenBounds.Y + match.Bounds.Y + target.ClickY);
        return new(ImageMatchStatus.Found, point, frame.Window, frame.ClientBounds);
    }

    internal ImageMatch Match(WindowImageFrame frame, CancellationToken token)
    {
        var match = pattern.Find(frame.Bitmap, target.MinimumSimilarity, token);
        if (match.Status != ImageMatchStatus.Found) return match;
        var point = new Point(frame.ScreenBounds.X + match.Bounds.X + target.ClickX, frame.ScreenBounds.Y + match.Bounds.Y + target.ClickY);
        return WindowImageCapture.RegionIsVisible(frame, match.Bounds, point) ? match : new(ImageMatchStatus.Obscured);
    }

    public bool CanClick(ImageClickSearch hit) => hit.Status == ImageMatchStatus.Found && WindowImageCapture.MatchesIdentity(hit.Window, target) &&
        (target.Background ? WindowImageCapture.TryClientArea(hit.Window, out var area) && area == hit.ClientBounds && area.Contains(hit.Point)
            : WindowImageCapture.CanClick(hit.Window, hit.ClientBounds, hit.Point));

    public void Dispose() => background?.Dispose();

    internal static string Describe(ImageMatchStatus status) => status switch
    {
        ImageMatchStatus.Ambiguous => "Mehrere mögliche Bildtreffer",
        ImageMatchStatus.WindowUnavailable => "Zielfenster nicht eindeutig oder nicht im Vordergrund",
        ImageMatchStatus.BackgroundUnavailable => "Zielfenster nicht eindeutig, geschlossen oder minimiert",
        ImageMatchStatus.CaptureUnavailable => "Zielfenster liefert kein verwendbares Hintergrundbild",
        ImageMatchStatus.ClickUnavailable => "Klickpunkt im Zielfenster nicht verfügbar",
        ImageMatchStatus.Obscured => "Bildziel nicht frei sichtbar",
        _ => "Kein passender Bildtreffer"
    };
}
