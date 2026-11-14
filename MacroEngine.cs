using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UltimateMacro;

public sealed class MacroPlayer
{
    private readonly IInputSink sink;
    private readonly Func<ImageClickTarget, IImageClickFinder> imageFinder;
    private readonly IBackgroundImageClicks backgroundClicks;
    private readonly Dictionary<int, (int Scan, bool Extended)> heldKeys = [];
    private readonly HashSet<string> heldButtons = [];
    private bool useScanCodes;
    public event Action<int, int>? Progress;
    public event Action<string?>? ImageTargetStatus;
    public MacroPlayer(IInputSink sink) : this(sink, target => new WindowImageClickFinder(target)) { }
    internal MacroPlayer(IInputSink sink, Func<ImageClickTarget, IImageClickFinder> imageFinder, IBackgroundImageClicks? backgroundClicks = null)
    {
        this.sink = sink;
        this.imageFinder = imageFinder;
        this.backgroundClicks = backgroundClicks ?? new BackgroundImageClicks();
    }

    public async Task RunAsync(IReadOnlyList<MacroAction> actions, PlaybackOptions options, CancellationToken cancellation)
    {
        useScanCodes = options.UseScanCodes;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        try
        {
            await Wait(options.StartDelayMs, 1, cancellation);
            if (options.MaximumRunSeconds > 0) deadline.CancelAfter(TimeSpan.FromSeconds(options.MaximumRunSeconds));
            var token = deadline.Token;
            for (var loop = 0; options.Loops == 0 || loop < options.Loops; loop++)
            {
                for (var index = 0; index < actions.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    Progress?.Invoke(loop + 1, index + 1);
                    await Execute(actions[index], options.Speed, token);
                }
                ReleaseAll();
                if (options.Loops != 0 && loop + 1 >= options.Loops) break;
                await Wait(Math.Max(1, options.LoopDelayMs), options.Speed, token);
            }
        }
        finally { ReleaseAll(); }
    }

    private async Task Execute(MacroAction action, decimal speed, CancellationToken token)
    {
        var key = action.Action is ActionKind.KeyPress or ActionKind.KeyDown or ActionKind.KeyUp or ActionKind.KeyHold or ActionKind.KeyRepeat
            ? KeyNames.Parse(action.Key) : 0;
        switch (action.Action)
        {
            case ActionKind.Delay:
                await Wait(action.DurationMs, speed, token);
                break;
            case ActionKind.KeyDown: SetKey(key, true, action); break;
            case ActionKind.KeyUp: SetKey(key, false, action); break;
            case ActionKind.KeyPress:
                for (var count = 0; count < action.Count; count++)
                {
                    token.ThrowIfCancellationRequested();
                    await Tap(key, action.DurationMs, action, speed, token);
                    if (count + 1 < action.Count) await Wait(Math.Max(1, action.IntervalMs), speed, token);
                }
                break;
            case ActionKind.KeyHold:
                SetKey(key, true, action);
                try
                {
                    if (action.DurationMs == 0) await Task.Delay(Timeout.Infinite, token);
                    else await Wait(action.DurationMs, speed, token);
                }
                finally { SetKey(key, false, action); }
                break;
            case ActionKind.KeyRepeat:
                await Repeat(action, key, speed, token);
                break;
            case ActionKind.MouseMove:
                if (action.DurationMs <= 0) sink.Move(action.X, action.Y);
                else
                {
                    var origin = Cursor.Position;
                    var watch = Stopwatch.StartNew();
                    var duration = (double)(action.DurationMs / speed);
                    while (watch.Elapsed.TotalMilliseconds < duration)
                    {
                        token.ThrowIfCancellationRequested();
                        var fraction = Math.Min(1, watch.Elapsed.TotalMilliseconds / duration);
                        sink.Move((int)(origin.X + ((long)action.X - origin.X) * fraction), (int)(origin.Y + ((long)action.Y - origin.Y) * fraction));
                        await Task.Delay(8, token);
                    }
                    sink.Move(action.X, action.Y);
                }
                break;
            case ActionKind.MouseClick:
                sink.Move(action.X, action.Y);
                for (var count = 0L; action.Count == 0 || count < action.Count; count++)
                {
                    token.ThrowIfCancellationRequested();
                    await Click(action, speed, token);
                    if (action.Count == 0 || count + 1 < action.Count) await Wait(Math.Max(1, action.IntervalMs), speed, token);
                }
                break;
            case ActionKind.ImageClick:
                await ClickImage(action, speed, token);
                break;
            case ActionKind.MouseDown:
                sink.Move(action.X, action.Y);
                SetButton(action.Key, true);
                break;
            case ActionKind.MouseUp:
                sink.Move(action.X, action.Y);
                SetButton(action.Key, false);
                break;
            case ActionKind.MouseScroll:
                sink.Scroll(action.Key == "Horizontal", checked(action.Y * action.Count));
                break;
            case ActionKind.Text:
                for (var index = 0; index < action.Text.Length; index++)
                {
                    token.ThrowIfCancellationRequested();
                    sink.Unicode(action.Text[index], true);
                    try { token.ThrowIfCancellationRequested(); }
                    finally { sink.Unicode(action.Text[index], false); }
                    if (action.IntervalMs > 0) await Wait(action.IntervalMs, speed, token);
                    else if (index % 128 == 0) await Task.Delay(1, token);
                }
                break;
        }
    }

    private async Task ClickImage(MacroAction action, decimal speed, CancellationToken token)
    {
        var target = action.ImageTarget ?? throw new InvalidDataException("Für diesen Bildklick wurde kein Ziel ausgewählt.");
        using var finder = imageFinder(target);
        try
        {
            for (var count = 0L; action.Count == 0 || count < action.Count; count++)
            {
                var wait = Stopwatch.StartNew();
                var limitMs = target.WaitSeconds * 1000L;
                var reason = ImageMatchStatus.Missing;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                if (limitMs != 0) deadline.CancelAfter(TimeSpan.FromMilliseconds(limitMs));
                while (limitMs == 0 || wait.ElapsedMilliseconds < limitMs)
                {
                    token.ThrowIfCancellationRequested();
                    ImageClickSearch hit;
                    try { hit = finder.Find(deadline.Token); }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested) { break; }
                    token.ThrowIfCancellationRequested();
                    reason = hit.Status;
                    if (hit.Status == ImageMatchStatus.Found)
                    {
                        if (limitMs != 0 && wait.ElapsedMilliseconds >= limitMs) { reason = ImageMatchStatus.Missing; break; }
                        if (finder.CanClick(hit))
                        {
                            if (target.Background)
                            {
                                token.ThrowIfCancellationRequested();
                                using var pressed = backgroundClicks.Press(hit, action.Key);
                                if (pressed is not null)
                                {
                                    ImageTargetStatus?.Invoke(null);
                                    await Wait(Math.Max(1, action.DurationMs), speed, token);
                                    break;
                                }
                            }
                            else
                            {
                                sink.Move(hit.Point.X, hit.Point.Y);
                                if (finder.CanClick(hit))
                                {
                                    token.ThrowIfCancellationRequested();
                                    ImageTargetStatus?.Invoke(null);
                                    await Click(action, speed, token);
                                    break;
                                }
                            }
                        }
                        reason = target.Background ? ImageMatchStatus.ClickUnavailable : ImageMatchStatus.Obscured;
                    }
                    ImageTargetStatus?.Invoke(WindowImageClickFinder.Describe(reason));
                    await Task.Delay(limitMs == 0 ? 150 : (int)Math.Clamp(limitMs - wait.ElapsedMilliseconds, 1, 150), token);
                }
                if (reason != ImageMatchStatus.Found)
                    throw new TimeoutException($"Bildziel: {WindowImageClickFinder.Describe(reason)}. Nach {target.WaitSeconds} Sekunden ohne Klick gestoppt.");
                if (action.Count == 0 || count + 1 < action.Count) await Wait(Math.Max(1, action.IntervalMs), speed, token);
            }
        }
        finally { ImageTargetStatus?.Invoke(null); }
    }

    private async Task Click(MacroAction action, decimal speed, CancellationToken token)
    {
        SetButton(action.Key, true);
        try { await Wait(Math.Max(1, action.DurationMs), speed, token); }
        finally { SetButton(action.Key, false); }
    }

    private async Task Repeat(MacroAction action, int key, decimal speed, CancellationToken token)
    {
        using var periodEnd = CancellationTokenSource.CreateLinkedTokenSource(token);
        if (action.DurationMs > 0) periodEnd.CancelAfter(TimeSpan.FromMilliseconds((double)(action.DurationMs / speed)));
        var repeatToken = periodEnd.Token;
        var interval = action.Count > 0 && action.DurationMs > 0
            ? Math.Max(1.0, (double)action.DurationMs / action.Count) : Math.Max(1, action.IntervalMs);
        var watch = Stopwatch.StartNew();
        try
        {
            for (var count = 0; action.Count == 0 || count < action.Count; count++)
            {
                repeatToken.ThrowIfCancellationRequested();
                var due = count * interval / (double)speed;
                var remaining = due - watch.Elapsed.TotalMilliseconds;
                if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), repeatToken);
                await Tap(key, (int)Math.Min(30, interval / 2), action, speed, repeatToken);
            }
            if (action.Count > 0 && action.DurationMs > 0)
            {
                var remaining = (double)(action.DurationMs / speed) - watch.Elapsed.TotalMilliseconds;
                if (remaining > 0) await Task.Delay(TimeSpan.FromMilliseconds(remaining), repeatToken);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && periodEnd.IsCancellationRequested) { }
    }

    private async Task Tap(int key, int duration, MacroAction action, decimal speed, CancellationToken token)
    {
        SetKey(key, true, action);
        try { await Wait(Math.Max(1, duration), speed, token); }
        finally { SetKey(key, false, action); }
    }

    private void SetKey(int key, bool down, MacroAction action)
    {
        if (!heldKeys.TryGetValue(key, out var details) || (down && action.ScanCode != 0))
            details = sink.ResolveKey(key, action.ScanCode, action.Extended, useScanCodes);
        if (down) heldKeys[key] = details;
        sink.Key(key, down, details.Scan, details.Extended);
        if (!down) heldKeys.Remove(key);
    }

    private void SetButton(string button, bool down)
    {
        if (down) heldButtons.Add(button);
        sink.Button(button, down);
        if (!down) heldButtons.Remove(button);
    }

    private void ReleaseAll()
    {
        List<Exception> errors = [];
        foreach (var entry in heldKeys.ToArray())
            try { sink.Key(entry.Key, false, entry.Value.Scan, entry.Value.Extended); }
            catch (Exception error) { errors.Add(error); }
        foreach (var button in heldButtons.ToArray())
            try { sink.Button(button, false); }
            catch (Exception error) { errors.Add(error); }
        heldKeys.Clear();
        heldButtons.Clear();
        if (errors.Count > 0) throw new AggregateException("Windows could not release one or more held inputs.", errors);
    }

    private static Task Wait(int milliseconds, decimal speed, CancellationToken token) => milliseconds <= 0
        ? Task.CompletedTask : Task.Delay(TimeSpan.FromMilliseconds(Math.Max(1, (double)(milliseconds / speed))), token);
}

public sealed class MacroRecorder : IDisposable
{
    private readonly Native.HookProc keyboardProc;
    private readonly Native.HookProc mouseProc;
    private nint keyboardHook;
    private nint mouseHook;
    private readonly Stopwatch watch = new();
    private readonly List<MacroAction> actions = [];
    private readonly HashSet<int> initiallyHeld = [];
    private HashSet<int> reserved = [];
    private RecorderOptions options = new();
    private long lastEvent;
    private long lastMovement;
    public bool IsRecording { get; private set; }
    public int EventCount => actions.Count;
    public event Action? LimitReached;

    public MacroRecorder()
    {
        keyboardProc = Keyboard;
        mouseProc = Mouse;
    }

    public void Start(RecorderOptions settings, HashSet<int> reservedKeys)
    {
        if (IsRecording) return;
        options = settings;
        reserved = reservedKeys;
        actions.Clear();
        initiallyHeld.Clear();
        for (var key = 1; key < 255; key++)
            if (Native.GetAsyncKeyState(key) < 0) initiallyHeld.Add(key);
        lastEvent = 0;
        lastMovement = -1000;
        var module = Native.GetModuleHandle(null);
        keyboardHook = Native.SetWindowsHookEx(13, keyboardProc, module, 0);
        mouseHook = Native.SetWindowsHookEx(14, mouseProc, module, 0);
        if (keyboardHook == 0 || mouseHook == 0)
        {
            Dispose();
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows could not start the input recorder.");
        }
        watch.Restart();
        IsRecording = true;
    }

    public List<MacroAction> Stop()
    {
        if (IsRecording && watch.ElapsedMilliseconds > lastEvent)
            actions.Add(new MacroAction { Action = ActionKind.Delay, DurationMs = (int)Math.Min(86_400_000, watch.ElapsedMilliseconds - lastEvent) });
        Dispose();
        return actions.Select(action => action.Copy()).ToList();
    }

    private nint Keyboard(int code, nuint message, nint data)
    {
        if (code >= 0 && IsRecording && options.Keyboard)
        {
            var info = Marshal.PtrToStructure<Native.KeyboardHook>(data);
            var key = (int)info.Key;
            var down = message is 0x100 or 0x104;
            if (info.Extra != Native.Marker && !reserved.Contains(key))
            {
                if (initiallyHeld.Contains(key)) { if (!down) initiallyHeld.Remove(key); }
                else if (message is 0x100 or 0x104 or 0x101 or 0x105)
                    Add(new MacroAction { Action = down ? ActionKind.KeyDown : ActionKind.KeyUp, Key = ((Keys)key).ToString(), ScanCode = (int)info.Scan, Extended = (info.Flags & 1) != 0 });
            }
        }
        return Native.CallNextHookEx(keyboardHook, code, message, data);
    }

    private nint Mouse(int code, nuint message, nint data)
    {
        if (code >= 0 && IsRecording)
        {
            var info = Marshal.PtrToStructure<Native.MouseHook>(data);
            if (info.Extra != Native.Marker)
            {
                var now = watch.ElapsedMilliseconds;
                if (message == 0x200 && options.MouseMovement && now - lastMovement >= options.MovementSampleMs)
                {
                    lastMovement = now;
                    Add(new MacroAction { Action = ActionKind.MouseMove, X = info.Point.X, Y = info.Point.Y, DurationMs = 0 });
                }
                else if (message is 0x20A or 0x20E && options.MouseWheel)
                {
                    Add(new MacroAction { Action = ActionKind.MouseMove, X = info.Point.X, Y = info.Point.Y, DurationMs = 0 });
                    Add(new MacroAction { Action = ActionKind.MouseScroll, Key = message == 0x20E ? "Horizontal" : "Vertical", Y = (short)(info.Data >> 16), Count = 1 });
                }
                else if (options.MouseButtons)
                {
                    var button = message switch { 0x201 or 0x202 => "Left", 0x204 or 0x205 => "Right", 0x207 or 0x208 => "Middle", 0x20B or 0x20C => (info.Data >> 16) == 1 ? "X1" : "X2", _ => "" };
                    if (button.Length > 0)
                    {
                        var virtualKey = button switch { "Left" => 1, "Right" => 2, "Middle" => 4, "X1" => 5, _ => 6 };
                        var down = message is 0x201 or 0x204 or 0x207 or 0x20B;
                        if (initiallyHeld.Contains(virtualKey)) { if (!down) initiallyHeld.Remove(virtualKey); }
                        else Add(new MacroAction { Action = down ? ActionKind.MouseDown : ActionKind.MouseUp, Key = button, X = info.Point.X, Y = info.Point.Y });
                    }
                }
            }
        }
        return Native.CallNextHookEx(mouseHook, code, message, data);
    }

    private void Add(MacroAction action)
    {
        if (actions.Count >= 200_000) { IsRecording = false; LimitReached?.Invoke(); return; }
        var now = watch.ElapsedMilliseconds;
        if (now > lastEvent) actions.Add(new MacroAction { Action = ActionKind.Delay, DurationMs = (int)Math.Min(86_400_000, now - lastEvent) });
        actions.Add(action);
        lastEvent = now;
    }

    public void Dispose()
    {
        IsRecording = false;
        watch.Stop();
        if (keyboardHook != 0) Native.UnhookWindowsHookEx(keyboardHook);
        if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
        keyboardHook = mouseHook = 0;
    }
}
