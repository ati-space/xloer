using System.IO;
using System.Windows.Media.Imaging;

namespace FastShot.Core;

/// <summary>Writes a looping animated GIF. WPF's encoder produces the frames; the byte stream is
/// then walked block by block to set each frame's delay and add the loop extension.</summary>
public static class GifWriter
{
    public static void Write(string path, IEnumerable<BitmapSource> frames, double frameDelaySeconds)
    {
        var encoder = new GifBitmapEncoder();
        foreach (var frame in frames) encoder.Frames.Add(BitmapFrame.Create(frame));
        if (encoder.Frames.Count == 0) throw new InvalidOperationException("GIF için kare yok.");
        using var memory = new MemoryStream();
        encoder.Save(memory);
        var delay = (ushort)Math.Clamp(Math.Round(frameDelaySeconds * 100), 2, 65535);
        File.WriteAllBytes(path, Patch(memory.ToArray(), delay));
    }

    /// <summary>Rewrites delays on every graphic control extension, inserts missing ones, and adds NETSCAPE2.0 looping.</summary>
    public static byte[] Patch(byte[] gif, ushort delayCentiseconds)
    {
        if (gif.Length < 13 || gif[0] != 'G' || gif[1] != 'I' || gif[2] != 'F') throw new InvalidDataException("GIF başlığı bulunamadı.");
        using var output = new MemoryStream(gif.Length + 64);
        // Header, bumped to 89a so extensions are legal.
        output.Write(gif, 0, 6);
        output.Position = 3;
        output.Write("89a"u8);
        output.Position = 6;
        // Logical screen descriptor + optional global colour table.
        var flags = gif[10];
        var tableSize = (flags & 0x80) != 0 ? 3 * (1 << ((flags & 7) + 1)) : 0;
        output.Write(gif, 6, 7 + tableSize);
        var position = 13 + tableSize;
        // Loop forever.
        output.Write([0x21, 0xFF, 0x0B]);
        output.Write("NETSCAPE2.0"u8);
        output.Write([0x03, 0x01, 0x00, 0x00, 0x00]);

        var pendingControl = false;
        while (position < gif.Length)
        {
            var marker = gif[position];
            if (marker == 0x3B)
            {
                output.WriteByte(0x3B);
                break;
            }
            if (marker == 0x21)
            {
                var label = gif[position + 1];
                if (label == 0xF9 && position + 7 < gif.Length)
                {
                    // Graphic control extension: 21 F9 04 flags delayLo delayHi transparent 00
                    output.Write(gif, position, 4);
                    output.WriteByte((byte)(delayCentiseconds & 0xFF));
                    output.WriteByte((byte)(delayCentiseconds >> 8));
                    output.Write(gif, position + 6, 2);
                    position += 8;
                    pendingControl = true;
                    continue;
                }
                if (label == 0xFF && position + 13 < gif.Length && gif[position + 2] == 0x0B &&
                    System.Text.Encoding.ASCII.GetString(gif, position + 3, 11) == "NETSCAPE2.0")
                {
                    // Drop an existing loop block; ours is already written.
                    position = SkipSubBlocks(gif, position + 14);
                    continue;
                }
                var start = position;
                position = SkipSubBlocks(gif, position + 2);
                output.Write(gif, start, position - start);
                continue;
            }
            if (marker == 0x2C)
            {
                if (!pendingControl)
                {
                    output.Write([0x21, 0xF9, 0x04, 0x00, (byte)(delayCentiseconds & 0xFF), (byte)(delayCentiseconds >> 8), 0x00, 0x00]);
                }
                pendingControl = false;
                var start = position;
                var localFlags = gif[position + 9];
                var localTable = (localFlags & 0x80) != 0 ? 3 * (1 << ((localFlags & 7) + 1)) : 0;
                position += 10 + localTable + 1; // descriptor, table, LZW minimum code size
                position = SkipSubBlocks(gif, position);
                output.Write(gif, start, position - start);
                continue;
            }
            // Unknown byte: copy through to stay lossless.
            output.WriteByte(marker);
            position++;
        }
        return output.ToArray();
    }

    static int SkipSubBlocks(byte[] data, int position)
    {
        while (position < data.Length)
        {
            var size = data[position];
            position++;
            if (size == 0) break;
            position += size;
        }
        return position;
    }
}
