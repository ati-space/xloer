using System.IO;
using System.Windows.Media.Imaging;

namespace FastShot.Core;

public enum ScrollAppendResult { Added, Unchanged, Ambiguous, LimitReached, Revisited }

/// <summary>Matches vertical overlaps of equal-sized BGRA frames. Refuses unrelated or ambiguous
/// frames rather than silently losing content. A fixed top/bottom band is retained only once.</summary>
public sealed class ScrollStitcher
{
    public const long MaxBytes = 128L * 1024 * 1024;
    readonly int width, height, stride;
    readonly MemoryStream pixels = new();
    byte[] previous;
    int position, furthest;
    int? fixedTop, fixedBottom;
    public int Height => (int)(pixels.Length / stride);
    /// <summary>Current viewport offset. Revisiting captured content never duplicates it.</summary>
    public int Position => position;

    public ScrollStitcher(byte[] first, int width, int height)
    {
        if (width < 32 || height < 64 || height > 32767 || first.LongLength != (long)width * height * 4 || first.LongLength > MaxBytes)
            throw new ArgumentException("Kaydırılacak alan en az 32 × 64 piksel olmalı ve 128 MB'ı aşmamalı.");
        this.width = width; this.height = height; stride = width * 4;
        previous = (byte[])first.Clone();
        pixels.Write(first);
    }

    public ScrollAppendResult Append(byte[] frame, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        if (frame.Length != previous.Length) throw new ArgumentException("Kare boyutu değişti.", nameof(frame));
        var xStep = Math.Max(1, width / 80);
        var yStep = Math.Max(1, height / 100);
        double RowError(int a, int b)
        {
            long sum = 0; var count = 0;
            for (var x = 4; x < width - 16; x += xStep)
            {
                var p = a * stride + x * 4; var q = b * stride + x * 4;
                for (var c = 0; c < 3; c++) { sum += Math.Abs(previous[p + c] - frame[q + c]); count++; }
            }
            return sum / (double)Math.Max(1, count);
        }
        double Score(int shift, int top, int bottom)
        {
            double sum = 0; var count = 0;
            for (var y = top + Math.Max(0, -shift); y < height - bottom - Math.Max(0, shift); y += yStep)
            { sum += RowError(y + shift, y); count++; }
            return count == 0 ? double.MaxValue : sum / count;
        }
        if (Score(0, 0, 0) < 0.6) return ScrollAppendResult.Unchanged;
        // Detect stationary chrome. A content-only selection is still preferable.
        var top = 0; var bottom = 0;
        while (top < height / 4 && RowError(top, top) < 0.6) top++;
        while (bottom < height / 6 && RowError(height - bottom - 1, height - bottom - 1) < 0.6) bottom++;
        // A large blank margin is not evidence of fixed browser chrome.
        if (top == height / 4) top = 0;
        if (bottom == height / 6) bottom = 0;
        if (top < 8) top = 0;
        if (bottom < 8) bottom = 0;
        top = fixedTop ?? top;
        bottom = fixedBottom ?? bottom;
        var maxShift = (height - top - bottom) * 3 / 4;
        var minShift = -Math.Min(position, maxShift);
        var scores = new double[maxShift - minShift + 1];
        var best = double.MaxValue; var offset = 0;
        for (var shift = minShift; shift <= maxShift; shift++)
        {
            if (shift == 0) continue;
            if ((shift & 63) == 0) cancellation.ThrowIfCancellationRequested();
            var score = scores[shift - minShift] = Score(shift, top, bottom);
            if (score < best) { best = score; offset = shift; }
        }
        if (offset == 0 || best > 6) return ScrollAppendResult.Ambiguous;
        // Repeated rows/blank pages do not contain enough evidence to infer a displacement.
        for (var shift = minShift; shift <= maxShift; shift++)
            if (shift != 0 && Math.Abs(shift - offset) > 3 && scores[shift - minShift] <= best + 0.8) return ScrollAppendResult.Ambiguous;
        var nextPosition = position + offset;
        var added = Math.Max(0, nextPosition - furthest);
        if (Height + added > 32767 || pixels.Length + (long)added * stride > MaxBytes) return ScrollAppendResult.LimitReached;
        fixedTop ??= top; fixedBottom ??= bottom;
        previous = (byte[])frame.Clone();
        position = nextPosition;
        if (added == 0) return ScrollAppendResult.Revisited;
        // Replace the old fixed footer with new content and the current footer.
        pixels.SetLength(pixels.Length - (long)bottom * stride);
        pixels.Position = pixels.Length;
        pixels.Write(frame, (height - bottom - added) * stride, (added + bottom) * stride);
        furthest = position;
        return ScrollAppendResult.Added;
    }

    public BitmapSource Image() => ImageExporter.FromBgra(pixels.ToArray(), width, Height, stride);

    /// <summary>A bounded thumbnail without copying the full (up to 128 MB) capture each time.</summary>
    public BitmapSource Preview(int maxWidth, int maxHeight)
    {
        if (maxWidth < 1 || maxHeight < 1) throw new ArgumentOutOfRangeException(nameof(maxWidth));
        var scale = Math.Min(1, Math.Min(maxWidth / (double)width, maxHeight / (double)Height));
        var w = Math.Max(1, (int)(width * scale)); var h = Math.Max(1, (int)(Height * scale));
        var data = new byte[w * h * 4]; var source = pixels.GetBuffer();
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                Buffer.BlockCopy(source, ((int)((long)y * Height / h) * width + (int)((long)x * width / w)) * 4, data, (y * w + x) * 4, 4);
        return ImageExporter.FromBgra(data, w, h, w * 4);
    }
}
