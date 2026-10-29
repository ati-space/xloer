using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace FastShot.Core.Media;

/// <summary>Helpers for raw PCM: big-endian writing, AIFF/CAF reading, mixing and resampling.</summary>
public static class AudioFiles
{
    public static void WriteBig(BinaryWriter writer, ushort value) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, value); writer.Write(b); }
    public static void WriteBig(BinaryWriter writer, uint value) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, value); writer.Write(b); }
    public static void WriteBig(BinaryWriter writer, long value) { Span<byte> b = stackalloc byte[8]; BinaryPrimitives.WriteInt64BigEndian(b, value); writer.Write(b); }

    /// <summary>An IEEE 754 80-bit extended float, as AIFF stores its sample rate.</summary>
    public static byte[] Extended80(double value)
    {
        var bytes = new byte[10];
        if (value <= 0) return bytes;
        var exponent = (int)Math.Floor(Math.Log2(value));
        var mantissa = (ulong)(value / Math.Pow(2, exponent - 63));
        var biased = (ushort)(exponent + 16383);
        bytes[0] = (byte)(biased >> 8);
        bytes[1] = (byte)biased;
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(2), mantissa);
        return bytes;
    }

    static double ReadExtended80(ReadOnlySpan<byte> bytes)
    {
        var exponent = ((bytes[0] & 0x7F) << 8) | bytes[1];
        var mantissa = BinaryPrimitives.ReadUInt64BigEndian(bytes[2..]);
        return mantissa * Math.Pow(2, exponent - 16383 - 63);
    }

    /// <summary>Reads uncompressed AIFF/AIFC or CAF into 16-bit little-endian PCM.</summary>
    public static (AudioFormat format, byte[] data)? TryReadPcm(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 12) return null;
            var magic = Encoding.ASCII.GetString(bytes, 0, 4);
            if (magic == "FORM") return ReadAiff(bytes);
            if (magic == "caff") return ReadCaf(bytes);
            return null;
        }
        catch { return null; }
    }

    static (AudioFormat, byte[])? ReadAiff(byte[] bytes)
    {
        var kind = Encoding.ASCII.GetString(bytes, 8, 4);
        var littleEndian = false;
        int channels = 0, bits = 0;
        double rate = 0;
        ReadOnlySpan<byte> samples = default;
        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, position, 4);
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(position + 4));
            var body = position + 8;
            if (id == "COMM")
            {
                channels = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(body));
                bits = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(body + 6));
                rate = ReadExtended80(bytes.AsSpan(body + 8, 10));
                if (kind == "AIFC" && size >= 22)
                {
                    var compression = Encoding.ASCII.GetString(bytes, body + 18, 4);
                    if (compression == "sowt") littleEndian = true;
                    else if (compression != "NONE") return null;
                }
            }
            else if (id == "SSND")
            {
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(body));
                var start = body + 8 + offset;
                samples = bytes.AsSpan(start, Math.Min(size - 8 - offset, bytes.Length - start));
            }
            position = body + size + (size & 1);
        }
        if (channels <= 0 || rate <= 0 || samples.IsEmpty) return null;
        return (new AudioFormat((int)Math.Round(rate), channels), To16(samples, bits, littleEndian, isFloat: false));
    }

    static (AudioFormat, byte[])? ReadCaf(byte[] bytes)
    {
        var position = 8;
        int channels = 0, bits = 0;
        double rate = 0;
        uint flags = 0;
        ReadOnlySpan<byte> samples = default;
        while (position + 12 <= bytes.Length)
        {
            var id = Encoding.ASCII.GetString(bytes, position, 4);
            var size = BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(position + 4));
            var body = position + 12;
            if (size < 0) size = bytes.Length - body;
            if (id == "desc")
            {
                rate = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(body)));
                if (Encoding.ASCII.GetString(bytes, body + 8, 4) != "lpcm") return null;
                flags = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(body + 12));
                channels = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(body + 24));
                bits = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(body + 28));
            }
            else if (id == "data")
            {
                samples = bytes.AsSpan(body + 4, (int)Math.Min(size - 4, bytes.Length - body - 4));
            }
            position = body + (int)size;
        }
        if (channels <= 0 || rate <= 0 || samples.IsEmpty) return null;
        return (new AudioFormat((int)Math.Round(rate), channels), To16(samples, bits, littleEndian: (flags & 2) != 0, isFloat: (flags & 1) != 0));
    }

    static byte[] To16(ReadOnlySpan<byte> samples, int bits, bool littleEndian, bool isFloat)
    {
        var size = Math.Max(1, bits / 8);
        var count = samples.Length / size;
        var output = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            var s = samples.Slice(i * size, size);
            int value;
            if (isFloat && size == 4)
            {
                var f = littleEndian ? BinaryPrimitives.ReadSingleLittleEndian(s) : BinaryPrimitives.ReadSingleBigEndian(s);
                value = (int)Math.Clamp(f * 32767, short.MinValue, short.MaxValue);
            }
            else
            {
                value = size switch
                {
                    1 => (sbyte)s[0] << 8,
                    2 => littleEndian ? BinaryPrimitives.ReadInt16LittleEndian(s) : BinaryPrimitives.ReadInt16BigEndian(s),
                    3 => littleEndian ? (sbyte)s[2] << 8 | s[1] : (sbyte)s[0] << 8 | s[1],
                    _ => (littleEndian ? BinaryPrimitives.ReadInt32LittleEndian(s) : BinaryPrimitives.ReadInt32BigEndian(s)) >> 16,
                };
            }
            BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan(i * 2), (short)value);
        }
        return output;
    }

    /// <summary>Averages extra channels down (e.g. 5.1 to stereo: odd channels left, even right).</summary>
    public static byte[] MixDown(byte[] pcm, int from, int to)
    {
        var frames = pcm.Length / (from * 2);
        var output = new byte[frames * to * 2];
        for (var f = 0; f < frames; f++)
        {
            for (var c = 0; c < to; c++)
            {
                int sum = 0, n = 0;
                for (var s = c; s < from; s += to)
                {
                    sum += BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan((f * from + s) * 2));
                    n++;
                }
                BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan((f * to + c) * 2), (short)Math.Clamp(sum / Math.Max(1, n), short.MinValue, short.MaxValue));
            }
        }
        return output;
    }

    /// <summary>Linear resampling of interleaved 16-bit PCM.</summary>
    public static byte[] Resample(byte[] pcm, int channels, int from, int to)
    {
        var frames = pcm.Length / (channels * 2);
        var outFrames = (int)((long)frames * to / from);
        var output = new byte[outFrames * channels * 2];
        for (var i = 0; i < outFrames; i++)
        {
            var position = i * (double)from / to;
            var index = (int)position;
            var fraction = position - index;
            var next = Math.Min(index + 1, frames - 1);
            for (var c = 0; c < channels; c++)
            {
                var a = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan((index * channels + c) * 2));
                var b = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan((next * channels + c) * 2));
                BinaryPrimitives.WriteInt16LittleEndian(output.AsSpan((i * channels + c) * 2), (short)Math.Round(a + (b - a) * fraction));
            }
        }
        return output;
    }
}
