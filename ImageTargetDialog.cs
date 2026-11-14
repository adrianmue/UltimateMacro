using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace UltimateMacro;

internal sealed class ImageTargetDialog : Form
{
    private readonly ImageTargetCanvas canvas = new() { Dock = DockStyle.Fill };
    private readonly Label windowLabel = new() { Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false };
    private readonly Label instructions = new() { Dock = DockStyle.Fill, AutoEllipsis = true, UseMnemonic = false };
    private readonly Button capture = new() { Text = "Zielfenster in 3 s erfassen", AutoSize = true, Height = 32 };
    private readonly Button preview = new() { Text = "Vorschau in 3 s (ohne Klick)", AutoSize = true, Height = 32 };
    private readonly CheckBox background = new() { Text = "Im Hintergrund klicken (Maus bleibt frei)", AutoSize = true, Margin = new Padding(10, 9, 3, 0) };
    private readonly Button use = new() { Text = "Bildziel übernehmen", AutoSize = true, Height = 32 };
    private readonly NumericUpDown similarity = new() { Minimum = 85, Maximum = 100, Width = 58 };
    private readonly NumericUpDown timeout = new() { Minimum = 0, Maximum = 600, Width = 58 };
    private readonly CancellationTokenSource lifetime = new();
    private ImageClickTarget? target;
    private Bitmap? displayed;
    private bool selecting;
    private bool working;
    private bool released;
    public ImageClickTarget? Result { get; private set; }

    internal ImageTargetDialog(ImageClickTarget? initial)
    {
        target = initial;
        Text = "Bildziel klicken — Ziel einrichten";
        Font = new Font("Segoe UI", 10);
        Size = new Size(1010, 740);
        MinimumSize = new Size(800, 575);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        BackColor = Color.White;
        MainForm.ConfigureButton(capture, capture.Text);
        MainForm.ConfigureButton(preview, preview.Text);
        MainForm.ConfigureButton(use, use.Text, primary: true);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        Controls.Add(layout);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        toolbar.Controls.AddRange([capture, preview, background]);
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(windowLabel, 0, 1);
        layout.Controls.Add(instructions, 0, 2);
        layout.Controls.Add(canvas, 0, 3);
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        options.Controls.AddRange([FieldLabel("Ähnlichkeit %"), similarity, FieldLabel("Wartezeit in s"), timeout, FieldLabel("0 = unbegrenzt")]);
        bottom.Controls.Add(options, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var cancel = MainForm.Button("Abbrechen");
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.AddRange([use, cancel]);
        bottom.Controls.Add(buttons, 1, 0);
        layout.Controls.Add(bottom, 0, 4);
        CancelButton = cancel;
        similarity.Value = initial?.MinimumSimilarity ?? 94;
        timeout.Value = initial?.WaitSeconds ?? 0;
        background.Checked = initial?.Background ?? false;
        background.CheckedChanged += (_, _) => UpdateButtons();
        capture.Click += async (_, _) => await Snapshot(false);
        preview.Click += async (_, _) => await Snapshot(true);
        use.Click += (_, _) =>
        {
            try { Result = ReadTarget(); DialogResult = DialogResult.OK; }
            catch (InvalidDataException error) { instructions.Text = error.Message; }
        };
        canvas.SelectionChanged += (_, _) =>
        {
            instructions.Text = canvas.ClickPoint.HasValue ? "Klickpunkt gesetzt. Übernimm das Bildziel oder teste es mit Vorschau." :
                "Klicke im markierten Ausschnitt auf den gewünschten Klickpunkt. Ziehen zeichnet einen neuen Rahmen.";
            UpdateButtons();
        };
        UpdateWindowLabel();
        if (initial is null) instructions.Text = "Erfasse zuerst das Zielfenster. Wechsle während des Countdowns dorthin und lass es im Vordergrund.";
        else
        {
            instructions.Text = "Gespeicherte Vorlage mit Klickpunkt. Die Vorschau sucht sie im aktuellen Fensterbild; sie bewegt die Maus nicht und klickt nicht.";
            try
            {
                var bitmap = initial.OpenTemplate();
                ShowImage(bitmap, false, new Rectangle(Point.Empty, bitmap.Size), new Point(initial.ClickX, initial.ClickY));
            }
            catch (InvalidDataException error) { instructions.Text = error.Message; }
        }
        UpdateButtons();
    }

    private async Task Snapshot(bool isPreview)
    {
        if (working) return;
        var backgroundPreview = false;
        try
        {
            var prepared = isPreview ? ReadTarget() : null;
            working = true;
            UpdateButtons();
            var token = lifetime.Token;
            if (prepared is { Background: true })
            {
                backgroundPreview = true;
                instructions.Text = "Hintergrund-Vorschau wird erstellt. Du musst nicht zum Zielfenster wechseln.";
                using var result = await Task.Run(() => ReadBackgroundPreview(prepared, token), token);
                token.ThrowIfCancellationRequested();
                if (result.Preview is null) instructions.Text = $"Hintergrund-Vorschau: {WindowImageClickFinder.Describe(result.Match.Status)}. Es wurde nicht geklickt.";
                else PresentSnapshot(prepared, result.Preview, true, result.Match);
                return;
            }
            for (var seconds = 3; seconds > 0; seconds--)
            {
                instructions.Text = $"Jetzt zum {(isPreview ? "gespeicherten" : "gewünschten")} Zielfenster wechseln (z. B. Alt+Tab). Aufnahme in {seconds} s …";
                await Task.Delay(1000, token);
            }
            var snapshot = await Task.Run(() => ReadSnapshot(prepared, token), token);
            using var frame = snapshot.Frame;
            token.ThrowIfCancellationRequested();
            Activate();
            if (frame is null || snapshot.Target is null)
            {
                instructions.Text = isPreview ? "Zielfenster nicht eindeutig oder nicht sichtbar. Wechsle während des Countdowns zum gespeicherten Fenster." :
                    "Kein geeignetes Fenster erfasst. Wechsle während des Countdowns zu einer anderen sichtbaren App.";
                return;
            }
            PresentSnapshot(snapshot.Target, frame.Bitmap, isPreview, snapshot.Match);
        }
        catch (OperationCanceledException) { }
        catch (Exception error)
        {
            if (!IsDisposed) { if (!backgroundPreview) Activate(); instructions.Text = error.Message; }
        }
        finally { working = false; if (!IsDisposed) UpdateButtons(); }
    }

    internal void PresentSnapshot(ImageClickTarget selected, Bitmap bitmap, bool isPreview, ImageMatch match)
    {
        target = selected;
        selecting = !isPreview;
        UpdateWindowLabel();
        var hit = isPreview && match.Status == ImageMatchStatus.Found;
        ShowImage((Bitmap)bitmap.Clone(), selecting, hit ? match.Bounds : default,
            hit ? new Point(match.Bounds.X + selected.ClickX, match.Bounds.Y + selected.ClickY) : null);
        var mode = selected.Background ? "Hintergrund-Vorschau" : "Vorschau";
        instructions.Text = !isPreview ? "Ziehe einen engen Rahmen um den Button. Wähle danach per Klick den Punkt innerhalb des Rahmens." : hit
            ? $"{mode}: Treffer mit {match.Similarity:F1} % Ähnlichkeit. Das Fadenkreuz zeigt den Klickpunkt. Es wurde nicht geklickt."
            : $"{mode}: {WindowImageClickFinder.Describe(match.Status)}. Es wurde nicht geklickt.";
        UpdateButtons();
    }

    private static BackgroundImageResult ReadBackgroundPreview(ImageClickTarget prepared, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var window = WindowImageCapture.ResolveBackground(prepared);
        if (!WindowImageCapture.TryClientArea(window, out var area)) return new(new(ImageMatchStatus.BackgroundUnavailable));
        using var capture = new BackgroundImageCapture(prepared);
        return capture.Search(window, area.Size, preview: true, token);
    }

    private static (ImageClickTarget? Target, WindowImageFrame? Frame, ImageMatch Match) ReadSnapshot(ImageClickTarget? prepared, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var selected = prepared ?? WindowImageCapture.DescribeForeground();
        if (selected is null) return default;
        var window = WindowImageCapture.ResolveForeground(selected);
        var frame = WindowImageCapture.Capture(window);
        if (frame is null) return default;
        try { return (selected, frame, prepared is null ? default : new WindowImageClickFinder(prepared).Match(frame, token)); }
        catch { frame.Dispose(); throw; }
    }

    private ImageClickTarget ReadTarget()
    {
        if (target is null) throw new InvalidDataException("Erfasse zuerst ein Zielfenster.");
        var configured = target with { MinimumSimilarity = (int)similarity.Value, WaitSeconds = (int)timeout.Value, Background = background.Checked };
        if (selecting)
        {
            var region = canvas.SelectedBounds;
            if (region.Width is < ImageClickTarget.MinimumSide or > ImageClickTarget.MaximumWidth ||
                region.Height is < ImageClickTarget.MinimumSide or > ImageClickTarget.MaximumHeight)
                throw new InvalidDataException("Markiere einen Ausschnitt von mindestens 12 × 12 und höchstens 480 × 320 Bildpunkten.");
            if (displayed is null || canvas.ClickPoint is not Point click)
                throw new InvalidDataException("Wähle den Klickpunkt innerhalb des markierten Bereichs.");
            using var crop = displayed.Clone(region, displayed.PixelFormat);
            _ = new ImagePattern(crop);
            configured = configured with { TemplatePng = ImageClickTarget.Encode(crop), ClickX = click.X - region.X, ClickY = click.Y - region.Y };
        }
        configured.Validate();
        return configured;
    }

    private void ShowImage(Bitmap bitmap, bool editable, Rectangle region, Point? click)
    {
        var previous = displayed;
        displayed = bitmap;
        canvas.SetImage(bitmap, editable, region, click);
        previous?.Dispose();
    }

    private void UpdateWindowLabel() => windowLabel.Text = target is null ? "Zielfenster: noch nicht gewählt" : $"Zielfenster: {target.WindowTitle}  ({target.ProcessName})";

    private void UpdateButtons()
    {
        capture.Enabled = similarity.Enabled = timeout.Enabled = background.Enabled = !working;
        preview.Text = background.Checked ? "Hintergrund-Vorschau (ohne Klick)" : "Vorschau in 3 s (ohne Klick)";
        var ready = selecting ? canvas.ClickPoint.HasValue : !string.IsNullOrEmpty(target?.TemplatePng);
        preview.Enabled = use.Enabled = !working && ready;
        canvas.Enabled = !working;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !released)
        {
            released = true;
            lifetime.Cancel();
            lifetime.Dispose();
            displayed?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static Label FieldLabel(string label) => new() { Text = label, AutoSize = true, Margin = new Padding(4, 7, 3, 0) };
}

internal class ImageTargetCanvas : Control
{
    private Bitmap? image;
    private bool editable;
    private Point? dragStart;
    private Point dragStartOnControl;
    private Point dragCurrent;
    internal Rectangle SelectedBounds { get; private set; }
    internal Point? ClickPoint { get; private set; }
    internal event EventHandler? SelectionChanged;

    internal ImageTargetCanvas()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(30, 40, 55);
    }

    internal void SetImage(Bitmap bitmap, bool allowSelection, Rectangle region, Point? click)
    {
        image = bitmap;
        editable = allowSelection;
        SelectedBounds = region;
        ClickPoint = click;
        dragStart = null;
        Cursor = editable ? Cursors.Cross : Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs args)
    {
        base.OnPaint(args);
        if (image is null)
        {
            TextRenderer.DrawText(args.Graphics, "Hier erscheint das Bild des ausgewählten Fensters.", Font, ClientRectangle, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        var display = ImageBounds();
        args.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        args.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        args.Graphics.DrawImage(image, display);
        var region = dragStart is Point start ? Between(start, dragCurrent) : SelectedBounds;
        if (!region.IsEmpty)
        {
            var outline = new RectangleF(display.X + region.X * display.Width / image.Width, display.Y + region.Y * display.Height / image.Height,
                region.Width * display.Width / image.Width, region.Height * display.Height / image.Height);
            using var pen = new Pen(editable ? Color.DeepSkyBlue : Color.LimeGreen, 2);
            args.Graphics.DrawRectangle(pen, outline.X, outline.Y, outline.Width, outline.Height);
        }
        if (dragStart is null && ClickPoint is Point click)
        {
            var centerX = display.X + (click.X + 0.5f) * display.Width / image.Width;
            var centerY = display.Y + (click.Y + 0.5f) * display.Height / image.Height;
            using var outline = new Pen(Color.Black, 4);
            using var crosshair = new Pen(Color.Yellow, 2);
            foreach (var pen in new[] { outline, crosshair })
            {
                args.Graphics.DrawLine(pen, centerX - 9, centerY, centerX + 9, centerY);
                args.Graphics.DrawLine(pen, centerX, centerY - 9, centerX, centerY + 9);
            }
        }
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (!editable || args.Button != MouseButtons.Left || !ImageBounds().Contains(args.Location)) return;
        dragStart = dragCurrent = ToImage(args.Location);
        dragStartOnControl = args.Location;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        if (dragStart is null) return;
        dragCurrent = ToImage(args.Location);
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        if (args.Button != MouseButtons.Left || dragStart is not Point start) return;
        var end = ToImage(args.Location);
        dragStart = null;
        Capture = false;
        if (Math.Abs(args.X - dragStartOnControl.X) > 3 || Math.Abs(args.Y - dragStartOnControl.Y) > 3)
        {
            SelectedBounds = Between(start, end);
            ClickPoint = null;
        }
        else if (SelectedBounds.Contains(end) && ImageBounds().Contains(args.Location)) ClickPoint = end;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Invalidate();
    }

    private RectangleF ImageBounds()
    {
        if (image is null || Width < 1 || Height < 1) return default;
        var scale = Math.Min(3f, Math.Min((float)Width / image.Width, (float)Height / image.Height));
        var width = image.Width * scale;
        var height = image.Height * scale;
        return new((Width - width) / 2, (Height - height) / 2, width, height);
    }

    private Point ToImage(Point point)
    {
        var display = ImageBounds();
        return new(Math.Clamp((int)((point.X - display.X) * image!.Width / display.Width), 0, image.Width - 1),
            Math.Clamp((int)((point.Y - display.Y) * image.Height / display.Height), 0, image.Height - 1));
    }

    private static Rectangle Between(Point start, Point end) => Rectangle.FromLTRB(Math.Min(start.X, end.X), Math.Min(start.Y, end.Y),
        Math.Max(start.X, end.X) + 1, Math.Max(start.Y, end.Y) + 1);
}
