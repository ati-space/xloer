using System.Media;

namespace FastShot;

public enum Sound { Shutter, RecordStart, RecordStop }

/// <summary>Short feedback sounds, synthesised once so nothing depends on files that may be missing.</summary>
public static class Sounds
{
    static readonly Dictionary<Sound, byte[]> cache = [];
    static SoundPlayer? player;

    public static void Play(Sound sound)
    {
        if (!Preferences.Shared.PlaySounds) return;
        try
        {
            if (!cache.TryGetValue(sound, out var wav)) cache[sound] = wav = Make(sound);
            player?.Dispose();
            player = new SoundPlayer(new MemoryStream(wav));
            player.Play();
        }
        catch { /* no audio device */ }
    }

    const int Rate = 44100;

    static byte[] Make(Sound sound)
    {
        var samples = sound switch
        {
            Sound.Shutter => Shutter(),
            Sound.RecordStart => Tones(660, 880),
            _ => Tones(880, 560),
        };
        return Wav(samples);
    }

    static float[] Shutter()
    {
        var random = new Random(7);
        var samples = new float[(int)(Rate * 0.16)];
        void Click(double at, double length, double gain)
        {
            var start = (int)(at * Rate);
            var count = (int)(length * Rate);
            for (var i = 0; i < count && start + i < samples.Length; i++)
            {
                var envelope = Math.Exp(-i / (double)count * 6);
                var noise = random.NextDouble() * 2 - 1;
                var body = Math.Sin(2 * Math.PI * 180 * i / Rate);
                samples[start + i] += (float)((noise * 0.7 + body * 0.3) * envelope * gain);
            }
        }
        Click(0, 0.045, 0.55);
        Click(0.075, 0.06, 0.45);
        return samples;
    }

    static float[] Tones(double first, double second)
    {
        const double length = 0.09;
        var count = (int)(Rate * length);
        var samples = new float[count * 2];
        for (var tone = 0; tone < 2; tone++)
        {
            var frequency = tone == 0 ? first : second;
            for (var i = 0; i < count; i++)
            {
                var envelope = Math.Min(1, i / 200.0) * Math.Min(1, (count - i) / 600.0);
                samples[tone * count + i] = (float)(Math.Sin(2 * Math.PI * frequency * i / Rate) * envelope * 0.32);
            }
        }
        return samples;
    }

    static byte[] Wav(float[] samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        var dataLength = samples.Length * 2;
        writer.Write("RIFF"u8);
        writer.Write(36 + dataLength);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(Rate);
        writer.Write(Rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataLength);
        foreach (var sample in samples) writer.Write((short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue));
        writer.Flush();
        return stream.ToArray();
    }
}
