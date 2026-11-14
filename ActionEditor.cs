namespace UltimateMacro;

public sealed class ActionEditor : Form
{
    private readonly ComboBox kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 290, FormattingEnabled = true };
    private readonly ComboBox key = new() { DropDownStyle = ComboBoxStyle.DropDown, Width = 290, MaxDropDownItems = 14 };
    private readonly CheckBox useCurrentCursor = new() { Text = "Aktuelle Mausposition verwenden", AutoSize = true };
    private readonly NumericUpDown x = MainForm.Number(-100000, 100000, 0);
    private readonly NumericUpDown y = MainForm.Number(-120000, 120000, 0);
    private readonly NumericUpDown duration = MainForm.Number(0, 86_400_000, 50);
    private readonly NumericUpDown count = MainForm.Number(0, 1_000_000, 1);
    private readonly NumericUpDown interval = MainForm.Number(0, 86_400_000, 100);
    private readonly TextBox text = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 104, Width = 320, MaxLength = 1_000_000, AcceptsReturn = true };
    private readonly Label help = UiTheme.Mark(new Label { Dock = DockStyle.Fill, Padding = new Padding(11, 9, 11, 7) }, UiRole.Callout);
    private readonly Button pick = MainForm.Button("Position in 3 s wählen");
    private readonly Label cursor = new() { AutoSize = true, Padding = new Padding(0, 8, 0, 0) };
    private readonly Button imageSetup = MainForm.Button("Bildziel einrichten…");
    private readonly FlowLayoutPanel screenPanel = new() { Dock = DockStyle.Fill, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
    private readonly Dictionary<Control, Label> fieldLabels = [];
    private readonly MacroAction original;
    private ImageClickTarget? imageTarget;
    private readonly System.Windows.Forms.Timer cursorTimer = new() { Interval = 100 };
    public MacroAction Result { get; private set; }

    public ActionEditor(MacroAction action)
    {
        original = action;
        imageTarget = action.ImageTarget;
        Result = action;
        Text = "Aktion konfigurieren";
        Font = new Font("Segoe UI", 10);
        ForeColor = Color.FromArgb(23, 36, 58);
        ClientSize = new Size(590, 390);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;
        BackColor = Color.White;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(22, 16, 22, 14) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Aktion konfigurieren", Font = new Font("Segoe UI", 16, FontStyle.Bold), AutoSize = true, Margin = Padding.Empty }, 0, 0);

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty };
        var fields = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, RowCount = 10, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 177));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var index = 0; index < fields.RowCount; index++) fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        scroll.Controls.Add(fields);
        layout.Controls.Add(scroll, 0, 1);

        kind.Format += (_, args) => { if (args.ListItem is ActionKind actionKind) args.Value = ActionDisplay.Name(actionKind); };
        kind.Items.AddRange(Enum.GetValues<ActionKind>().Cast<object>().ToArray());
        AddField(fields, 0, "Art der Aktion", kind);
        AddField(fields, 1, "Taste", key);
        AddField(fields, 2, "Klickposition", useCurrentCursor);
        AddField(fields, 3, "X-Koordinate", x);
        AddField(fields, 4, "Y-Koordinate", y);
        AddField(fields, 5, "Dauer (ms)", duration);
        AddField(fields, 6, "Anzahl", count);
        AddField(fields, 7, "Abstand (ms)", interval);
        screenPanel.Controls.AddRange([pick, cursor, imageSetup]);
        AddField(fields, 8, "Bildschirmposition", screenPanel);
        AddField(fields, 9, "Text", text);
        layout.Controls.Add(help, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 7, 0, 0), Margin = Padding.Empty };
        var save = MainForm.Button("Aktion speichern", SaveAction, primary: true);
        save.Name = "SaveAction";
        var cancel = MainForm.Button("Abbrechen");
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([save, cancel]);
        layout.Controls.Add(buttons, 0, 3);
        AcceptButton = save;
        CancelButton = cancel;
        imageSetup.Click += (_, _) =>
        {
            using var dialog = new ImageTargetDialog(imageTarget);
            UiTheme.Apply(dialog, UiTheme.Current(this));
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            imageTarget = dialog.Result;
            UpdateImageButton();
        };
        pick.Click += async (_, _) =>
        {
            kind.Enabled = pick.Enabled = save.Enabled = useCurrentCursor.Enabled = false;
            for (var seconds = 3; seconds > 0; seconds--)
            {
                pick.Text = $"Position in {seconds} s …";
                await Task.Delay(1000);
                if (IsDisposed) return;
            }
            var position = Cursor.Position;
            x.Value = Math.Clamp(position.X, x.Minimum, x.Maximum);
            y.Value = Math.Clamp(position.Y, y.Minimum, y.Maximum);
            pick.Text = "Position in 3 s wählen";
            kind.Enabled = pick.Enabled = save.Enabled = useCurrentCursor.Enabled = true;
        };
        cursorTimer.Tick += (_, _) => cursor.Text = $"X {Cursor.Position.X}  Y {Cursor.Position.Y}";
        cursorTimer.Start();
        Disposed += (_, _) => cursorTimer.Dispose();
        useCurrentCursor.Checked = action.Action != ActionKind.MouseClick || action.UseCurrentCursorPosition;
        useCurrentCursor.CheckedChanged += (_, _) => UpdateFields(false);
        kind.SelectedIndexChanged += (_, _) => { if (!UiTheme.IsApplying(this)) UpdateFields(true); };
        kind.SelectedItem = action.Action;
        UpdateFields(false);
        key.Text = action.Key;
        x.Value = Math.Clamp(action.X, x.Minimum, x.Maximum);
        y.Value = Math.Clamp(action.Y, y.Minimum, y.Maximum);
        duration.Value = action.DurationMs;
        count.Value = action.Count;
        interval.Value = action.IntervalMs;
        text.Text = action.Text;
    }

    private void UpdateFields(bool defaults)
    {
        if (kind.SelectedItem is not ActionKind action) return;
        var isKey = action is ActionKind.KeyPress or ActionKind.KeyDown or ActionKind.KeyUp or ActionKind.KeyHold or ActionKind.KeyRepeat;
        var isImage = action == ActionKind.ImageClick;
        var isClick = action == ActionKind.MouseClick;
        var isButton = isImage || action is ActionKind.MouseClick or ActionKind.MouseDown or ActionKind.MouseUp;
        var isScroll = action == ActionKind.MouseScroll;
        var position = action is ActionKind.MouseMove or ActionKind.MouseDown or ActionKind.MouseUp || (isClick && !useCurrentCursor.Checked);
        var hasDuration = action is ActionKind.Delay or ActionKind.KeyHold or ActionKind.KeyRepeat or ActionKind.KeyPress or ActionKind.MouseMove or ActionKind.MouseClick or ActionKind.ImageClick;
        var hasCount = action is ActionKind.KeyPress or ActionKind.KeyRepeat or ActionKind.MouseClick or ActionKind.MouseScroll or ActionKind.ImageClick;
        var hasInterval = action is ActionKind.KeyPress or ActionKind.KeyRepeat or ActionKind.MouseClick or ActionKind.Text or ActionKind.ImageClick;
        var hasText = action == ActionKind.Text;
        var hasKey = isKey || isButton || isScroll;

        SetField(key, hasKey, isButton ? "Maustaste" : isScroll ? "Richtung" : "Taste");
        SetField(useCurrentCursor, isClick);
        SetField(x, position);
        SetField(y, position || isScroll, isScroll ? "Scroll-Einheiten" : "Y-Koordinate");
        SetField(duration, hasDuration);
        SetField(count, hasCount, action is ActionKind.KeyRepeat or ActionKind.MouseClick or ActionKind.ImageClick ? "Anzahl (0 = unbegrenzt)" : "Anzahl");
        SetField(interval, hasInterval, action == ActionKind.KeyRepeat ? "Intervall (ms)" : "Abstand (ms)");
        SetField(text, hasText);
        SetField(screenPanel, position || isImage, isImage ? "Bildziel" : "Bildschirmposition");
        pick.Visible = cursor.Visible = position;
        imageSetup.Visible = isImage;
        UpdateImageButton();

        if (defaults)
        {
            key.Items.Clear();
            if (isKey) key.Items.AddRange(KeyNames.EditorChoices.Cast<object>().ToArray());
            if (isButton) key.Items.AddRange(new object[] { "Left", "Right", "Middle", "X1", "X2" });
            if (isScroll) key.Items.AddRange(new object[] { "Vertical", "Horizontal" });
            key.Text = isButton ? "Left" : isScroll ? "Vertical" : "A";
            duration.Value = action is ActionKind.Delay or ActionKind.KeyHold ? 1000 : action is ActionKind.MouseMove or ActionKind.KeyRepeat ? 0 : 50;
            count.Value = 1;
            interval.Value = hasText ? 0 : 100;
            if (isScroll) y.Value = 120;
        }
        help.Text = action switch
        {
            ActionKind.Delay => "Der Ablauf wartet diese Zeit und fährt dann mit der nächsten Aktion fort.",
            ActionKind.KeyPress => "Tippt die Taste so oft wie angegeben. Dauer ist die Haltezeit pro Druck, Abstand die Pause zwischen zwei Drücken.",
            ActionKind.KeyDown => "Hält die Taste gedrückt. Füge „Taste loslassen“ hinzu, um sie vor dem Ende des Durchlaufs freizugeben.",
            ActionKind.KeyUp => "Lässt die Taste los. Nutze diese Aktion nach „Taste drücken“ für Tastenkombinationen.",
            ActionKind.KeyHold => "Hält die Taste für die angegebene Zeit. Dauer 0 hält sie bis zum manuellen Stopp.",
            ActionKind.KeyRepeat => "Anzahl und Dauer verteilen die Drücke über die Zeit. Ist einer der Werte 0, gilt das Intervall. Sind beide 0, wird bis zum Stopp wiederholt.",
            ActionKind.MouseMove => "Bewegt den Zeiger zu X und Y. Dauer 0 springt sofort dorthin.",
            ActionKind.MouseClick when useCurrentCursor.Checked => "Klickt dort, wo sich die Maus gerade befindet. Du kannst sie während der Wiederholung bewegen. Anzahl 0 wiederholt bis zum Stopp.",
            ActionKind.MouseClick => "Bewegt den Zeiger zu X und Y, bevor die Klicks beginnen. Anzahl 0 wiederholt bis zum Stopp. Abstand ist die Pause zwischen zwei Klicks.",
            ActionKind.ImageClick => "Markiere das Ziel und teste es in der Vorschau. Vor jedem Klick wird neu gesucht; bei unklarem Treffer wartet das Makro.",
            ActionKind.MouseDown => "Bewegt den Zeiger zur Position und hält die Maustaste. Füge Bewegung und Loslassen hinzu, um zu ziehen.",
            ActionKind.MouseUp => "Bewegt den Zeiger zur Position und lässt die Maustaste los.",
            ActionKind.MouseScroll => "120 Einheiten entsprechen einer Stufe nach oben oder rechts; −120 scrollt in die Gegenrichtung.",
            _ => "Gibt den Text im aktiven Fenster ein. Mit Abstand 0 entfällt die zusätzliche Pause zwischen Zeichen."
        };
        var rows = new[] { true, hasKey, isClick, position, position || isScroll, hasDuration, hasCount, hasInterval, position || isImage, hasText }.Count(visible => visible);
        ClientSize = new Size(590, Math.Max(266, 204 + rows * 35 + (hasText ? 78 : 0)));
    }

    private void UpdateImageButton() => imageSetup.Text = imageTarget is null ? "Bildziel einrichten…" : "Bildziel bearbeiten / Vorschau…";

    private void SaveAction()
    {
        try
        {
            var selected = (ActionKind)kind.SelectedItem!;
            Result = new MacroAction
            {
                Action = selected, Key = key.Text.Trim(), X = (int)x.Value, Y = (int)y.Value, DurationMs = (int)duration.Value,
                UseCurrentCursorPosition = selected == ActionKind.MouseClick && useCurrentCursor.Checked,
                Count = (int)count.Value, IntervalMs = (int)interval.Value, Text = text.Text,
                ScanCode = selected == original.Action && key.Text.Trim() == original.Key ? original.ScanCode : 0,
                Extended = selected == original.Action && key.Text.Trim() == original.Key && original.Extended,
                ImageTarget = selected == ActionKind.ImageClick ? imageTarget : null
            };
            MacroFiles.ValidateActions([Result], []);
            DialogResult = DialogResult.OK;
        }
        catch (Exception error) { MessageBox.Show(this, error.Message, "Aktion prüfen", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void AddField(TableLayoutPanel layout, int row, string title, Control input)
    {
        var label = new Label { Text = title, AutoSize = true, MaximumSize = new Size(166, 0), Margin = new Padding(0, 7, 6, 0) };
        input.Margin = new Padding(0, 2, 0, 7);
        input.AccessibleName = title;
        fieldLabels.Add(input, label);
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(input, 1, row);
    }

    private void SetField(Control input, bool visible, string? title = null)
    {
        var label = fieldLabels[input];
        if (title is not null) { label.Text = title; input.AccessibleName = title; }
        input.Visible = label.Visible = visible;
    }
}
