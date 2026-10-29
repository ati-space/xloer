namespace FastShot.Core.Media;

/// <summary>Sample rate and channel count of 16-bit PCM audio.</summary>
public sealed record AudioFormat(int SampleRate, int Channels)
{
    public static readonly AudioFormat Standard = new(48000, 2);
    public int BlockAlign => Channels * 2;
}

/// <summary>Encodes top-down BGRA frames into an H.264 or HEVC MP4 (plus optional AAC audio)
/// with the Media Foundation sink writer. Thread-safe: video and audio may arrive from different threads.</summary>
public sealed unsafe class VideoWriter : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public int FramesPerSecond { get; }
    public bool HEVC { get; }
    public AudioFormat? Audio { get; }

    IMFSinkWriter? writer;
    readonly uint videoStream;
    readonly uint audioStream;
    readonly object gate = new();
    bool finished;
    long lastVideoTime = -1;

    public VideoWriter(string path, int width, int height, int framesPerSecond, bool hevc = false, int? bitrate = null, AudioFormat? audio = null)
    {
        if (width % 2 != 0 || height % 2 != 0) throw new ArgumentException("Video boyutları çift sayı olmalı.");
        MF.EnsureStarted();
        Width = width;
        Height = height;
        FramesPerSecond = Math.Max(1, framesPerSecond);

        MF.Check(MF.MFCreateAttributes(out var attributes, 2), "MFCreateAttributes");
        attributes.SetUINT32(ref MF.MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS, 1);
        attributes.SetUINT32(ref MF.MF_SINK_WRITER_DISABLE_THROTTLING, 1);
        MF.Check(MF.MFCreateSinkWriterFromURL(path, IntPtr.Zero, attributes, out writer), "Video dosyası oluşturma");
        MF.Release(attributes);

        var bits = bitrate ?? DefaultBitrate(width, height, FramesPerSecond);
        MF.Check(MF.MFCreateMediaType(out var output), "MFCreateMediaType");
        output.SetGUID(ref MF.MF_MT_MAJOR_TYPE, ref MF.MFMediaType_Video);
        var subtype = hevc ? MF.MFVideoFormat_HEVC : MF.MFVideoFormat_H264;
        output.SetGUID(ref MF.MF_MT_SUBTYPE, ref subtype);
        output.SetUINT32(ref MF.MF_MT_AVG_BITRATE, (uint)bits);
        output.SetUINT32(ref MF.MF_MT_INTERLACE_MODE, MF.MFVideoInterlace_Progressive);
        output.SetUINT64(ref MF.MF_MT_FRAME_SIZE, MF.Pack((uint)width, (uint)height));
        output.SetUINT64(ref MF.MF_MT_FRAME_RATE, MF.Pack((uint)FramesPerSecond, 1));
        output.SetUINT64(ref MF.MF_MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
        SetColorInfo(output);
        var hr = writer.AddStream(output, out videoStream);
        HEVC = hevc;
        if (hr < 0 && hevc)
        {
            // No HEVC encoder on this machine: fall back to H.264.
            var h264 = MF.MFVideoFormat_H264;
            output.SetGUID(ref MF.MF_MT_SUBTYPE, ref h264);
            HEVC = false;
            hr = writer.AddStream(output, out videoStream);
        }
        MF.Release(output);
        MF.Check(hr, "Video kodlayıcı");

        MF.Check(MF.MFCreateMediaType(out var input), "MFCreateMediaType");
        input.SetGUID(ref MF.MF_MT_MAJOR_TYPE, ref MF.MFMediaType_Video);
        input.SetGUID(ref MF.MF_MT_SUBTYPE, ref MF.MFVideoFormat_NV12);
        input.SetUINT32(ref MF.MF_MT_INTERLACE_MODE, MF.MFVideoInterlace_Progressive);
        input.SetUINT64(ref MF.MF_MT_FRAME_SIZE, MF.Pack((uint)width, (uint)height));
        input.SetUINT64(ref MF.MF_MT_FRAME_RATE, MF.Pack((uint)FramesPerSecond, 1));
        input.SetUINT64(ref MF.MF_MT_PIXEL_ASPECT_RATIO, MF.Pack(1, 1));
        input.SetUINT32(ref MF.MF_MT_DEFAULT_STRIDE, (uint)width);
        SetColorInfo(input);
        hr = writer.SetInputMediaType(videoStream, input, null);
        MF.Release(input);
        MF.Check(hr, "Video giriş biçimi");

        if (audio is not null && TryAddAudio(audio, out audioStream)) Audio = audio;

        MF.Check(writer.BeginWriting(), "Yazmaya başlama");
    }

    static void SetColorInfo(IMFMediaType type)
    {
        type.SetUINT32(ref MF.MF_MT_YUV_MATRIX, MF.MFVideoTransferMatrix_BT709);
        type.SetUINT32(ref MF.MF_MT_VIDEO_NOMINAL_RANGE, MF.MFNominalRange_16_235);
        type.SetUINT32(ref MF.MF_MT_VIDEO_PRIMARIES, MF.MFVideoPrimaries_BT709);
        type.SetUINT32(ref MF.MF_MT_TRANSFER_FUNCTION, MF.MFVideoTransFunc_709);
    }

    bool TryAddAudio(AudioFormat format, out uint stream)
    {
        stream = 0;
        if (format.SampleRate is not (44100 or 48000) || format.Channels is < 1 or > 2) return false;
        MF.Check(MF.MFCreateMediaType(out var output), "MFCreateMediaType");
        output.SetGUID(ref MF.MF_MT_MAJOR_TYPE, ref MF.MFMediaType_Audio);
        output.SetGUID(ref MF.MF_MT_SUBTYPE, ref MF.MFAudioFormat_AAC);
        output.SetUINT32(ref MF.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        output.SetUINT32(ref MF.MF_MT_AUDIO_SAMPLES_PER_SECOND, (uint)format.SampleRate);
        output.SetUINT32(ref MF.MF_MT_AUDIO_NUM_CHANNELS, (uint)format.Channels);
        output.SetUINT32(ref MF.MF_MT_AUDIO_AVG_BYTES_PER_SECOND, format.Channels == 2 ? 24000u : 16000u);
        var hr = writer!.AddStream(output, out stream);
        MF.Release(output);
        if (hr < 0) return false;

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
        return hr >= 0;
    }

    public static int DefaultBitrate(int width, int height, int fps)
    {
        // Roughly 0.12 bits per pixel per frame; 1080p60 lands near 15 Mbps.
        var bits = (long)width * height * fps * 0.12;
        return (int)Math.Clamp(bits, 1_000_000, 60_000_000);
    }

    /// <summary>Writes one top-down BGRA frame from unmanaged memory; time and duration in seconds.</summary>
    public void WriteFrame(IntPtr bgra, int stride, double time, double duration)
    {
        var length = (uint)(Width * Height * 3 / 2);
        MF.Check(MF.MFCreateMemoryBuffer(length, out var buffer), "MFCreateMemoryBuffer");
        try
        {
            MF.Check(buffer.Lock(out var pointer, out _, out _), "Lock");
            try { PixelConvert.BgraToNv12(bgra, stride, Width, Height, pointer); }
            finally { buffer.Unlock(); }
            buffer.SetCurrentLength(length);
            WriteBuffer(videoStream, buffer, time, duration, video: true);
        }
        finally { MF.Release(buffer); }
    }

    public void WriteFrame(byte[] bgra, int stride, double time, double duration)
    {
        fixed (byte* pointer = bgra) WriteFrame((IntPtr)pointer, stride, time, duration);
    }

    /// <summary>Writes interleaved 16-bit PCM in <see cref="Audio"/>'s format.</summary>
    public void WriteAudio(ReadOnlySpan<byte> pcm, double time)
    {
        if (Audio is null || pcm.Length == 0) return;
        var length = (uint)pcm.Length;
        MF.Check(MF.MFCreateMemoryBuffer(length, out var buffer), "MFCreateMemoryBuffer");
        try
        {
            MF.Check(buffer.Lock(out var pointer, out _, out _), "Lock");
            pcm.CopyTo(new Span<byte>((void*)pointer, pcm.Length));
            buffer.Unlock();
            buffer.SetCurrentLength(length);
            var frames = pcm.Length / Audio.BlockAlign;
            WriteBuffer(audioStream, buffer, time, frames / (double)Audio.SampleRate, video: false);
        }
        finally { MF.Release(buffer); }
    }

    void WriteBuffer(uint stream, IMFMediaBuffer buffer, double time, double duration, bool video)
    {
        MF.Check(MF.MFCreateSample(out var sample), "MFCreateSample");
        try
        {
            sample.AddBuffer(buffer);
            var ticks = (long)Math.Round(Math.Max(0, time) * 10_000_000);
            lock (gate)
            {
                if (writer is null || finished) throw new InvalidOperationException("Yazıcı kapandı.");
                if (video)
                {
                    if (ticks <= lastVideoTime) ticks = lastVideoTime + 1;
                    lastVideoTime = ticks;
                }
                sample.SetSampleTime(ticks);
                sample.SetSampleDuration(Math.Max(1, (long)Math.Round(duration * 10_000_000)));
                MF.Check(writer.WriteSample(stream, sample), "WriteSample");
            }
        }
        finally { MF.Release(sample); }
    }

    public void Finish()
    {
        lock (gate)
        {
            if (writer is null || finished) return;
            finished = true;
            var hr = writer.Finalize();
            MF.Release(writer);
            writer = null;
            MF.Check(hr, "Video dosyasını tamamlama");
        }
    }

    public void Dispose()
    {
        try { Finish(); } catch { /* best effort on dispose */ }
    }
}
