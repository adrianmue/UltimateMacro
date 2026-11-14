namespace UltimateMacro;

internal sealed class MacroDeleteDialog : Form
{
    internal MacroDeleteDialog(MacroLibraryEntry entry, bool keepDraft)
    {
        Text = "Makro endgültig löschen";
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(520, keepDraft ? 264 : 232);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = MaximizeBox = false;
        ShowInTaskbar = false;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(21, 15, 21, 12) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        Controls.Add(layout);

        layout.Controls.Add(new Label { Text = "Makro endgültig löschen?", AutoSize = true, Font = new Font("Segoe UI", 14, FontStyle.Bold) }, 0, 0);
        layout.Controls.Add(new Label
        {
            Text = $"„{entry.Name}“ wird von der Festplatte gelöscht und nicht in den Papierkorb verschoben." +
                (keepDraft ? "\n\nDeine ungespeicherten Änderungen bleiben als Entwurf geöffnet." : ""),
            Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false
        }, 0, 1);
        layout.Controls.Add(UiTheme.Mark(new Label { Text = entry.FilePath, Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false }, UiRole.Muted), 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty };
        var delete = UiTheme.Mark(MainForm.Button("Endgültig löschen"), UiRole.Stop);
        delete.Name = "ConfirmDeleteMacro";
        delete.DialogResult = DialogResult.OK;
        var cancel = MainForm.Button("Abbrechen");
        cancel.Name = "CancelDeleteMacro";
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([delete, cancel]);
        layout.Controls.Add(buttons, 0, 3);
        AcceptButton = CancelButton = cancel;
        Shown += (_, _) => cancel.Focus();
    }
}
