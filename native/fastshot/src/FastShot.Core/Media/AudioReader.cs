using System.IO;

namespace FastShot.Core.Media;

/// <summary>Decodes the first audio track of any file Media Foundation can open (audio or video) to 16-bit PCM.
/// AIFF and CAF, which Windows does not read, are parsed directly.</summary>
public abstract class AudioReader : IDisposable
{
    public required AudioFormat Format { get; init; }
    public double Duration { get; init; }

    /// <summary>The next run of interleaved 16-bit PCM, or null at the end.</summary>
    public abstract byte[]? Read();

    public virtual void Dispose() { }

    /// <param name="sampleRate">Ask for this rate (Windows resamples); null keeps the file's own.</param>
    /// <param name="maxChannels">Mix down to at most this many channels.</param>
    public static AudioReader Open(string path, int? sampleRate = null, int maxChannels = 2)
    {
        var name = Path.GetFileName(path);
        try
        {
            return new MediaFoundationAudioReader(path, sampleRate, maxChannels);
        }
        catch (NoAudioException)
        {
            throw Convert.ConversionException.NoAudio(name);
        }
        catch (Exception)
        {
            var pcm = AudioFiles.TryReadPcm(path) ?? throw Convert.ConversionException.Unreadable(name);
            return new MemoryAudioReader(pcm.format, pcm.data, sampleRate, maxChannels);
        }
    }

    internal sealed class NoAudioException() : Exception("no audio");
}

sealed class MediaFoundationAudioReader : AudioReader
{
    IMFSourceReader? reader;
    readonly uint stream;
    readonly int sourceChannels;
    readonly int channels;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MediaFoundationAudioReader(string path, int? sampleRate, int maxChannels)
    {
        MF.EnsureStarted();
        MF.Check(MF.MFCreateSourceReaderFromURL(path, null, out reader), "Ses dosyası açma");
        stream = uint.MaxValue;
        for (uint i = 0; i < 32; i++)
        {
            if (reader.GetNativeMediaType(i, 0, out var type) < 0) break;
            type.GetMajorType(out var major);
            MF.Release(type);
            if (major == MF.MFMediaType_Audio) { stream = i; break; }
        }
        if (stream == uint.MaxValue)
        {
            MF.Release(reader);
            reader = null;
            throw new NoAudioException();
        }
        reader.SetStreamSelection(MF.MF_SOURCE_READER_ALL_STREAMS, 0);
        reader.SetStreamSelection(stream, 1);

        reader.GetNativeMediaType(stream, 0, out var native);
        native.GetUINT32(ref MF.MF_MT_AUDIO_NUM_CHANNELS, out var nativeChannels);
        native.GetUINT32(ref MF.MF_MT_AUDIO_SAMPLES_PER_SECOND, out var nativeRate);
        MF.Release(native);

        // Ask Windows for the exact PCM we want; fall back to "any 16-bit PCM" and convert channels ourselves.
        var wantChannels = (int)Math.Clamp(nativeChannels == 0 ? 2 : nativeChannels, 1, (uint)maxChannels);
        var wantRate = sampleRate ?? (int)(nativeRate == 0 ? 48000 : nativeRate);
        if (!TrySetPcm(wantRate, wantChannels) && !TrySetPcm(null, null)) throw new IOException("PCM çıkışı ayarlanamadı.");

        reader.GetCurrentMediaType(stream, out var current);
        current.GetUINT32(ref MF.MF_MT_AUDIO_SAMPLES_PER_SECOND, out var rate);
        current.GetUINT32(ref MF.MF_MT_AUDIO_NUM_CHANNELS, out var actual);
        MF.Release(current);
        sourceChannels = (int)Math.Max(1, actual);
        channels = Math.Min(sourceChannels, maxChannels);
        Format = new AudioFormat((int)rate, channels);
        if (reader.GetPresentationAttribute(MF.MF_SOURCE_READER_MEDIASOURCE, ref MF.MF_PD_DURATION, out var duration) >= 0)
            Duration = duration.value / 10_000_000.0;
    }

    bool TrySetPcm(int? rate, int? count)
    {
        MF.Check(MF.MFCreateMediaType(out var wanted), "MFCreateMediaType");
        wanted.SetGUID(ref MF.MF_MT_MAJOR_TYPE, ref MF.MFMediaType_Audio);
        wanted.SetGUID(ref MF.MF_MT_SUBTYPE, ref MF.MFAudioFormat_PCM);
        wanted.SetUINT32(ref MF.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        if (rate is { } r && count is { } c)
        {
            wanted.SetUINT32(ref MF.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)r);
            wanted.SetUINT32(ref MF.MF_MT_AUDIO_NUM_CHANNELS, (uint)c);
            wanted.SetUINT32(ref MF.MF_MT_AUDIO_BLOCK_ALIGNMENT, (uint)(c * 2));
            wanted.SetUINT32(ref MF.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)(r * c * 2));
        }
        var hr = reader!.SetCurrentMediaType(stream, IntPtr.Zero, wanted);
        MF.Release(wanted);
        return hr >= 0;
    }

    public override unsafe byte[]? Read()
    {
        while (true)
        {
            MF.Check(reader!.ReadSample(stream, 0, out _, out var flags, out _, out var sample), "ReadSample");
            if ((flags & MF.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
            {
                MF.Release(sample);
                return null;
            }
            if (sample is null) continue;
            try
            {
                MF.Check(sample.ConvertToContiguousBuffer(out var buffer), "ConvertToContiguousBuffer");
                try
                {
                    MF.Check(buffer.Lock(out var pointer, out _, out var length), "Lock");
                    try
                    {
                        var data = new byte[length];
                        new ReadOnlySpan<byte>((void*)pointer, (int)length).CopyTo(data);
                        return channels == sourceChannels ? data : AudioFiles.MixDown(data, sourceChannels, channels);
                    }
                    finally { buffer.Unlock(); }
                }
                finally { MF.Release(buffer); }
            }
            finally { MF.Release(sample); }
        }
    }

    public override void Dispose()
    {
        MF.Release(reader);
        reader = null;
    }
}

sealed class MemoryAudioReader : AudioReader
{
    readonly byte[] data;
    int position;

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public MemoryAudioReader(AudioFormat source, byte[] pcm, int? sampleRate, int maxChannels)
    {
        var channels = Math.Min(source.Channels, maxChannels);
        var mixed = channels == source.Channels ? pcm : AudioFiles.MixDown(pcm, source.Channels, channels);
        var rate = sampleRate ?? source.SampleRate;
        data = rate == source.SampleRate ? mixed : AudioFiles.Resample(mixed, channels, source.SampleRate, rate);
        Format = new AudioFormat(rate, channels);
        Duration = data.Length / (double)(Format.BlockAlign * rate);
    }

    public override byte[]? Read()
    {
        if (position >= data.Length) return null;
        var count = Math.Min(data.Length - position, Format.BlockAlign * Format.SampleRate / 2);
        var chunk = data.AsSpan(position, count).ToArray();
        position += count;
        return chunk;
    }
}
