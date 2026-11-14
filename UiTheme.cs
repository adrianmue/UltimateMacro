using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UltimateMacro;

internal enum UiRole { Standard, Canvas, Surface, Soft, Muted, Callout, Primary, Stop, Navigation, ActiveNavigation }
internal enum AppearanceMode { Windows, Light, Dark }

internal sealed record UiPalette(bool IsDark, Color Canvas, Color Surface, Color Soft, Color Input, Color Ink, Color Muted,
    Color Accent, Color AccentSurface, Color AccentText, Color Line, Color Danger, Color DangerHover);

internal static class UiTheme
{
    internal static readonly UiPalette Light = new(false, Color.FromArgb(242, 245, 250), Color.White, Color.FromArgb(247, 249, 252), Color.White,
        Color.FromArgb(23, 36, 58), Color.FromArgb(80, 97, 122), Color.FromArgb(35, 76, 181), Color.FromArgb(232, 239, 255), Color.FromArgb(35, 76, 181),
        Color.FromArgb(219, 227, 239), Color.FromArgb(167, 39, 52), Color.FromArgb(139, 29, 42));

    internal static readonly UiPalette Dark = new(true, Color.FromArgb(18, 24, 35), Color.FromArgb(25, 33, 46), Color.FromArgb(32, 42, 58), Color.FromArgb(21, 29, 42),
        Color.FromArgb(237, 243, 253), Color.FromArgb(174, 189, 211), Color.FromArgb(55, 91, 200), Color.FromArgb(41, 60, 96), Color.FromArgb(175, 200, 255),
        Color.FromArgb(57, 72, 95), Color.FromArgb(164, 49, 61), Color.FromArgb(185, 58, 71));

    private static readonly ConditionalWeakTable<Control, RoleState> Roles = new();
    private static readonly ConditionalWeakTable<Form, WindowState> Windows = new();

    private sealed class RoleState { internal UiRole Role; }
    private sealed class WindowState { internal UiPalette Palette = Light; internal bool Applying; }

    internal static T Mark<T>(T control, UiRole role) where T : Control
    {
        Roles.GetValue(control, static _ => new RoleState()).Role = role;
        return control;
    }

    internal static UiPalette Current(Form form) => Windows.TryGetValue(form, out var state) ? state.Palette : Light;
    internal static bool IsApplying(Form form) => Windows.TryGetValue(form, out var state) && state.Applying;

    internal static bool WindowsAppsUseDarkMode()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }

    internal static void Apply(Form form, UiPalette palette)
    {
        var state = Windows.GetValue(form, window =>
        {
            var theme = new WindowState();
            window.HandleCreated += (_, _) => StyleTitleBar(window, theme.Palette);
            return theme;
        });
        state.Palette = palette;
        state.Applying = true;
        try
        {
            ApplyTree(form, palette, []);
            if (form.IsHandleCreated) StyleTitleBar(form, palette);
            form.Invalidate(true);
        }
        finally { state.Applying = false; }
    }

    internal static void StyleButton(Button button, UiPalette palette)
    {
        var role = Role(button);
        var filled = button.Enabled && role is UiRole.Primary or UiRole.Stop;
        var active = role == UiRole.ActiveNavigation;
        button.BackColor = filled ? role == UiRole.Stop ? palette.Danger : palette.Accent
            : active ? palette.AccentSurface : button.Enabled ? palette.Surface : palette.Soft;
        button.ForeColor = filled ? Color.White : active ? palette.AccentText : role == UiRole.Navigation || !button.Enabled ? palette.Muted : palette.Ink;
        button.FlatAppearance.BorderColor = palette.Line;
        button.FlatAppearance.BorderSize = filled || role == UiRole.Navigation ? 0 : 1;
        button.FlatAppearance.MouseOverBackColor = filled ? role == UiRole.Stop ? palette.DangerHover : ControlPaint.Dark(palette.Accent, 0.12f) : palette.AccentSurface;
        button.FlatAppearance.MouseDownBackColor = filled ? ControlPaint.Dark(button.BackColor, 0.18f) : palette.AccentSurface;
    }

    private static UiRole Role(Control control) => Roles.TryGetValue(control, out var state) ? state.Role : UiRole.Standard;

    private static void ApplyTree(Control control, UiPalette palette, HashSet<ContextMenuStrip> menus)
    {
        var role = Role(control);
        if (control is Button button) StyleButton(button, palette);
        else
        {
            control.ForeColor = role is UiRole.Muted ? palette.Muted : role is UiRole.Callout ? palette.AccentText : palette.Ink;
            if (role == UiRole.Canvas) control.BackColor = palette.Canvas;
            else if (role == UiRole.Soft) control.BackColor = palette.Soft;
            else if (role == UiRole.Callout) control.BackColor = palette.AccentSurface;
            else if (role == UiRole.Surface || control is Form) control.BackColor = palette.Surface;
            else if (control is TextBoxBase or ComboBox or NumericUpDown) control.BackColor = palette.Input;
        }
        if (control is DataGridView grid) StyleGrid(grid, palette);
        if (control.ContextMenuStrip is { } menu && menus.Add(menu))
        {
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors(palette));
            menu.BackColor = palette.Surface;
            menu.ForeColor = palette.Ink;
            foreach (ToolStripItem item in menu.Items) item.ForeColor = palette.Ink;
        }
        foreach (Control child in control.Controls) ApplyTree(child, palette, menus);
    }

    private static void StyleGrid(DataGridView grid, UiPalette palette)
    {
        grid.BackgroundColor = palette.Soft;
        grid.GridColor = palette.Line;
        grid.DefaultCellStyle.BackColor = palette.Surface;
        grid.DefaultCellStyle.ForeColor = palette.Ink;
        grid.DefaultCellStyle.SelectionBackColor = palette.AccentSurface;
        grid.DefaultCellStyle.SelectionForeColor = palette.Ink;
        grid.ColumnHeadersDefaultCellStyle.BackColor = grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Soft;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Muted;
        grid.RowHeadersDefaultCellStyle.BackColor = palette.Surface;
        grid.RowHeadersDefaultCellStyle.ForeColor = palette.Muted;
        grid.RowHeadersDefaultCellStyle.SelectionBackColor = palette.AccentSurface;
        grid.RowHeadersDefaultCellStyle.SelectionForeColor = palette.Muted;
    }

    private static void StyleTitleBar(Form form, UiPalette palette)
    {
        const int darkFrame = 20;
        const int captionColor = 35;
        const int captionText = 36;
        var dark = palette.IsDark ? 1 : 0;
        DwmSetWindowAttribute(form.Handle, darkFrame, ref dark, sizeof(int));
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var caption = palette.IsDark ? ColorTranslator.ToWin32(palette.Surface) : -1;
        var text = palette.IsDark ? ColorTranslator.ToWin32(palette.Ink) : -1;
        DwmSetWindowAttribute(form.Handle, captionColor, ref caption, sizeof(int));
        DwmSetWindowAttribute(form.Handle, captionText, ref text, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    private sealed class MenuColors(UiPalette palette) : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => palette.Surface;
        public override Color MenuBorder => palette.Line;
        public override Color MenuItemBorder => palette.Line;
        public override Color MenuItemSelected => palette.AccentSurface;
        public override Color MenuItemSelectedGradientBegin => palette.AccentSurface;
        public override Color MenuItemSelectedGradientEnd => palette.AccentSurface;
        public override Color SeparatorDark => palette.Line;
        public override Color SeparatorLight => palette.Surface;
    }
}

internal sealed class PaletteComboBox : ComboBox
{
    public PaletteComboBox()
    {
        // Changing DrawMode on a live ComboBox recreates its native window.
        FlatStyle = FlatStyle.Flat;
        DrawMode = DrawMode.OwnerDrawFixed;
    }

    protected override void OnDrawItem(DrawItemEventArgs args)
    {
        var palette = FindForm() is { } form ? UiTheme.Current(form) : UiTheme.Light;
        var selected = (args.State & DrawItemState.Selected) != 0 && (args.State & DrawItemState.ComboBoxEdit) == 0;
        using var fill = new SolidBrush(selected ? palette.AccentSurface : palette.Input);
        args.Graphics.FillRectangle(fill, args.Bounds);
        var text = args.Index >= 0 && args.Index < Items.Count ? GetItemText(Items[args.Index]) : Text;
        var bounds = Rectangle.Inflate(args.Bounds, -3, 0);
        TextRenderer.DrawText(args.Graphics, text, Font, bounds, !Enabled ? palette.Muted : selected ? palette.AccentText : palette.Ink,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        args.DrawFocusRectangle();
    }
}

internal sealed class PaletteButton : Button
{
    protected override void OnPaint(PaintEventArgs args)
    {
        if (Enabled || FindForm() is not { } form || !UiTheme.Current(form).IsDark)
        {
            base.OnPaint(args);
            return;
        }

        var palette = UiTheme.Current(form);
        args.Graphics.Clear(palette.Soft);
        using var border = new Pen(palette.Line);
        args.Graphics.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        TextRenderer.DrawText(args.Graphics, Text, Font, ClientRectangle, palette.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
