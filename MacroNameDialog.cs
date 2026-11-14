namespace UltimateMacro;

internal sealed class MacroNameDialog : Form
{
    private readonly TextBox name = new() { Dock = DockStyle.Fill, MaxLength = 120 };
    public string MacroName => name.Text.Trim();

    public MacroNameDialog(MacroLibrary library, string initialName, string title)
        : this(initialName, title, "Speichern", library.DirectoryPath, name => library.GetPath(name)) { }

    public MacroNameDialog(string initialName, string title, string buttonText, string directory, Action<string> submit)
    {
        Text = title;
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(540, 230);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = MaximizeBox = false;
        BackColor = Color.White;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(new Label { Text = "Makroname", AutoSize = true }, 0, 0);
        name.Text = initialName;
        layout.Controls.Add(name, 0, 1);
        layout.Controls.Add(UiTheme.Mark(new Label { Text = "Speicherort:\r\n" + directory, Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false }, UiRole.Muted), 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = MainForm.Button(buttonText, primary: true);
        var cancel = MainForm.Button("Abbrechen");
        cancel.DialogResult = DialogResult.Cancel;
        save.Click += (_, _) =>
        {
            try { submit(MacroName); DialogResult = DialogResult.OK; }
            catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, error.Message, "Makroname", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                name.Focus();
                name.SelectAll();
            }
        };
        buttons.Controls.AddRange([save, cancel]);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
        Shown += (_, _) => { name.Focus(); name.SelectAll(); };
    }
}
