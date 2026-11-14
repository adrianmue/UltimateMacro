using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Text.Json.Serialization;

namespace UltimateMacro;

public sealed record ImageClickTarget
{
    internal const int MinimumSide = 12;
    internal const int MaximumWidth = 480;
    internal const int MaximumHeight = 320;

    public string WindowTitle { get; init; } = "";
    public string WindowClass { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string TemplatePng { get; init; } = "";
    public int ClickX { get; init; }
    public int ClickY { get; init; }
    public int MinimumSimilarity { get; init; } = 94;
    public int WaitSeconds { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Background { get; init; }
    [JsonIgnore] internal nint SelectedWindow { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(WindowTitle) || WindowTitle.Length > 1024 ||
            string.IsNullOrWhiteSpace(WindowClass) || WindowClass.Length > 256 ||
            string.IsNullOrWhiteSpace(ProcessName) || ProcessName.Length > 260)
            throw new InvalidDataException("Das Zielfenster für den Bildklick fehlt oder ist ungültig.");
        if (MinimumSimilarity is < 85 or > 100 || WaitSeconds is < 0 or > 600)
            throw new InvalidDataException("Ähnlichkeit oder Wartezeit für den Bildklick liegt außerhalb des zulässigen Bereichs.");
        ReadTemplateBytes();
    }

    internal Bitmap OpenTemplate()
    {
        var bytes = ReadTemplateBytes();
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var decoded = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
            if (decoded.Width != BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)) ||
                decoded.Height != BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)))
                throw new InvalidDataException("Die Bildvorlage hat ungültige Abmessungen.");
            var bitmap = new Bitmap(decoded.Width, decoded.Height, PixelFormat.Format32bppArgb);
            try
            {
                using var graphics = Graphics.FromImage(bitmap);
                graphics.DrawImageUnscaled(decoded, 0, 0);
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
        }
        catch (Exception error) when (error is ArgumentException or OutOfMemoryException or System.Runtime.InteropServices.ExternalException)
        {
            throw new InvalidDataException("Die Bildvorlage konnte nicht gelesen werden.", error);
        }
    }

    internal static string Encode(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return Convert.ToBase64String(stream.ToArray());
    }

    private byte[] ReadTemplateBytes()
    {
        if (string.IsNullOrEmpty(TemplatePng) || TemplatePng.Length > 1_000_000)
            throw new InvalidDataException("Die Bildvorlage fehlt oder ist zu groß.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(TemplatePng); }
        catch (FormatException error) { throw new InvalidDataException("Die Bildvorlage ist ungültig.", error); }
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        if (bytes.Length < 33 || !bytes.AsSpan(0, 8).SequenceEqual(signature) || !bytes.AsSpan(12, 4).SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Die Bildvorlage muss ein gültiges PNG sein.");
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        if (width is < MinimumSide or > MaximumWidth || height is < MinimumSide or > MaximumHeight ||
            ClickX < 0 || ClickX >= width || ClickY < 0 || ClickY >= height)
            throw new InvalidDataException("Die Größe der Bildvorlage oder ihr Klickpunkt ist ungültig.");
        return bytes;
    }
}
