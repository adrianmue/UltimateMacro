namespace UltimateMacro;

internal static class ActionDisplay
{
    internal static string Name(ActionKind action) => action switch
    {
        ActionKind.Delay => "Warten",
        ActionKind.KeyPress => "Taste tippen",
        ActionKind.KeyDown => "Taste drücken",
        ActionKind.KeyUp => "Taste loslassen",
        ActionKind.KeyHold => "Taste halten",
        ActionKind.KeyRepeat => "Taste wiederholen",
        ActionKind.MouseMove => "Maus bewegen",
        ActionKind.MouseClick => "Mausklick",
        ActionKind.MouseDown => "Maustaste drücken",
        ActionKind.MouseUp => "Maustaste loslassen",
        ActionKind.MouseScroll => "Scrollen",
        ActionKind.Text => "Text eingeben",
        ActionKind.ImageClick => "Bildziel klicken",
        _ => action.ToString()
    };

    internal static string Target(MacroAction action) => action.Action switch
    {
        ActionKind.Delay => "—",
        ActionKind.MouseMove => $"X {action.X} · Y {action.Y}",
        ActionKind.MouseClick when action.UseCurrentCursorPosition => $"{ButtonName(action.Key)} · Aktuelle Mausposition",
        ActionKind.MouseClick or ActionKind.MouseDown or ActionKind.MouseUp => $"{ButtonName(action.Key)} · X {action.X} · Y {action.Y}",
        ActionKind.MouseScroll => action.Key == "Horizontal" ? "Horizontal" : "Vertikal",
        ActionKind.Text => TextPreview(action.Text),
        ActionKind.ImageClick => action.ImageTarget?.WindowTitle ?? "Bildziel nicht eingerichtet",
        _ => action.Key
    };

    internal static string Details(MacroAction action) => action.Action switch
    {
        ActionKind.Delay => $"{action.DurationMs:N0} ms",
        ActionKind.KeyPress or ActionKind.MouseClick => Taps(action),
        ActionKind.KeyHold => action.DurationMs == 0 ? "Bis zum Stopp" : $"{action.DurationMs:N0} ms halten",
        ActionKind.KeyRepeat when action.DurationMs > 0 && action.Count > 0 => $"{action.Count:N0}× in {action.DurationMs:N0} ms",
        ActionKind.KeyRepeat => $"{(action.Count == 0 && action.DurationMs > 0 ? $"{action.DurationMs:N0} ms lang" : Count(action.Count))} · Intervall {action.IntervalMs:N0} ms",
        ActionKind.MouseMove => action.DurationMs == 0 ? "Sofort" : $"In {action.DurationMs:N0} ms",
        ActionKind.MouseScroll => $"{action.Y} × {action.Count:N0} Scroll-Einheiten",
        ActionKind.Text => $"{action.Text.Length:N0} Zeichen" + (action.IntervalMs > 0 ? $" · {action.IntervalMs:N0} ms Abstand" : ""),
        ActionKind.ImageClick => $"{(action.ImageTarget is { Background: true } ? "Hintergrund" : "Vordergrund")} · {ButtonName(action.Key)} · {Taps(action)}",
        _ => action.ScanCode > 0 ? "Aufgezeichneter Tastencode" : "—"
    };

    private static string Taps(MacroAction action) => $"{Count(action.Count)} · {action.DurationMs:N0} ms halten" +
        (action.Count == 1 ? "" : $" · {action.IntervalMs:N0} ms Abstand");

    private static string Count(int count) => count == 0 ? "Bis zum Stopp" : $"{count:N0}×";

    private static string ButtonName(string key) => key switch { "Left" => "Links", "Right" => "Rechts", "Middle" => "Mitte", _ => key };

    private static string TextPreview(string text) => (text.Length > 120 ? text[..120] + "…" : text).Replace('\r', ' ').Replace('\n', ' ');
}
