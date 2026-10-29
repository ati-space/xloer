using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FastShot;

/// <summary>Sums audio from several sources onto one 48 kHz stereo timeline. Each source writes its
/// samples at the index its capture timestamp gives, so a source that goes quiet (loopback sends
/// nothing while nothing plays) leaves silence instead of pulling later sound forward.</summary>
public sealed class AudioMixer
{
    public const int Rate = 48000;
    const int Capacity = Rate * 10;
    readonly float[] ring = new float[Capacity * 2];
    readonly object gate = new();
    long flushed;

    public long Flushed { get { lock (gate) return flushed; } }

    public void Add(long index, float[] stereo, int frames)
    {
        lock (gate)
        {
            for (var i = 0; i < frames; i++)
            {
                var at = index + i;
                if (at < flushed) continue;
                if (at >= flushed + Capacity) break;
                var position = (int)(at % Capacity) * 2;
                ring[position] += stereo[i * 2];
                ring[position + 1] += stereo[i * 2 + 1];
            }
        }
    }

    /// <summary>Takes everything before <paramref name="until"/> as 16-bit PCM, with the time of its first sample.</summary>
    public (byte[] pcm, double time) Flush(long until)
    {
        lock (gate)
        {
            var time = flushed / (double)Rate;
            if (until <= flushed) return ([], time);
            var frames = (int)Math.Min(until - flushed, Capacity);
            var pcm = new byte[frames * 4];
            for (var i = 0; i < frames; i++)
            {
                var position = (int)((flushed + i) % Capacity) * 2;
                for (var channel = 0; channel < 2; channel++)
                {
                    var value = (short)Math.Clamp(ring[position + channel] * 32767f, short.MinValue, short.MaxValue);
                    pcm[i * 4 + channel * 2] = (byte)(value & 0xFF);
                    pcm[i * 4 + channel * 2 + 1] = (byte)((value >> 8) & 0xFF);
                    ring[position + channel] = 0;
                }
            }
            flushed += frames;
            return (pcm, time);
        }
    }
}

/// <summary>One WASAPI endpoint in shared mode: the default speakers in loopback ("what you hear")
/// or the default microphone. Packets are converted to 48 kHz stereo float and handed to the mixer.</summary>
public sealed class AudioSource : IDisposable
{
    public enum Kind { System, Microphone }

    readonly Kind kind;
    readonly AudioMixer mixer;
    readonly long start100ns;
    readonly Thread thread;
    readonly ManualResetEventSlim ready = new(false);
    volatile bool stopping;
    string? error;

    AudioSource(Kind kind, AudioMixer mixer, long start100ns)
    {
        this.kind = kind;
        this.mixer = mixer;
        this.start100ns = start100ns;
        thread = new Thread(Run) { IsBackground = true, Name = $"FastShot audio {kind}" };
    }

    /// <summary>Starts capturing; returns null with a reason when the device cannot be opened.</summary>
    public static AudioSource? TryStart(Kind kind, AudioMixer mixer, long start100ns, out string? error)
    {
        var source = new AudioSource(kind, mixer, start100ns);
        source.thread.Start();
        source.ready.Wait(TimeSpan.FromSeconds(3));
        error = source.error;
        if (error is null) return source;
        source.Dispose();
        return null;
    }

    public static long Now100ns() => (long)(Stopwatch.GetTimestamp() * (10_000_000.0 / Stopwatch.Frequency));

    void Run()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        IAudioClient? client = null;
        IAudioCaptureClient? capture = null;
        var format = IntPtr.Zero;
        var ownsFormat = false;
        try
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E"))!)!;
            Check(enumerator.GetDefaultAudioEndpoint(kind == Kind.System ? 0 : 1, 0, out device), kind == Kind.System ? "Ses çıkışı bulunamadı." : "Mikrofon bulunamadı.");
            var iid = typeof(IAudioClient).GUID;
            Check(device!.Activate(ref iid, 23, IntPtr.Zero, out var unknown), "Ses aygıtı açılamadı.");
            client = (IAudioClient)unknown;

            const uint loopback = 0x00020000, autoConvert = 0x80000000, srcDefaultQuality = 0x08000000;
            var flags = kind == Kind.System ? loopback : 0;
            // Ask for 48 kHz stereo float and let Windows convert; fall back to the device's own mix format.
            format = WaveFormat.Allocate(AudioMixer.Rate, 2);
            ownsFormat = true;
            var hr = client.Initialize(0, flags | autoConvert | srcDefaultQuality, 10_000_000, 0, format, IntPtr.Zero);
            if (hr < 0)
            {
                Marshal.FreeHGlobal(format);
                ownsFormat = false;
                Check(client.GetMixFormat(out format), "Ses biçimi okunamadı.");
                hr = client.Initialize(0, flags, 10_000_000, 0, format, IntPtr.Zero);
            }
            if (hr == unchecked((int)0x80070005)) throw new UnauthorizedAccessException("Windows, FastShot'ın mikrofona erişmesine izin vermiyor (Ayarlar › Gizlilik ve güvenlik › Mikrofon).");
            Check(hr, "Ses kaydı başlatılamadı.");
            var info = WaveFormat.Read(format);
            var serviceId = typeof(IAudioCaptureClient).GUID;
            Check(client.GetService(ref serviceId, out var service), "Ses kaydı başlatılamadı.");
            capture = (IAudioCaptureClient)service;
            Check(client.Start(), "Ses kaydı başlatılamadı.");
            ready.Set();

            var resampler = new Resampler(info.SampleRate, AudioMixer.Rate);
            long? nextIndex = null;
            while (!stopping)
            {
                Thread.Sleep(10);
                while (!stopping && capture.GetNextPacketSize(out var packet) >= 0 && packet > 0)
                {
                    if (capture.GetBuffer(out var data, out var frames, out var bufferFlags, out _, out var qpc) < 0) break;
                    try
                    {
                        var stereo = info.ToStereo(data, (int)frames, (bufferFlags & 2) != 0);
                        var (output, count) = resampler.Process(stereo, (int)frames);
                        var expected = ((long)qpc - start100ns) * AudioMixer.Rate / 10_000_000;
                        var discontinuity = (bufferFlags & 1) != 0;
                        if (nextIndex is not { } index || discontinuity || Math.Abs(expected - index) > AudioMixer.Rate / 20) nextIndex = expected;
                        if (count > 0) mixer.Add(nextIndex.Value, output, count);
                        nextIndex += count;
                    }
                    finally
                    {
                        capture.ReleaseBuffer(frames);
                    }
                }
            }
            client.Stop();
        }
        catch (Exception failure)
        {
            error ??= failure.Message;
            ready.Set();
        }
        finally
        {
            if (format != IntPtr.Zero)
            {
                if (ownsFormat) Marshal.FreeHGlobal(format); else Marshal.FreeCoTaskMem(format);
            }
            foreach (var com in new object?[] { capture, client, device, enumerator })
                if (com is not null && Marshal.IsComObject(com)) Marshal.ReleaseComObject(com);
        }
    }

    void Check(int hr, string message)
    {
        if (hr >= 0) return;
        error = message;
        throw new InvalidOperationException(message);
    }

    public void Dispose()
    {
        stopping = true;
        if (thread.IsAlive) thread.Join(2000);
    }

    /// <summary>The device format, and conversion of its packets to interleaved stereo float.</summary>
    sealed record WaveFormat(int SampleRate, int Channels, int Bits, bool Float, int BlockAlign)
    {
        static readonly Guid floatFormat = new("00000003-0000-0010-8000-00aa00389b71");

        public static IntPtr Allocate(int rate, int channels)
        {
            var memory = Marshal.AllocHGlobal(40);
            Marshal.WriteInt16(memory, 0, unchecked((short)0xFFFE));
            Marshal.WriteInt16(memory, 2, (short)channels);
            Marshal.WriteInt32(memory, 4, rate);
            Marshal.WriteInt32(memory, 8, rate * channels * 4);
            Marshal.WriteInt16(memory, 12, (short)(channels * 4));
            Marshal.WriteInt16(memory, 14, 32);
            Marshal.WriteInt16(memory, 16, 22);
            Marshal.WriteInt16(memory, 18, 32);
            Marshal.WriteInt32(memory, 20, channels == 2 ? 3 : 4);
            var guid = floatFormat.ToByteArray();
            Marshal.Copy(guid, 0, memory + 24, 16);
            return memory;
        }

        public static WaveFormat Read(IntPtr memory)
        {
            var tag = (ushort)Marshal.ReadInt16(memory, 0);
            var channels = Marshal.ReadInt16(memory, 2);
            var rate = Marshal.ReadInt32(memory, 4);
            var blockAlign = Marshal.ReadInt16(memory, 12);
            var bits = Marshal.ReadInt16(memory, 14);
            var isFloat = tag == 3;
            if (tag == 0xFFFE)
            {
                var guid = new byte[16];
                Marshal.Copy(memory + 24, guid, 0, 16);
                isFloat = new Guid(guid) == floatFormat;
            }
            return new WaveFormat(rate, channels, bits, isFloat, blockAlign);
        }

        public unsafe float[] ToStereo(IntPtr data, int frames, bool silent)
        {
            var output = new float[frames * 2];
            if (silent || data == IntPtr.Zero) return output;
            var bytes = (byte*)data;
            var size = Bits / 8;
            for (var i = 0; i < frames; i++)
            {
                var frame = bytes + i * BlockAlign;
                var left = Sample(frame, size);
                var right = Channels > 1 ? Sample(frame + size, size) : left;
                output[i * 2] = left;
                output[i * 2 + 1] = right;
            }
            return output;
        }

        unsafe float Sample(byte* p, int size)
        {
            if (Float) return size == 8 ? (float)*(double*)p : *(float*)p;
            return size switch
            {
                2 => *(short*)p / 32768f,
                3 => ((p[0] << 8) | (p[1] << 16) | (p[2] << 24)) / 2147483648f,
                4 => *(int*)p / 2147483648f,
                _ => (p[0] - 128) / 128f,
            };
        }
    }

    /// <summary>Linear resampling of interleaved stereo; keeps its phase between packets.</summary>
    sealed class Resampler(int from, int to)
    {
        double position;
        float lastLeft, lastRight;

        public (float[] output, int frames) Process(float[] input, int frames)
        {
            if (from == to) return (input, frames);
            var step = from / (double)to;
            var capacity = (int)(frames / step) + 2;
            var output = new float[capacity * 2];
            var count = 0;
            while (count < capacity)
            {
                var index = (int)Math.Floor(position);
                if (index >= frames) break;
                var fraction = (float)(position - index);
                float l0, r0;
                if (index < 0) { l0 = lastLeft; r0 = lastRight; }
                else { l0 = input[index * 2]; r0 = input[index * 2 + 1]; }
                var next = Math.Min(index + 1, frames - 1);
                var l1 = input[next * 2];
                var r1 = input[next * 2 + 1];
                output[count * 2] = l0 + (l1 - l0) * fraction;
                output[count * 2 + 1] = r0 + (r1 - r0) * fraction;
                count++;
                position += step;
            }
            position -= frames;
            if (frames > 0)
            {
                lastLeft = input[(frames - 1) * 2];
                lastRight = input[(frames - 1) * 2 + 1];
            }
            return (output, count);
        }
    }
}

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice? endpoint);
    [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
    [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    [PreserveSig] int OpenPropertyStore(int access, out IntPtr properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}

[ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioClient
{
    [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr sessionGuid);
    [PreserveSig] int GetBufferSize(out uint frames);
    [PreserveSig] int GetStreamLatency(out long latency);
    [PreserveSig] int GetCurrentPadding(out uint padding);
    [PreserveSig] int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closest);
    [PreserveSig] int GetMixFormat(out IntPtr format);
    [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
    [PreserveSig] int Start();
    [PreserveSig] int Stop();
    [PreserveSig] int Reset();
    [PreserveSig] int SetEventHandle(IntPtr handle);
    [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
}

[ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioCaptureClient
{
    [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
    [PreserveSig] int ReleaseBuffer(uint frames);
    [PreserveSig] int GetNextPacketSize(out uint frames);
}
