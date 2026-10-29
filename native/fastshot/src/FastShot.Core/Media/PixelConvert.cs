namespace FastShot.Core.Media;

/// <summary>BGRA ⇄ NV12 (BT.709, limited range). YUV planes are unambiguous top-down, so the
/// encoder and decoder never disagree about which way is up. Rows are converted in parallel.</summary>
public static unsafe class PixelConvert
{
    static byte Clamp(int v) => (byte)(v < 0 ? 0 : v > 255 ? 255 : v);

    /// <summary>Converts a top-down BGRA image into NV12 (width × height × 3/2 bytes). Width and height must be even.</summary>
    public static void BgraToNv12(IntPtr bgra, int stride, int width, int height, IntPtr nv12)
    {
        var src = bgra;
        var dst = nv12;
        Parallel.For(0, height / 2, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, pair =>
        {
            var y0 = pair * 2;
            var row0 = (byte*)src + (long)y0 * stride;
            var row1 = row0 + stride;
            var yRow0 = (byte*)dst + (long)y0 * width;
            var yRow1 = yRow0 + width;
            var uvRow = (byte*)dst + (long)width * height + (long)pair * width;
            for (var x = 0; x < width; x += 2)
            {
                var p0 = row0 + x * 4;
                var p1 = row1 + x * 4;
                // Y = 16 + 0.1826R + 0.6142G + 0.0620B  (×256 fixed point)
                yRow0[x] = Clamp(16 + ((47 * p0[2] + 157 * p0[1] + 16 * p0[0] + 128) >> 8));
                yRow0[x + 1] = Clamp(16 + ((47 * p0[6] + 157 * p0[5] + 16 * p0[4] + 128) >> 8));
                yRow1[x] = Clamp(16 + ((47 * p1[2] + 157 * p1[1] + 16 * p1[0] + 128) >> 8));
                yRow1[x + 1] = Clamp(16 + ((47 * p1[6] + 157 * p1[5] + 16 * p1[4] + 128) >> 8));
                int b = p0[0] + p0[4] + p1[0] + p1[4];
                int g = p0[1] + p0[5] + p1[1] + p1[5];
                int r = p0[2] + p0[6] + p1[2] + p1[6];
                // Averages of four pixels (×1024 fixed point, ÷4 folded in).
                // U = 128 - 0.1006R - 0.3386G + 0.4392B ; V = 128 + 0.4392R - 0.3989G - 0.0403B
                uvRow[x] = Clamp(128 + ((-26 * r - 87 * g + 112 * b + 512) >> 10));
                uvRow[x + 1] = Clamp(128 + ((112 * r - 102 * g - 10 * b + 512) >> 10));
            }
        });
    }

    public static void BgraToNv12(byte[] bgra, int stride, int width, int height, IntPtr nv12)
    {
        fixed (byte* src = bgra) BgraToNv12((IntPtr)src, stride, width, height, nv12);
    }

    /// <summary>Converts NV12 planes into top-down BGRA (stride = width × 4).</summary>
    public static void Nv12ToBgra(IntPtr yPlane, int yStride, IntPtr uvPlane, int uvStride, int width, int height, byte[] bgra)
    {
        fixed (byte* dstPointer = bgra)
        {
            var dst = (IntPtr)dstPointer;
            Parallel.For(0, height, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, y =>
            {
                var yRow = (byte*)yPlane + (long)y * yStride;
                var uvRow = (byte*)uvPlane + (long)(y / 2) * uvStride;
                var output = (byte*)dst + (long)y * width * 4;
                for (var x = 0; x < width; x++, output += 4)
                {
                    var c = yRow[x] - 16;
                    var d = uvRow[x & ~1] - 128;
                    var e = uvRow[(x & ~1) + 1] - 128;
                    // R = 1.164C + 1.793E ; G = 1.164C - 0.213D - 0.533E ; B = 1.164C + 2.112D  (×256)
                    var c298 = 298 * c + 128;
                    output[0] = Clamp((c298 + 541 * d) >> 8);
                    output[1] = Clamp((c298 - 55 * d - 136 * e) >> 8);
                    output[2] = Clamp((c298 + 459 * e) >> 8);
                    output[3] = 255;
                }
            });
        }
    }

    /// <summary>Area-averaging resize of a BGRA frame (sharp and alias-free when shrinking).</summary>
    public static VideoFrame Resize(VideoFrame frame, int width, int height)
    {
        var source = frame.Bgra;
        int sw = frame.Width, sh = frame.Height;
        var output = new byte[width * height * 4];
        var sx = sw / (double)width;
        var sy = sh / (double)height;
        Parallel.For(0, height, y =>
        {
            var y0 = (int)(y * sy);
            var y1 = Math.Max(y0 + 1, Math.Min(sh, (int)Math.Ceiling((y + 1) * sy)));
            for (var x = 0; x < width; x++)
            {
                var x0 = (int)(x * sx);
                var x1 = Math.Max(x0 + 1, Math.Min(sw, (int)Math.Ceiling((x + 1) * sx)));
                int b = 0, g = 0, r = 0, n = 0;
                for (var yy = y0; yy < y1; yy++)
                {
                    var row = yy * sw * 4;
                    for (var xx = x0; xx < x1; xx++)
                    {
                        var o = row + xx * 4;
                        b += source[o]; g += source[o + 1]; r += source[o + 2];
                        n++;
                    }
                }
                var d = (y * width + x) * 4;
                output[d] = (byte)(b / n); output[d + 1] = (byte)(g / n); output[d + 2] = (byte)(r / n); output[d + 3] = 255;
            }
        });
        return new VideoFrame { Bgra = output, Width = width, Height = height, Time = frame.Time, Duration = frame.Duration };
    }
}
