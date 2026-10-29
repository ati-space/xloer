using System.IO;

namespace FastShot.Core.Media;

/// <summary>Writes 16-bit PCM into an audio file: AAC (.m4a), MP3 or FLAC through Media Foundation,
/// WAV, AIFF and CAF directly.</summary>
public abstract class AudioWriter : IDisposable
{
    public abstract void Write(ReadOnlySpan<byte> pcm);
    public abstract void Finish();
    public void Dispose() { try { Finish(); } catch { /* best effort */ } }

    /// <param name="bytesPerSecond">AAC/MP3 bit rate in bytes per second (16000 = 128 kbps); null picks a high quality.</param>
    public static AudioWriter Create(string path, string codec, AudioFormat format, int? bytesPerSecond = null) => codec switch
    {
        "wav" or "aiff" or "caf" => new PcmFileWriter(path, codec, format),
        _ => new MediaFoundationAudioWriter(path, codec, format, bytesPerSecond),
    };

    /// <summary>The sample rates each Media Foundation encoder accepts.</summary>
    public static int SupportedRate(string codec, int rate) => codec switch
    {
        "m4a" => rate <= 44100 ? 44100 : 48000,
        "mp3" => rate <= 32000 ? 32000 : rate <= 44100 ? 44100 : 48000,
        "flac" => Math.Clamp(rate, 8000, 192000),
        _ => rate,
    };

    public static bool CanEncode(string codec)
    {
        var path = Path.Combine(Path.GetTempPath(), $"fastshot-probe-{Guid.NewGuid():N}.{codec}");
        try
        {
            var format = new AudioFormat(44100, 2);
            using (var writer = new MediaFoundationAudioWriter(path, codec, format))
            {
                writer.Write(new byte[44100 * 4 / 5]);
                writer.Finish();
            }
            return new FileInfo(path).Length > 0;
        }
        catch { return false; }
        finally { Convert.FileConverter.TryDelete(path); }
    }
}

sealed class MediaFoundationAudioWriter : AudioWriter
{
    static readonly Guid mp3 = new("00000055-0000-0010-8000-00AA00389B71");
    static readonly Guid flac = new("0000F1AC-0000-0010-8000-00AA00389B71");

    IMFSinkWriter? writer;
    readonly uint stream;
    readonly AudioFormat format;
    long frames;
    bool finished;

    public MediaFoundationAudioWriter(string path, string codec, AudioFormat format, int? bytesPerSecond = null)
    {
        MF.EnsureStarted();
        this.format = format;
        MF.Check(MF.MFCreateSinkWriterFromURL(path, IntPtr.Zero, null, out writer), "Ses dosyası oluşturma");

        MF.Check(MF.MFCreateMediaType(out var output), "MFCreateMediaType");
        output.SetGUID(ref MF.MF_MT_MAJOR_TYPE, ref MF.MFMediaType_Audio);
        var subtype = codec switch { "mp3" => mp3, "flac" => flac, _ => MF.MFAudioFormat_AAC };
        output.SetGUID(ref MF.MF_MT_SUBTYPE, ref subtype);
        output.SetUINT32(ref MF.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)format.SampleRate);
        output.SetUINT32(ref MF.MF_MT_AUDIO_NUM_CHANNELS, (uint)format.Channels);
        switch (codec)
        {
            case "m4a":
                output.SetUINT32(ref MF.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                output.SetUINT32(ref MF.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)(bytesPerSecond ?? (format.Channels > 1 ? 24000 : 16000)));
                break;
            case "mp3":
                output.SetUINT32(ref MF.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)(bytesPerSecond ?? (format.Channels > 1 ? 24000 : 16000)));
                output.SetUINT32(ref MF.MF_MT_AUDIO_BLOCK_ALIGNMENT, 1);
                break;
            case "flac":
                output.SetUINT32(ref MF.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
                break;
        }
        var hr = writer.AddStream(output, out stream);
        MF.Release(output);
        MF.Check(hr, "Ses kodlayıcı");

        MF.Check(MF.MFCreateMediaType(out var input), "MFCreateMediaType");
        input.SetGUID(ref MF.MF_MT_MAJOR_TYPE, ref MF.MFMediaType_Audio);
        input.SetGUID(ref MF.MF_MT_SUBTYPE, ref MF.MFAudioFormat_PCM);
        input.SetUINT32(ref MF.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        input.SetUINT32(ref MF.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)format.SampleRate);
        input.SetUINT32(ref MF.MF_MT_AUDIO_NUM_CHANNELS, (uint)format.Channels);
        input.SetUINT32(ref MF.MF_MT_AUDIO_BLOCK_ALIGNMENT, (uint)format.BlockAlign);
        input.SetUINT32(ref MF.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, (uint)(format.SampleRate * format.BlockAlign));
        input.SetUINT32(ref MF.MF_MT_ALL_SAMPLES_INDEPENDENT, 1);
        hr = writer.SetInputMediaType(stream, input, null);
        MF.Release(input);
        MF.Check(hr, "Ses giriş biçimi");
        MF.Check(writer.BeginWriting(), "Yazmaya başlama");
    }

    public override unsafe void Write(ReadOnlySpan<byte> pcm)
    {
        if (pcm.Length == 0) return;
        var length = pcm.Length - pcm.Length % format.BlockAlign;
        MF.Check(MF.MFCreateMemoryBuffer((uint)length, out var buffer), "MFCreateMemoryBuffer");
        try
        {
            MF.Check(buffer.Lock(out var pointer, out _, out _), "Lock");
            pcm[..length].CopyTo(new Span<byte>((void*)pointer, length));
            buffer.Unlock();
            buffer.SetCurrentLength((uint)length);
            MF.Check(MF.MFCreateSample(out var sample), "MFCreateSample");
            try
            {
                sample.AddBuffer(buffer);
                var count = length / format.BlockAlign;
                sample.SetSampleTime(frames * 10_000_000 / format.SampleRate);
                sample.SetSampleDuration((long)count * 10_000_000 / format.SampleRate);
                frames += count;
                MF.Check(writer!.WriteSample(stream, sample), "WriteSample");
            }
            finally { MF.Release(sample); }
        }
        finally { MF.Release(buffer); }
    }

    public override void Finish()
    {
        if (finished || writer is null) return;
        finished = true;
        var hr = writer.Finalize();
        MF.Release(writer);
        writer = null;
        MF.Check(hr, "Ses dosyasını tamamlama");
    }
}

/// <summary>Uncompressed PCM containers written by hand: WAV (little-endian), AIFF (big-endian), CAF.</summary>
sealed class PcmFileWriter : AudioWriter
{
    readonly FileStream file;
    readonly string codec;
    readonly AudioFormat format;
    long dataBytes;
    bool finished;

    public PcmFileWriter(string path, string codec, AudioFormat format)
    {
        this.codec = codec;
        this.format = format;
        file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        // Headers are rewritten with the real sizes in Finish.
        WriteHeader();
    }

    void WriteHeader()
    {
        file.Position = 0;
        using var header = new BinaryWriter(file, System.Text.Encoding.ASCII, leaveOpen: true);
        switch (codec)
        {
            case "wav":
                header.Write("RIFF"u8);
                header.Write((uint)Math.Min(uint.MaxValue, 36 + dataBytes));
                header.Write("WAVEfmt "u8);
                header.Write(16);
                header.Write((short)1);
                header.Write((short)format.Channels);
                header.Write(format.SampleRate);
                header.Write(format.SampleRate * format.BlockAlign);
                header.Write((short)format.BlockAlign);
                header.Write((short)16);
                header.Write("data"u8);
                header.Write((uint)Math.Min(uint.MaxValue, dataBytes));
                break;
            case "aiff":
                header.Write("FORM"u8);
                AudioFiles.WriteBig(header, (uint)(4 + 26 + 16 + dataBytes));
                header.Write("AIFF"u8);
                header.Write("COMM"u8);
                AudioFiles.WriteBig(header, 18u);
                AudioFiles.WriteBig(header, (ushort)format.Channels);
                AudioFiles.WriteBig(header, (uint)(dataBytes / format.BlockAlign));
                AudioFiles.WriteBig(header, (ushort)16);
                header.Write(AudioFiles.Extended80(format.SampleRate));
                header.Write("SSND"u8);
                AudioFiles.WriteBig(header, (uint)(8 + dataBytes));
                AudioFiles.WriteBig(header, 0u);
                AudioFiles.WriteBig(header, 0u);
                break;
            case "caf":
                header.Write("caff"u8);
                AudioFiles.WriteBig(header, (ushort)1);
                AudioFiles.WriteBig(header, (ushort)0);
                header.Write("desc"u8);
                AudioFiles.WriteBig(header, 32L);
                AudioFiles.WriteBig(header, BitConverter.DoubleToInt64Bits(format.SampleRate));
                header.Write("lpcm"u8);
                AudioFiles.WriteBig(header, 2u); // little-endian integer samples
                AudioFiles.WriteBig(header, (uint)format.BlockAlign);
                AudioFiles.WriteBig(header, 1u);
                AudioFiles.WriteBig(header, (uint)format.Channels);
                AudioFiles.WriteBig(header, 16u);
                header.Write("data"u8);
                AudioFiles.WriteBig(header, 4 + dataBytes);
                AudioFiles.WriteBig(header, 0u); // edit count
                break;
        }
    }

    public override void Write(ReadOnlySpan<byte> pcm)
    {
        if (codec == "aiff")
        {
            var swapped = pcm.ToArray();
            for (var i = 0; i + 1 < swapped.Length; i += 2) (swapped[i], swapped[i + 1]) = (swapped[i + 1], swapped[i]);
            file.Write(swapped);
        }
        else
        {
            file.Write(pcm);
        }
        dataBytes += pcm.Length;
    }

    public override void Finish()
    {
        if (finished) return;
        finished = true;
        var end = file.Position;
        WriteHeader();
        file.Position = end;
        file.Dispose();
    }
}
