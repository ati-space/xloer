using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace FastShot.Core;

/// <summary>JSON shaped exactly like the macOS app writes it (Swift Codable): points as [x, y],
/// sizes as [w, h], ISO 8601 dates, upper-case UUIDs. A .fastshot package opens on both.</summary>
public static class ProjectJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new PointConverter(), new SizeConverter(), new DateConverter(), new GuidConverter() },
    };

    sealed class PointConverter : JsonConverter<Point>
    {
        public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                double x = 0, y = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString();
                    reader.Read();
                    if (name == "x") x = reader.GetDouble(); else if (name == "y") y = reader.GetDouble(); else reader.Skip();
                }
                return new Point(x, y);
            }
            reader.Read(); var px = reader.GetDouble();
            reader.Read(); var py = reader.GetDouble();
            reader.Read();
            return new Point(px, py);
        }

        public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.X);
            writer.WriteNumberValue(value.Y);
            writer.WriteEndArray();
        }
    }

    sealed class SizeConverter : JsonConverter<Size>
    {
        public override Size Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                double w = 0, h = 0;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString();
                    reader.Read();
                    if (name == "width") w = reader.GetDouble(); else if (name == "height") h = reader.GetDouble(); else reader.Skip();
                }
                return new Size(Math.Max(0, w), Math.Max(0, h));
            }
            reader.Read(); var width = reader.GetDouble();
            reader.Read(); var height = reader.GetDouble();
            reader.Read();
            return new Size(Math.Max(0, width), Math.Max(0, height));
        }

        public override void Write(Utf8JsonWriter writer, Size value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.Width);
            writer.WriteNumberValue(value.Height);
            writer.WriteEndArray();
        }
    }

    sealed class DateConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            DateTime.Parse(reader.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
    }

    sealed class GuidConverter : JsonConverter<Guid>
    {
        public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => Guid.Parse(reader.GetString()!);
        public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("D").ToUpperInvariant());
    }
}
