using System.Drawing.Imaging;

namespace UltimateMacro;

internal static partial class SelfTests
{
    private static async Task TestImageTargets()
    {
        using var template = TestButtonImage();
        var target = new ImageClickTarget
        {
            WindowTitle = "Bildziel-Testfenster", WindowClass = "test-window", ProcessName = "test-app",
            SelectedWindow = 123, TemplatePng = ImageClickTarget.Encode(template), ClickX = 27, ClickY = 12, WaitSeconds = 0
        };
        var action = new MacroAction { Action = ActionKind.ImageClick, ImageTarget = target, Key = "Left", Count = 0, DurationMs = 1, IntervalMs = 1 };
        var restored = MacroFiles.Parse(MacroFiles.Serialize(new MacroDocument { Actions = [action] })).Actions.Single().ImageTarget!;
        Check(restored.SelectedWindow == 0 && (target with { SelectedWindow = 0 }) == restored, "Image target template, click offset and options survive saving/loading without a stale window handle");
        var backgroundAction = action.Copy();
        backgroundAction.ImageTarget = target with { Background = true };
        var reloadedBackground = MacroFiles.Parse(MacroFiles.Serialize(new MacroDocument { Actions = [backgroundAction] })).Actions.Single();
        Check(reloadedBackground.ImageTarget is { Background: true } && !restored.Background, "Background mode is saved per action; older macros keep the foreground behavior");
        ExpectFailure(() => (target with { ClickX = 10000 }).Validate(), "Image click points outside the template are rejected");
        ExpectFailure(() => (target with { TemplatePng = "invalid png" }).Validate(), "Malformed image templates are rejected");

        using var decoded = restored.OpenTemplate();
        var pattern = new ImagePattern(decoded);
        var first = new Point(141, 107);
        var second = new Point(453, 286);
        using var before = TestWindowImage(template, first);
        using var after = TestWindowImage(template, second);
        using var duplicates = TestWindowImage(template, first, second);
        using var empty = TestWindowImage(template);
        var firstMatch = pattern.Find(before, 94, CancellationToken.None);
        var secondMatch = pattern.Find(after, 94, CancellationToken.None);
        Check(firstMatch.Status == ImageMatchStatus.Found && firstMatch.Bounds.Location == first && secondMatch.Status == ImageMatchStatus.Found && secondMatch.Bounds.Location == second,
            "Pixel matching locates the same button at two different positions after an opaque PNG round trip");
        Check(pattern.Find(duplicates, 94, CancellationToken.None).Status == ImageMatchStatus.Ambiguous && pattern.Find(empty, 94, CancellationToken.None).Status == ImageMatchStatus.Missing,
            "Identical visible buttons are ambiguous and a missing button is not clicked");
        using var flat = empty.Clone(new Rectangle(0, 0, 16, 16), empty.PixelFormat);
        ExpectFailure(() => _ = new ImagePattern(flat), "Flat selections are rejected as poor image targets");

        using var cancellation = new CancellationTokenSource();
        var clickCount = 0;
        var sink = new TestSink { ButtonStateChanged = down => { if (!down && ++clickCount == 2) cancellation.Cancel(); } };
        var finder = new TestImageFinder([
            new(ImageMatchStatus.Found, new Point(first.X + target.ClickX, first.Y + target.ClickY)),
            new(ImageMatchStatus.Missing), new(ImageMatchStatus.Ambiguous),
            new(ImageMatchStatus.Found, new Point(second.X + target.ClickX, second.Y + target.ClickY))]);
        var player = new MacroPlayer(sink, _ => finder);
        var waits = new List<string>();
        player.ImageTargetStatus += status => { if (status is not null) waits.Add(status); };
        await Cancelled(player.RunAsync([action], Options(), cancellation.Token));
        Check(finder.Searches == 4 && waits.Count == 2 && sink.Events.SequenceEqual([
            "Move 168 119", "Button Left True", "Button Left False", "Move 480 298", "Button Left True", "Button Left False"]) && sink.Held.Count == 0,
            "Continuous image clicks search again, wait through missing and duplicate targets, and click only the new point");

        var obscuredSink = new TestSink();
        var changed = new TestImageFinder([new(ImageMatchStatus.Found, first), new(ImageMatchStatus.Found, second)], failAfterFirstMove: true);
        var single = action.Copy();
        single.Count = 1;
        await new MacroPlayer(obscuredSink, _ => changed).RunAsync([single], Options(), CancellationToken.None);
        Check(obscuredSink.Events.SequenceEqual(["Move 141 107", "Move 453 286", "Button Left True", "Button Left False"]),
            "Changing window visibility between movement and click prevents a stale click");

        var missingSink = new TestSink();
        single.ImageTarget = target with { WaitSeconds = 1 };
        try
        {
            await new MacroPlayer(missingSink, _ => new TestImageFinder([])).RunAsync([single], Options(), CancellationToken.None);
            throw new Exception("Image target should time out");
        }
        catch (TimeoutException) { }
        Check(missingSink.Events.Count == 0, "A missing image reaches its configured timeout without moving or clicking");

        using var waitingCancellation = new CancellationTokenSource(70);
        await Cancelled(new MacroPlayer(missingSink, _ => new TestImageFinder([])).RunAsync([action], Options(), waitingCancellation.Token));
        Check(missingSink.Events.Count == 0, "Stop cancels indefinite image waiting without moving or clicking");

        using (var stopBackground = new CancellationTokenSource())
        {
            var dispatcher = new TestBackgroundClicks(stopBackground);
            var noGlobalInput = new TestSink { ThrowOnMove = true };
            var backgroundFinder = new TestImageFinder([new(ImageMatchStatus.Found, first), new(ImageMatchStatus.Missing),
                new(ImageMatchStatus.Ambiguous), new(ImageMatchStatus.Found, second)]);
            await Cancelled(new MacroPlayer(noGlobalInput, _ => backgroundFinder, dispatcher).RunAsync([backgroundAction], Options(), stopBackground.Token));
            Check(noGlobalInput.Events.Count == 0 && backgroundFinder.Searches == 4 && dispatcher.Pressed.SequenceEqual([first, second]) && dispatcher.Released == 2,
                "Continuous background clicks re-search, wait on ambiguous results, and release without sending global mouse input");
        }

        using (var canvas = new TestImageCanvas { Size = after.Size })
        {
            canvas.SetImage(after, true, Rectangle.Empty, null);
            canvas.Gesture(second, new Point(second.X + template.Width - 1, second.Y + template.Height - 1));
            var click = new Point(second.X + target.ClickX, second.Y + target.ClickY);
            canvas.Gesture(click, click);
            Check(canvas.SelectedBounds == new Rectangle(second, template.Size) && canvas.ClickPoint == click, "Dragging a target and choosing a point map to the original window pixels");
        }
        using (var dialog = new ImageTargetDialog(backgroundAction.ImageTarget))
        {
            dialog.PresentSnapshot(backgroundAction.ImageTarget!, after, true, secondMatch);
            dialog.Show();
            await Task.Delay(60);
            Render(dialog, "image-target-preview.png");
            UiTheme.Apply(dialog, UiTheme.Dark);
            Render(dialog, "image-target-dark-preview.png");
            var canvas = FindControls(dialog).OfType<ImageTargetCanvas>().Single();
            Check(canvas.ClickPoint == new Point(480, 298) && FindControls(dialog).OfType<Label>().Any(label => label.Text.Contains("Es wurde nicht geklickt", StringComparison.Ordinal)),
                "The preview marks the relocated click point on a still image");
            FindControls(dialog).OfType<Button>().Single(button => button.Text == "Bildziel übernehmen").PerformClick();
            Check(dialog.DialogResult == DialogResult.OK && dialog.Result is { Background: true } && dialog.Result.ClickX == target.ClickX &&
                FindControls(dialog).OfType<Button>().Any(button => button.Text == "Hintergrund-Vorschau (ohne Klick)"), "Image target dialog retains the background option and relative click point after preview");
            dialog.Close();
        }
        using var editor = new ActionEditor(action);
        editor.Show();
        await Task.Delay(60);
        Check(FindControls(editor).OfType<ComboBox>().Any(combo => combo.Text == "Bildziel klicken"), "The action editor shows Bildziel klicken by name");
        FindControls(editor).OfType<Button>().Single(button => button.Name == "SaveAction").PerformClick();
        Check(editor.DialogResult == DialogResult.OK && editor.Result.ImageTarget == target && editor.Result.Count == 0, "The action editor saves a continuous image click with its target");
        editor.Close();
    }

    private static async Task TestBackgroundWindow(Form form)
    {
        using var template = TestButtonImage();
        var button = new Point(118, 63);
        using var image = TestWindowImage(template, button);
        using var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        using var receiver = new PictureBox { Location = new Point(13, 17), Size = image.Size, Image = image, SizeMode = PictureBoxSizeMode.Normal };
        panel.Controls.Add(receiver);
        form.Controls.Add(panel);
        panel.BringToFront();
        var messages = new List<(bool Down, Point Point)>();
        receiver.MouseDown += (_, args) => messages.Add((true, args.Location));
        receiver.MouseUp += (_, args) => messages.Add((false, args.Location));
        using var cover = new Form { Bounds = form.Bounds, StartPosition = FormStartPosition.Manual, FormBorderStyle = FormBorderStyle.None,
            BackColor = Color.DarkSlateBlue, ShowInTaskbar = false, TopMost = true, Text = "Ultimate Macro foreground test cover" };
        try
        {
            form.TopMost = false;
            cover.Show();
            await FocusVerificationWindow(cover);
            Check(WindowImageCapture.TryClientArea(form.Handle, out var area), "Background test target has an open client area");
            var receiverClick = new Point(button.X + 23, button.Y + 10);
            var screenPoint = receiver.PointToScreen(receiverClick);
            var underPointer = Native.WindowFromPoint(new Native.Point { X = screenPoint.X, Y = screenPoint.Y });
            Check(Native.GetForegroundWindow() == cover.Handle && Native.GetAncestor(underPointer, 2) == cover.Handle, "A separate foreground window fully covers the background click point");
            var target = new ImageClickTarget { TemplatePng = ImageClickTarget.Encode(template), ClickX = 23, ClickY = 10, Background = true };
            using var capture = new BackgroundImageCapture(target);
            using var preview = await Task.Run(() => capture.Search(form.Handle, area.Size, preview: true, CancellationToken.None));
            Check(preview.Preview is not null && preview.Match.Status == ImageMatchStatus.Found && preview.Match.Bounds.Location == new Point(button.X + receiver.Left, button.Y + receiver.Top) &&
                Native.GetForegroundWindow() == cover.Handle && messages.Count == 0, "The real background preview finds the covered target without activating it or posting clicks");
            receiver.Location = new Point(57, 35);
            using var fresh = await Task.Run(() => capture.Search(form.Handle, area.Size, preview: false, CancellationToken.None));
            Check(fresh.Match.Status == ImageMatchStatus.Found && fresh.Match.Bounds.Location == new Point(button.X + receiver.Left, button.Y + receiver.Top) &&
                fresh.Preview is null, "The capture worker follows the moved button in the covered window without transferring a screenshot to playback");
            var click = new Point(fresh.Match.Bounds.X + target.ClickX, fresh.Match.Bounds.Y + target.ClickY);
            var cursor = Cursor.Position;
            using (var pressed = new BackgroundImageClicks().Press(new(ImageMatchStatus.Found, click, form.Handle, area), "Left"))
                Check(pressed is not null, "Background button messages were accepted for the selected child control");
            Check(Cursor.Position == cursor, "Posting background mouse messages does not move the system cursor");
            await Task.Delay(100);
            Check(messages.SequenceEqual([(true, receiverClick), (false, receiverClick)]) && Native.GetForegroundWindow() == cover.Handle,
                "The covered child receives the correct local press and release while the other window keeps focus");
            form.WindowState = FormWindowState.Minimized;
            Check(!WindowImageCapture.TryClientArea(form.Handle, out _), "A minimized target cannot receive new background clicks");
        }
        finally { cover.Close(); form.WindowState = FormWindowState.Normal; form.Controls.Remove(panel); }
    }

    private static async Task TestWindowCapture(Form form)
    {
        using var template = TestButtonImage();
        var point = new Point(118, 63);
        using var image = TestWindowImage(template, point);
        using var surface = new PictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Normal };
        form.Controls.Add(surface);
        surface.BringToFront();
        try
        {
            await FocusVerificationWindow(form);
            using var frame = WindowImageCapture.Capture(form.Handle);
            Check(frame is not null, "Windows captures the visible client area of the foreground test window");
            var match = new ImagePattern(template).Find(frame!.Bitmap, 94, CancellationToken.None);
            var screenPoint = surface.PointToScreen(new Point(point.X + 23, point.Y + 10));
            Check(match.Status == ImageMatchStatus.Found && new Point(frame.ScreenBounds.X + match.Bounds.X + 23, frame.ScreenBounds.Y + match.Bounds.Y + 10) == screenPoint &&
                WindowImageCapture.RegionIsVisible(frame, match.Bounds, screenPoint), "The real window capture finds the button and maps it to its visible screen coordinates without sending input");
            Check(WindowImageCapture.DescribeForeground() is null, "Ultimate Macro's own windows cannot be chosen as image targets");
        }
        finally { form.Controls.Remove(surface); }
    }

    private static Bitmap TestButtonImage()
    {
        var bitmap = new Bitmap(86, 34, PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(28, 91, 194));
        graphics.DrawRectangle(Pens.White, 1, 1, 83, 31);
        using var font = new Font("Segoe UI", 10, FontStyle.Bold);
        TextRenderer.DrawText(graphics, "WEITER", font, new Rectangle(10, 0, 75, 34), Color.White, TextFormatFlags.VerticalCenter);
        graphics.FillPolygon(Brushes.LightGreen, [new Point(8, 10), new Point(8, 24), new Point(17, 17)]);
        return bitmap;
    }

    private static Bitmap TestWindowImage(Bitmap template, params Point[] positions)
    {
        var bitmap = new Bitmap(640, 390, PixelFormat.Format32bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.FromArgb(237, 241, 247));
        graphics.FillRectangle(Brushes.White, 18, 44, 604, 320);
        using var font = new Font("Segoe UI", 13, FontStyle.Bold);
        TextRenderer.DrawText(graphics, "Beispielfenster", font, new Point(21, 13), Color.FromArgb(20, 45, 80));
        foreach (var position in positions) graphics.DrawImageUnscaled(template, position);
        return bitmap;
    }

    private sealed class TestImageFinder(IEnumerable<ImageClickSearch> results, bool failAfterFirstMove = false) : IImageClickFinder
    {
        private readonly Queue<ImageClickSearch> pending = new(results);
        private int checks;
        public int Searches { get; private set; }
        public ImageClickSearch Find(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Searches++;
            return pending.TryDequeue(out var next) ? next : new(ImageMatchStatus.Missing);
        }
        public bool CanClick(ImageClickSearch hit) => !failAfterFirstMove || ++checks != 2;
    }

    private sealed class TestImageCanvas : ImageTargetCanvas
    {
        internal void Gesture(Point start, Point end)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, start.X, start.Y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, end.X, end.Y, 0));
        }
    }

    private sealed class TestBackgroundClicks(CancellationTokenSource stop) : IBackgroundImageClicks
    {
        internal List<Point> Pressed { get; } = [];
        internal int Released { get; private set; }

        public IDisposable Press(ImageClickSearch hit, string button)
        {
            Pressed.Add(hit.Point);
            return new Release(() => { if (++Released == 2) stop.Cancel(); });
        }

        private sealed class Release(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}
