using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace UltimateMacro;

internal static partial class SelfTests
{
    private static readonly List<string> results = [];

    [DllImport("user32.dll", EntryPoint = "SendMessageW", ExactSpelling = true)]
    private static extern nint SendTestWindowMessage(nint window, uint message, nuint wParam, nint lParam);

    internal static void Run(bool includeNative = true, bool testCapture = false, bool testDrag = false)
    {
        using var form = new Form { Text = "Ultimate Macro verification", Size = new Size(600, 260), StartPosition = FormStartPosition.CenterScreen };
        var text = new TextBox { Multiline = true, Dock = DockStyle.Fill };
        form.Controls.Add(text);
        form.Shown += async (_, _) =>
        {
            try
            {
                TestModels();
                TestActionOrder();
                TestLibrary();
                await TestImageTargets();
                await TestMouseClickRepetition();
                await TestPlayback();
                await TestScanCodePlayback();
                await TestKeySequenceLoop();
                if (testCapture)
                {
                    await TestWindowCapture(form);
                    await TestBackgroundWindow(form);
                }
                if (includeNative) await TestNative(form, text);
                else results.Add("SKIP: Native foreground input integration was excluded from this run.");
                form.Hide();
                using (var verification = new VerificationDirectory())
                {
                    var document = MacroFiles.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Examples", "04 - Type text and shortcut.macro.json")));
                    var library = new MacroLibrary(verification.Path);
                    library.Save("Sample text & shortcut", document, false);
                    var windowsDark = false;
                    using var main = new MainForm(verification.Path, () => windowsDark);
                    main.Show();
                    await Task.Delay(150);
                    Render(main, "empty-preview.png");
                    var grid = FindControls(main).OfType<DataGridView>().Single();
                    var binding = (BindingSource)grid.DataSource!;
                    var list = (System.ComponentModel.BindingList<MacroAction>)binding.DataSource!;
                    var selector = FindControls(main).OfType<ListBox>().Single(input => input.Name == "SavedMacros");
                    var name = FindControls(main).OfType<TextBox>().Single(input => input.Name == "MacroName");
                    var open = FindControls(main).OfType<Button>().Single(button => button.Name == "OpenSavedMacro");
                    var save = FindControls(main).OfType<Button>().Single(button => button.Name == "SaveMacro");
                    open.PerformClick();
                    Check(list.Count == document.Actions.Count && name.Text == "Sample text & shortcut", "Saved-macro selector loads the named list into the editor");
                    Check(grid.Columns.Cast<DataGridViewColumn>().Select(column => column.HeaderText).SequenceEqual(["Aktion", "Ziel", "Details"]) &&
                        (string?)grid.Rows[0].Cells[0].FormattedValue == "Text eingeben", "The redesigned grid shows a three-column overview with descriptive action names");
                    var actionGrid = (ActionGrid)grid;
                    var firstRow = grid.GetRowDisplayRectangle(0, false);
                    var lastRow = grid.GetRowDisplayRectangle(grid.Rows.Count - 1, false);
                    Check(actionGrid.AllowDrop && actionGrid.InsertionAt(new Point(60, firstRow.Top + 5)) == 0 &&
                        actionGrid.InsertionAt(new Point(60, firstRow.Bottom - 5)) == 1 && actionGrid.InsertionAt(new Point(60, lastRow.Bottom + 5)) == list.Count &&
                        actionGrid.InsertionAt(new Point(60, 3)) == -1, "Drag targets distinguish before/after rows, end of list, and the column header");
                    name.Text = "Edited sample";
                    save.PerformClick();
                    Check(library.List().Count == 2 && ((MacroLibraryEntry)selector.SelectedItem!).Name == "Edited sample", "Saving from the UI creates a named file beside the app and refreshes the selector");
                    var edited = list[0].Copy();
                    edited.Text = "Updated library macro";
                    list[0] = edited;
                    save.PerformClick();
                    Check(MacroFiles.Parse(File.ReadAllText(library.SavePath("Edited sample"))).Actions[0].Text == edited.Text && library.List().Count == 2, "Saving an existing selection updates it without creating a duplicate");
                    library.Save("Added outside the app", document, false);
                    var libraryMenu = FindControls(main).OfType<Button>().Single(button => button.Name == "LibraryMenu").ContextMenuStrip!;
                    libraryMenu.Items.Cast<ToolStripItem>().Single(item => item.Text == "Bibliothek aktualisieren").PerformClick();
                    Check(selector.Items.Count == 3, "Refresh discovers macros added outside the app");
                    FindControls(main).OfType<Button>().Single(button => button.Name == "NewMacro").PerformClick();
                    Check(list.Count == 0 && name.Text.Length == 0 && selector.Items.Count == 3, "New list clears the editor while retaining the saved library");
                    selector.SelectedItem = selector.Items.Cast<MacroLibraryEntry>().Single(entry => entry.Name == "Sample text & shortcut");
                    open.PerformClick();
                    var repeatList = FindControls(main).OfType<CheckBox>().Single(check => check.Text == "Bis zum Stopp wiederholen");
                    var loops = FindControls(main).OfType<NumericUpDown>().Single(input => input.Maximum == 1_000_000);
                    loops.Value = 3;
                    repeatList.Checked = true;
                    Check(loops.Value == 0, "The main repeat checkbox sets the whole-list loop count to zero");
                    repeatList.Checked = false;
                    Check(loops.Value == 3, "Unchecking repeat restores the previous finite loop count");
                    loops.Value = 0;
                    Check(repeatList.Checked, "Setting Loops to zero in Settings also checks repeat on the main window");
                    var scanCodes = FindControls(main).OfType<CheckBox>().Single(check => check.Text == "Tasten als Scan-Codes senden");
                    Check(!scanCodes.Checked, "Older macros keep their virtual-key default in Settings");
                    scanCodes.Checked = true;
                    save.PerformClick();
                    var savedPlayback = MacroFiles.Parse(File.ReadAllText(library.SavePath("Sample text & shortcut"))).Playback;
                    Check(savedPlayback.Loops == 0 && savedPlayback.UseScanCodes, "Saving preserves repeat and scan-code mode in the macro");
                    selector.SelectedItem = selector.Items.Cast<MacroLibraryEntry>().Single(entry => entry.Name == "Edited sample");
                    open.PerformClick();
                    Check(!repeatList.Checked && !scanCodes.Checked, "Loading a different macro restores its own playback modes");
                    selector.SelectedItem = selector.Items.Cast<MacroLibraryEntry>().Single(entry => entry.Name == "Sample text & shortcut");
                    open.PerformClick();
                    Check(repeatList.Checked && scanCodes.Checked, "Reloading restores repeat and scan-code mode without changing actions");
                    Check(!FindControls(main).OfType<Label>().Any(label => label.Text.StartsWith("Cannot register", StringComparison.Ordinal)), "Default control hotkeys register in the main application");
                    Render(main, "ui-preview.png");
                    var settingsView = FindControls(main).OfType<Button>().Single(button => button.Name == "SettingsView");
                    var fullSize = main.Size;
                    main.Size = main.MinimumSize;
                    await Task.Delay(70);
                    Render(main, "compact-preview.png");
                    Check(repeatList.Parent!.ClientRectangle.Contains(repeatList.Bounds) && save.Parent!.ClientRectangle.Contains(save.Bounds) &&
                        open.Parent!.ClientRectangle.Contains(open.Bounds), "Core controls fit at the minimum window size");
                    main.Size = fullSize;
                    settingsView.PerformClick();
                    await Task.Delay(100);
                    Render(main, "settings-preview.png");
                    var appearance = FindControls(main).OfType<ComboBox>().Single(combo => combo.Name == "AppearanceMode");
                    var savedMacro = File.ReadAllText(library.SavePath("Sample text & shortcut"));
                    var savedPreferences = File.ReadAllText(Path.Combine(verification.Path, "preferences.json"));
                    var appearancePath = Path.Combine(verification.Path, "appearance.json");
                    Check(Equals(appearance.SelectedItem, AppearanceMode.Windows) && appearance.Text == "Wie Windows" && main.BackColor == UiTheme.Light.Canvas,
                        "Wie Windows is the default and starts in the current Windows app theme");
                    using (var openEditor = new ActionEditor(new MacroAction { Key = "F" }))
                    {
                        UiTheme.Apply(openEditor, UiTheme.Current(main));
                        openEditor.Show(main);
                        var keyField = FindControls(openEditor).OfType<ComboBox>().Single(combo => combo.DropDownStyle == ComboBoxStyle.DropDown);
                        var holdField = FindControls(openEditor).OfType<NumericUpDown>().Single(input => input.AccessibleName == "Dauer (ms)");
                        holdField.Value = 93;
                        windowsDark = true;
                        SendTestWindowMessage(main.Handle, 0x001a, 0, 0);
                        await Task.Delay(80);
                        Check(main.BackColor == UiTheme.Dark.Canvas, "A Windows setting-change message updates the main window");
                        Check(openEditor.BackColor == UiTheme.Dark.Surface, "A Windows setting-change message updates the open editor");
                        Check(keyField.Text == "F" && holdField.Value == 93, "Live theme changes preserve values being edited");
                        Check(Equals(appearance.SelectedItem, AppearanceMode.Windows) && !File.Exists(appearancePath),
                            "Automatic system changes do not write or replace an explicit appearance preference");
                        openEditor.Close();
                    }
                    windowsDark = false;
                    SendTestWindowMessage(main.Handle, 0x031a, 0, 0);
                    await Task.Delay(80);
                    Check(main.BackColor == UiTheme.Light.Canvas, "System mode follows Windows back to light during the session");
                    appearance.SelectedItem = AppearanceMode.Dark;
                    Check(main.BackColor == UiTheme.Dark.Canvas && grid.DefaultCellStyle.BackColor == UiTheme.Dark.Surface &&
                        name.BackColor == UiTheme.Dark.Input && libraryMenu.BackColor == UiTheme.Dark.Surface, "Explicit dark mode overrides Windows and styles the window, grid, fields, and menus");
                    Check(File.ReadAllText(library.SavePath("Sample text & shortcut")) == savedMacro &&
                        File.ReadAllText(Path.Combine(verification.Path, "preferences.json")) == savedPreferences && !main.Text.StartsWith('*'),
                        "Changing appearance does not change the macro or its playback settings");
                    Render(main, "settings-dark-preview.png");
                    FindControls(main).OfType<Button>().Single(button => button.Name == "ActionView").PerformClick();
                    Render(main, "dark-preview.png");
                    selector.SelectedItem = selector.Items.Cast<MacroLibraryEntry>().Single(entry => entry.Name == "Edited sample");
                    open.PerformClick();
                    Check(Equals(appearance.SelectedItem, AppearanceMode.Dark) && main.BackColor == UiTheme.Dark.Canvas, "Loading another macro leaves the global appearance active");
                    if (testDrag)
                    {
                        await TestLiveActionDrag(main, actionGrid, list);
                        save.PerformClick();
                    }
                    var deleteCommand = libraryMenu.Items.Cast<ToolStripItem>().Single(item => item.Name == "DeleteSavedMacro");
                    var otherMacro = selector.Items.Cast<MacroLibraryEntry>().Single(entry => entry.Name == "Added outside the app");
                    selector.SelectedItem = otherMacro;
                    var rowRename = selector.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(item => item.Text == "Umbenennen…");
                    AnswerMacroRename(rowRename, otherMacro.Name, newName: null);
                    Check(File.Exists(otherMacro.FilePath) && Equals(selector.SelectedItem, otherMacro), "Cancelling rename keeps the selected file and name");
                    using var beforeRename = JsonDocument.Parse(File.ReadAllText(otherMacro.FilePath));
                    AnswerMacroRename(rowRename, otherMacro.Name, "Renamed in the library");
                    var renamed = (MacroLibraryEntry)selector.SelectedItem!;
                    Check(renamed.Name == "Renamed in the library" && !File.Exists(otherMacro.FilePath) &&
                        selector.Items.Count == 3 && name.Text == "Edited sample" && !main.Text.StartsWith('*'), "Renaming from the list selects the new filename and leaves another open macro intact");
                    using var afterRename = JsonDocument.Parse(File.ReadAllText(renamed.FilePath));
                    Check(afterRename.RootElement.GetProperty("Name").GetString() == renamed.Name && beforeRename.RootElement.EnumerateObject()
                        .Where(property => property.Name != "Name").All(property => JsonElement.DeepEquals(property.Value, afterRename.RootElement.GetProperty(property.Name))),
                        "Rename updates the name inside the file while preserving the other macro data");
                    var exported = Path.Combine(verification.Path, "Copied under another filename.json");
                    File.Copy(renamed.FilePath, exported);
                    CheckImportName(main, exported, renamed.Name);
                    otherMacro = renamed;
                    AnswerMacroDeletion(deleteCommand, otherMacro, confirm: false);
                    Check(File.Exists(otherMacro.FilePath) && selector.Items.Count == 3, "Cancelling deletion keeps the selected file and library entry");
                    var rowDelete = selector.ContextMenuStrip!.Items.Cast<ToolStripItem>().Single(item => item.Text == "Endgültig löschen…");
                    AnswerMacroDeletion(rowDelete, otherMacro, confirm: true);
                    Check(!File.Exists(otherMacro.FilePath) && selector.Items.Count == 2 && name.Text == "Edited sample" && list.Count == document.Actions.Count,
                        "Confirming deletion from the list menu removes the selected file and leaves another open macro intact");

                    var openMacro = (MacroLibraryEntry)selector.SelectedItem!;
                    list.Add(new MacroAction { Action = ActionKind.Delay, DurationMs = 123 });
                    AnswerMacroRename(rowRename, openMacro.Name, "Renamed while editing");
                    var renamedOpen = (MacroLibraryEntry)selector.SelectedItem!;
                    Check(!File.Exists(openMacro.FilePath) && renamedOpen.Name == "Renamed while editing" && name.Text == renamedOpen.Name &&
                        list[^1].DurationMs == 123 && main.Text.StartsWith('*') && MacroFiles.Parse(File.ReadAllText(renamedOpen.FilePath)).Actions.Count == document.Actions.Count,
                        "Renaming the open macro updates its name while leaving unsaved changes in the editor");
                    save.PerformClick();
                    Check(!File.Exists(openMacro.FilePath) && selector.Items.Count == 2 && !main.Text.StartsWith('*') &&
                        MacroFiles.Parse(File.ReadAllText(renamedOpen.FilePath)).Actions[^1].DurationMs == 123, "Saving after a rename updates the renamed file");
                    openMacro = renamedOpen;
                    list.Add(new MacroAction { Action = ActionKind.Delay, DurationMs = 123 });
                    AnswerMacroDeletion(deleteCommand, openMacro, confirm: true);
                    Check(!File.Exists(openMacro.FilePath) && selector.Items.Count == 1 && name.Text == openMacro.Name &&
                        list[^1].DurationMs == 123 && main.Text.StartsWith('*'), "Deleting the open macro preserves unsaved edits as a draft");
                    name.Text = "Saved draft";
                    save.PerformClick();
                    var savedDraft = (MacroLibraryEntry)selector.SelectedItem!;
                    Check(savedDraft.Name == "Saved draft" && File.Exists(savedDraft.FilePath) && !File.Exists(openMacro.FilePath), "The draft can be saved under a new name");
                    AnswerMacroDeletion(deleteCommand, savedDraft, confirm: true);
                    Check(!File.Exists(savedDraft.FilePath) && selector.Items.Count == 1 && list.Count == 0 && name.Text.Length == 0 && !main.Text.StartsWith('*'),
                        "Deleting a clean open macro clears the editor and keeps the remaining entry");
                    main.Close();
                    using (var reopened = new MainForm(verification.Path, () => windowsDark))
                    {
                        reopened.Show();
                        await Task.Delay(80);
                        var themeSwitch = FindControls(reopened).OfType<ComboBox>().Single(combo => combo.Name == "AppearanceMode");
                        Check(Equals(themeSwitch.SelectedItem, AppearanceMode.Dark) && reopened.BackColor == UiTheme.Dark.Canvas, "Explicit dark mode is restored on restart");
                        windowsDark = true;
                        themeSwitch.SelectedItem = AppearanceMode.Light;
                        Check(reopened.BackColor == UiTheme.Light.Canvas && File.ReadAllText(appearancePath).Contains("\"Light\"", StringComparison.Ordinal),
                            "Explicit light mode overrides Windows dark mode and persists");
                        themeSwitch.SelectedItem = AppearanceMode.Windows;
                        Check(reopened.BackColor == UiTheme.Dark.Canvas && File.ReadAllText(appearancePath).Contains("\"Windows\"", StringComparison.Ordinal),
                            "Switching back to Wie Windows immediately follows and saves the system preference");
                        reopened.Close();
                    }
                    File.WriteAllText(appearancePath, "{\"DarkMode\":true}");
                    using (var legacy = new MainForm(verification.Path, () => false))
                    {
                        legacy.Show();
                        await Task.Delay(60);
                        var legacyChoice = FindControls(legacy).OfType<ComboBox>().Single(combo => combo.Name == "AppearanceMode");
                        Check(Equals(legacyChoice.SelectedItem, AppearanceMode.Dark) && legacy.BackColor == UiTheme.Dark.Canvas,
                            "A saved choice from the earlier dark-mode switch remains an explicit override");
                        legacy.Close();
                    }
                    using var nameDialog = new MacroNameDialog(library, "My named recording", "Save recording");
                    UiTheme.Apply(nameDialog, UiTheme.Dark);
                    nameDialog.Show();
                    await Task.Delay(100);
                    Render(nameDialog, "save-name-preview.png");
                    FindControls(nameDialog).OfType<Button>().Single(button => button.Text == "Speichern").PerformClick();
                    Check(nameDialog.DialogResult == DialogResult.OK && nameDialog.MacroName == "My named recording", "Name dialog accepts a recording name");
                    nameDialog.Close();
                }
                using (var editor = new ActionEditor(new MacroAction { Action = ActionKind.MouseClick, Key = "Left", Count = 0, DurationMs = 50, IntervalMs = 100 }))
                {
                    editor.Show();
                    await Task.Delay(100);
                    Render(editor, "editor-preview.png");
                    UiTheme.Apply(editor, UiTheme.Dark);
                    Render(editor, "editor-dark-preview.png");
                    Check(editor.BackColor == UiTheme.Dark.Surface && FindControls(editor).OfType<ComboBox>().All(combo => combo.BackColor == UiTheme.Dark.Input),
                        "The action editor uses the dark palette for the window and its fields");
                    Check(FindControls(editor).OfType<NumericUpDown>().Any(input => input.AccessibleName == "X-Koordinate" && input.Visible), "Mouse click editing shows position fields");
                    FindControls(editor).OfType<Button>().Single(button => button.Name == "SaveAction").PerformClick();
                    Check(editor.DialogResult == DialogResult.OK && editor.Result.Count == 0, "Mouse click editor accepts and saves Count 0");
                    editor.Close();
                }
                foreach (var key in new[] { "F", "E" })
                {
                    using var editor = new ActionEditor(new MacroAction());
                    editor.Show();
                    Check(FindControls(editor).OfType<NumericUpDown>().All(input => input.AccessibleName != "X-Koordinate" || !input.Visible), "Key editing hides irrelevant coordinates");
                    var choice = FindControls(editor).OfType<ComboBox>().Single(combo => combo.DropDownStyle == ComboBoxStyle.DropDown);
                    choice.SelectedItem = key;
                    FindControls(editor).OfType<Button>().Single(button => button.Name == "SaveAction").PerformClick();
                    Check(editor.DialogResult == DialogResult.OK && editor.Result.Action == ActionKind.KeyPress && editor.Result.Key == key,
                        $"The KeyPress dropdown can select and save {key} as a separate action");
                    editor.Close();
                }
                Check(true, "Main window, settings and action editor render");
                results.Add(includeNative ? "ALL TESTS PASSED" : "ALL REQUESTED TESTS PASSED");
                Environment.ExitCode = 0;
            }
            catch (Exception error) { results.Add("FAILED: " + error); Environment.ExitCode = 1; }
            finally { File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), results); form.Close(); }
        };
        Application.Run(form);
    }

    private static void Render(Form form, string name)
    {
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(AppContext.BaseDirectory, name));
    }

    private static void TestModels()
    {
        Check(KeyNames.Parse("Ctrl") == 162 && KeyNames.Parse("1") == 49 && KeyNames.Parse("Enter") == 13, "Key aliases resolve");
        Check(Enumerable.Range('A', 26).All(letter => KeyNames.EditorChoices.Contains(((char)letter).ToString())) &&
            KeyNames.EditorChoices.All(key => KeyNames.Parse(key) > 0), "All letters and listed key choices are valid playback keys");
        Check(Hotkey.Parse("Ctrl+Shift+F8") == new Hotkey(6, 119), "Hotkey combinations parse");
        var document = new MacroDocument { Actions = [new() { Action = ActionKind.Text, Text = "Hello ä 😀\n", IntervalMs = 0 }] };
        Check(MacroFiles.Parse(MacroFiles.Serialize(document)).Actions[0].Text == document.Actions[0].Text, "JSON preserves Unicode and configuration");
        ExpectFailure(() => KeyNames.Parse("DefinitelyNotAKey"), "Invalid keys rejected");
        ExpectFailure(() => MacroFiles.ValidateActions([new() { Key = "F12" }], [123]), "Control keys reserved during playback");
        ExpectFailure(() => MacroFiles.Parse("{\"Version\":999}"), "Unsupported versions rejected");
        ExpectFailure(() => MacroFiles.ValidateActions([new() { Action = ActionKind.Delay, DurationMs = -1 }], []), "Negative timings rejected");
        ExpectFailure(() => MacroFiles.Parse("{\"Actions\":null}"), "Missing action list rejected");
        ExpectFailure(() => MacroFiles.ValidateOptions(new MacroDocument { Hotkeys = new() { Play = "F8" } }), "Duplicate hotkeys rejected");
        ExpectFailure(() => Hotkey.Parse("F12"), "Windows-reserved F12 rejected as a control hotkey");
    }

    private static async Task TestKeySequenceLoop()
    {
        var examplePath = Path.Combine(AppContext.BaseDirectory, "Examples", "05 - F then E until stopped.macro.json");
        var macro = MacroFiles.Parse(File.ReadAllText(examplePath));
        Check(macro.Playback.Loops == 0 && macro.Playback.MaximumRunSeconds == 0 && macro.Playback.UseScanCodes,
            "The F-then-E example uses scan codes and repeats until manual stop");
        macro.Playback.StartDelayMs = 0;
        macro.Playback.Speed = 10;
        using var cancellation = new CancellationTokenSource();
        var sink = new TestSink { ScanMapper = key => key == (int)Keys.F ? (33, false) : (18, false) };
        var player = new MacroPlayer(sink);
        player.Progress += (loop, action) => { if (loop == 4 && action == 1) cancellation.Cancel(); };
        await Cancelled(player.RunAsync(macro.Actions, macro.Playback, cancellation.Token));
        string[] onePass = ["Key 70 True", "Key 70 False", "Key 69 True", "Key 69 False"];
        Check(sink.Events.SequenceEqual(Enumerable.Repeat(onePass, 3).SelectMany(pass => pass)) &&
            sink.KeySignals.Select(signal => signal.Scan).SequenceEqual(Enumerable.Repeat(new[] { 33, 33, 18, 18 }, 3).SelectMany(pass => pass)) && sink.Held.Count == 0,
            "The whole list repeats F then E in order and releases all keys when stopped");
    }

    private static async Task TestScanCodePlayback()
    {
        var windows = new WindowsInputSink();
        var diagnosticPath = Path.Combine(AppContext.BaseDirectory, "Examples", "07 - Input test B - Scan codes.macro.json");
        var diagnostic = MacroFiles.Parse(File.ReadAllText(diagnosticPath));
        var sameAsDiagnostic = true;
        foreach (var action in diagnostic.Actions.Where(action => action.Action == ActionKind.KeyPress))
        {
            var key = KeyNames.Parse(action.Key);
            var mapped = windows.ResolveKey(key, 0, false, true);
            var virtualKey = windows.ResolveKey(key, 0, false, false);
            var explicitScan = windows.ResolveKey(key, action.ScanCode, action.Extended, false);
            var press = WindowsInputSink.CreateKeyInput(key, true, mapped.Scan, mapped.Extended);
            var release = WindowsInputSink.CreateKeyInput(key, false, mapped.Scan, mapped.Extended);
            Check(press.Key == 0 && press.Scan > 0 && press.Flags == 8 && release.Scan == press.Scan && release.Flags == 10 &&
                virtualKey.Scan == 0 && explicitScan == (action.ScanCode, action.Extended), $"Manual {action.Key} can use scan codes while virtual mode and recorded codes remain available");
            sameAsDiagnostic &= mapped == explicitScan;
        }
        if (sameAsDiagnostic) Check(true, "Mapped F and E emit the same scan codes as the working Test B on this keyboard layout");
        else results.Add("NOTE: This keyboard layout maps F or E differently from Test B's physical keys.");
        var arrow = windows.ResolveKey((int)Keys.Right, 0, false, true);
        Check(arrow is { Scan: > 0, Extended: true } && WindowsInputSink.CreateKeyInput((int)Keys.Right, true, arrow.Scan, arrow.Extended).Flags == 9,
            "Extended keys retain the Windows E0 flag in scan-code mode");

        using var cancellation = new CancellationTokenSource();
        var lookups = 0;
        var sink = new TestSink { ScanMapper = _ => ++lookups == 1 ? (33, false) : (18, false) };
        var player = new MacroPlayer(sink);
        player.Progress += (_, action) => { if (action == 3) cancellation.Cancel(); };
        var options = Options();
        options.UseScanCodes = true;
        await Cancelled(player.RunAsync([new() { Action = ActionKind.KeyDown, Key = "F" }, new() { Action = ActionKind.KeyDown, Key = "F" },
            new() { Action = ActionKind.Delay, DurationMs = 1000 }], options, cancellation.Token));
        Check(lookups == 1 && sink.KeySignals.SequenceEqual([(70, true, 33, false), (70, true, 33, false), (70, false, 33, false)]) && sink.Held.Count == 0,
            "A held key reuses the same scan code for repeated down events and stop cleanup");
    }

    private static async Task TestPlayback()
    {
        var sink = new TestSink();
        await new MacroPlayer(sink).RunAsync([
            new() { Action = ActionKind.KeyPress, Key = "A", Count = 3, DurationMs = 1, IntervalMs = 1 },
            new() { Action = ActionKind.MouseDown, Key = "Left", X = -20, Y = 10 },
            new() { Action = ActionKind.MouseMove, X = 50, Y = 60, DurationMs = 0 },
            new() { Action = ActionKind.MouseUp, Key = "Left", X = 50, Y = 60 },
            new() { Action = ActionKind.MouseScroll, Key = "Vertical", Y = -120, Count = 2 },
            new() { Action = ActionKind.Text, Text = "ä", IntervalMs = 0 }
        ], Options(), CancellationToken.None);
        Check(sink.Events.Count(value => value == "Key 65 True") == 3 && sink.Held.Count == 0, "Counted taps and balanced releases");
        Check(sink.Events.Contains("Move -20 10") && sink.Events.Contains("Scroll False -240"), "Dragging, negative coordinates and wheel deltas");
        Check(sink.Events.Contains("Unicode ä True") && sink.Events.Contains("Unicode ä False"), "Unicode press/release pairs");
        sink = new TestSink();
        using (var cancel = new CancellationTokenSource(80))
            await Cancelled(new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.KeyHold, Key = "Ctrl", DurationMs = 0 }], Options(), cancel.Token));
        Check(sink.Events.Contains("Key 162 False") && sink.Held.Count == 0, "Stop releases indefinite key holds");
        sink = new TestSink();
        using (var cancel = new CancellationTokenSource(80))
            await Cancelled(new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.MouseDown, Key = "Left" }, new() { Action = ActionKind.Delay, DurationMs = 10000 }], Options(), cancel.Token));
        Check(sink.Events.Contains("Button Left False") && sink.Held.Count == 0, "Stop releases held mouse buttons");
        sink = new TestSink();
        await new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.KeyRepeat, Key = "B", Count = 5, DurationMs = 500 }], Options(), CancellationToken.None);
        Check(sink.Events.Count(value => value == "Key 66 True") == 5, "Five presses over a specific period");
        sink = new TestSink();
        await new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.KeyRepeat, Key = "C", Count = 0, DurationMs = 150, IntervalMs = 40 }], Options(), CancellationToken.None);
        Check(sink.Events.Count(value => value == "Key 67 True") >= 2 && sink.Held.Count == 0, "Duration-limited repetition terminates");
        sink = new TestSink();
        using (var cancel = new CancellationTokenSource(100))
            await Cancelled(new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.KeyRepeat, Key = "D", Count = 0, DurationMs = 0, IntervalMs = 20 }], Options(), cancel.Token));
        Check(sink.Events.Count(value => value == "Key 68 True") >= 2 && sink.Held.Count == 0, "Indefinite repetition stops on cancellation");
        sink = new TestSink();
        await new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.KeyDown, Key = "A" }], new() { Loops = 3, StartDelayMs = 0, LoopDelayMs = 1 }, CancellationToken.None);
        Check(sink.Events.Count(value => value == "Key 65 False") == 3, "Held keys released at every loop boundary");
        sink = new TestSink();
        using (var cancel = new CancellationTokenSource(80))
            await Cancelled(new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.KeyPress }], new() { StartDelayMs = 10000 }, cancel.Token));
        Check(sink.Events.Count == 0, "Countdown stops before sending input");
        sink = new TestSink();
        var watch = Stopwatch.StartNew();
        await Cancelled(new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.KeyHold, DurationMs = 0 }], new() { StartDelayMs = 0, MaximumRunSeconds = 1 }, CancellationToken.None));
        Check(watch.Elapsed.TotalSeconds < 2 && sink.Held.Count == 0, "Maximum runtime stops infinite actions");
        sink = new TestSink { ThrowOnMove = true };
        try { await new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.KeyDown }, new() { Action = ActionKind.MouseMove }], Options(), CancellationToken.None); throw new Exception("Expected failure"); }
        catch (IOException) { }
        Check(sink.Held.Count == 0, "Input errors release previously held keys");
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Examples"), "*.json")) MacroFiles.Parse(File.ReadAllText(file));
        Check(true, "Supplied examples validate");
    }

    private static void CheckImportName(MainForm main, string path, string expectedName)
    {
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        var suggestedName = "";
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.OfType<MacroNameDialog>().SingleOrDefault();
            if (dialog is null) return;
            timer.Stop();
            suggestedName = FindControls(dialog).OfType<TextBox>().Single().Text;
            dialog.CancelButton!.PerformClick();
        };
        timer.Start();
        main.ImportMacro(path);
        Check(suggestedName == expectedName, "Import suggests the updated internal name even when the file was copied under another filename");
    }

    private static void AnswerMacroRename(ToolStripItem command, string initialName, string? newName)
    {
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        var correctDialog = false;
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.OfType<MacroNameDialog>().SingleOrDefault();
            if (dialog is null) return;
            timer.Stop();
            var input = FindControls(dialog).OfType<TextBox>().Single();
            correctDialog = input.Text == initialName && dialog.AcceptButton is Button { Text: "Umbenennen" };
            if (newName is null) dialog.CancelButton!.PerformClick();
            else
            {
                input.Text = newName;
                dialog.AcceptButton!.PerformClick();
            }
        };
        timer.Start();
        command.PerformClick();
        Check(correctDialog, "The rename dialog starts with the selected macro name");
    }

    private static void AnswerMacroDeletion(ToolStripItem command, MacroLibraryEntry entry, bool confirm)
    {
        using var timer = new System.Windows.Forms.Timer { Interval = 50 };
        var correctDialog = false;
        timer.Tick += (_, _) =>
        {
            var dialog = Application.OpenForms.OfType<MacroDeleteDialog>().SingleOrDefault();
            if (dialog is null) return;
            timer.Stop();
            var controls = FindControls(dialog).ToArray();
            correctDialog = dialog.AcceptButton is Button { Name: "CancelDeleteMacro" } &&
                controls.OfType<Label>().Any(label => label.Text.Contains($"„{entry.Name}“", StringComparison.Ordinal)) &&
                controls.OfType<Label>().Any(label => label.Text == entry.FilePath);
            controls.OfType<Button>().Single(button => button.Name == (confirm ? "ConfirmDeleteMacro" : "CancelDeleteMacro")).PerformClick();
        };
        timer.Start();
        command.PerformClick();
        Check(correctDialog, "Deletion confirmation names the selected macro and its file, with Cancel as the default");
    }

    private static void TestLibrary()
    {
        using var verification = new VerificationDirectory();
        var library = new MacroLibrary(verification.Path);
        Check(library.List().Count == 0 && !Directory.Exists(library.DirectoryPath), "Empty library can be listed without writing files");
        var document = new MacroDocument { Actions = [new() { Action = ActionKind.Text, Text = "Recorded ä", IntervalMs = 0 }] };
        var path = library.Save("Morning routine ä", document, false);
        Check(System.IO.Path.GetDirectoryName(path) == library.DirectoryPath && MacroFiles.Parse(File.ReadAllText(path)).Name == "Morning routine ä", "Named macros save inside Recordings with their name preserved");
        Check(library.List().Single().Name == "Morning routine ä", "Saved macro appears by name in the library");
        var original = File.ReadAllText(path);
        try { library.Save("Morning routine ä", new MacroDocument(), false); throw new Exception("Expected overwrite protection"); }
        catch (IOException) { }
        Check(File.ReadAllText(path) == original && !Directory.GetFiles(library.DirectoryPath, "*.tmp").Any(), "Existing macros are protected unless replacement is authorized, with temporary files cleaned up");
        library.Save("Morning routine ä", new MacroDocument(), true);
        Check(MacroFiles.Parse(File.ReadAllText(path)).Actions.Count == 0, "Authorized save replaces the existing macro");
        var legacy = System.IO.Path.Combine(verification.Path, "Older macro.macro.json");
        File.WriteAllText(legacy, "{\"Version\":1,\"Actions\":[]}");
        var preferences = System.IO.Path.Combine(verification.Path, "preferences.json");
        File.WriteAllText(preferences, "{}");
        Check(library.List().Any(entry => entry.Name == "Older macro" && entry.BesideApplication) && library.List().Count == 2, "Previously saved macros beside the executable are listed without treating preferences as a macro");
        var manual = System.IO.Path.Combine(library.DirectoryPath, "Manual list.json");
        File.WriteAllText(manual, "{\"Name\":\"Original manual name\",\"Actions\":[],\"Custom\":{\"Name\":\"Leave nested names alone\"}}");
        Check(library.SavePath("Manual list") == manual, "Existing plain JSON lists in Recordings are updated without duplicate filenames");
        var imported = MacroFiles.Parse(File.ReadAllText(legacy));
        library.Save("Imported list", imported, false);
        Check(File.ReadAllText(legacy) == "{\"Version\":1,\"Actions\":[]}" && library.List().Any(entry => entry.Name == "Imported list"), "Import adds a named library copy and preserves the original source file");
        var manualEntry = library.List().Single(entry => entry.FilePath == manual);
        var importedPath = library.SavePath("Imported list");
        var importedContents = File.ReadAllText(importedPath);
        try { library.Rename(manualEntry, "IMPORTED LIST"); throw new Exception("Expected rename conflict"); }
        catch (IOException) { }
        Check(File.Exists(manual) && File.ReadAllText(importedPath) == importedContents, "Rename refuses an existing library name without overwriting either file");
        Check(library.Rename(manualEntry, manualEntry.Name) == manual && MacroFiles.Parse(File.ReadAllText(manual)).Name == manualEntry.Name,
            "Confirming the same name repairs an older rename in place");
        var caseRename = library.Rename(manualEntry, "manual list");
        using (var caseContents = JsonDocument.Parse(File.ReadAllText(caseRename)))
        {
            Check(Path.GetFileName(caseRename) == "manual list.json" && library.List().Any(entry => entry.FilePath == caseRename) &&
                caseContents.RootElement.GetProperty("Name").GetString() == "manual list" &&
                caseContents.RootElement.GetProperty("Custom").GetProperty("Name").GetString() == "Leave nested names alone",
                "Capitalization-only rename updates the internal name and preserves extra properties in a manual list");
        }
        var legacyEntry = library.List().Single(entry => entry.FilePath == legacy);
        var renamedLegacy = library.Rename(legacyEntry, "Renamed legacy");
        legacyEntry = library.List().Single(entry => entry.FilePath == renamedLegacy);
        Check(!File.Exists(legacy) && legacyEntry.BesideApplication && Path.GetDirectoryName(renamedLegacy) == verification.Path &&
            MacroFiles.Parse(File.ReadAllText(renamedLegacy)).Name == "Renamed legacy", "Renaming adds the name to an unnamed legacy macro and keeps it beside the application");
        Check(library.Delete(legacyEntry) && !File.Exists(renamedLegacy) && !library.Delete(legacyEntry) && File.Exists(importedPath),
            "A legacy macro can be deleted by exact file without removing its imported copy");
        var readOnlyEntry = library.List().Single(entry => entry.FilePath == importedPath);
        var readOnlyTarget = library.GetPath("Read-only rename attempt");
        File.SetAttributes(importedPath, FileAttributes.ReadOnly);
        try
        {
            try { library.Rename(readOnlyEntry, "Read-only rename attempt"); throw new Exception("Expected read-only rename failure"); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
            Check(File.ReadAllText(importedPath) == importedContents && !File.Exists(readOnlyTarget) && !Directory.GetFiles(library.DirectoryPath, "*.tmp").Any(),
                "If the file update fails, rename restores the original filename and contents");
        }
        finally
        {
            if (File.Exists(importedPath)) File.SetAttributes(importedPath, FileAttributes.Normal);
            if (File.Exists(readOnlyTarget)) File.SetAttributes(readOnlyTarget, FileAttributes.Normal);
        }
        Check(!library.Delete(new MacroLibraryEntry("preferences", preferences)) && File.Exists(preferences), "Deletion only accepts files listed in the library");
        foreach (var invalid in new[] { "", "   ", "../escape", "..\\escape", "C:\\outside", "CON", "con.txt", "LPT1", "name.", new string('a', 121) })
            ExpectFailure(() => library.GetPath(invalid), $"Invalid library name rejected: {invalid}");
        Check(MacroFiles.Parse("{\"Version\":1,\"Actions\":[]}").Name == "", "Older unnamed macro files remain compatible");
    }

    private static async Task TestMouseClickRepetition()
    {
        var action = new MacroAction { Action = ActionKind.MouseClick, Key = "Left", Count = 0, DurationMs = 0, IntervalMs = 0 };
        MacroFiles.ValidateActions([action], []);
        var restored = MacroFiles.Parse(MacroFiles.Serialize(new MacroDocument { Actions = [action] }));
        Check(restored.Actions[0].Count == 0, "Infinite mouse clicks validate and survive saving/loading");
        ExpectFailure(() => MacroFiles.ValidateActions([new() { Action = ActionKind.MouseClick, Key = "Left", Count = -1 }], []), "Negative mouse click counts remain invalid");
        var sink = new TestSink();
        await new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.MouseClick, Key = "Right", Count = 3, DurationMs = 1, IntervalMs = 1 }], Options(), CancellationToken.None);
        Check(sink.Events.Count(value => value == "Button Right True") == 3 && sink.Events.Count(value => value == "Button Right False") == 3 && sink.Held.Count == 0, "Finite mouse clicks still run exactly Count times");
        using (var cancel = new CancellationTokenSource())
        {
            var clicks = 0;
            sink = new TestSink { ButtonStateChanged = down => { if (down && ++clicks == 3) cancel.Cancel(); } };
            await Cancelled(new MacroPlayer(sink).RunAsync([action, new() { Action = ActionKind.Text, Text = "Later row", IntervalMs = 0 }], Options(), cancel.Token));
            Check(clicks == 3 && sink.Events.Count(value => value == "Button Left False") == 3 && sink.Held.Count == 0 && !sink.Events.Any(value => value.StartsWith("Unicode", StringComparison.Ordinal)), "Infinite clicks repeat at zero timings, stop during a press and release the button without advancing rows");
        }
        using (var cancel = new CancellationTokenSource())
        {
            sink = new TestSink { ButtonStateChanged = down => { if (!down) cancel.Cancel(); } };
            var gapAction = action.Copy();
            gapAction.IntervalMs = 10000;
            await Cancelled(new MacroPlayer(sink).RunAsync([gapAction], Options(), cancel.Token));
            Check(sink.Events.Count(value => value == "Button Left True") == 1 && sink.Held.Count == 0, "Infinite clicks stop during the interval without another press");
        }
    }

    private static async Task TestNative(Form form, TextBox text)
    {
        Check(Marshal.SizeOf<Native.Input>() == (IntPtr.Size == 8 ? 40 : 28), "Native INPUT structure has correct Windows ABI size");
        using (var hotkeys = new HotkeyRegistration(form.Handle))
        {
            hotkeys.Apply(new() { Record = "Ctrl+Alt+F5", StopRecording = "Ctrl+Alt+F6", Play = "Ctrl+Alt+F7", StopAll = "Ctrl+Alt+F11" });
            Check(hotkeys.ReservedKeys.SetEquals([116, 117, 118, 122]), "Global Windows hotkeys register");
        }
        await FocusVerificationWindow(form, text);
        var sink = new WindowsInputSink();
        using var recorder = new MacroRecorder();
        recorder.Start(new(), []);
        recorder.Stop();
        Check(true, "Windows keyboard and mouse recording hooks install and uninstall");
        if (Native.GetForegroundWindow() != form.Handle)
        {
            results.Add($"SKIP: Foreground playback integration; Windows did not allow the test window to take focus (foreground={Native.GetForegroundWindow()}, test={form.Handle}). No input was sent to other applications.");
            return;
        }
        Check(true, "Native tests own foreground window");
        recorder.Start(new() { MouseMovement = false }, [123]);
        await new MacroPlayer(sink).RunAsync([new() { Action = ActionKind.Text, Text = "Macro ä", IntervalMs = 5 }, new() { Action = ActionKind.KeyPress, Key = "D1", Count = 2, DurationMs = 5, IntervalMs = 10 }], Options(), CancellationToken.None);
        await Task.Delay(100);
        var ownInput = recorder.Stop();
        Check(text.Text == "Macro ä11", "Real Windows input delivers Unicode and repeated keys");
        Check(ownInput.All(action => action.Action == ActionKind.Delay), "Recorder ignores its own injected input");
        recorder.Start(new() { MouseMovement = true }, []);
        Native.Send(new Native.Input { Type = 1, Data = new Native.InputUnion { Keyboard = new Native.KeyInput { Key = 90 } } });
        Native.Send(new Native.Input { Type = 1, Data = new Native.InputUnion { Keyboard = new Native.KeyInput { Key = 90, Flags = 2 } } });
        Native.Send(new Native.Input { Data = new Native.InputUnion { Mouse = new Native.MouseInput { X = 2, Y = 1, Flags = 1 } } });
        await Task.Delay(100);
        var captured = recorder.Stop();
        Check(captured.Any(action => action.Action == ActionKind.KeyDown && action.Key == "Z") && captured.Any(action => action.Action == ActionKind.KeyUp && action.Key == "Z"), "Low-level hooks capture external key down/up");
        Check(captured.Any(action => action.Action == ActionKind.MouseMove), "Low-level hooks capture mouse movement");
        await new MacroPlayer(sink).RunAsync(captured, Options(), CancellationToken.None);
        await Task.Delay(50);
        Check(text.Text.EndsWith("zz", StringComparison.OrdinalIgnoreCase), "Recorded scan codes replay into Windows");
    }

    private static async Task FocusVerificationWindow(Form form, Control? control = null)
    {
        Native.ShowWindow(form.Handle, 9);
        form.TopMost = true;
        form.BringToFront();
        Native.SetForegroundWindow(form.Handle);
        form.Activate();
        control?.Focus();
        await Task.Delay(120);
        if (Native.GetForegroundWindow() != form.Handle)
        {
            var foregroundThread = Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out _);
            var testThread = Native.GetCurrentThreadId();
            if (Native.AttachThreadInput(testThread, foregroundThread, true))
            {
                try { Native.SetForegroundWindow(form.Handle); form.Activate(); control?.Focus(); }
                finally { Native.AttachThreadInput(testThread, foregroundThread, false); }
            }
            await Task.Delay(120);
        }
    }

    private static PlaybackOptions Options() => new() { StartDelayMs = 0, LoopDelayMs = 0, Loops = 1 };
    private static IEnumerable<Control> FindControls(Control parent) => parent.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(FindControls(control)));
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); results.Add("PASS: " + name); }
    private static void ExpectFailure(Action operation, string name)
    {
        try { operation(); } catch (InvalidDataException) { Check(true, name); return; }
        throw new Exception(name);
    }
    private static async Task Cancelled(Task task) { try { await task; } catch (OperationCanceledException) { return; } throw new Exception("Expected cancellation"); }

    private sealed class TestSink : IInputSink
    {
        public List<string> Events { get; } = [];
        public List<(int Key, bool Down, int Scan, bool Extended)> KeySignals { get; } = [];
        public HashSet<string> Held { get; } = [];
        public bool ThrowOnMove { get; init; }
        public Action<bool>? ButtonStateChanged { get; init; }
        public Func<int, (int Scan, bool Extended)>? ScanMapper { get; init; }
        public (int Scan, bool Extended) ResolveKey(int key, int scan, bool extended, bool useScanCodes) =>
            useScanCodes && scan == 0 && ScanMapper is not null ? ScanMapper(key) : (scan, extended);
        public void Key(int key, bool down, int scan = 0, bool extended = false)
        {
            Events.Add($"Key {key} {down}");
            KeySignals.Add((key, down, scan, extended));
            if (down) Held.Add("K" + key); else Held.Remove("K" + key);
        }
        public void Unicode(char character, bool down) => Events.Add($"Unicode {character} {down}");
        public void Move(int x, int y) { if (ThrowOnMove) throw new IOException("Test failure"); Events.Add($"Move {x} {y}"); }
        public void Button(string button, bool down)
        {
            Events.Add($"Button {button} {down}");
            if (down) Held.Add(button); else Held.Remove(button);
            ButtonStateChanged?.Invoke(down);
        }
        public void Scroll(bool horizontal, int delta) => Events.Add($"Scroll {horizontal} {delta}");
    }

    private sealed class VerificationDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "Verification", Guid.NewGuid().ToString("N"));
        public VerificationDirectory() => Directory.CreateDirectory(Path);
        public void Dispose()
        {
            var root = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "Verification")) + System.IO.Path.DirectorySeparatorChar;
            var target = System.IO.Path.GetFullPath(Path);
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new IOException("Verification cleanup path is outside its temporary directory.");
            Directory.Delete(target, true);
        }
    }
}
