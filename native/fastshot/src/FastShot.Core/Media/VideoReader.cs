using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace FastShot.Core.Media;

/// <summary>One decoded frame, top-down BGRA.</summary>
public sealed class VideoFrame
{
    public required byte[] Bgra { get; init; }
    public required int Width { get; init; }
    public required int Height { get; init; }
    public int Stride => Width * 4;
    /// <summary>Presentation time in seconds.</summary>
    public required double Time { get; init; }
    public required double Duration { get; init; }

    public BitmapSource ToBitmap() => ImageExporter.FromBgra(Bgra, Width, Height, Stride);
}

/// <summary>A run of decoded interleaved 16-bit PCM.</summary>
public sealed class AudioChunk
{
    public required byte[] Pcm { get; init; }
    public required double Time { get; init; }
    public required AudioFormat Format { get; init; }
    public int Frames => Pcm.Length / Format.BlockAlign;
    public double Duration => Frames / (double)Format.SampleRate;
}

/// <summary>Decodes a video file with the Media Foundation source reader: frames as BGRA and,
/// when asked for, the first audio track as 16-bit PCM. Not thread-safe: one reader per thread.</summary>
public sealed unsafe class VideoReader : IDisposable
{
    IMFSourceReader? reader;
    int codedWidth, codedHeight, stride;
    int offsetX, offsetY;
    uint videoIndex = uint.MaxValue, audioIndex = uint.MaxValue;

    public int Width { get; private set; }
    public int Height { get; private set; }
    public double Duration { get; private set; }
    public double FrameRate { get; private set; } = 30;
    public double FrameDuration => FrameRate > 0 ? 1 / FrameRate : 1 / 30.0;
    /// <summary>Set when the reader was opened with audio and the file has a decodable audio track.</summary>
    public AudioFormat? Audio { get; private set; }
    /// <summary>True when the file has any audio track, whether or not it is being decoded.</summary>
    public bool HasAudioTrack { get; private set; }

    public VideoReader(string path, bool audio = false)
    {
        MF.EnsureStarted();
        MF.Check(MF.MFCreateAttributes(out var attributes, 1), "MFCreateAttributes");
        attributes.SetUINT32(ref MF.MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, 1);
        var hr = MF.MFCreateSourceReaderFromURL(path, attributes, out reader);
        MF.Release(attributes);
        MF.Check(hr, "Video dosyası açma");

        // Map stream indices to media types.
        for (uint i = 0; i < 32; i++)
        {
            if (reader.GetNativeMediaType(i, 0, out var type) < 0) break;
            type.GetMajorType(out var major);
            if (major == MF.MFMediaType_Video && videoIndex == uint.MaxValue) videoIndex = i;
            if (major == MF.MFMediaType_Audio && audioIndex == uint.MaxValue) audioIndex = i;
            MF.Release(type);
        }
        if (videoIndex == uint.MaxValue) throw new MediaException("Dosyada video bulunamadı.", 0);
        HasAudioTrack = audioIndex != uint.MaxValue;

        reader.SetStreamSelection(MF.MF_SOURCE_READER_ALL_STREAMS, 0);
        MF.Check(reader.SetStreamSelection(videoIndex, 1), "SetStreamSelection");

        MF.Check(reader.GetNativeMediaType(videoIndex, 0, out var native), "GetNativeMediaType");
        if (native.GetUINT64(ref MF.MF_MT_FRAME_RATE, out var rate) >= 0)
        {
            var (num, den) = MF.Unpack(rate);
            if (den > 0 && num > 0) FrameRate = (double)num / den;
        }
        MF.Release(native);

        MF.Check(MF.MFCreateMediaType(out var wanted), "MFCreateMediaType");
        wanted.SetGUID(ref MF.MF_MT_MAJOR_TYPE, ref MF.MFMediaType_Video);
        wanted.SetGUID(ref MF.MF_MT_SUBTYPE, ref MF.MFVideoFormat_NV12);
        hr = reader.SetCurrentMediaType(videoIndex, IntPtr.Zero, wanted);
        MF.Release(wanted);
        MF.Check(hr, "Video çözücü");
        ReadLayout();

        if (audio && HasAudioTrack) ConfigureAudio();

        if (reader.GetPresentationAttribute(MF.MF_SOURCE_READER_MEDIASOURCE, ref MF.MF_PD_DURATION, out var duration) >= 0)
            Duration = duration.value / 10_000_000.0;
    }

    void ConfigureAudio()
    {
        MF.Check(MF.MFCreateMediaType(out var wanted), "MFCreateMediaType");
        wanted.SetGUID(ref MF.MF_MT_MAJOR_TYPE, ref MF.MFMediaType_Audio);
        wanted.SetGUID(ref MF.MF_MT_SUBTYPE, ref MF.MFAudioFormat_PCM);
        wanted.SetUINT32(ref MF.MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
        var hr = reader!.SetCurrentMediaType(audioIndex, IntPtr.Zero, wanted);
        MF.Release(wanted);
        if (hr < 0) return;
        if (reader.GetCurrentMediaType(audioIndex, out var current) < 0) return;
        current.GetUINT32(ref MF.MF_MT_AUDIO_SAMPLES_PER_SECOND, out var sampleRate);
        current.GetUINT32(ref MF.MF_MT_AUDIO_NUM_CHANNELS, out var channels);
        current.GetUINT32(ref MF.MF_MT_AUDIO_BITS_PER_SAMPLE, out var bits);
        MF.Release(current);
        if (bits != 16 || sampleRate == 0 || channels == 0) return;
        if (reader.SetStreamSelection(audioIndex, 1) < 0) return;
        Audio = new AudioFormat((int)sampleRate, (int)channels);
    }

    void ReadLayout()
    {
        MF.Check(reader!.GetCurrentMediaType(videoIndex, out var current), "GetCurrentMediaType");
        MF.Check(current.GetUINT64(ref MF.MF_MT_FRAME_SIZE, out var size), "MF_MT_FRAME_SIZE");
        var (w, h) = MF.Unpack(size);
        codedWidth = (int)w;
        codedHeight = (int)h;
        stride = current.GetUINT32(ref MF.MF_MT_DEFAULT_STRIDE, out var s) >= 0 && (int)s > 0 ? (int)s : codedWidth;
        Width = codedWidth;
        Height = codedHeight;
        offsetX = offsetY = 0;
        if (current.GetBlobSize(ref MF.MF_MT_MINIMUM_DISPLAY_APERTURE, out var blobSize) >= 0 && blobSize >= 16)
        {
            var memory = Marshal.AllocHGlobal((int)blobSize);
            try
            {
                if (current.GetBlob(ref MF.MF_MT_MINIMUM_DISPLAY_APERTURE, memory, blobSize, out _) >= 0)
                {
                    var area = Marshal.PtrToStructure<VideoArea>(memory);
                    if (area.Width > 0 && area.Height > 0 && area.Width <= codedWidth && area.Height <= codedHeight)
                    {
                        Width = area.Width;
                        Height = area.Height;
                        offsetX = Math.Max(0, (int)area.OffsetX);
                        offsetY = Math.Max(0, (int)area.OffsetY);
                    }
                }
            }
            finally { Marshal.FreeHGlobal(memory); }
        }
        Width &= ~1;
        Height &= ~1;
        MF.Release(current);
    }

    /// <summary>Jumps to a time; the next read returns the first decodable frame at or before it.</summary>
    public void Seek(double seconds)
    {
        var position = PropVariant.Int64((long)Math.Round(Math.Max(0, seconds) * 10_000_000));
        MF.Check(reader!.SetCurrentPosition(ref MF.GUID_NULL, ref position), "SetCurrentPosition");
    }

    /// <summary>Reads the next video frame, or null at the end of the stream.</summary>
    public VideoFrame? ReadFrame()
    {
        while (true)
        {
            MF.Check(reader!.ReadSample(videoIndex, 0, out _, out var flags, out var timestamp, out var sample), "ReadSample");
            if ((flags & MF.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0) ReadLayout();
            if ((flags & MF.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
            {
                MF.Release(sample);
                return null;
            }
            if (sample is null) continue;
            try { return DecodeVideo(sample, timestamp); }
            finally { MF.Release(sample); }
        }
    }

    /// <summary>Reads whatever comes next in file order: a VideoFrame, an AudioChunk, or null at the end of every stream.</summary>
    public object? ReadNext()
    {
        while (true)
        {
            MF.Check(reader!.ReadSample(MF.MF_SOURCE_READER_ANY_STREAM, 0, out var streamIndex, out var flags, out var timestamp, out var sample), "ReadSample");
            if (streamIndex == videoIndex && (flags & MF.MF_SOURCE_READERF_CURRENTMEDIATYPECHANGED) != 0) ReadLayout();
            if ((flags & MF.MF_SOURCE_READERF_ENDOFSTREAM) != 0)
            {
                MF.Release(sample);
                // One stream ended; deselect it and keep reading the other.
                reader.SetStreamSelection(streamIndex, 0);
                if (streamIndex == videoIndex) videoIndex = uint.MaxValue - 1;
                if (streamIndex == audioIndex) audioIndex = uint.MaxValue - 1;
                var videoOpen = videoIndex < uint.MaxValue - 1;
                var audioOpen = Audio is not null && audioIndex < uint.MaxValue - 1;
                if (!videoOpen && !audioOpen) return null;
                continue;
            }
            if (sample is null) continue;
            try
            {
                if (streamIndex == videoIndex) return DecodeVideo(sample, timestamp);
                if (streamIndex == audioIndex && Audio is not null) return DecodeAudio(sample, timestamp);
            }
            finally { MF.Release(sample); }
        }
    }

    AudioChunk DecodeAudio(IMFSample sample, long timestamp)
    {
        MF.Check(sample.ConvertToContiguousBuffer(out var buffer), "ConvertToContiguousBuffer");
        try
        {
            MF.Check(buffer.Lock(out var pointer, out _, out var length), "Lock");
            try
            {
                var pcm = new byte[length - length % Audio!.BlockAlign];
                new ReadOnlySpan<byte>((void*)pointer, pcm.Length).CopyTo(pcm);
                return new AudioChunk { Pcm = pcm, Time = timestamp / 10_000_000.0, Format = Audio };
            }
            finally { buffer.Unlock(); }
        }
        finally { MF.Release(buffer); }
    }

    VideoFrame DecodeVideo(IMFSample sample, long timestamp)
    {
        sample.GetSampleDuration(out var duration);
        var bgra = new byte[Width * Height * 4];
        var decoded = false;
        // Prefer the 2D buffer: it reports the real pitch of hardware-aligned surfaces.
        if (sample.GetBufferCount(out var count) >= 0 && count == 1 && sample.GetBufferByIndex(0, out var first) >= 0)
        {
            try
            {
                if (first is IMF2DBuffer surface && surface.Lock2D(out var scan0, out var pitch) >= 0)
                {
                    try
                    {
                        if (pitch > 0)
                        {
                            var y = scan0 + offsetY * pitch + offsetX;
                            var uv = scan0 + codedHeight * pitch + (offsetY / 2) * pitch + offsetX;
                            PixelConvert.Nv12ToBgra(y, pitch, uv, pitch, Width, Height, bgra);
                            decoded = true;
                        }
                    }
                    finally { surface.Unlock2D(); }
                }
            }
            finally { MF.Release(first); }
        }
        if (!decoded)
        {
            MF.Check(sample.ConvertToContiguousBuffer(out var buffer), "ConvertToContiguousBuffer");
            try
            {
                MF.Check(buffer.Lock(out var pointer, out _, out var length), "Lock");
                try
                {
                    var rowStride = stride > 0 ? stride : codedWidth;
                    var planeRows = rowStride > 0 ? Math.Min(codedHeight, (int)(length * 2 / 3 / rowStride)) : codedHeight;
                    var y = pointer + offsetY * rowStride + offsetX;
                    var uv = pointer + planeRows * rowStride + (offsetY / 2) * rowStride + offsetX;
                    PixelConvert.Nv12ToBgra(y, rowStride, uv, rowStride, Width, Height, bgra);
                }
                finally { buffer.Unlock(); }
            }
            finally { MF.Release(buffer); }
        }
        return new VideoFrame
        {
            Bgra = bgra, Width = Width, Height = Height,
            Time = timestamp / 10_000_000.0,
            Duration = duration > 0 ? duration / 10_000_000.0 : FrameDuration,
        };
    }

    /// <summary>The frame shown at <paramref name="seconds"/>: seeks to the previous keyframe and decodes forward.</summary>
    public VideoFrame? FrameAt(double seconds)
    {
        var target = Math.Max(0, Math.Min(seconds, Math.Max(0, Duration - 0.001)));
        Seek(target);
        VideoFrame? last = null;
        while (true)
        {
            var frame = ReadFrame();
            if (frame is null) return last;
            if (frame.Time + frame.Duration > target + 0.0005) return frame;
            last = frame;
        }
    }

    public void Dispose()
    {
        MF.Release(reader);
        reader = null;
    }
}
