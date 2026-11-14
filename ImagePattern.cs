using System.Buffers;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace UltimateMacro;

internal enum ImageMatchStatus { Missing, Ambiguous, Found, WindowUnavailable, Obscured, BackgroundUnavailable, CaptureUnavailable, ClickUnavailable }

internal readonly record struct ImageMatch(ImageMatchStatus Status, Rectangle Bounds = default, double Similarity = 0);

internal sealed class ImagePattern
{
    private readonly int width;
    private readonly int height;
    private readonly int[] template;
    private readonly Point[] anchors;
    private readonly Point[] samples;

    internal ImagePattern(Bitmap bitmap)
    {
        width = bitmap.Width;
        height = bitmap.Height;
        template = new int[width * height];
        ReadPixels(bitmap, template);
        var mean = MeanColor(template);
        var contrast = new List<(Point Point, int Error)>();
        for (var cellY = 0; cellY < 4; cellY++)
        for (var cellX = 0; cellX < 4; cellX++)
        {
            var strongest = (Point: new Point(), Error: -1);
            for (var sampleY = cellY * height / 4; sampleY < (cellY + 1) * height / 4; sampleY++)
            for (var sampleX = cellX * width / 4; sampleX < (cellX + 1) * width / 4; sampleX++)
            {
                var error = ColorError(template[sampleY * width + sampleX], mean);
                if (error > strongest.Error) strongest = (new Point(sampleX, sampleY), error);
            }
            contrast.Add(strongest);
        }
        if (contrast.Max(item => item.Error) < 36)
            throw new InvalidDataException("Der Ausschnitt ist zu einfarbig. Markiere den Button mit Schrift, Symbol oder einer deutlichen Kante.");
        anchors = contrast.OrderByDescending(item => item.Error).Select(item => item.Point).ToArray();
        samples = (from sampleY in Enumerable.Range(0, 8)
                   from sampleX in Enumerable.Range(0, 8)
                   select new Point((2 * sampleX + 1) * width / 16, (2 * sampleY + 1) * height / 16)).ToArray();
    }

    internal ImageMatch Find(Bitmap image, int minimumSimilarity, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var frameWidth = image.Width;
        var frameHeight = image.Height;
        if (width > frameWidth || height > frameHeight) return new(ImageMatchStatus.Missing);
        var frame = ArrayPool<int>.Shared.Rent(checked(frameWidth * frameHeight));
        try
        {
            ReadPixels(image, frame);
            var anchorOffsets = anchors.Select(point => (Offset: point.Y * frameWidth + point.X, Color: template[point.Y * width + point.X])).ToArray();
            var sampleOffsets = samples.Select(point => (Offset: point.Y * frameWidth + point.X, Color: template[point.Y * width + point.X])).ToArray();
            var averageLimit = (100 - minimumSimilarity) * 765 / 100.0;
            var anchorLimit = Math.Max(45, (int)(averageLimit * 3.5));
            var sampleLimit = (int)(averageLimit * sampleOffsets.Length * 1.5);
            var totalLimit = (int)(averageLimit * template.Length);
            var matches = new List<(int X, int Y, int Error)>();
            var candidates = 0;
            for (var originY = 0; originY <= frameHeight - height; originY++)
            {
                token.ThrowIfCancellationRequested();
                for (var originX = 0; originX <= frameWidth - width; originX++)
                {
                    var offset = originY * frameWidth + originX;
                    var anchorIndex = 0;
                    for (; anchorIndex < anchorOffsets.Length; anchorIndex++)
                    {
                        var anchor = anchorOffsets[anchorIndex];
                        if (ColorError(frame[offset + anchor.Offset], anchor.Color) > anchorLimit) break;
                    }
                    if (anchorIndex != anchorOffsets.Length) continue;
                    var sampleError = 0;
                    foreach (var sample in sampleOffsets)
                    {
                        sampleError += ColorError(frame[offset + sample.Offset], sample.Color);
                        if (sampleError > sampleLimit) break;
                    }
                    if (sampleError > sampleLimit) continue;
                    if (++candidates > 256) return new(ImageMatchStatus.Ambiguous);
                    var error = Compare(frame, frameWidth, offset, totalLimit, token);
                    if (error <= totalLimit) matches.Add((originX, originY, error));
                    if (error == 0 && matches.Any(match => match.Error == 0 && IsSeparate(match.X, match.Y, originX, originY)))
                        return new(ImageMatchStatus.Ambiguous);
                }
            }
            if (matches.Count == 0) return new(ImageMatchStatus.Missing);
            var best = matches.MinBy(match => match.Error);
            var separation = 0.015 * 765 * template.Length;
            if (matches.Any(match => match.Error - best.Error <= separation && IsSeparate(match.X, match.Y, best.X, best.Y)))
                return new(ImageMatchStatus.Ambiguous);
            return new(ImageMatchStatus.Found, new Rectangle(best.X, best.Y, width, height), 100 - 100.0 * best.Error / (765.0 * template.Length));
        }
        finally { ArrayPool<int>.Shared.Return(frame); }
    }

    private bool IsSeparate(int firstX, int firstY, int secondX, int secondY) =>
        Math.Abs(firstX - secondX) > Math.Max(2, width / 4) || Math.Abs(firstY - secondY) > Math.Max(2, height / 4);

    private int Compare(int[] frame, int frameWidth, int offset, int limit, CancellationToken token)
    {
        var total = 0;
        for (var row = 0; row < height; row++)
        {
            if ((row & 15) == 0) token.ThrowIfCancellationRequested();
            var frameIndex = offset + row * frameWidth;
            var templateIndex = row * width;
            for (var column = 0; column < width; column++) total += ColorError(frame[frameIndex + column], template[templateIndex + column]);
            if (total > limit) break;
        }
        return total;
    }

    private static int ColorError(int first, int second) => Math.Abs(((first >> 16) & 255) - ((second >> 16) & 255)) +
        Math.Abs(((first >> 8) & 255) - ((second >> 8) & 255)) + Math.Abs((first & 255) - (second & 255));

    private static int MeanColor(int[] pixels)
    {
        var red = 0;
        var green = 0;
        var blue = 0;
        foreach (var pixel in pixels) { red += (pixel >> 16) & 255; green += (pixel >> 8) & 255; blue += pixel & 255; }
        return (red / pixels.Length << 16) | (green / pixels.Length << 8) | blue / pixels.Length;
    }

    private static void ReadPixels(Bitmap bitmap, int[] pixels)
    {
        var format = bitmap.PixelFormat == PixelFormat.Format32bppRgb ? PixelFormat.Format32bppRgb : PixelFormat.Format32bppArgb;
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, format);
        try
        {
            if (data.Stride == bitmap.Width * sizeof(int)) Marshal.Copy(data.Scan0, pixels, 0, bitmap.Width * bitmap.Height);
            else for (var row = 0; row < bitmap.Height; row++) Marshal.Copy(data.Scan0 + row * data.Stride, pixels, row * bitmap.Width, bitmap.Width);
        }
        finally { bitmap.UnlockBits(data); }
    }
}
