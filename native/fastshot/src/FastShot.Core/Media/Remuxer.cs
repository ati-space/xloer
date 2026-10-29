using System.IO;

namespace FastShot.Core.Media;

/// <summary>Copies the compressed video and audio of a file into an MP4 without re-encoding (MOV → MP4 and the like).
/// Fast and lossless; throws when the codecs do not fit MP4, so the caller can re-encode instead.</summary>
public static class Remuxer
{
    /// <param name="keepVideo">Copy the video track.</param>
    /// <param name="keepAudio">Copy the audio track. With video off the output is audio-only (name it .m4a).</param>
    public static void ToMp4(string input, string output, Action<double> progress, CancellationToken cancellation = default, bool keepVideo = true, bool keepAudio = true)
    {
        MF.EnsureStarted();
        MF.Check(MF.MFCreateSourceReaderFromURL(input, null, out var reader), "Dosya açma");
        IMFSinkWriter? writer = null;
        try
        {
            var map = new Dictionary<uint, uint>();
            var types = new List<(uint source, IMFMediaType type)>();
            bool video = false, audio = false;
            for (uint i = 0; i < 32; i++)
            {
                if (reader.GetNativeMediaType(i, 0, out var type) < 0) break;
                type.GetMajorType(out var major);
                if ((major == MF.MFMediaType_Video && !video && keepVideo) || (major == MF.MFMediaType_Audio && !audio && keepAudio))
                {
                    if (major == MF.MFMediaType_Video) video = true; else audio = true;
                    types.Add((i, type));
                }
                else
                {
                    MF.Release(type);
                }
            }
            if (types.Count == 0) throw new IOException("Kopyalanacak iz yok.");
            if (keepVideo && !video) throw new IOException("Video izi yok.");
            reader.SetStreamSelection(MF.MF_SOURCE_READER_ALL_STREAMS, 0);
            MF.Check(MF.MFCreateSinkWriterFromURL(output, IntPtr.Zero, null, out writer), "Dosya oluşturma");
            foreach (var (source, type) in types)
            {
                reader.SetStreamSelection(source, 1);
                MF.Check(reader.SetCurrentMediaType(source, IntPtr.Zero, type), "Akış türü");
                MF.Check(writer.AddStream(type, out var target), "MP4 akışı");
                map[source] = target;
                MF.Release(type);
            }
            var duration = 0.0;
            if (reader.GetPresentationAttribute(MF.MF_SOURCE_READER_MEDIASOURCE, ref MF.MF_PD_DURATION, out var value) >= 0) duration = value.value / 10_000_000.0;
            MF.Check(writer.BeginWriting(), "Yazmaya başlama");
            var open = new HashSet<uint>(map.Keys);
            long? origin = null;
            var written = 0;
            while (open.Count > 0)
            {
                cancellation.ThrowIfCancellationRequested();
                MF.Check(reader.ReadSample(MF.MF_SOURCE_READER_ANY_STREAM, 0, out var stream, out var flags, out var timestamp, out var sample), "ReadSample");
                if ((flags & MF.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
                {
                    MF.Release(sample);
                    open.Remove(stream);
                    reader.SetStreamSelection(stream, 0);
                    continue;
                }
                if (sample is null || !map.TryGetValue(stream, out var target))
                {
                    MF.Release(sample);
                    continue;
                }
                try
                {
                    // Start at zero even when the source's first sample does not.
                    origin ??= timestamp;
                    sample.SetSampleTime(Math.Max(0, timestamp - origin.Value));
                    MF.Check(writer.WriteSample(target, sample), "WriteSample");
                    written++;
                }
                finally { MF.Release(sample); }
                if (duration > 0) progress(Math.Min(0.99, (timestamp - (origin ?? 0)) / 10_000_000.0 / duration));
            }
            if (written == 0) throw new IOException("Kopyalanacak örnek yok.");
            MF.Check(writer.Finalize(), "Dosyayı tamamlama");
        }
        finally
        {
            MF.Release(writer);
            MF.Release(reader);
        }
    }
}

public static class MediaProbe
{
    public static bool CanEncodeHevc()
    {
        var path = Path.Combine(Path.GetTempPath(), $"fastshot-probe-{Guid.NewGuid():N}.mp4");
        try
        {
            using var writer = new VideoWriter(path, 64, 64, 30, hevc: true);
            var frame = new byte[64 * 64 * 4];
            for (var i = 0; i < 3; i++) writer.WriteFrame(frame, 256, i / 30.0, 1 / 30.0);
            var hevc = writer.HEVC;
            writer.Finish();
            return hevc;
        }
        catch { return false; }
        finally { Convert.FileConverter.TryDelete(path); }
    }
}
