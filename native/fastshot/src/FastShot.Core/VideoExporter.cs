using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using FastShot.Core.Media;

namespace FastShot.Core;

public enum VideoFormat { Mp4, Gif }

public static class VideoFormatExtensions
{
    public static string FileExtension(this VideoFormat format) => format == VideoFormat.Mp4 ? "mp4" : "gif";
}

public sealed class VideoExportRequest
{
    public required string Source { get; init; }
    public required Size CanvasSize { get; init; }
    public List<Annotation> Annotations { get; init; } = [];
    public (double start, double end)? Trim { get; init; }
    public bool PreferHEVC { get; init; }
    /// <summary>Re-encode even when the source could be copied as-is (e.g. to change codec).</summary>
    public bool ForceReencode { get; init; }
    /// <summary>Largest output frame, e.g. (1920, 1080); portrait sources use it turned. Null keeps the source size.</summary>
    public (int width, int height)? MaxSize { get; init; }
    /// <summary>Multiplies the default bit rate (below 1 for smaller files).</summary>
    public double BitrateScale { get; init; } = 1;
    public bool IncludeAudio { get; init; } = true;
}

/// <summary>Paints the annotations visible at a frame's time over that frame. Overlays without
/// pixelation are cached, because a mark looks the same on every frame it spans; pixelation
/// samples the frame itself and is drawn per frame.</summary>
public sealed class AnnotationFrameRenderer
{
    readonly Size canvasSize;
    readonly List<Annotation> annotations;
    List<Guid> cachedKey = [];
    byte[]? cachedOverlay;
    int cachedWidth, cachedHeight;

    public AnnotationFrameRenderer(Size canvasSize, IEnumerable<Annotation> annotations)
    {
        this.canvasSize = canvasSize;
        this.annotations = annotations.ToList();
    }

    public bool HasAnnotations => annotations.Count > 0;

    /// <summary>Returns the frame's BGRA pixels with the marks burned in; the input array is reused when nothing is visible.</summary>
    public byte[] Render(VideoFrame frame)
    {
        var visible = annotations.Where(a => a.IsVisible(frame.Time + frame.Duration * 0.5)).ToList();
        if (visible.Count == 0) return frame.Bgra;
        if (visible.Any(a => a.Kind == AnnotationKind.pixelate))
        {
            var flattened = ImageExporter.Flatten(frame.ToBitmap(), canvasSize, visible);
            var pixels = new byte[frame.Width * frame.Height * 4];
            flattened.CopyPixels(pixels, frame.Stride, 0);
            return Unpremultiply(pixels);
        }
        var key = visible.Select(a => a.Id).ToList();
        if (cachedOverlay is null || !key.SequenceEqual(cachedKey) || cachedWidth != frame.Width || cachedHeight != frame.Height)
        {
            var overlay = ImageExporter.Overlay(frame.Width, frame.Height, canvasSize, visible);
            cachedOverlay = new byte[frame.Width * frame.Height * 4];
            overlay.CopyPixels(cachedOverlay, frame.Stride, 0);
            cachedKey = key;
            cachedWidth = frame.Width;
            cachedHeight = frame.Height;
        }
        var output = new byte[frame.Bgra.Length];
        Composite(frame.Bgra, cachedOverlay, output);
        return output;
    }

    /// <summary>Premultiplied overlay over an opaque frame.</summary>
    static void Composite(byte[] source, byte[] overlay, byte[] output)
    {
        for (var i = 0; i < source.Length; i += 4)
        {
            var a = overlay[i + 3];
            if (a == 0)
            {
                output[i] = source[i]; output[i + 1] = source[i + 1]; output[i + 2] = source[i + 2]; output[i + 3] = 255;
                continue;
            }
            var inverse = 255 - a;
            output[i] = (byte)(overlay[i] + (source[i] * inverse + 127) / 255);
            output[i + 1] = (byte)(overlay[i + 1] + (source[i + 1] * inverse + 127) / 255);
            output[i + 2] = (byte)(overlay[i + 2] + (source[i + 2] * inverse + 127) / 255);
            output[i + 3] = 255;
        }
    }

    static byte[] Unpremultiply(byte[] pixels)
    {
        for (var i = 0; i < pixels.Length; i += 4) pixels[i + 3] = 255;
        return pixels;
    }
}

public static class VideoExporter
{
    /// <summary>Runs the export on a dedicated thread; the progress callback is invoked from that thread.</summary>
    public static Task ExportAsync(VideoExportRequest request, string outputPath, VideoFormat format, Action<double> progress, CancellationToken cancellation = default)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                Export(request, outputPath, format, progress, cancellation);
                completion.SetResult();
            }
            catch (Exception error)
            {
                completion.SetException(error);
            }
        }) { IsBackground = true, Name = "FastShot export" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    /// <summary>Writes an MP4 (or GIF) with the annotations burned in. Untouched recordings are copied.</summary>
    public static void Export(VideoExportRequest request, string outputPath, VideoFormat format, Action<double> progress, CancellationToken cancellation = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        if (format == VideoFormat.Gif)
        {
            ExportGif(request, outputPath, progress, cancellation: cancellation);
            return;
        }
        if (File.Exists(outputPath)) File.Delete(outputPath);
        using var reader = new VideoReader(request.Source, audio: request.IncludeAudio);
        var duration = reader.Duration;
        (double start, double end)? trim = request.Trim is { } range && !(range.start <= 0.01 && range.end >= duration - 0.01) ? range : null;
        var sameContainer = Path.GetExtension(request.Source).Equals(".mp4", StringComparison.OrdinalIgnoreCase);
        if (request.Annotations.Count == 0 && trim is null && sameContainer && !request.ForceReencode)
        {
            File.Copy(request.Source, outputPath, overwrite: true);
            progress(1);
            return;
        }
        var start = trim?.start ?? 0;
        var end = trim?.end ?? duration;
        var fps = (int)Math.Clamp(Math.Round(reader.FrameRate), 1, 120);
        // The sink writer picks the container from the extension, so the temporary file keeps .mp4.
        var temp = Path.Combine(Path.GetDirectoryName(outputPath)!, $".{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.mp4");
        var (outWidth, outHeight) = OutputSize(reader.Width, reader.Height, request.MaxSize);
        var bitrate = (int)(VideoWriter.DefaultBitrate(outWidth, outHeight, fps) * request.BitrateScale);
        using (var writer = new VideoWriter(temp, outWidth, outHeight, fps, request.PreferHEVC, bitrate, audio: reader.Audio))
        {
            var renderer = new AnnotationFrameRenderer(request.CanvasSize, request.Annotations);
            reader.Seek(start);
            var written = 0;
            double lastTime = -1;
            var videoDone = false;
            while (true)
            {
                cancellation.ThrowIfCancellationRequested();
                var item = reader.ReadNext();
                if (item is null) break;
                if (item is VideoFrame frame)
                {
                    if (videoDone || frame.Time + frame.Duration <= start) continue;
                    if (frame.Time >= end - 0.0005)
                    {
                        videoDone = true;
                        if (writer.Audio is null) break;
                        continue;
                    }
                    var time = Math.Max(0, frame.Time - start);
                    if (time <= lastTime) continue;
                    lastTime = time;
                    if (outWidth != frame.Width || outHeight != frame.Height) frame = PixelConvert.Resize(frame, outWidth, outHeight);
                    var pixels = renderer.Render(frame);
                    writer.WriteFrame(pixels, frame.Stride, time, Math.Min(frame.Duration, end - frame.Time));
                    written++;
                    if (end > start) progress(Math.Min(0.99, (frame.Time - start) / (end - start)));
                }
                else if (item is AudioChunk chunk && writer.Audio is { } pcm)
                {
                    if (chunk.Time >= end)
                    {
                        if (videoDone) break;
                        continue;
                    }
                    if (chunk.Time + chunk.Duration <= start) continue;
                    var skip = chunk.Time < start ? (int)Math.Round((start - chunk.Time) * pcm.SampleRate) : 0;
                    var take = Math.Min(chunk.Frames, (int)Math.Round((end - chunk.Time) * pcm.SampleRate)) - skip;
                    if (take <= 0) continue;
                    writer.WriteAudio(chunk.Pcm.AsSpan(skip * pcm.BlockAlign, take * pcm.BlockAlign),
                                      Math.Max(0, chunk.Time + skip / (double)pcm.SampleRate - start));
                }
            }
            if (written == 0) throw new VideoExportException("Videodan kare okunamadı.");
            writer.Finish();
        }
        File.Move(temp, outputPath, overwrite: true);
        progress(1);
    }

    /// <summary>Fits the frame inside <paramref name="max"/> (turned for portrait video), even-sized for the encoder.</summary>
    public static (int width, int height) OutputSize(int width, int height, (int width, int height)? max)
    {
        if (max is not { } limit) return (width & ~1, height & ~1);
        var (longSide, shortSide) = (Math.Max(limit.width, limit.height), Math.Min(limit.width, limit.height));
        var scale = width >= height ? Math.Min(1.0, Math.Min(longSide / (double)width, shortSide / (double)height))
                                    : Math.Min(1.0, Math.Min(shortSide / (double)width, longSide / (double)height));
        return (Math.Max(2, (int)(width * scale) & ~1), Math.Max(2, (int)(height * scale) & ~1));
    }

    /// <summary>Renders an animated GIF of the (trimmed) recording with its annotations.</summary>
    public static void ExportGif(VideoExportRequest request, string outputPath, Action<double> progress, double fps = 12, int maxWidth = 1000, CancellationToken cancellation = default)
    {
        using var reader = new VideoReader(request.Source);
        var duration = reader.Duration;
        var start = request.Trim?.start ?? 0;
        var end = request.Trim?.end ?? duration;
        var renderer = new AnnotationFrameRenderer(request.CanvasSize, request.Annotations);
        var scale = Math.Min(1.0, maxWidth / (double)Math.Max(1, reader.Width));
        var width = Math.Max(1, (int)Math.Round(reader.Width * scale));
        var height = Math.Max(1, (int)Math.Round(reader.Height * scale));
        var frames = new List<BitmapSource>();
        var step = 1 / fps;
        var next = start;
        reader.Seek(start);
        while (next < end - 0.0005)
        {
            cancellation.ThrowIfCancellationRequested();
            var frame = reader.ReadFrame();
            if (frame is null) break;
            if (frame.Time + frame.Duration <= next) continue;
            var pixels = renderer.Render(frame);
            var bitmap = ImageExporter.FromBgra(pixels, frame.Width, frame.Height, frame.Stride);
            if (scale < 1) bitmap = ImageExporter.Scaled(bitmap, Math.Max(width, height));
            // One decoded frame can stand in for several GIF frames when the source runs faster than the GIF.
            while (next < end - 0.0005 && frame.Time + frame.Duration > next)
            {
                frames.Add(bitmap);
                next += step;
            }
            if (end > start) progress(Math.Min(0.95, (next - start) / (end - start)));
        }
        if (frames.Count == 0) throw new VideoExportException("Videodan kare okunamadı.");
        GifWriter.Write(outputPath, frames, step);
        progress(1);
    }

    /// <summary>A still of the recording at <paramref name="time"/>, used for thumbnails and paused previews.</summary>
    public static BitmapSource? Frame(string path, double time, int? maxSide = null)
    {
        using var reader = new VideoReader(path);
        var frame = reader.FrameAt(time);
        if (frame is null) return null;
        var bitmap = frame.ToBitmap();
        return maxSide is { } side ? ImageExporter.Scaled(bitmap, side) : bitmap;
    }

    /// <summary>Duration and pixel size without decoding frames.</summary>
    public static (double duration, int width, int height, double fps, bool audio) Probe(string path)
    {
        using var reader = new VideoReader(path);
        return (reader.Duration, reader.Width, reader.Height, reader.FrameRate, reader.HasAudioTrack);
    }
}

public sealed class VideoExportException(string message) : Exception(message);
