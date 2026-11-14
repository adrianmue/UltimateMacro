using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltimateMacro;

public enum ActionKind
{
    Delay, KeyPress, KeyDown, KeyUp, KeyHold, KeyRepeat,
    MouseMove, MouseClick, MouseDown, MouseUp, MouseScroll, Text, ImageClick
}

public sealed class MacroAction
{
    public ActionKind Action { get; set; } = ActionKind.KeyPress;
    public string Key { get; set; } = "A";
    public int X { get; set; }
    public int Y { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UseCurrentCursorPosition { get; set; }
    public int DurationMs { get; set; } = 50;
    public int Count { get; set; } = 1;
    public int IntervalMs { get; set; } = 100;
    public string Text { get; set; } = "";
    public int ScanCode { get; set; }
    public bool Extended { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ImageClickTarget? ImageTarget { get; set; }

    public MacroAction Copy() => (MacroAction)MemberwiseClone();
}

public sealed class PlaybackOptions
{
    public int Loops { get; set; } = 1;
    public int LoopDelayMs { get; set; } = 100;
    public int StartDelayMs { get; set; } = 2000;
    public int MaximumRunSeconds { get; set; }
    public decimal Speed { get; set; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool UseScanCodes { get; set; }
}

public sealed class RecorderOptions
{
    public bool Keyboard { get; set; } = true;
    public bool MouseButtons { get; set; } = true;
    public bool MouseMovement { get; set; } = true;
    public bool MouseWheel { get; set; } = true;
    public int MovementSampleMs { get; set; } = 15;
}

public sealed class HotkeyOptions
{
    public string Record { get; set; } = "F8";
    public string StopRecording { get; set; } = "F9";
    public string Play { get; set; } = "F10";
    public string StopAll { get; set; } = "F11";
    public string[] Values() => [Record, StopRecording, Play, StopAll];
}

public sealed class MacroDocument
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "";
    public List<MacroAction> Actions { get; set; } = [];
    public PlaybackOptions Playback { get; set; } = new();
    public RecorderOptions Recording { get; set; } = new();
    public HotkeyOptions Hotkeys { get; set; } = new();
}

public static class MacroFiles
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Serialize(MacroDocument document) => JsonSerializer.Serialize(document, JsonOptions);

    public static MacroDocument Parse(string json)
    {
        var document = JsonSerializer.Deserialize<MacroDocument>(json, JsonOptions) ?? throw new InvalidDataException("Empty macro file.");
        if (document.Version != 1) throw new InvalidDataException("Unsupported macro file version.");
        if (document.Actions is null || document.Playback is null || document.Recording is null || document.Hotkeys is null)
            throw new InvalidDataException("The file is missing actions or configuration.");
        ValidateOptions(document);
        ValidateActions(document.Actions, []);
        return document;
    }

    public static void ValidateOptions(MacroDocument document)
    {
        var options = document.Playback;
        if (options.Loops < 0 || options.Loops > 1_000_000 || options.LoopDelayMs < 0 || options.LoopDelayMs > 3_600_000 ||
            options.StartDelayMs < 0 || options.StartDelayMs > 60_000 || options.MaximumRunSeconds < 0 || options.MaximumRunSeconds > 604800 ||
            options.Speed < 0.1m || options.Speed > 10m)
            throw new InvalidDataException("Playback settings are out of range.");
        if (document.Recording.MovementSampleMs < 1 || document.Recording.MovementSampleMs > 1000)
            throw new InvalidDataException("Mouse sample interval must be 1–1000 ms.");
        var hotkeys = document.Hotkeys.Values().Select(Hotkey.Parse).ToArray();
        if (hotkeys.Distinct().Count() != 4) throw new InvalidDataException("Each control hotkey must be different.");
    }

    public static void ValidateActions(IEnumerable<MacroAction> actions, HashSet<int> reservedKeys)
    {
        var row = 0;
        foreach (var action in actions)
        {
            row++;
            if (action is null) throw new InvalidDataException($"Action {row} is empty.");
            if (!Enum.IsDefined(action.Action)) throw new InvalidDataException($"Action {row}: unknown action type.");
            if (action.DurationMs < 0 || action.DurationMs > 86_400_000 || action.IntervalMs < 0 || action.IntervalMs > 86_400_000 ||
                action.Count < 0 || action.Count > 1_000_000 || action.ScanCode < 0 || action.ScanCode > 65535)
                throw new InvalidDataException($"Action {row}: duration, interval or count is out of range.");
            if (action.Action is ActionKind.KeyPress or ActionKind.KeyDown or ActionKind.KeyUp or ActionKind.KeyHold or ActionKind.KeyRepeat)
            {
                var key = KeyNames.Parse(action.Key);
                if (reservedKeys.Contains(key)) throw new InvalidDataException($"Action {row}: {action.Key} is reserved for a control hotkey. Choose a different key or hotkey.");
            }
            if (action.Action is ActionKind.MouseClick or ActionKind.MouseDown or ActionKind.MouseUp or ActionKind.ImageClick)
                KeyNames.ValidateButton(action.Key);
            if (action.Action == ActionKind.ImageClick)
            {
                if (action.ImageTarget is null) throw new InvalidDataException($"Action {row}: select an image target first.");
                action.ImageTarget.Validate();
            }
            if (action.Action == ActionKind.MouseScroll && (action.Count < 1 || action.Count > 10000 || action.Y == 0 || Math.Abs((long)action.Y) > 120000 || action.Key is not ("Vertical" or "Horizontal")))
                throw new InvalidDataException($"Action {row}: wheel needs Vertical/Horizontal, Count 1–10000 and Y from -120000 to 120000 (one notch = 120).");
            if (action.Action == ActionKind.KeyPress && action.Count < 1)
                throw new InvalidDataException($"Action {row}: key press Count must be at least 1.");
            if (action.Action == ActionKind.Text && (action.Text is null || action.Text.Length > 1_000_000))
                throw new InvalidDataException($"Action {row}: text is missing or too long.");
        }
    }
}

public static class KeyNames
{
    internal static IReadOnlyList<string> EditorChoices { get; } = [
        .. Enumerable.Range('A', 26).Select(letter => ((char)letter).ToString()),
        .. Enumerable.Range('0', 10).Select(digit => ((char)digit).ToString()),
        "Space", "Enter", "Tab", "Backspace", "Delete", "Insert", "Escape", "Home", "End", "PageUp", "PageDown",
        "Left", "Right", "Up", "Down", "Ctrl", "Shift", "Alt", "Win", "CapsLock", "NumLock", "PrintScreen",
        .. Enumerable.Range(1, 24).Select(number => $"F{number}"),
        .. Enumerable.Range(0, 10).Select(number => $"NumPad{number}")
    ];

    public static int Parse(string? name)
    {
        name = name?.Trim();
        name = name?.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => "LControlKey", "SHIFT" => "LShiftKey", "ALT" => "LMenu", "WIN" or "WINDOWS" => "LWin",
            "ENTER" => "Return", "ESC" => "Escape", "SPACEBAR" => "Space", "PAGEUP" => "Prior", "PAGEDOWN" => "Next",
            "BACKSPACE" => "Back", "DEL" => "Delete", _ => name
        };
        if (name?.Length == 1 && char.IsAsciiDigit(name[0])) name = "D" + name;
        if (!Enum.TryParse<Keys>(name, true, out var key) || !Enum.IsDefined(key) || (int)key <= 6 || (int)key > 254)
            throw new InvalidDataException($"Unknown key '{name}'. Examples: A, 1, Space, Enter, Ctrl, Left, F1.");
        return (int)key;
    }

    public static void ValidateButton(string? name)
    {
        if (name is not ("Left" or "Right" or "Middle" or "X1" or "X2"))
            throw new InvalidDataException("Mouse button must be Left, Right, Middle, X1 or X2.");
    }
}

public readonly record struct Hotkey(uint Modifiers, int Key)
{
    public static Hotkey Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("Hotkeys cannot be empty.");
        var pieces = value.Split('+', StringSplitOptions.TrimEntries);
        uint modifiers = 0;
        foreach (var piece in pieces[..^1])
            modifiers |= piece.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => 2u, "ALT" => 1u, "SHIFT" => 4u, "WIN" or "WINDOWS" => 8u,
                _ => throw new InvalidDataException($"Unknown hotkey modifier '{piece}'.")
            };
        var key = KeyNames.Parse(pieces[^1]);
        if (key == (int)Keys.F12)
            throw new InvalidDataException("Windows reserves F12 for debuggers. Choose another control hotkey, such as F11 or Ctrl+F11.");
        if (key is 16 or 17 or 18 or 91 or 92 or >= 160 and <= 165)
            throw new InvalidDataException("A hotkey needs a non-modifier key, for example Ctrl+F8.");
        return new Hotkey(modifiers, key);
    }
}
