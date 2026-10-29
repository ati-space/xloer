using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace FastShot.Core.Convert.Pdf;

// A small PDF object model: enough to read any unencrypted PDF (classic or stream cross-reference tables,
// object streams, damaged files by rescanning) and to write a new one. Page content is copied untouched,
// so text stays selectable and vectors stay sharp.

public abstract class PdfObject;

public sealed class PdfNumber(double value, bool integer) : PdfObject
{
    public double Value { get; } = value;
    public bool IsInteger { get; } = integer;
    public int Int => (int)Value;
}

public sealed class PdfName(string value) : PdfObject
{
    public string Value { get; } = value;
}

public sealed class PdfString(byte[] bytes, bool hex) : PdfObject
{
    public byte[] Bytes { get; } = bytes;
    public bool Hex { get; } = hex;
}

public sealed class PdfBoolean(bool value) : PdfObject
{
    public bool Value { get; } = value;
}

public sealed class PdfNull : PdfObject
{
    public static readonly PdfNull Instance = new();
}

public sealed class PdfArray(List<PdfObject> items) : PdfObject
{
    public List<PdfObject> Items { get; } = items;
}

public class PdfDictionary(Dictionary<string, PdfObject> entries) : PdfObject
{
    public Dictionary<string, PdfObject> Entries { get; } = entries;
    public PdfObject? this[string key] => Entries.TryGetValue(key, out var value) ? value : null;
    public string? NameOf(string key) => (this[key] as PdfName)?.Value;
}

public sealed class PdfStream(Dictionary<string, PdfObject> entries, byte[] data) : PdfDictionary(entries)
{
    /// <summary>The bytes as stored in the file, still encoded with the stream's filters.</summary>
    public byte[] Data { get; set; } = data;
}

public sealed class PdfReference(int number, int generation) : PdfObject
{
    public int Number { get; } = number;
    public int Generation { get; } = generation;
}

sealed class PdfKeyword(string value) : PdfObject
{
    public string Value { get; } = value;
}

/// <summary>Tokenises PDF syntax from a byte buffer.</summary>
sealed class PdfLexer(byte[] data, int position = 0)
{
    readonly byte[] data = data;
    public int Position = position;

    static bool IsWhite(byte b) => b is 0 or 9 or 10 or 12 or 13 or 32;
    static bool IsDelimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';



    public void SkipWhite()
    {
        while (Position < data.Length)
        {
            var b = data[Position];
            if (IsWhite(b)) { Position++; continue; }
            if (b == '%')
            {
                while (Position < data.Length && data[Position] != 10 && data[Position] != 13) Position++;
                continue;
            }
            break;
        }
    }

    public PdfObject ReadObject()
    {
        SkipWhite();
        if (Position >= data.Length) throw new InvalidDataException("PDF beklenmedik şekilde bitti.");
        var b = data[Position];
        switch (b)
        {
            case (byte)'/':
                return ReadName();
            case (byte)'(':
                return ReadLiteral();
            case (byte)'[':
            {
                Position++;
                var items = new List<PdfObject>();
                while (true)
                {
                    SkipWhite();
                    if (Position >= data.Length) break;
                    if (data[Position] == ']') { Position++; break; }
                    items.Add(ReadObject());
                }
                return new PdfArray(items);
            }
            case (byte)'<':
                if (Position + 1 < data.Length && data[Position + 1] == '<') return ReadDictionary();
                return ReadHex();
            case (byte)']' or (byte)'>' or (byte)')' or (byte)'}' or (byte)'{':
                Position++;
                return ReadObject();
        }
        if (b is (byte)'+' or (byte)'-' or (byte)'.' || (b >= '0' && b <= '9'))
        {
            var number = ReadNumber();
            if (number.IsInteger && number.Value >= 0)
            {
                // "12 0 R" is a reference.
                var save = Position;
                SkipWhite();
                if (Position < data.Length && data[Position] >= '0' && data[Position] <= '9')
                {
                    var generation = ReadNumber();
                    SkipWhite();
                    if (generation.IsInteger && Position < data.Length && data[Position] == 'R' &&
                        (Position + 1 >= data.Length || IsWhite(data[Position + 1]) || IsDelimiter(data[Position + 1])))
                    {
                        Position++;
                        return new PdfReference(number.Int, generation.Int);
                    }
                }
                Position = save;
            }
            return number;
        }
        var start = Position;
        while (Position < data.Length && !IsWhite(data[Position]) && !IsDelimiter(data[Position])) Position++;
        if (Position == start) { Position++; return PdfNull.Instance; }
        var word = Encoding.Latin1.GetString(data, start, Position - start);
        return word switch
        {
            "true" => new PdfBoolean(true),
            "false" => new PdfBoolean(false),
            "null" => PdfNull.Instance,
            _ => new PdfKeyword(word),
        };
    }

    PdfNumber ReadNumber()
    {
        var start = Position;
        if (data[Position] is (byte)'+' or (byte)'-') Position++;
        var dot = false;
        while (Position < data.Length && ((data[Position] >= '0' && data[Position] <= '9') || data[Position] == '.'))
        {
            if (data[Position] == '.') dot = true;
            Position++;
        }
        var text = Encoding.Latin1.GetString(data, start, Position - start);
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
        return new PdfNumber(value, !dot);
    }

    PdfName ReadName()
    {
        Position++;
        var bytes = new List<byte>();
        while (Position < data.Length && !IsWhite(data[Position]) && !IsDelimiter(data[Position]))
        {
            if (data[Position] == '#' && Position + 2 < data.Length &&
                byte.TryParse(Encoding.Latin1.GetString(data, Position + 1, 2), NumberStyles.HexNumber, null, out var value))
            {
                bytes.Add(value);
                Position += 3;
                continue;
            }
            bytes.Add(data[Position++]);
        }
        return new PdfName(Encoding.UTF8.GetString(bytes.ToArray()));
    }

    PdfString ReadLiteral()
    {
        Position++;
        var bytes = new List<byte>();
        var depth = 1;
        while (Position < data.Length)
        {
            var b = data[Position++];
            if (b == '\\' && Position < data.Length)
            {
                var e = data[Position++];
                switch (e)
                {
                    case (byte)'n': bytes.Add(10); break;
                    case (byte)'r': bytes.Add(13); break;
                    case (byte)'t': bytes.Add(9); break;
                    case (byte)'b': bytes.Add(8); break;
                    case (byte)'f': bytes.Add(12); break;
                    case 13:
                        if (Position < data.Length && data[Position] == 10) Position++;
                        break;
                    case 10: break;
                    default:
                        if (e >= '0' && e <= '7')
                        {
                            var value = e - '0';
                            for (var i = 0; i < 2 && Position < data.Length && data[Position] >= '0' && data[Position] <= '7'; i++) value = value * 8 + (data[Position++] - '0');
                            bytes.Add((byte)value);
                        }
                        else bytes.Add(e);
                        break;
                }
                continue;
            }
            if (b == '(') depth++;
            if (b == ')' && --depth == 0) break;
            bytes.Add(b);
        }
        return new PdfString(bytes.ToArray(), hex: false);
    }

    PdfString ReadHex()
    {
        Position++;
        var hex = new StringBuilder();
        while (Position < data.Length && data[Position] != '>')
        {
            var c = (char)data[Position++];
            if (Uri.IsHexDigit(c)) hex.Append(c);
        }
        Position++;
        if (hex.Length % 2 == 1) hex.Append('0');
        return new PdfString(System.Convert.FromHexString(hex.ToString()), hex: true);
    }

    PdfDictionary ReadDictionary()
    {
        Position += 2;
        var entries = new Dictionary<string, PdfObject>();
        while (true)
        {
            SkipWhite();
            if (Position >= data.Length) break;
            if (data[Position] == '>' && Position + 1 < data.Length && data[Position + 1] == '>') { Position += 2; break; }
            var key = ReadObject();
            if (key is not PdfName name) continue;
            var value = ReadObject();
            entries[name.Value] = value;
        }
        return new PdfDictionary(entries);
    }
}

/// <summary>An opened PDF file.</summary>
public sealed class PdfFile
{
    readonly byte[] data;
    readonly Dictionary<int, long> offsets = [];
    readonly Dictionary<int, (int stream, int index)> packed = [];
    readonly Dictionary<int, PdfObject?> cache = [];
    readonly Dictionary<int, (byte[] data, int first, Dictionary<int, int> offsets)> objectStreams = [];

    public PdfDictionary Trailer { get; private set; } = new([]);
    public string Name { get; }

    PdfFile(byte[] data, string name)
    {
        this.data = data;
        Name = name;
    }

    public static PdfFile Load(string path)
    {
        var name = Path.GetFileName(path);
        var file = new PdfFile(File.ReadAllBytes(path), name);
        try
        {
            file.ReadCrossReferences();
            if (file.Catalog is null) throw new InvalidDataException();
        }
        catch
        {
            file.Rebuild();
            if (file.Catalog is null) throw ConversionException.Unreadable(name);
        }
        if (file.Trailer["Encrypt"] is not null) throw new ConversionException($"{name} şifreli; şifreli PDF'ler değiştirilemiyor.");
        return file;
    }

    public PdfDictionary? Catalog => Resolve(Trailer["Root"]) as PdfDictionary;
    public PdfDictionary? Info => Resolve(Trailer["Info"]) as PdfDictionary;

    // MARK: Cross references

    void ReadCrossReferences()
    {
        var tail = Encoding.Latin1.GetString(data, Math.Max(0, data.Length - 2048), Math.Min(2048, data.Length));
        var index = tail.LastIndexOf("startxref", StringComparison.Ordinal);
        if (index < 0) throw new InvalidDataException("startxref yok.");
        var lexer = new PdfLexer(data, Math.Max(0, data.Length - 2048) + index + 9);
        var offset = (lexer.ReadObject() as PdfNumber)?.Int ?? throw new InvalidDataException();
        var seen = new HashSet<int>();
        PdfDictionary? first = null;
        while (offset > 0 && offset < data.Length && seen.Add(offset))
        {
            var trailer = ReadSection(offset);
            first ??= trailer;
            if (trailer["XRefStm"] is PdfNumber hybrid && seen.Add(hybrid.Int)) ReadSection(hybrid.Int);
            offset = (trailer["Prev"] as PdfNumber)?.Int ?? 0;
        }
        Trailer = first ?? throw new InvalidDataException();
    }

    PdfDictionary ReadSection(int offset)
    {
        var lexer = new PdfLexer(data, offset);
        lexer.SkipWhite();
        if (lexer.Position + 4 <= data.Length && Encoding.Latin1.GetString(data, lexer.Position, 4) == "xref")
        {
            lexer.Position += 4;
            while (true)
            {
                var token = lexer.ReadObject();
                if (token is PdfKeyword { Value: "trailer" }) return lexer.ReadObject() as PdfDictionary ?? new([]);
                if (token is not PdfNumber start) throw new InvalidDataException();
                var count = ((PdfNumber)lexer.ReadObject()).Int;
                for (var i = 0; i < count; i++)
                {
                    var entryOffset = (PdfNumber)lexer.ReadObject();
                    lexer.ReadObject();
                    var kind = (PdfKeyword)lexer.ReadObject();
                    var number = start.Int + i;
                    if (kind.Value == "n" && !offsets.ContainsKey(number) && !packed.ContainsKey(number)) offsets[number] = (long)entryOffset.Value;
                }
            }
        }
        // A cross-reference stream.
        var (_, stream) = ParseIndirect(offset);
        if (stream is not PdfStream xref || xref.NameOf("Type") != "XRef") throw new InvalidDataException();
        var decoded = Decode(xref);
        var widths = ((PdfArray)xref["W"]!).Items.Select(o => ((PdfNumber)o).Int).ToArray();
        var ranges = (xref["Index"] as PdfArray)?.Items.Select(o => ((PdfNumber)Resolve(o)!).Int).ToList() ?? [0, ((PdfNumber)xref["Size"]!).Int];
        var row = widths.Sum();
        var position = 0;
        for (var r = 0; r + 1 < ranges.Count; r += 2)
        {
            for (var i = 0; i < ranges[r + 1] && position + row <= decoded.Length; i++, position += row)
            {
                long Field(int which)
                {
                    long value = 0;
                    var at = position + widths.Take(which).Sum();
                    for (var k = 0; k < widths[which]; k++) value = (value << 8) | decoded[at + k];
                    return value;
                }
                var type = widths[0] == 0 ? 1 : Field(0);
                var number = ranges[r] + i;
                if (offsets.ContainsKey(number) || packed.ContainsKey(number)) continue;
                if (type == 1) offsets[number] = Field(1);
                else if (type == 2) packed[number] = ((int)Field(1), (int)Field(2));
            }
        }
        return xref;
    }

    /// <summary>For damaged files: find every "n g obj" and the last trailer by scanning.</summary>
    void Rebuild()
    {
        offsets.Clear();
        packed.Clear();
        cache.Clear();
        var text = Encoding.Latin1.GetString(data);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"(?<![0-9])(\d+)\s+(\d+)\s+obj\b"))
            offsets[int.Parse(match.Groups[1].Value)] = match.Index;
        var trailer = text.LastIndexOf("trailer", StringComparison.Ordinal);
        if (trailer >= 0 && new PdfLexer(data, trailer + 7).ReadObject() is PdfDictionary dictionary) Trailer = dictionary;
        if (Catalog is not null) return;
        // No usable trailer: look for the catalog (and object streams holding it).
        foreach (var number in offsets.Keys.ToList())
        {
            if (Get(number) is PdfStream { } s && s.NameOf("Type") == "XRef" && s["Root"] is PdfReference) { Trailer = s; return; }
            if (Get(number) is PdfDictionary d && d.NameOf("Type") == "Catalog")
            {
                Trailer = new PdfDictionary(new() { ["Root"] = new PdfReference(number, 0) });
                return;
            }
        }
    }

    // MARK: Objects

    public PdfObject? Resolve(PdfObject? value)
    {
        for (var hops = 0; value is PdfReference reference && hops < 32; hops++) value = Get(reference.Number);
        return value;
    }

    public PdfObject? Get(int number)
    {
        if (cache.TryGetValue(number, out var cached)) return cached;
        cache[number] = null; // guards against cycles while parsing
        PdfObject? result = null;
        try
        {
            if (offsets.TryGetValue(number, out var offset)) result = ParseIndirect((int)offset).value;
            else if (packed.TryGetValue(number, out var location)) result = FromObjectStream(location.stream, number);
        }
        catch { result = null; }
        cache[number] = result;
        return result;
    }



    (int number, PdfObject? value) ParseIndirect(int offset)
    {
        var lexer = new PdfLexer(data, offset);
        var number = (lexer.ReadObject() as PdfNumber)?.Int ?? -1;
        lexer.ReadObject();
        if (lexer.ReadObject() is not PdfKeyword { Value: "obj" }) throw new InvalidDataException();
        var value = lexer.ReadObject();
        if (value is PdfDictionary dictionary)
        {
            var save = lexer.Position;
            lexer.SkipWhite();
            if (lexer.Position + 6 <= data.Length && Encoding.Latin1.GetString(data, lexer.Position, 6) == "stream")
            {
                var start = lexer.Position + 6;
                if (start < data.Length && data[start] == 13) start++;
                if (start < data.Length && data[start] == 10) start++;
                var length = Resolve(dictionary["Length"]) is PdfNumber n ? n.Int : -1;
                if (length < 0 || start + length > data.Length || !Encoding.Latin1.GetString(data, start + length, Math.Min(20, data.Length - start - length)).TrimStart().StartsWith("endstream"))
                {
                    // Wrong or missing length: find endstream instead.
                    var end = IndexOf(data, "endstream"u8, start);
                    if (end < 0) end = data.Length;
                    length = end - start;
                    while (length > 0 && (data[start + length - 1] == 10 || data[start + length - 1] == 13)) length--;
                }
                return (number, new PdfStream(dictionary.Entries, data.AsSpan(start, length).ToArray()));
            }
            lexer.Position = save;
        }
        return (number, value);
    }

    static int IndexOf(byte[] haystack, ReadOnlySpan<byte> needle, int start)
    {
        var index = haystack.AsSpan(start).IndexOf(needle);
        return index < 0 ? -1 : start + index;
    }

    PdfObject? FromObjectStream(int streamNumber, int number)
    {
        if (!objectStreams.TryGetValue(streamNumber, out var parsed))
        {
            if (Get(streamNumber) is not PdfStream stream) return null;
            var decoded = Decode(stream);
            var count = (Resolve(stream["N"]) as PdfNumber)?.Int ?? 0;
            var first = (Resolve(stream["First"]) as PdfNumber)?.Int ?? 0;
            var lexer = new PdfLexer(decoded);
            var table = new Dictionary<int, int>();
            for (var i = 0; i < count; i++)
            {
                var objectNumber = (lexer.ReadObject() as PdfNumber)?.Int ?? -1;
                var objectOffset = (lexer.ReadObject() as PdfNumber)?.Int ?? 0;
                table[objectNumber] = objectOffset;
            }
            parsed = (decoded, first, table);
            objectStreams[streamNumber] = parsed;
        }
        return parsed.offsets.TryGetValue(number, out var at) ? new PdfLexer(parsed.data, parsed.first + at).ReadObject() : null;
    }

    // MARK: Streams

    /// <summary>Applies the stream's filters (Flate with PNG/TIFF predictors, ASCIIHex, ASCII85).</summary>
    public byte[] Decode(PdfStream stream)
    {
        var filters = Resolve(stream["Filter"]) switch
        {
            PdfName name => [name.Value],
            PdfArray array => array.Items.Select(i => (Resolve(i) as PdfName)?.Value ?? "").ToList(),
            _ => new List<string>(),
        };
        var parameters = Resolve(stream["DecodeParms"]) switch
        {
            PdfDictionary d => [d],
            PdfArray a => a.Items.Select(i => Resolve(i) as PdfDictionary).ToList(),
            _ => new List<PdfDictionary?>(),
        };
        var bytes = stream.Data;
        for (var i = 0; i < filters.Count; i++)
        {
            var p = i < parameters.Count ? parameters[i] : null;
            bytes = filters[i] switch
            {
                "FlateDecode" or "Fl" => Unpredict(Inflate(bytes), p),
                "ASCIIHexDecode" or "AHx" => AsciiHex(bytes),
                "ASCII85Decode" or "A85" => Ascii85(bytes),
                _ => throw new NotSupportedException(filters[i]),
            };
        }
        return bytes;
    }

    public static byte[] Inflate(byte[] bytes)
    {
        using var output = new MemoryStream();
        try
        {
            // Skip the zlib header and ignore the checksum: many writers get it wrong.
            using var input = new MemoryStream(bytes, 2, Math.Max(0, bytes.Length - 2));
            using var inflate = new DeflateStream(input, CompressionMode.Decompress);
            inflate.CopyTo(output);
        }
        catch (InvalidDataException) when (output.Length > 0) { /* truncated stream: keep what decoded */ }
        return output.ToArray();
    }

    public static byte[] Deflate(byte[] bytes)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(bytes);
        return output.ToArray();
    }

    byte[] Unpredict(byte[] bytes, PdfDictionary? parameters)
    {
        var predictor = (Resolve(parameters?["Predictor"]) as PdfNumber)?.Int ?? 1;
        if (predictor < 10) return bytes;
        var colors = (Resolve(parameters?["Colors"]) as PdfNumber)?.Int ?? 1;
        var bits = (Resolve(parameters?["BitsPerComponent"]) as PdfNumber)?.Int ?? 8;
        var columns = (Resolve(parameters?["Columns"]) as PdfNumber)?.Int ?? 1;
        var bpp = Math.Max(1, colors * bits / 8);
        var row = (colors * bits * columns + 7) / 8;
        var output = new MemoryStream();
        var previous = new byte[row];
        for (var position = 0; position + row < bytes.Length + 1 && position < bytes.Length; position += row + 1)
        {
            var type = bytes[position];
            var current = new byte[row];
            Array.Copy(bytes, position + 1, current, 0, Math.Min(row, bytes.Length - position - 1));
            for (var i = 0; i < row; i++)
            {
                var left = i >= bpp ? current[i - bpp] : 0;
                var up = previous[i];
                var upLeft = i >= bpp ? previous[i - bpp] : 0;
                current[i] = type switch
                {
                    1 => (byte)(current[i] + left),
                    2 => (byte)(current[i] + up),
                    3 => (byte)(current[i] + (left + up) / 2),
                    4 => (byte)(current[i] + Paeth(left, up, upLeft)),
                    _ => current[i],
                };
            }
            output.Write(current);
            previous = current;
        }
        return output.ToArray();
    }

    static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    static byte[] AsciiHex(byte[] bytes)
    {
        var text = new string(Encoding.Latin1.GetString(bytes).TakeWhile(c => c != '>').Where(Uri.IsHexDigit).ToArray());
        if (text.Length % 2 == 1) text += "0";
        return System.Convert.FromHexString(text);
    }

    static byte[] Ascii85(byte[] bytes)
    {
        var output = new MemoryStream();
        var group = new List<int>();
        foreach (var b in bytes)
        {
            if (b == '~') break;
            if (b == 'z' && group.Count == 0) { output.Write(new byte[4]); continue; }
            if (b < '!' || b > 'u') continue;
            group.Add(b - '!');
            if (group.Count == 5)
            {
                long value = group.Aggregate(0L, (v, d) => v * 85 + d);
                output.Write([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);
                group.Clear();
            }
        }
        if (group.Count > 1)
        {
            var count = group.Count;
            while (group.Count < 5) group.Add(84);
            long value = group.Aggregate(0L, (v, d) => v * 85 + d);
            var last = new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
            output.Write(last, 0, count - 1);
        }
        return output.ToArray();
    }

    // MARK: Pages

    static readonly string[] inheritable = ["Resources", "MediaBox", "CropBox", "Rotate"];

    /// <summary>Every page in order, with the attributes it inherits from the page tree filled in.</summary>
    public List<(int number, PdfDictionary page)> Pages()
    {
        var pages = new List<(int, PdfDictionary)>();
        var visited = new HashSet<int>();
        void Walk(PdfObject? node, Dictionary<string, PdfObject> inherited)
        {
            var number = (node as PdfReference)?.Number ?? -1;
            if (number >= 0 && !visited.Add(number)) return;
            if (Resolve(node) is not PdfDictionary dictionary) return;
            var type = dictionary.NameOf("Type");
            if (type == "Pages" || (type is null && dictionary["Kids"] is not null))
            {
                var next = new Dictionary<string, PdfObject>(inherited);
                foreach (var key in inheritable)
                    if (dictionary[key] is { } value) next[key] = value;
                if (Resolve(dictionary["Kids"]) is PdfArray kids)
                    foreach (var kid in kids.Items) Walk(kid, next);
                return;
            }
            var page = new PdfDictionary(new Dictionary<string, PdfObject>(dictionary.Entries));
            foreach (var (key, value) in inherited)
                page.Entries.TryAdd(key, value);
            pages.Add((number, page));
        }
        Walk(Catalog?["Pages"], []);
        return pages;
    }
}

/// <summary>Collects objects for a new PDF and writes it with a classic cross-reference table.</summary>
public sealed class PdfBuilder
{
    readonly List<PdfObject?> objects = [];

    public int Reserve()
    {
        objects.Add(null);
        return objects.Count;
    }

    public int Add(PdfObject value)
    {
        objects.Add(value);
        return objects.Count;
    }

    public void Set(int number, PdfObject value) => objects[number - 1] = value;

    public void Save(string path, int root, int? info = null)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        var offsets = new long[objects.Count];
        void Text(string text) => file.Write(Encoding.Latin1.GetBytes(text));
        Text("%PDF-1.7\n%âãÏÓ\n");
        for (var i = 0; i < objects.Count; i++)
        {
            offsets[i] = file.Position;
            Text($"{i + 1} 0 obj\n");
            var value = objects[i] ?? PdfNull.Instance;
            if (value is PdfStream stream)
            {
                stream.Entries["Length"] = new PdfNumber(stream.Data.Length, true);
                Write(file, new PdfDictionary(stream.Entries));
                Text("\nstream\n");
                file.Write(stream.Data);
                Text("\nendstream");
            }
            else
            {
                Write(file, value);
            }
            Text("\nendobj\n");
        }
        var xref = file.Position;
        Text($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) Text($"{offset:D10} 00000 n \n");
        var id = System.Convert.ToHexString(Guid.NewGuid().ToByteArray());
        Text($"trailer\n<< /Size {objects.Count + 1} /Root {root} 0 R{(info is { } i2 ? $" /Info {i2} 0 R" : "")} /ID [<{id}> <{id}>] >>\nstartxref\n{xref}\n%%EOF\n");
    }

    static void Write(Stream output, PdfObject value)
    {
        void Text(string text) => output.Write(Encoding.Latin1.GetBytes(text));
        switch (value)
        {
            case PdfNumber n:
                Text(n.IsInteger ? ((long)n.Value).ToString(CultureInfo.InvariantCulture) : n.Value.ToString("0.######", CultureInfo.InvariantCulture));
                break;
            case PdfName name:
            {
                var builder = new StringBuilder("/");
                foreach (var b in Encoding.UTF8.GetBytes(name.Value))
                {
                    if (b < 33 || b > 126 || "()<>[]{}/%#".Contains((char)b)) builder.Append($"#{b:X2}");
                    else builder.Append((char)b);
                }
                Text(builder.ToString());
                break;
            }
            case PdfString s:
                if (s.Hex)
                {
                    Text($"<{System.Convert.ToHexString(s.Bytes)}>");
                }
                else
                {
                    output.WriteByte((byte)'(');
                    foreach (var b in s.Bytes)
                    {
                        if (b is (byte)'(' or (byte)')' or (byte)'\\') output.WriteByte((byte)'\\');
                        if (b == 13) { Text("\\r"); continue; }
                        output.WriteByte(b);
                    }
                    output.WriteByte((byte)')');
                }
                break;
            case PdfBoolean b:
                Text(b.Value ? "true" : "false");
                break;
            case PdfReference r:
                Text($"{r.Number} {r.Generation} R");
                break;
            case PdfArray array:
                Text("[");
                for (var i = 0; i < array.Items.Count; i++)
                {
                    if (i > 0) Text(" ");
                    Write(output, array.Items[i]);
                }
                Text("]");
                break;
            case PdfDictionary dictionary:
                Text("<<");
                foreach (var (key, item) in dictionary.Entries)
                {
                    Write(output, new PdfName(key));
                    Text(" ");
                    Write(output, item);
                    Text("\n");
                }
                Text(">>");
                break;
            default:
                Text("null");
                break;
        }
    }
}

/// <summary>Copies objects from one PDF into a <see cref="PdfBuilder"/>, renumbering references. Works through a queue,
/// so long chains (outline siblings, annotation lists) never exhaust the stack.</summary>
public sealed class PdfCopier(PdfFile source, PdfBuilder target)
{
    readonly Dictionary<int, int> map = [];
    readonly Queue<(int from, int to)> pending = new();

    /// <summary>Keys dropped everywhere (e.g. Metadata when cleaning).</summary>
    public HashSet<string> SkipKeys { get; init; } = [];
    /// <summary>Lets the caller rewrite streams (image recompression) as they are copied.</summary>
    /// <summary>(source stream, its copy with renumbered references, source file) → the stream to write.</summary>
    public Func<PdfStream, PdfStream, PdfFile, PdfStream>? TransformStream { get; init; }

    public PdfReference Map(int sourceNumber, int targetNumber)
    {
        map[sourceNumber] = targetNumber;
        return new PdfReference(targetNumber, 0);
    }



    public PdfObject Copy(PdfObject value)
    {
        switch (value)
        {
            case PdfReference reference:
                if (!map.TryGetValue(reference.Number, out var number))
                {
                    number = target.Reserve();
                    map[reference.Number] = number;
                    pending.Enqueue((reference.Number, number));
                }
                return new PdfReference(number, 0);
            case PdfArray array:
                return new PdfArray(array.Items.Select(Copy).ToList());
            case PdfStream stream:
            {
                var copy = new PdfStream(CopyEntries(stream.Entries), stream.Data);
                return TransformStream?.Invoke(stream, copy, source) ?? copy;
            }
            case PdfDictionary dictionary:
                return new PdfDictionary(CopyEntries(dictionary.Entries));
            default:
                return value;
        }
    }

    Dictionary<string, PdfObject> CopyEntries(Dictionary<string, PdfObject> entries)
    {
        var copy = new Dictionary<string, PdfObject>();
        foreach (var (key, item) in entries)
            if (!SkipKeys.Contains(key)) copy[key] = Copy(item);
        return copy;
    }

    /// <summary>Copies everything referenced so far.</summary>
    public void Drain()
    {
        while (pending.TryDequeue(out var job))
            target.Set(job.to, source.Get(job.from) is { } value ? Copy(value) : PdfNull.Instance);
    }
}
