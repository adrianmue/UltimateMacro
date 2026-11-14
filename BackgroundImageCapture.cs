using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;

namespace UltimateMacro;

internal sealed record BackgroundImageResult(ImageMatch Match, Bitmap? Preview = null) : IDisposable
{
    public void Dispose() => Preview?.Dispose();
}

internal sealed class BackgroundImageCapture(ImageClickTarget target) : IDisposable
{
    private const int MaximumPixels = 12_000_000;
    private const int MaximumPreviewBytes = 52_000_000;
    private Process? worker;
    private BinaryWriter? requests;
    private BinaryReader? responses;

    internal BackgroundImageResult Search(nint window, Size size, bool preview, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ValidSize(size)) return new(new(ImageMatchStatus.CaptureUnavailable));
        try
        {
            var initialize = worker is null || worker.HasExited;
            if (initialize) StartWorker();
            var writer = requests!;
            var reader = responses!;
            var operation = Task.Run(() => Exchange(writer, reader, initialize, window, size, preview));
            try { return operation.WaitAsync(preview ? TimeSpan.FromSeconds(5) : TimeSpan.FromSeconds(2), token).GetAwaiter().GetResult(); }
            catch
            {
                Dispose();
                _ = operation.ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully) task.Result.Dispose();
                    else _ = task.Exception;
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                throw;
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or TimeoutException or Win32Exception or InvalidOperationException or ArgumentException)
        {
            token.ThrowIfCancellationRequested();
            return new(new(ImageMatchStatus.CaptureUnavailable));
        }
    }

    private BackgroundImageResult Exchange(BinaryWriter writer, BinaryReader reader, bool initialize, nint window, Size size, bool preview)
    {
        if (initialize)
        {
            writer.Write(target.TemplatePng);
            writer.Write(target.ClickX);
            writer.Write(target.ClickY);
            writer.Write(target.MinimumSimilarity);
        }
        writer.Write((long)window);
        writer.Write(size.Width);
        writer.Write(size.Height);
        writer.Write(preview);
        writer.Flush();
        var status = (ImageMatchStatus)reader.ReadByte();
        var match = new ImageMatch(status, new Rectangle(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()), reader.ReadDouble());
        var length = reader.ReadInt32();
        if (length == 0) return new(match);
        if (!preview || length is < 0 or > MaximumPreviewBytes) throw new InvalidDataException("Ungültige Hintergrundvorschau.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        using var stream = new MemoryStream(bytes, writable: false);
        using var decoded = Image.FromStream(stream);
        return new(match, new Bitmap(decoded));
    }

    private void StartWorker()
    {
        Dispose();
        var path = Environment.ProcessPath ?? throw new InvalidOperationException("Der Aufnahmeprozess konnte nicht gestartet werden.");
        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardInput = true, RedirectStandardOutput = true
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(path), "dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "UltimateMacro.dll"));
        start.ArgumentList.Add("--image-capture-worker");
        worker = Process.Start(start) ?? throw new InvalidOperationException("Der Aufnahmeprozess konnte nicht gestartet werden.");
        requests = new BinaryWriter(worker.StandardInput.BaseStream);
        responses = new BinaryReader(worker.StandardOutput.BaseStream);
    }

    public void Dispose()
    {
        var process = worker;
        worker = null;
        if (process is not null)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception error) when (error is InvalidOperationException or Win32Exception) { }
        }
        try { requests?.Dispose(); } catch (IOException) { }
        responses?.Dispose();
        process?.Dispose();
        requests = null;
        responses = null;
    }

    internal static void RunWorker()
    {
        using var input = new BinaryReader(Console.OpenStandardInput());
        using var output = new BinaryWriter(Console.OpenStandardOutput());
        try
        {
            var configured = new ImageClickTarget { TemplatePng = input.ReadString(), ClickX = input.ReadInt32(), ClickY = input.ReadInt32(), MinimumSimilarity = input.ReadInt32() };
            using var template = configured.OpenTemplate();
            var pattern = new ImagePattern(template);
            while (true)
            {
                var window = (nint)input.ReadInt64();
                var size = new Size(input.ReadInt32(), input.ReadInt32());
                var preview = input.ReadBoolean();
                using var frame = Capture(window, size);
                var match = frame is null ? new(ImageMatchStatus.CaptureUnavailable) : pattern.Find(frame, configured.MinimumSimilarity, CancellationToken.None);
                output.Write((byte)match.Status);
                output.Write(match.Bounds.X);
                output.Write(match.Bounds.Y);
                output.Write(match.Bounds.Width);
                output.Write(match.Bounds.Height);
                output.Write(match.Similarity);
                if (preview && frame is not null)
                {
                    using var encoded = new MemoryStream();
                    frame.Save(encoded, ImageFormat.Png);
                    if (encoded.Length <= MaximumPreviewBytes)
                    {
                        output.Write((int)encoded.Length);
                        encoded.WriteTo(output.BaseStream);
                    }
                    else output.Write(0);
                }
                else output.Write(0);
                output.Flush();
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or Win32Exception or System.Runtime.InteropServices.ExternalException) { }
    }

    private static Bitmap? Capture(nint window, Size size)
    {
        if (!ValidSize(size) || !WindowImageCapture.TryClientArea(window, out var area) || area.Size != size) return null;
        var frame = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
        var retained = false;
        try
        {
            using var graphics = Graphics.FromImage(frame);
            graphics.Clear(Color.Black);
            var device = graphics.GetHdc();
            bool rendered;
            try { rendered = Native.PrintWindow(window, device, 3) || Native.PrintWindow(window, device, 1); }
            finally { graphics.ReleaseHdc(device); }
            retained = rendered && WindowImageCapture.TryClientArea(window, out var after) && after == area;
            return retained ? frame : null;
        }
        finally { if (!retained) frame.Dispose(); }
    }

    private static bool ValidSize(Size size) => size.Width >= ImageClickTarget.MinimumSide && size.Height >= ImageClickTarget.MinimumSide &&
        (long)size.Width * size.Height <= MaximumPixels;
}
