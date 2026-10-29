using System.IO;
using System.Windows;
using FastShot.Core.Media;

namespace FastShot.Core.Convert;

/// <summary>Video and audio conversions through Media Foundation.</summary>
public static class MediaConversion
{
    /// <summary>To MP4: copies the streams untouched when MP4 can hold them, otherwise re-encodes (always for HEVC).</summary>
    public static void ConvertVideo(string input, string output, bool hevc, Action<double> progress, CancellationToken cancellation)
    {
        if (!hevc)
        {
            try
            {
                Remuxer.ToMp4(input, output, progress, cancellation);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch { FileConverter.TryDelete(output); }
        }
        (double duration, int width, int height, double fps, bool audio) probe;
        try { probe = VideoExporter.Probe(input); }
        catch { throw ConversionException.Unreadable(Path.GetFileName(input)); }
        var request = new VideoExportRequest
        {
            Source = input, CanvasSize = new Size(Math.Max(1, probe.width), Math.Max(1, probe.height)), PreferHEVC = hevc, ForceReencode = true,
        };
        VideoExporter.Export(request, output, VideoFormat.Mp4, progress, cancellation);
    }

    public static void ConvertToGif(string input, string output, Action<double> progress, CancellationToken cancellation)
    {
        (double duration, int width, int height, double fps, bool audio) probe;
        try { probe = VideoExporter.Probe(input); }
        catch { throw ConversionException.Unreadable(Path.GetFileName(input)); }
        var request = new VideoExportRequest { Source = input, CanvasSize = new Size(Math.Max(1, probe.width), Math.Max(1, probe.height)) };
        VideoExporter.ExportGif(request, output, progress, cancellation: cancellation);
    }

    /// <summary>Decodes the first audio track (of an audio or video file) and writes it with <paramref name="codec"/>.</summary>
    public static void ConvertAudio(string input, string codec, string output, Action<double> progress, CancellationToken cancellation, int? bytesPerSecond = null)
    {
        var native = codec switch
        {
            "mp3" => Capabilities.NativeMp3,
            "flac" => Capabilities.NativeFlac,
            _ => true,
        };
        if (!native)
        {
            ConvertWithFfmpeg(input, codec, output, progress, cancellation);
            return;
        }
        using var probe = AudioReader.Open(input);
        var rate = AudioWriter.SupportedRate(codec, probe.Format.SampleRate);
        probe.Dispose();
        using var reader = AudioReader.Open(input, rate);
        if (reader.Format.SampleRate != rate && codec is "m4a" or "mp3")
            throw ConversionException.Unsupported(Path.GetFileName(input));
        using var writer = AudioWriter.Create(output, codec, reader.Format, bytesPerSecond);
        long bytes = 0;
        while (reader.Read() is { } chunk)
        {
            cancellation.ThrowIfCancellationRequested();
            writer.Write(chunk);
            bytes += chunk.Length;
            if (reader.Duration > 0) progress(Math.Min(0.99, bytes / (double)reader.Format.BlockAlign / reader.Format.SampleRate / reader.Duration));
        }
        if (bytes == 0) throw ConversionException.NoAudio(Path.GetFileName(input));
        writer.Finish();
    }

    static void ConvertWithFfmpeg(string input, string codec, string output, Action<double> progress, CancellationToken cancellation)
    {
        var tool = ExternalTools.Ffmpeg ?? throw ConversionException.Unsupported(Path.GetFileName(input));
        // Decode with Windows first so anything Windows plays works, then let ffmpeg encode.
        var wav = Path.Combine(Path.GetTempPath(), $"fastshot-{Guid.NewGuid():N}.wav");
        try
        {
            ConvertAudio(input, "wav", wav, fraction => progress(fraction * 0.6), cancellation);
            var arguments = codec == "mp3"
                ? new[] { "-y", "-loglevel", "error", "-i", wav, "-codec:a", "libmp3lame", "-q:a", "2", output }
                : ["-y", "-loglevel", "error", "-i", wav, "-codec:a", "flac", output];
            ExternalTools.Run(tool, arguments, Path.GetFileName(input), cancellation: cancellation);
        }
        finally { FileConverter.TryDelete(wav); }
    }
}
