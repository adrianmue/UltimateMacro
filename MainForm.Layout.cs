namespace UltimateMacro;

public sealed partial class MainForm
{
    private UiPalette Palette => UiTheme.Current(this);

    private void BuildLayout()
    {
        UiTheme.Mark(this, UiRole.Canvas);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 14), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        Controls.Add(root);
        root.Controls.Add(BuildHeader(), 0, 0);

        var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 264));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(split, 0, 1);
        BuildLibraryBar();
        split.Controls.Add(libraryBar, 0, 0);
        split.Controls.Add(BuildWorkspace(), 1, 0);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 7, 0, 0), Margin = Padding.Empty };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 76));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        status.Dock = hotkeySummary.Dock = cursorLabel.Dock = DockStyle.Fill;
        status.UseMnemonic = false;
        status.AutoEllipsis = true;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Font = new Font(Font, FontStyle.Bold);
        UiTheme.Mark(hotkeySummary, UiRole.Muted);
        UiTheme.Mark(cursorLabel, UiRole.Muted);
        hotkeySummary.AutoEllipsis = true;
        cursorLabel.TextAlign = ContentAlignment.TopRight;
        footer.Controls.Add(status, 0, 0);
        updateButton.Visible = false;
        updateButton.Anchor = AnchorStyles.Right;
        updateButton.MinimumSize = new Size(0, 27);
        updateButton.Margin = Padding.Empty;
        updateButton.Click += async (_, _) => await ShowUpdateResultAsync();
        footer.Controls.Add(updateButton, 1, 0);
        footer.Controls.Add(hotkeySummary, 0, 1);
        footer.Controls.Add(cursorLabel, 1, 1);
        root.Controls.Add(footer, 0, 2);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 288));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var brand = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        brand.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        brand.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        brand.Controls.Add(new Label { Text = "Ultimate Macro", Font = new Font("Segoe UI", 25, FontStyle.Bold), AutoSize = true, Margin = Padding.Empty }, 0, 0);
        brand.Controls.Add(UiTheme.Mark(new Label { Text = "Aufnehmen, bearbeiten, abspielen", AutoSize = true, Margin = new Padding(2, 0, 0, 0) }, UiRole.Muted), 0, 1);
        header.Controls.Add(brand, 0, 0);

        var commands = Bar();
        commands.FlowDirection = FlowDirection.RightToLeft;
        commands.Padding = new Padding(0, 9, 0, 0);
        ConfigureButton(playButton, "▶  Abspielen", () => Guard(StartPlayback), primary: true);
        ConfigureButton(recordButton, "●  Aufnehmen", () => Guard(StartRecording));
        ConfigureButton(stopButton, "■  Stopp", StopAll);
        UiTheme.Mark(stopButton, UiRole.Stop);
        stopButton.MinimumSize = new Size(102, 38);
        commands.Controls.AddRange([stopButton, repeatList, playButton, recordButton]);
        header.Controls.Add(commands, 1, 0);
        return header;
    }

    private void BuildLibraryBar()
    {
        libraryBar.Dock = DockStyle.Fill;
        UiTheme.Mark(libraryBar, UiRole.Surface);
        libraryBar.Margin = new Padding(0, 0, 14, 0);
        libraryBar.Padding = new Padding(14, 13, 14, 10);
        libraryBar.ColumnCount = 1;
        libraryBar.RowCount = 5;
        libraryBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        libraryBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        libraryBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        libraryBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        libraryBar.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        libraryBar.Controls.Add(new Label { Text = "Bibliothek", Font = new Font("Segoe UI", 15, FontStyle.Bold), AutoSize = true, Margin = Padding.Empty }, 0, 0);
        libraryCount.Dock = DockStyle.Fill;
        UiTheme.Mark(libraryCount, UiRole.Muted);
        libraryBar.Controls.Add(libraryCount, 0, 1);
        var tools = Bar();
        var newButton = Button("+ Neu", () => Guard(NewDocument));
        newButton.Name = "NewMacro";
        var more = MenuButton("Mehr ▾", "Weitere Bibliotheksbefehle", ("Bibliothek aktualisieren", () => Guard(() => RefreshLibrary())),
            ("Speicherordner öffnen", () => Guard(OpenLibraryFolder)), ("Beispiele öffnen", OpenExamples),
            ("Endgültig löschen…", () => Guard(DeleteSelectedMacro)));
        more.Name = "LibraryMenu";
        var libraryMenu = more.ContextMenuStrip!;
        var delete = libraryMenu.Items[libraryMenu.Items.Count - 1];
        delete.Name = "DeleteSavedMacro";
        libraryMenu.Items.Insert(libraryMenu.Items.Count - 1, new ToolStripSeparator());
        libraryMenu.Opening += (_, _) => delete.Enabled = !Busy && savedMacros.SelectedIndex >= 0;
        tools.Controls.AddRange([newButton, more]);
        libraryBar.Controls.Add(tools, 0, 2);

        var listHost = UiTheme.Mark(new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 0, 0) }, UiRole.Soft);
        UiTheme.Mark(savedMacros, UiRole.Soft);
        savedMacros.BorderStyle = BorderStyle.None;
        savedMacros.DrawMode = DrawMode.OwnerDrawFixed;
        savedMacros.ItemHeight = 36;
        savedMacros.DrawItem += DrawLibraryItem;
        savedMacros.SelectedIndexChanged += (_, _) => RefreshSelectionButtons();
        var rowMenu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false, Padding = new Padding(4) };
        rowMenu.Items.Add("Öffnen", null, (_, _) => Guard(LoadSelectedMacro));
        rowMenu.Items.Add("Umbenennen…", null, (_, _) => Guard(RenameSelectedMacro));
        rowMenu.Items.Add(new ToolStripSeparator());
        rowMenu.Items.Add("Endgültig löschen…", null, (_, _) => Guard(DeleteSelectedMacro));
        rowMenu.Opening += (_, args) => args.Cancel = Busy || savedMacros.SelectedIndex < 0;
        savedMacros.ContextMenuStrip = rowMenu;
        savedMacros.Disposed += (_, _) => rowMenu.Dispose();
        savedMacros.MouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Right) savedMacros.SelectedIndex = savedMacros.IndexFromPoint(args.Location);
        };
        savedMacros.DoubleClick += (_, _) => { if (savedMacros.IndexFromPoint(savedMacros.PointToClient(Cursor.Position)) >= 0) Guard(LoadSelectedMacro); };
        savedMacros.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; Guard(LoadSelectedMacro); }
            if (args.KeyCode == Keys.Delete) { args.SuppressKeyPress = true; Guard(DeleteSelectedMacro); }
        };
        emptyLibrary.Text = "Noch keine gespeicherten Makros.\n\nBeginne mit „Neu“ oder importiere eine vorhandene Datei.";
        emptyLibrary.Dock = DockStyle.Fill;
        emptyLibrary.Padding = new Padding(10, 14, 10, 0);
        UiTheme.Mark(emptyLibrary, UiRole.Muted);
        listHost.Controls.AddRange([savedMacros, emptyLibrary]);
        libraryBar.Controls.Add(listHost, 0, 3);

        var bottom = Bar();
        bottom.Padding = new Padding(0, 8, 0, 0);
        ConfigureButton(openSavedButton, "Öffnen", () => Guard(LoadSelectedMacro));
        bottom.Controls.AddRange([openSavedButton, Button("Importieren…", () => Guard(Open))]);
        libraryBar.Controls.Add(bottom, 0, 4);
    }

    private Control BuildWorkspace()
    {
        var workspace = UiTheme.Mark(new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(13, 9, 13, 10), ColumnCount = 1, RowCount = 3, Margin = Padding.Empty }, UiRole.Surface);
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        fileBar.Dock = DockStyle.Fill;
        fileBar.ColumnCount = 4;
        fileBar.Margin = Padding.Empty;
        fileBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        fileBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        fileBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fileBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fileBar.Controls.Add(UiTheme.Mark(new Label { Text = "Makroname", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(2, 0, 0, 8) }, UiRole.Muted), 0, 0);
        macroName.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        macroName.BorderStyle = BorderStyle.FixedSingle;
        macroName.Margin = new Padding(0, 0, 10, 8);
        fileBar.Controls.Add(macroName, 1, 0);
        var save = Button("Speichern", () => Guard(() => Save(false)));
        save.Name = "SaveMacro";
        fileBar.Controls.Add(save, 2, 0);
        fileBar.Controls.Add(MenuButton("Datei ▾", "Weitere Dateibefehle", ("Kopie speichern…", () => Guard(() => Save(true))),
            ("Nach Updates suchen…", async () => await CheckForUpdatesAsync(manual: true))), 3, 0);
        workspace.Controls.Add(fileBar, 0, 0);

        var navigation = Bar();
        var content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        AddWorkspacePage(navigation, content, actionPage, "Ablauf", "ActionView");
        AddWorkspacePage(navigation, content, settingsTab, "Einstellungen", "SettingsView");
        AddWorkspacePage(navigation, content, BuildHelp(), "Hilfe", "HelpView");
        workspace.Controls.Add(navigation, 0, 1);
        workspace.Controls.Add(content, 0, 2);
        BuildActionView(actionPage);
        BuildSettings();
        SelectWorkspace(actionPage);
        return workspace;
    }

    private void AddWorkspacePage(Control navigation, Control container, Control page, string title, string name)
    {
        var tab = Button(title, () => SelectWorkspace(page));
        tab.Name = name;
        tab.Padding = new Padding(14, 1, 14, 1);
        workspacePages.Add((page, tab));
        navigation.Controls.Add(tab);
        container.Controls.Add(page);
    }

    private void SelectWorkspace(Control selected)
    {
        foreach (var section in workspacePages)
        {
            var active = ReferenceEquals(section.Page, selected);
            section.Page.Visible = active;
            UiTheme.Mark(section.Tab, active ? UiRole.ActiveNavigation : UiRole.Navigation);
            UiTheme.StyleButton(section.Tab, Palette);
        }
        selected.BringToFront();
    }

    private void BuildActionView(Control tab)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        tab.Controls.Add(layout);
        editBar.Dock = DockStyle.Fill;
        editBar.ColumnCount = 2;
        editBar.Margin = Padding.Empty;
        editBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        actionCount.Dock = DockStyle.Fill;
        actionCount.TextAlign = ContentAlignment.MiddleLeft;
        UiTheme.Mark(actionCount, UiRole.Muted);
        editBar.Controls.Add(actionCount, 0, 0);
        var commands = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var add = Button("+ Aktion hinzufügen", () => Guard(AddAction), primary: true);
        ConfigureButton(editActionButton, "Bearbeiten", () => Guard(EditAction));
        var more = MenuButton("Aktionen ▾", "Weitere Aktionen", ("Duplizieren", () => Guard(Duplicate)), ("Nach oben", () => Guard(() => MoveSelected(-1))),
            ("Nach unten", () => Guard(() => MoveSelected(1))), ("Löschen", () => Guard(DeleteSelected)), ("Aufnahme rückgängig", () => Guard(UndoRecording)));
        more.Name = "ActionsMenu";
        var menu = more.ContextMenuStrip!;
        menu.Opening += (_, args) =>
        {
            if (Busy) { args.Cancel = true; return; }
            var index = SelectedIndex;
            menu.Items[0].Enabled = index >= 0;
            menu.Items[1].Enabled = index > 0;
            menu.Items[2].Enabled = index >= 0 && index < actions.Count - 1;
            menu.Items[3].Enabled = grid.SelectedRows.Count > 0;
            menu.Items[4].Enabled = beforeRecording is not null;
        };
        commands.Controls.AddRange([add, editActionButton, more]);
        editBar.Controls.Add(commands, 1, 0);
        layout.Controls.Add(editBar, 0, 0);

        ConfigureGrid();
        grid.ContextMenuStrip = menu;
        var host = UiTheme.Mark(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, UiRole.Soft);
        emptyActions.Dock = DockStyle.Fill;
        var emptyLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        emptyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        emptyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        emptyLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        emptyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        emptyLayout.Controls.Add(new Label { Text = "Dein Ablauf ist noch leer", TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 14, FontStyle.Bold) }, 0, 1);
        emptyLayout.Controls.Add(UiTheme.Mark(new Label { Text = "Füge eine Aktion hinzu, nimm einen Ablauf auf\noder öffne ein Makro aus der Bibliothek.", TextAlign = ContentAlignment.TopCenter, Dock = DockStyle.Fill }, UiRole.Muted), 0, 2);
        emptyActions.Controls.Add(emptyLayout);
        host.Controls.AddRange([grid, emptyActions]);
        layout.Controls.Add(host, 0, 1);
        layout.Controls.Add(UiTheme.Mark(new Label { Text = "Ziehen sortiert die Aktionen. Doppelklick öffnet den Editor.", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, UiRole.Muted), 0, 2);
    }

    private void ConfigureGrid()
    {
        grid.Dock = DockStyle.Fill;
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = grid.RowHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 0, 0);
        grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = grid.AllowUserToResizeRows = false;
        grid.AutoGenerateColumns = false;
        grid.ReadOnly = true;
        grid.MultiSelect = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersWidth = 48;
        grid.RowHeadersWidthSizeMode = DataGridViewRowHeadersWidthSizeMode.DisableResizing;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ColumnHeadersHeight = 34;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.RowTemplate.Height = 39;
        grid.DefaultCellStyle.Padding = new Padding(8, 0, 5, 0);
        AddColumn("Aktion", nameof(MacroAction.Action), 26);
        AddColumn("Ziel", nameof(MacroAction.Key), 32);
        AddColumn("Details", nameof(MacroAction.DurationMs), 42);
        grid.CellFormatting += (_, args) =>
        {
            if (args.RowIndex < 0 || args.RowIndex >= actions.Count) return;
            var action = actions[args.RowIndex];
            args.Value = args.ColumnIndex switch { 0 => ActionDisplay.Name(action.Action), 1 => ActionDisplay.Target(action), _ => ActionDisplay.Details(action) };
            args.FormattingApplied = true;
        };
        grid.RowPostPaint += (_, args) => TextRenderer.DrawText(args.Graphics, (args.RowIndex + 1).ToString(), grid.Font,
            new Rectangle(args.RowBounds.Left + 2, args.RowBounds.Top, grid.RowHeadersWidth - 9, args.RowBounds.Height),
            Palette.Muted, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        grid.MoveRequested += (selection, insertion) => Guard(() => ReorderActions(selection, insertion));
        grid.SelectionChanged += (_, _) => RefreshSelectionButtons();
        grid.CellMouseDown += (_, args) =>
        {
            if (args.Button == MouseButtons.Right && args.RowIndex >= 0 && !grid.Rows[args.RowIndex].Selected) SelectRow(args.RowIndex);
        };
        grid.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0 && !Busy) Guard(EditAction); };
        grid.KeyDown += (_, args) =>
        {
            if (Busy) return;
            if (args.KeyCode == Keys.Delete) { args.SuppressKeyPress = true; Guard(DeleteSelected); }
            if (args.KeyCode == Keys.Enter) { args.SuppressKeyPress = true; Guard(EditAction); }
        };
    }

    private void RefreshActionView()
    {
        var hasActions = actions.Count > 0;
        grid.Visible = hasActions;
        emptyActions.Visible = !hasActions;
        actionCount.Text = actions.Count == 1 ? "1 Aktion" : $"{actions.Count:N0} Aktionen";
        playButton.Enabled = !Busy && hasActions;
        UiTheme.StyleButton(playButton, Palette);
        RefreshSelectionButtons();
    }

    private void RefreshLibraryView()
    {
        var count = savedMacros.Items.Count;
        libraryCount.Text = count == 1 ? "1 gespeichertes Makro" : $"{count:N0} gespeicherte Makros";
        savedMacros.Visible = count > 0;
        emptyLibrary.Visible = count == 0;
        RefreshSelectionButtons();
    }

    private void RefreshSelectionButtons()
    {
        openSavedButton.Enabled = !Busy && savedMacros.SelectedIndex >= 0;
        editActionButton.Enabled = !Busy && SelectedIndex >= 0;
        UiTheme.StyleButton(openSavedButton, Palette);
        UiTheme.StyleButton(editActionButton, Palette);
    }

    private void DrawLibraryItem(object? sender, DrawItemEventArgs args)
    {
        if (args.Index < 0 || args.Index >= savedMacros.Items.Count) return;
        var selected = (args.State & DrawItemState.Selected) != 0;
        var palette = Palette;
        using var fill = new SolidBrush(selected ? palette.AccentSurface : palette.Soft);
        args.Graphics.FillRectangle(fill, args.Bounds);
        if (selected)
        {
            using var marker = new SolidBrush(palette.AccentText);
            args.Graphics.FillRectangle(marker, args.Bounds.Left, args.Bounds.Top + 6, 3, args.Bounds.Height - 12);
        }
        var bounds = Rectangle.Inflate(args.Bounds, -11, 0);
        TextRenderer.DrawText(args.Graphics, savedMacros.Items[args.Index]?.ToString(), savedMacros.Font, bounds, selected ? palette.AccentText : palette.Ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if (savedMacros.Focused) args.DrawFocusRectangle();
    }

    private static Button MenuButton(string title, string accessibleName, params (string Title, Action Run)[] commands)
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = false, Padding = new Padding(4) };
        foreach (var command in commands) menu.Items.Add(command.Title, null, (_, _) => command.Run());
        var button = Button(title);
        button.AccessibleName = accessibleName;
        button.ContextMenuStrip = menu;
        button.Click += (_, _) => menu.Show(button, new Point(0, button.Height));
        button.Disposed += (_, _) => menu.Dispose();
        return button;
    }
}
