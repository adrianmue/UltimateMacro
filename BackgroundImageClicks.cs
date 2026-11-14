using System.ComponentModel;
using System.Runtime.InteropServices;

namespace UltimateMacro;

internal interface IBackgroundImageClicks
{
    IDisposable? Press(ImageClickSearch hit, string button);
}

internal sealed class BackgroundImageClicks : IBackgroundImageClicks
{
    public IDisposable? Press(ImageClickSearch hit, string button)
    {
        if (hit.Status != ImageMatchStatus.Found || !WindowImageCapture.TryClientArea(hit.Window, out var area) ||
            area != hit.ClientBounds || !area.Contains(hit.Point) || !Native.IsWindowEnabled(hit.Window)) return null;

        var receiver = hit.Window;
        var position = new Native.Point { X = hit.Point.X, Y = hit.Point.Y };
        for (var depth = 0; depth < 32; depth++)
        {
            var child = Native.ChildWindowFromPointEx(receiver, position, 0x0001 | 0x0004);
            if (child == receiver) break;
            if (child == 0 || !Native.IsWindowEnabled(child) || depth == 31) return null;
            Native.MapWindowPoints(receiver, child, ref position, 1);
            receiver = child;
        }

        if (position.X is < 0 or > short.MaxValue || position.Y is < 0 or > short.MaxValue ||
            Native.GetAncestor(receiver, 2) != hit.Window ||
            !Native.GetClientRect(receiver, out var client) || position.X >= client.Right || position.Y >= client.Bottom ||
            Native.GetWindowThreadProcessId(receiver, out var process) == 0) return null;

        var (down, up, mask, extended) = button switch
        {
            "Left" => (0x0201u, 0x0202u, 0x0001u, 0u),
            "Right" => (0x0204u, 0x0205u, 0x0002u, 0u),
            "Middle" => (0x0207u, 0x0208u, 0x0010u, 0u),
            "X1" => (0x020Bu, 0x020Cu, 0x0020u, 0x00010000u),
            "X2" => (0x020Bu, 0x020Cu, 0x0040u, 0x00020000u),
            _ => throw new InvalidDataException("Ungültige Maustaste für den Hintergrundklick.")
        };
        var coordinates = (nint)((uint)position.X | ((uint)position.Y << 16));
        Send(receiver, down, mask | extended, coordinates);
        return new PressedButton(hit.Window, receiver, process, up, extended, coordinates);
    }

    private static void Send(nint receiver, uint message, uint buttons, nint coordinates)
    {
        if (!Native.PostMessage(receiver, message, buttons, coordinates))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows hat den Hintergrundklick abgewiesen. Prüfe, ob das Zielfenster noch geöffnet ist und nicht mit höheren Rechten läuft.");
    }

    private sealed class PressedButton(nint root, nint receiver, uint process, uint message, uint buttons, nint coordinates) : IDisposable
    {
        private bool released;

        public void Dispose()
        {
            if (released) return;
            released = true;
            if (Native.GetWindowThreadProcessId(receiver, out var currentProcess) != 0 && currentProcess == process && Native.GetAncestor(receiver, 2) == root)
                Send(receiver, message, buttons, coordinates);
        }
    }
}
