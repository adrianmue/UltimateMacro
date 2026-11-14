using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UltimateMacro;

public sealed partial class MainForm : Form
{
    private static readonly Icon appIcon = new(typeof(MainForm), "UltimateMacro.ico");
    private readonly BindingList<MacroAction> actions = [];
    private readonly BindingSource source = new();
    private readonly ActionGrid grid = new();
    private readonly Label status = new();
    private readonly Label hotkeySummary = new();
    private readonly Label cursorLabel = new();
    private readonly Button recordButton = new PaletteButton();
    private readonly Button playButton = new PaletteButton();
    private readonly Button stopButton = new PaletteButton();
    private readonly CheckBox repeatList = new() { Text = "Bis zum Stopp wiederholen", AutoSize = true, Margin = new Padding(3, 11, 15, 0) };
    private readonly TableLayoutPanel fileBar = new();
    private readonly TableLayoutPanel libraryBar = new();
    private readonly ListBox savedMacros = new() { Name = "SavedMacros", AccessibleName = "Gespeicherte Makros", Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly TextBox macroName = new() { Name = "MacroName", AccessibleName = "Makroname", Dock = DockStyle.Fill, MaxLength = 120, PlaceholderText = "z. B. Meine Routine" };
    private readonly Button openSavedButton = new PaletteButton { Name = "OpenSavedMacro" };
    private readonly Button editActionButton = new PaletteButton { Name = "EditAction" };
    private readonly Label libraryCount = new();
    private readonly Label actionCount = new();
    private readonly Label emptyLibrary = new();
    private readonly Panel emptyActions = new();
    private readonly MacroLibrary library;
    private readonly TableLayoutPanel editBar = new();
    private readonly Panel actionPage = new() { Dock = DockStyle.Fill, Padding = new Padding(6, 3, 6, 0) };
    private readonly Panel settingsTab = new() { Dock = DockStyle.Fill };
    private readonly List<(Control Page, Button Tab)> workspacePages = [];
    private readonly NumericUpDown loops = Number(0, 1_000_000, 1);
    private readonly NumericUpDown loopDelay = Number(0, 3_600_000, 100);
    private readonly NumericUpDown startDelay = Number(0, 60_000, 2000);
    private readonly NumericUpDown maxRun = Number(0, 604800, 0);
    private readonly NumericUpDown speed = Number(0.1m, 10, 1, 2);
    private readonly CheckBox useScanCodes = Check("Tasten als Scan-Codes senden", false);
    private readonly ComboBox appearanceChoice = new() { Name = "AppearanceMode", AccessibleName = "Darstellung", DropDownStyle = ComboBoxStyle.DropDownList,
        FormattingEnabled = true, Width = 155, Margin = new Padding(0, 6, 0, 0) };
    private readonly NumericUpDown sample = Number(1, 1000, 15);
    private readonly CheckBox captureKeys = Check("Tastatur", true);
    private readonly CheckBox captureButtons = Check("Maustasten und Ziehen", true);
    private readonly CheckBox captureMovement = Check("Mausbewegungen", true);
    private readonly CheckBox captureWheel = Check("Mausrad", true);
    private readonly CheckBox appendRecording = Check("Aufnahme an Ablauf anhängen", false);
    private readonly TextBox[] hotkeyFields = [new(), new(), new(), new()];
    private readonly MacroRecorder recorder = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 100 };
    private HotkeyRegistration? hotkeys;
    private CancellationTokenSource? playbackCancellation;
    private Task? playbackTask;
    private bool playing;
    private bool recordingSession;
    private bool dirty;
    private bool initializing;
    private bool closing;
    private decimal finiteLoopCount = 1;
    private string? currentPath;
    private List<MacroAction>? beforeRecording;
    private readonly string preferencesPath;
    private readonly string appearancePath;
    private readonly Func<bool> windowsUsesDarkMode;
    private AppearanceMode selectedAppearance;
    private bool applyingAppearance;
    private readonly Stopwatch sessionWatch = new();
    private int currentLoop;
    private int currentAction;
    private bool usesGlobalPlaybackInput;
    private string? imageTargetStatus;

    public MainForm(string? applicationDirectory = null) : this(applicationDirectory, UiTheme.WindowsAppsUseDarkMode) { }

    internal MainForm(string? applicationDirectory, Func<bool> windowsUsesDarkMode)
    {
        this.windowsUsesDarkMode = windowsUsesDarkMode;
        applicationDirectory ??= AppContext.BaseDirectory;
        library = new MacroLibrary(applicationDirectory);
        preferencesPath = Path.Combine(applicationDirectory, "preferences.json");
        appearancePath = Path.Combine(applicationDirectory, "appearance.json");
        Text = "Ultimate Macro";
        Icon = appIcon;
        Font = new Font("Segoe UI", 10);
        Size = new Size(1260, 850);
        MinimumSize = new Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        BuildLayout();
        repeatList.CheckedChanged += (_, _) =>
        {
            if (repeatList.Checked) loops.Value = 0;
            else if (loops.Value == 0) loops.Value = finiteLoopCount;
        };
        loops.ValueChanged += (_, _) =>
        {
            if (loops.Value > 0) finiteLoopCount = loops.Value;
            repeatList.Checked = loops.Value == 0;
        };
        initializing = true;
        ApplyDocumentToControls(new MacroDocument());
        try
        {
            if (File.Exists(preferencesPath)) ApplyDocumentToControls(MacroFiles.Parse(File.ReadAllText(preferencesPath)));
        }
        catch (Exception error) { status.Text = "Could not load preferences: " + error.Message; }
        LoadAppearance();
        initializing = false;
        source.DataSource = actions;
        grid.DataSource = source;
        actions.ListChanged += (_, _) => { MarkDirty(); RefreshActionView(); };
        macroName.TextChanged += (_, _) => MarkDirty();
        foreach (var control in AllControls(settingsTab))
        {
            if (control is NumericUpDown number) number.ValueChanged += (_, _) => MarkDirty();
            else if (control is CheckBox check) check.CheckedChanged += (_, _) => MarkDirty();
            else if (control is TextBox text) text.TextChanged += (_, _) => MarkDirty();
        }
        recorder.LimitReached += () => BeginInvoke(() =>
        {
            StopRecording();
            MessageBox.Show(this, "Die Aufnahme wurde bei 200.000 Aktionen beendet. Speichere sie, bevor du weitermachst.", "Aufnahmelimit");
        });
        Shown += (_, _) =>
        {
            hotkeys = new HotkeyRegistration(Handle);
            try { hotkeys.Apply(ReadHotkeys()); SetStatus("Bereit"); }
            catch (Exception error) { ShowError(error); SelectWorkspace(settingsTab); }
            UpdateHotkeySummary();
            Guard(() => RefreshLibrary());
        };
        timer.Tick += (_, _) => RefreshStatus();
        timer.Start();
        FormClosing += OnClosing;
        FormClosed += (_, _) => { timer.Dispose(); recorder.Dispose(); hotkeys?.Dispose(); playbackCancellation?.Dispose(); };
        KeyDown += (_, args) =>
        {
            if (args.Control && args.KeyCode == Keys.S) { args.SuppressKeyPress = true; Guard(() => Save(false)); }
            if (args.Control && args.KeyCode == Keys.O && !Busy) { args.SuppressKeyPress = true; Guard(Open); }
        };
        ApplyAppearance(force: true);
        appearanceChoice.SelectedIndexChanged += (_, _) => ChangeAppearance();
        UpdateState();
    }

    private bool Busy => playing || recordingSession;

    private void BuildSettings()
    {
        settingsTab.Padding = new Padding(16);
        settingsTab.AutoScroll = true;
        var settings = new TableLayoutPanel { Dock = DockStyle.Top, Height = 478, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        settings.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        settingsTab.Controls.Add(settings);
        var appearance = UiTheme.Mark(new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(9, 0, 9, 0), Margin = new Padding(0, 0, 8, 8) }, UiRole.Soft);
        appearance.Controls.Add(new Label { Text = "Darstellung", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(0, 9, 15, 0) });
        appearanceChoice.Format += (_, args) => args.Value = args.ListItem switch
        {
            AppearanceMode.Light => "Hell",
            AppearanceMode.Dark => "Dunkel",
            _ => "Wie Windows"
        };
        appearanceChoice.Items.AddRange(Enum.GetValues<AppearanceMode>().Cast<object>().ToArray());
        appearanceChoice.SelectedItem = AppearanceMode.Windows;
        appearance.Controls.Add(appearanceChoice);
        settings.Controls.Add(appearance, 0, 0);
        var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = Padding.Empty };
        for (var index = 0; index < 3; index++) columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        settings.Controls.Add(columns, 0, 1);
        var playback = SettingsGroup("Wiedergabe", out var playbackFields);
        AddSetting(playbackFields, "Durchläufe (0 = unbegrenzt)", loops);
        AddSetting(playbackFields, "Pause zwischen Durchläufen (ms)", loopDelay);
        AddSetting(playbackFields, "Startverzögerung (ms)", startDelay);
        AddSetting(playbackFields, "Zeitlimit (s, 0 = aus)", maxRun);
        AddSetting(playbackFields, "Geschwindigkeit", speed);
        playbackFields.Controls.Add(useScanCodes);
        columns.Controls.Add(playback, 0, 0);
        var recording = SettingsGroup("Aufnahme", out var recordingFields);
        recordingFields.Controls.AddRange([captureKeys, captureButtons, captureMovement, captureWheel]);
        AddSetting(recordingFields, "Maus-Abtastintervall (ms)", sample);
        recordingFields.Controls.Add(appendRecording);
        recordingFields.Controls.Add(UiTheme.Mark(new Label { AutoSize = true, Text = "Ohne Anhängen ersetzt die Aufnahme den aktuellen Ablauf." }, UiRole.Muted));
        columns.Controls.Add(recording, 1, 0);
        var shortcuts = SettingsGroup("Tastenkürzel", out var hotkeyControls);
        string[] labels = ["Aufnahme starten", "Aufnahme beenden", "Wiedergabe starten", "Alles stoppen"];
        for (var index = 0; index < labels.Length; index++)
        {
            hotkeyFields[index].Width = 180;
            AddSetting(hotkeyControls, labels[index], hotkeyFields[index]);
        }
        hotkeyControls.Controls.Add(UiTheme.Mark(new Label { AutoSize = true, Text = "Beispiele: F8, Ctrl+F8, Alt+Shift+M" }, UiRole.Muted));
        hotkeyControls.Controls.Add(Button("Übernehmen", () => Guard(ApplySettings), primary: true));
        columns.Controls.Add(shortcuts, 2, 0);
    }

    private static Panel BuildHelp()
    {
        var page = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18) };
        page.Controls.Add(UiTheme.Mark(new TextBox
        {
            Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None,
            ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 11),
            Text = GuideText
        }, UiRole.Surface));
        return page;
    }

    private void AddColumn(string title, string property, int weight) => grid.Columns.Add(new DataGridViewTextBoxColumn
    {
        HeaderText = title, DataPropertyName = property, FillWeight = weight, SortMode = DataGridViewColumnSortMode.NotSortable
    });

    private void StartRecording()
    {
        if (Busy || grid.ReorderInProgress) return;
        ApplySettings();
        var settings = ReadDocument();
        if (!settings.Recording.Keyboard && !settings.Recording.MouseButtons && !settings.Recording.MouseMovement && !settings.Recording.MouseWheel)
            throw new InvalidOperationException("Enable at least one input category in Settings.");
        beforeRecording = actions.Select(action => action.Copy()).ToList();
        recorder.Start(settings.Recording, hotkeys!.ReservedKeys);
        recordingSession = true;
        sessionWatch.Restart();
        UpdateState();
        SetStatus("Aufnahme läuft …");
    }

    private void StopRecording()
    {
        if (!recordingSession) return;
        var captured = recorder.Stop();
        recordingSession = false;
        if (!appendRecording.Checked) actions.Clear();
        ReplaceActions(appendRecording.Checked ? actions.Concat(captured).ToList() : captured);
        UpdateState();
        SetStatus($"Aufnahme beendet · {captured.Count:N0} Aktionen erfasst. Du kannst den Ablauf jetzt prüfen und speichern.");
    }

    private async void StartPlayback()
    {
        if (Busy || grid.ReorderInProgress) return;
        try
        {
            if (actions.Count == 0) throw new InvalidOperationException("Füge zuerst eine Aktion hinzu oder starte eine Aufnahme.");
            ApplySettings();
            var document = ReadDocument();
            MacroFiles.ValidateActions(document.Actions, hotkeys!.ReservedKeys);
            var usesGlobalInput = document.Actions.Any(action => action.Action != ActionKind.Delay &&
                !(action.Action == ActionKind.ImageClick && action.ImageTarget is { Background: true }));
            if (usesGlobalInput && document.Playback.StartDelayMs == 0 &&
                Enumerable.Range(1, 254).Any(key => Native.GetAsyncKeyState(key) < 0 && !hotkeys.ReservedKeys.Contains(key)))
                throw new InvalidOperationException("Release physical keys and mouse buttons before starting, or set a start countdown in Settings.");
            playing = true;
            usesGlobalPlaybackInput = usesGlobalInput;
            currentLoop = currentAction = 0;
            imageTargetStatus = null;
            playbackCancellation = new CancellationTokenSource();
            var player = new MacroPlayer(new WindowsInputSink());
            player.Progress += (loop, action) => { Volatile.Write(ref currentLoop, loop); Volatile.Write(ref currentAction, action); };
            player.ImageTargetStatus += waiting => Volatile.Write(ref imageTargetStatus, waiting);
            sessionWatch.Restart();
            UpdateState();
            SetStatus(usesGlobalInput ? "Wiedergabe startet gleich. Wechsle zum Zielfenster." : "Hintergrund-Wiedergabe startet nach dem Countdown.");
            playbackTask = Task.Run(() => player.RunAsync(document.Actions, document.Playback, playbackCancellation.Token));
            await playbackTask;
            SetStatus("Wiedergabe abgeschlossen.");
        }
        catch (OperationCanceledException) { SetStatus("Wiedergabe gestoppt."); }
        catch (Exception error) { if (!closing) ShowError(error); }
        finally
        {
            playing = false;
            playbackCancellation?.Dispose();
            playbackCancellation = null;
            playbackTask = null;
            if (!IsDisposed) UpdateState();
        }
    }

    private void StopAll()
    {
        Guard(StopRecording);
        if (playing) { playbackCancellation?.Cancel(); SetStatus("Wiedergabe wird gestoppt …"); }
    }

    private void ApplySettings()
    {
        if (Busy) return;
        var document = ReadDocument();
        MacroFiles.ValidateOptions(document);
        hotkeys?.Apply(document.Hotkeys);
        UpdateHotkeySummary();
        SavePreferences(document);
        SetStatus("Einstellungen übernommen.");
    }

    private void SavePreferences(MacroDocument document)
    {
        var preferences = new MacroDocument { Playback = document.Playback, Recording = document.Recording, Hotkeys = document.Hotkeys };
        try { AtomicWrite(preferencesPath, MacroFiles.Serialize(preferences)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus("Settings applied for this session. Could not save preferences: " + error.Message);
        }
    }

    private sealed record AppearanceSettings(AppearanceMode? Mode,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? DarkMode = null);

    private void LoadAppearance()
    {
        try
        {
            if (!File.Exists(appearancePath)) return;
            var settings = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(appearancePath), MacroFiles.JsonOptions);
            var mode = settings?.Mode ?? (settings?.DarkMode switch { true => AppearanceMode.Dark, false => AppearanceMode.Light, null => AppearanceMode.Windows });
            selectedAppearance = Enum.IsDefined(mode) ? mode : AppearanceMode.Windows;
            appearanceChoice.SelectedItem = selectedAppearance;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus("Darstellung konnte nicht geladen werden: " + error.Message);
        }
    }

    private void ChangeAppearance()
    {
        if (applyingAppearance || appearanceChoice.SelectedItem is not AppearanceMode mode || mode == selectedAppearance) return;
        selectedAppearance = mode;
        ApplyAppearance();
        try { AtomicWrite(appearancePath, JsonSerializer.Serialize(new AppearanceSettings(mode), MacroFiles.JsonOptions)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            SetStatus("Darstellung geändert, konnte aber nicht gespeichert werden: " + error.Message);
        }
    }

    private void ApplyAppearance(bool force = false)
    {
        if (applyingAppearance || IsDisposed || Disposing) return;
        var mode = selectedAppearance;
        var palette = mode == AppearanceMode.Dark || (mode == AppearanceMode.Windows && windowsUsesDarkMode()) ? UiTheme.Dark : UiTheme.Light;
        if (!force && ReferenceEquals(Palette, palette)) return;
        applyingAppearance = true;
        try
        {
            UiTheme.Apply(this, palette);
            foreach (var dialog in Application.OpenForms.Cast<Form>().ToArray())
            {
                for (var owner = dialog.Owner; owner is not null; owner = owner.Owner)
                {
                    if (!ReferenceEquals(owner, this)) continue;
                    UiTheme.Apply(dialog, palette);
                    break;
                }
            }
        }
        finally { applyingAppearance = false; }
    }

    private MacroDocument ReadDocument() => new()
    {
        Name = macroName.Text.Trim(),
        Actions = actions.Select(action => action.Copy()).ToList(),
        Playback = new PlaybackOptions
        {
            Loops = (int)loops.Value, LoopDelayMs = (int)loopDelay.Value, StartDelayMs = (int)startDelay.Value,
            MaximumRunSeconds = (int)maxRun.Value, Speed = speed.Value, UseScanCodes = useScanCodes.Checked
        },
        Recording = new RecorderOptions
        {
            Keyboard = captureKeys.Checked, MouseButtons = captureButtons.Checked, MouseMovement = captureMovement.Checked,
            MouseWheel = captureWheel.Checked, MovementSampleMs = (int)sample.Value
        },
        Hotkeys = ReadHotkeys()
    };

    private HotkeyOptions ReadHotkeys() => new()
    {
        Record = hotkeyFields[0].Text.Trim(), StopRecording = hotkeyFields[1].Text.Trim(),
        Play = hotkeyFields[2].Text.Trim(), StopAll = hotkeyFields[3].Text.Trim()
    };

    private void ApplyDocumentToControls(MacroDocument document)
    {
        macroName.Text = document.Name ?? "";
        finiteLoopCount = Math.Max(1, document.Playback.Loops);
        loops.Value = document.Playback.Loops;
        loopDelay.Value = document.Playback.LoopDelayMs;
        startDelay.Value = document.Playback.StartDelayMs;
        maxRun.Value = document.Playback.MaximumRunSeconds;
        speed.Value = document.Playback.Speed;
        useScanCodes.Checked = document.Playback.UseScanCodes;
        sample.Value = document.Recording.MovementSampleMs;
        captureKeys.Checked = document.Recording.Keyboard;
        captureButtons.Checked = document.Recording.MouseButtons;
        captureMovement.Checked = document.Recording.MouseMovement;
        captureWheel.Checked = document.Recording.MouseWheel;
        var values = document.Hotkeys.Values();
        for (var index = 0; index < values.Length; index++) hotkeyFields[index].Text = values[index];
    }

    private void AddAction()
    {
        if (Busy) return;
        using var editor = new ActionEditor(new MacroAction());
        UiTheme.Apply(editor, Palette);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        var position = SelectedIndex < 0 ? actions.Count : SelectedIndex + 1;
        actions.Insert(position, editor.Result);
        SelectRow(position);
    }

    private void EditAction()
    {
        if (Busy || SelectedIndex < 0) return;
        var index = SelectedIndex;
        using var editor = new ActionEditor(actions[index].Copy());
        UiTheme.Apply(editor, Palette);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        actions[index] = editor.Result;
        SelectRow(index);
    }

    private int SelectedIndex => grid.CurrentRow?.Index ?? -1;
    private void Duplicate()
    {
        if (Busy || SelectedIndex < 0) return;
        var index = SelectedIndex;
        actions.Insert(index + 1, actions[index].Copy());
        SelectRow(index + 1);
    }

    private void DeleteSelected()
    {
        if (Busy) return;
        var indices = grid.SelectedRows.Cast<DataGridViewRow>().Select(row => row.Index).OrderDescending().ToArray();
        foreach (var index in indices) actions.RemoveAt(index);
    }

    private void MoveSelected(int offset)
    {
        if (Busy || SelectedIndex < 0) return;
        var index = SelectedIndex;
        var destination = index + offset;
        if (destination < 0 || destination >= actions.Count) return;
        ReorderActions([actions[index]], destination + (offset > 0 ? 1 : 0));
    }

    private void ReorderActions(MacroAction[] selection, int insertion)
    {
        if (Busy) return;
        var reordered = ActionOrder.Move(actions, selection, insertion, out var destination);
        if (reordered is null) return;
        ReplaceActions(reordered);
        SelectRow(destination);
        for (var index = destination + 1; index < destination + selection.Length; index++) grid.Rows[index].Selected = true;
        SetStatus(selection.Length == 1 ? "Aktion verschoben." : $"{selection.Length:N0} Aktionen verschoben.");
    }

    private void SelectRow(int index)
    {
        if (index < 0 || index >= grid.Rows.Count) return;
        grid.ClearSelection();
        grid.CurrentCell = grid.Rows[index].Cells[0];
        grid.Rows[index].Selected = true;
    }

    private void ReplaceActions(List<MacroAction> replacements)
    {
        actions.RaiseListChangedEvents = false;
        actions.Clear();
        foreach (var action in replacements) actions.Add(action);
        actions.RaiseListChangedEvents = true;
        source.ResetBindings(false);
        RefreshActionView();
        MarkDirty();
    }

    private void UndoRecording()
    {
        if (Busy) return;
        if (beforeRecording is null) { SetStatus("Es gibt keine Aufnahme zum Rückgängigmachen."); return; }
        ReplaceActions(beforeRecording);
        beforeRecording = null;
        SetStatus("Vorheriger Ablauf wiederhergestellt.");
    }

    private void NewDocument()
    {
        if (Busy || !ConfirmDiscard()) return;
        ClearDocument();
        SetStatus("Neuer Ablauf.");
    }

    private void ClearDocument()
    {
        actions.Clear();
        currentPath = null;
        macroName.Clear();
        savedMacros.SelectedIndex = -1;
        beforeRecording = null;
        dirty = false;
        UpdateTitle();
        SelectWorkspace(actionPage);
    }

    private void Open()
    {
        if (Busy || !ConfirmDiscard()) return;
        using var dialog = new OpenFileDialog { Title = "Makro importieren", Filter = "Makro-Dateien (*.macro.json;*.json)|*.macro.json;*.json|Alle Dateien (*.*)|*.*" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ImportMacro(dialog.FileName);
    }

    internal void ImportMacro(string path)
    {
        var document = MacroFiles.Parse(File.ReadAllText(path));
        using var nameDialog = new MacroNameDialog(library, string.IsNullOrWhiteSpace(document.Name) ? MacroLibrary.NameFromPath(path) : document.Name, "Importiertes Makro benennen");
        UiTheme.Apply(nameDialog, Palette);
        if (nameDialog.ShowDialog(this) != DialogResult.OK) return;
        var destination = library.SavePath(nameDialog.MacroName);
        var overwrite = File.Exists(destination);
        if (overwrite && !ConfirmOverwrite(nameDialog.MacroName)) return;
        var saved = library.Save(nameDialog.MacroName, document, overwrite);
        RefreshLibrary(saved);
        LoadMacro(saved, document);
        SetStatus($"„{document.Name}“ wurde importiert.");
    }

    private void LoadSelectedMacro()
    {
        if (Busy) return;
        if (savedMacros.SelectedItem is not MacroLibraryEntry entry) { SetStatus("Wähle zuerst ein Makro in der Bibliothek."); return; }
        if (!ConfirmDiscard()) { RefreshLibrary(currentPath); return; }
        var document = MacroFiles.Parse(File.ReadAllText(entry.FilePath));
        document.Name = entry.Name;
        LoadMacro(entry.FilePath, document);
    }

    private void LoadMacro(string path, MacroDocument document)
    {
        hotkeys!.Apply(document.Hotkeys);
        initializing = true;
        try { ApplyDocumentToControls(document); ReplaceActions(document.Actions); }
        finally { initializing = false; }
        currentPath = path;
        beforeRecording = null;
        dirty = false;
        UpdateTitle();
        UpdateHotkeySummary();
        SavePreferences(document);
        RefreshLibrary(path);
        SelectWorkspace(actionPage);
        SetStatus($"„{document.Name}“ geöffnet · {actions.Count:N0} Aktionen");
    }

    private bool Save(bool saveAs)
    {
        if (Busy) return false;
        var document = ReadDocument();
        MacroFiles.ValidateOptions(document);
        MacroFiles.ValidateActions(document.Actions, []);
        var name = document.Name;
        if (saveAs || string.IsNullOrWhiteSpace(name))
        {
            using var dialog = new MacroNameDialog(library, string.IsNullOrWhiteSpace(name) ? "Mein Makro" : name + " – Kopie", saveAs ? "Kopie speichern" : "Makro benennen");
            UiTheme.Apply(dialog, Palette);
            if (dialog.ShowDialog(this) != DialogResult.OK) return false;
            name = dialog.MacroName;
        }
        var path = library.SavePath(name);
        var overwrite = File.Exists(path);
        if (overwrite && (saveAs || !string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase)) && !ConfirmOverwrite(name)) return false;
        hotkeys?.Apply(document.Hotkeys);
        UpdateHotkeySummary();
        path = library.Save(name, document, overwrite);
        currentPath = path;
        macroName.Text = document.Name;
        dirty = false;
        UpdateTitle();
        SavePreferences(document);
        RefreshLibrary(path);
        SetStatus($"„{document.Name}“ gespeichert.");
        return true;
    }

    private bool ConfirmOverwrite(string name) => MessageBox.Show(this, $"Ein Makro namens „{name}“ ist bereits gespeichert. Möchtest du es ersetzen?", "Makro ersetzen", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    private void RenameSelectedMacro()
    {
        if (Busy || grid.ReorderInProgress || savedMacros.SelectedItem is not MacroLibraryEntry entry) return;
        string? path = null;
        using var dialog = new MacroNameDialog(entry.Name, "Makro umbenennen", "Umbenennen", Path.GetDirectoryName(entry.FilePath)!,
            name => path = library.Rename(entry, name));
        UiTheme.Apply(dialog, Palette);
        if (dialog.ShowDialog(this) != DialogResult.OK || path is null) return;
        if (path == entry.FilePath)
        {
            SetStatus($"„{entry.Name}“ ist in der Makrodatei als Name gespeichert.");
            return;
        }

        if (string.Equals(currentPath, entry.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            currentPath = path;
            if (macroName.Text.Trim() == entry.Name)
            {
                initializing = true;
                try { macroName.Text = dialog.MacroName; }
                finally { initializing = false; }
                UpdateTitle();
            }
        }
        RefreshLibrary(path);
        SetStatus($"„{entry.Name}“ wurde in „{dialog.MacroName}“ umbenannt.");
    }

    private void DeleteSelectedMacro()
    {
        if (Busy || grid.ReorderInProgress || savedMacros.SelectedItem is not MacroLibraryEntry entry) return;
        var isOpen = string.Equals(currentPath, entry.FilePath, StringComparison.OrdinalIgnoreCase);
        var keepDraft = isOpen && dirty;
        using var confirm = new MacroDeleteDialog(entry, keepDraft);
        UiTheme.Apply(confirm, Palette);
        if (confirm.ShowDialog(this) != DialogResult.OK) return;

        var removed = library.Delete(entry);
        if (isOpen)
        {
            if (keepDraft) { currentPath = null; MarkDirty(); }
            else ClearDocument();
        }
        RefreshLibrary(currentPath);
        var message = removed ? $"„{entry.Name}“ wurde endgültig gelöscht." : $"„{entry.Name}“ war nicht mehr vorhanden.";
        SetStatus(keepDraft ? message + " Der ungespeicherte Entwurf bleibt geöffnet." : message);
    }

    private void RefreshLibrary(string? selectedPath = null)
    {
        selectedPath ??= (savedMacros.SelectedItem as MacroLibraryEntry)?.FilePath ?? currentPath;
        savedMacros.BeginUpdate();
        try
        {
            savedMacros.Items.Clear();
            savedMacros.Items.AddRange(library.List().Cast<object>().ToArray());
            savedMacros.SelectedIndex = -1;
            for (var index = 0; index < savedMacros.Items.Count; index++)
                if (savedMacros.Items[index] is MacroLibraryEntry entry && string.Equals(entry.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase)) savedMacros.SelectedIndex = index;
            if (savedMacros.SelectedIndex < 0 && savedMacros.Items.Count > 0) savedMacros.SelectedIndex = 0;
        }
        finally { savedMacros.EndUpdate(); }
        RefreshLibraryView();
    }

    private void OpenLibraryFolder()
    {
        Directory.CreateDirectory(library.DirectoryPath);
        Process.Start(new ProcessStartInfo(library.DirectoryPath) { UseShellExecute = true });
    }

    private static void AtomicWrite(string path, string content)
    {
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, content);
        File.Move(temporary, path, true);
    }

    private bool ConfirmDiscard()
    {
        if (!dirty) return true;
        var answer = MessageBox.Show(this, "Möchtest du die Änderungen am aktuellen Makro speichern?", "Ungespeicherte Änderungen", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        return answer == DialogResult.No || answer == DialogResult.Yes && Save(false);
    }

    private async void OnClosing(object? sender, FormClosingEventArgs args)
    {
        if (closing) return;
        if (Busy)
        {
            args.Cancel = true;
            StopAll();
            if (playbackTask is not null)
            {
                try { await playbackTask; } catch { }
            }
            BeginInvoke(Close);
            return;
        }
        try
        {
            if (!ConfirmDiscard()) { args.Cancel = true; return; }
            var document = ReadDocument();
            MacroFiles.ValidateOptions(document);
            SavePreferences(document);
            closing = true;
        }
        catch (Exception error) { args.Cancel = true; ShowError(error); }
    }

    private void RefreshStatus()
    {
        var position = Cursor.Position;
        cursorLabel.Text = $"Cursor: X {position.X}   Y {position.Y}";
        if (recordingSession) status.Text = $"● Aufnahme  {sessionWatch.Elapsed:mm\\:ss}  ·  {recorder.EventCount:N0} Aktionen  ·  Beenden: {hotkeys?.Current.StopRecording}";
        else if (playing)
        {
            var action = Volatile.Read(ref currentAction);
            var waiting = Volatile.Read(ref imageTargetStatus);
            status.Text = waiting is not null ? $"Bildziel: {waiting} — warte auf Treffer · Stopp: {hotkeys?.Current.StopAll}" : action == 0
                ? $"Start in {Math.Max(0, (double)startDelay.Value / 1000 - sessionWatch.Elapsed.TotalSeconds):0.0} s · {(usesGlobalPlaybackInput ? "Zum Zielfenster wechseln" : "Hintergrundmodus")} · Stopp: {hotkeys?.Current.StopAll}"
                : $"▶ Wiedergabe · Durchlauf {Volatile.Read(ref currentLoop):N0} · Aktion {action:N0}/{actions.Count:N0} · Stopp: {hotkeys?.Current.StopAll}";
        }
    }

    private void UpdateState()
    {
        recordButton.Enabled = !Busy;
        playButton.Enabled = !Busy && actions.Count > 0;
        repeatList.Enabled = !Busy;
        fileBar.Enabled = editBar.Enabled = settingsTab.Enabled = !Busy;
        libraryBar.Enabled = !Busy;
        grid.Enabled = !Busy;
        stopButton.Text = recordingSession ? "■  Aufnahme beenden" : "■  Stopp";
        stopButton.Enabled = Busy;
        UiTheme.StyleButton(stopButton, Palette);
        RefreshActionView();
        RefreshLibraryView();
    }

    private void UpdateHotkeySummary()
    {
        var options = hotkeys?.Current ?? ReadHotkeys();
        hotkeySummary.Text = hotkeys is { IsRegistered: false } ? "Keine aktiven Tastenkürzel. Prüfe die Einstellungen."
            : $"{options.Record} Aufnahme   ·   {options.StopRecording} Aufnahme beenden   ·   {options.Play} Abspielen   ·   {options.StopAll} Stopp";
    }

    private void SetStatus(string message) { if (!IsDisposed) status.Text = message; }
    private void MarkDirty() { if (!initializing) { dirty = true; UpdateTitle(); } }
    private void UpdateTitle() => Text = $"{(dirty ? "* " : "")}{(string.IsNullOrWhiteSpace(macroName.Text) ? "Unbenannt" : macroName.Text.Trim())} — Ultimate Macro";
    private void Guard(Action operation) { try { operation(); } catch (Exception error) { ShowError(error); } }
    private void ShowError(Exception error)
    {
        SetStatus(error.Message);
        MessageBox.Show(this, error.Message, "Ultimate Macro", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312)
        {
            switch ((int)message.WParam)
            {
                case 1: if (Native.IsWindowEnabled(Handle)) Guard(StartRecording); break;
                case 2: Guard(StopRecording); break;
                case 3: if (Native.IsWindowEnabled(Handle)) StartPlayback(); break;
                case 4: StopAll(); break;
            }
        }
        base.WndProc(ref message);
        if (message.Msg is 0x001a or 0x031a && IsHandleCreated && !Disposing && !IsDisposed && selectedAppearance == AppearanceMode.Windows)
            BeginInvoke(() => { if (selectedAppearance == AppearanceMode.Windows) ApplyAppearance(); });
    }

    private void OpenExamples()
    {
        Guard(() =>
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Examples");
            if (!Directory.Exists(path)) path = Path.Combine(AppContext.BaseDirectory, "..", "Examples");
            Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) { UseShellExecute = true });
        });
    }

    private static IEnumerable<Control> AllControls(Control parent) => parent.Controls.Cast<Control>().SelectMany(control => new[] { control }.Concat(AllControls(control)));
    private static FlowLayoutPanel Bar() => new() { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
    private static CheckBox Check(string text, bool value) => new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(3, 8, 3, 8) };
    internal static NumericUpDown Number(decimal minimum, decimal maximum, decimal value, int decimals = 0) => new()
    {
        Minimum = minimum, Maximum = maximum, Value = value, DecimalPlaces = decimals, Increment = decimals > 0 ? 0.1m : 1,
        Width = 180, ThousandsSeparator = true
    };
    internal static Button Button(string text, Action? click = null, bool primary = false)
    {
        var button = new PaletteButton();
        ConfigureButton(button, text, click, primary);
        return button;
    }
    internal static void ConfigureButton(Button button, string text, Action? click = null, bool primary = false)
    {
        button.Text = text;
        button.AutoSize = true;
        button.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        button.MinimumSize = new Size(0, 36);
        button.Padding = new Padding(10, 1, 10, 1);
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        UiTheme.Mark(button, primary ? UiRole.Primary : UiRole.Standard);
        UiTheme.StyleButton(button, UiTheme.Light);
        button.Margin = new Padding(0, 0, 7, 0);
        if (click is not null) button.Click += (_, _) => click();
    }
    private static GroupBox SettingsGroup(string title, out FlowLayoutPanel fields)
    {
        var group = UiTheme.Mark(new GroupBox { Text = title, Dock = DockStyle.Fill, Padding = new Padding(10), Margin = new Padding(0, 0, 8, 0) }, UiRole.Soft);
        fields = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        var content = fields;
        content.SizeChanged += (_, _) =>
        {
            foreach (var label in content.Controls.OfType<Label>()) label.MaximumSize = new Size(Math.Max(120, content.ClientSize.Width - 15), 0);
        };
        group.Controls.Add(fields);
        return group;
    }
    private static void AddSetting(FlowLayoutPanel fields, string label, Control input)
    {
        fields.Controls.Add(UiTheme.Mark(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 8, 3, 2) }, UiRole.Muted));
        fields.Controls.Add(input);
    }

    private const string GuideText = """
        DEIN ERSTER ABLAUF

        1. Klicke auf „Aufnehmen“ (F8) und führe die gewünschten Eingaben aus.
        2. Beende die Aufnahme mit F9 oder „Aufnahme beenden“.
        3. Prüfe den Ablauf, gib ihm einen Namen und klicke auf „Speichern“.
        4. Starte mit „Abspielen“ (F10) und wechsle während des Countdowns zum Zielfenster.
        5. „Stopp“ (F11) beendet die Wiedergabe und gibt gehaltene Eingaben frei.

        AKTIONEN UND WIEDERHOLUNG

        „Aktion hinzufügen“ öffnet den Editor. Ein Doppelklick bearbeitet eine vorhandene Zeile.
        Ziehe Aktionen an die gewünschte Stelle. Die Linie zeigt die Einfügeposition.
        Das Menü „Aktionen“ enthält auch Duplizieren, Verschieben, Löschen und Aufnahme rückgängig.
        Für F → E: „Taste tippen“ für F und danach für E hinzufügen. Bei Bedarf „Warten“ dazwischen setzen.
        „Bis zum Stopp wiederholen“ wiederholt den ganzen Ablauf. In den Einstellungen entspricht das 0 Durchläufen.
        Wenn nur manuell gestoppt werden soll, setze das Zeitlimit auf 0.
        Für Moon Sweepers: Einstellungen → Wiedergabe → „Tasten als Scan-Codes senden“.

        BILDZIELE

        Erfasse ein Fenster, markiere den Button und wähle den Klickpunkt.
        Die Vorschau klickt nicht. Vor jedem echten Klick sucht das Makro das Ziel neu.
        Der Hintergrundmodus funktioniert mit kompatiblen, nicht minimierten Fenstern.

        BIBLIOTHEK UND TASTENKÜRZEL

        Ein Doppelklick auf ein Makro öffnet seinen Ablauf. „Importieren“ kopiert eine vorhandene JSON-Datei in die Bibliothek.
        Unter „Mehr“ findest du den Speicherordner, Beispielmakros und die Aktualisierung der Bibliothek.
        Per Rechtsklick auf ein Makro kannst du es umbenennen oder nach Rückfrage endgültig löschen.
        Unter „Datei“ kannst du eine Kopie speichern. Änderungen an einem gespeicherten Makro werden über „Speichern“ übernommen.
        Tastenkürzel lassen sich in den Einstellungen ändern. F12 ist unter Windows für Debugger reserviert.
        Einstellungen → Darstellung: „Wie Windows“ folgt dem App-Design von Windows. Alternativ kannst du „Hell“ oder „Dunkel“ wählen.
        """;
}
