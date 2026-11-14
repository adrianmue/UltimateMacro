using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace UltimateMacro;

internal static class Native
{
    internal static readonly nuint Marker = 0x554D4143;
    internal delegate nint HookProc(int code, nuint message, nint data);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left; public int Top; public int Right; public int Bottom; }
    internal delegate bool EnumWindowProc(nint window, nint data);
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHook { public uint Key; public uint Scan; public uint Flags; public uint Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseHook { public Point Point; public uint Data; public uint Flags; public uint Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseInput { public int X; public int Y; public uint Data; public uint Flags; public uint Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyInput { public ushort Key; public ushort Scan; public uint Flags; public uint Time; public nuint Extra; }
    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Input { public uint Type; public InputUnion Data; }

    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook, int code, nuint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] internal static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool UnregisterHotKey(nint window, int id);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", ExactSpelling = true)] internal static extern uint MapVirtualKeyExW(uint key, uint mode, nint layout);
    [DllImport("user32.dll")] internal static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowEnabled(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ClientToScreen(nint window, ref Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint window, StringBuilder text, int capacity);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint window, StringBuilder name, int capacity);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool EnumWindows(EnumWindowProc callback, nint data);
    [DllImport("user32.dll")] internal static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] internal static extern nint ChildWindowFromPointEx(nint window, Point point, uint flags);
    [DllImport("user32.dll")] internal static extern int MapWindowPoints(nint from, nint to, ref Point point, uint count);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool PrintWindow(nint window, nint deviceContext, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool AttachThreadInput(uint thread, uint otherThread, [MarshalAs(UnmanagedType.Bool)] bool attach);

    internal static void Send(Input input)
    {
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows blocked simulated input. If the target is running as administrator, run Ultimate Macro as administrator too.");
    }
}

public interface IInputSink
{
    (int Scan, bool Extended) ResolveKey(int key, int scan, bool extended, bool useScanCodes) => (scan, extended);
    void Key(int key, bool down, int scan = 0, bool extended = false);
    void Unicode(char character, bool down);
    void Move(int x, int y);
    void Button(string button, bool down);
    void Scroll(bool horizontal, int delta);
}

public sealed class WindowsInputSink : IInputSink
{
    public (int Scan, bool Extended) ResolveKey(int key, int scan, bool extended, bool useScanCodes)
    {
        if (scan != 0) return (scan, extended);
        var thread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
        var mapped = Native.MapVirtualKeyExW((uint)key, 4, Native.GetKeyboardLayout(thread));
        var prefix = mapped & 0xff00;
        var mappedScan = (int)(mapped & 0xff);
        var isExtended = prefix == 0xe000;
        if (!useScanCodes || mappedScan == 0 || prefix is not (0 or 0xe000)) return (0, extended || isExtended);
        var navigationKey = key is >= (int)Keys.Prior and <= (int)Keys.Down or (int)Keys.Insert or (int)Keys.Delete;
        return (mappedScan, extended || isExtended || navigationKey);
    }

    public void Key(int key, bool down, int scan = 0, bool extended = false) => Native.Send(new Native.Input
    {
        Type = 1, Data = new Native.InputUnion { Keyboard = CreateKeyInput(key, down, scan, extended) }
    });

    internal static Native.KeyInput CreateKeyInput(int key, bool down, int scan, bool extended) => new()
    {
        Key = scan > 0 ? (ushort)0 : (ushort)key, Scan = (ushort)scan,
        Flags = (down ? 0u : 2u) | (scan > 0 ? 8u : 0u) | (extended ? 1u : 0u), Extra = Native.Marker
    };

    public void Unicode(char character, bool down) => Native.Send(new Native.Input
    {
        Type = 1, Data = new Native.InputUnion { Keyboard = new Native.KeyInput { Scan = character, Flags = 4u | (down ? 0u : 2u), Extra = Native.Marker } }
    });

    public void Move(int x, int y)
    {
        var left = Native.GetSystemMetrics(76);
        var top = Native.GetSystemMetrics(77);
        var width = Native.GetSystemMetrics(78);
        var height = Native.GetSystemMetrics(79);
        SendMouse(0x0001 | 0x8000 | 0x4000, 0,
            (int)Math.Clamp(((long)x - left) * 65536 / Math.Max(1, width), 0, 65535),
            (int)Math.Clamp(((long)y - top) * 65536 / Math.Max(1, height), 0, 65535));
    }

    public void Button(string button, bool down)
    {
        KeyNames.ValidateButton(button);
        var flags = button switch { "Left" => down ? 2u : 4u, "Right" => down ? 8u : 16u, "Middle" => down ? 32u : 64u, _ => down ? 128u : 256u };
        SendMouse(flags, button == "X1" ? 1u : button == "X2" ? 2u : 0u);
    }

    public void Scroll(bool horizontal, int delta) => SendMouse(horizontal ? 0x1000u : 0x800u, unchecked((uint)delta));
    private static void SendMouse(uint flags, uint data, int x = 0, int y = 0) => Native.Send(new Native.Input
    {
        Type = 0, Data = new Native.InputUnion { Mouse = new Native.MouseInput { X = x, Y = y, Data = data, Flags = flags, Extra = Native.Marker } }
    });
}

public sealed class HotkeyRegistration : IDisposable
{
    private readonly nint window;
    private Hotkey[] registered = [];
    public HotkeyOptions Current { get; private set; } = new();
    public bool IsRegistered => registered.Length == 4;
    public HashSet<int> ReservedKeys => Current.Values().Select(value => Hotkey.Parse(value).Key).ToHashSet();
    public HotkeyRegistration(nint window) => this.window = window;

    public void Apply(HotkeyOptions options)
    {
        var parsed = options.Values().Select(Hotkey.Parse).ToArray();
        if (parsed.Distinct().Count() != 4) throw new InvalidDataException("Each control hotkey must be different.");
        if (registered.SequenceEqual(parsed)) { Current = options; return; }
        var previous = registered;
        Unregister();
        for (var index = 0; index < parsed.Length; index++)
        {
            if (Native.RegisterHotKey(window, index + 1, parsed[index].Modifiers | 0x4000, (uint)parsed[index].Key)) continue;
            var windowsError = Marshal.GetLastWin32Error();
            Unregister();
            var restored = true;
            for (var restore = 0; restore < previous.Length; restore++)
                restored &= Native.RegisterHotKey(window, restore + 1, previous[restore].Modifiers | 0x4000, (uint)previous[restore].Key);
            if (restored) registered = previous;
            else Unregister();
            throw new InvalidOperationException($"Cannot register {options.Values()[index]} (Windows error {windowsError}). Another program may be using it. Choose a different hotkey.");
        }
        Current = options;
        registered = parsed;
    }

    private void Unregister() { for (var index = 1; index <= 4; index++) Native.UnregisterHotKey(window, index); registered = []; }
    public void Dispose() => Unregister();
}
