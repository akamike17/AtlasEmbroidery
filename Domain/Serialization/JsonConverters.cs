namespace AtlasEmbroidery.Domain.Serialization;

using System.Text.Json;
using System.Text.Json.Serialization;
using AtlasEmbroidery.Domain.Models;

/// <summary>
/// JSON Converter para Point (serializa como {x,y} en lugar de array)
/// </summary>
public sealed class PointJsonConverter : JsonConverter<Point>
{
    public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for Point");

        int x = 0, y = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            string prop = reader.GetString() ?? "";
            reader.Read();

            switch (prop.ToLowerInvariant())
            {
                case "x": x = reader.GetInt32(); break;
                case "y": y = reader.GetInt32(); break;
            }
        }
        return new Point(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON Converter para Rectangle
/// </summary>
public sealed class RectangleJsonConverter : JsonConverter<Rectangle>
{
    public override Rectangle Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for Rectangle");

        int x = 0, y = 0, w = 0, h = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            string prop = reader.GetString() ?? "";
            reader.Read();

            switch (prop.ToLowerInvariant())
            {
                case "x": x = reader.GetInt32(); break;
                case "y": y = reader.GetInt32(); break;
                case "width": w = reader.GetInt32(); break;
                case "height": h = reader.GetInt32(); break;
            }
        }
        return new Rectangle(x, y, w, h);
    }

    public override void Write(Utf8JsonWriter writer, Rectangle value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("width", value.Width);
        writer.WriteNumber("height", value.Height);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON Converter para ThreadColor
/// </summary>
public sealed class ThreadColorJsonConverter : JsonConverter<ThreadColor>
{
    public override ThreadColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            // Formato hex: "#RRGGBB" o "RRGGBB"
            string hex = reader.GetString() ?? "#000000";
            return ThreadColor.FromHex(hex);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object or string for ThreadColor");

        byte r = 0, g = 0, b = 0;
        string brand = "", code = "", name = "", desc = "";

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            string prop = reader.GetString() ?? "";
            reader.Read();

            switch (prop.ToLowerInvariant())
            {
                case "r": r = (byte)reader.GetInt32(); break;
                case "g": g = (byte)reader.GetInt32(); break;
                case "b": b = (byte)reader.GetInt32(); break;
                case "brand": brand = reader.GetString() ?? ""; break;
                case "code": code = reader.GetString() ?? ""; break;
                case "name": name = reader.GetString() ?? ""; break;
                case "description": desc = reader.GetString() ?? ""; break;
            }
        }
        return new ThreadColor(r, g, b, brand, code, name, desc);
    }

    public override void Write(Utf8JsonWriter writer, ThreadColor value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("r", value.R);
        writer.WriteNumber("g", value.G);
        writer.WriteNumber("b", value.B);
        if (!string.IsNullOrEmpty(value.Brand)) writer.WriteString("brand", value.Brand);
        if (!string.IsNullOrEmpty(value.Code)) writer.WriteString("code", value.Code);
        if (!string.IsNullOrEmpty(value.Name)) writer.WriteString("name", value.Name);
        if (!string.IsNullOrEmpty(value.Description)) writer.WriteString("description", value.Description);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON Converter para StitchPoint
/// </summary>
public sealed class StitchPointJsonConverter : JsonConverter<StitchPoint>
{
    public override StitchPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for StitchPoint");

        int x = 0, y = 0;
        StitchType type = StitchType.Running;
        byte needle = 1, colorIndex = 0;
        ushort flags = 0;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            string prop = reader.GetString() ?? "";
            reader.Read();

            switch (prop.ToLowerInvariant())
            {
                case "x": x = reader.GetInt32(); break;
                case "y": y = reader.GetInt32(); break;
                case "type": type = (StitchType)reader.GetInt32(); break;
                case "needle": needle = (byte)reader.GetInt32(); break;
                case "colorindex": colorIndex = (byte)reader.GetInt32(); break;
                case "flags": flags = (ushort)reader.GetInt32(); break;
            }
        }
        return new StitchPoint(x, y, type, needle, colorIndex, flags);
    }

    public override void Write(Utf8JsonWriter writer, StitchPoint value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("type", (int)value.Type);
        writer.WriteNumber("needle", value.Needle);
        writer.WriteNumber("colorIndex", value.ColorIndex);
        if (value.Flags != 0) writer.WriteNumber("flags", value.Flags);
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON Converter para StitchType (serializa como número)
/// </summary>
public sealed class StitchTypeJsonConverter : JsonConverter<StitchType>
{
    public override StitchType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return (StitchType)reader.GetByte();
        }
        if (reader.TokenType == JsonTokenType.String)
        {
            string str = reader.GetString() ?? "";
            if (Enum.TryParse<StitchType>(str, true, out var result))
                return result;
        }
        return StitchType.Running;
    }

    public override void Write(Utf8JsonWriter writer, StitchType value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue((byte)value);
    }
}

/// <summary>
/// JSON Converter para PathSegment (polimórfico - usa discriminador $type)
/// </summary>
public sealed class PathSegmentJsonConverter : JsonConverter<PathSegment>
{
    public override PathSegment Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for PathSegment");

        // First pass: find the type discriminator
        string? typeDiscriminator = null;
        var properties = new Dictionary<string, JsonElement>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            string prop = reader.GetString() ?? "";
            reader.Read();

            if (prop.Equals("$type", StringComparison.OrdinalIgnoreCase))
            {
                typeDiscriminator = reader.GetString();
            }
            else
            {
                properties[prop] = JsonElement.ParseValue(ref reader);
            }
        }

        // Create the appropriate type
        PathSegment segment = typeDiscriminator switch
        {
            "LineSegment" => new LineSegment(),
            "QuadraticBezierSegment" => new QuadraticBezierSegment(),
            "CubicBezierSegment" => new CubicBezierSegment(),
            _ => new LineSegment() // Default
        };

        // Deserialize properties
        var json = JsonSerializer.Serialize(properties);
        var deserialized = JsonSerializer.Deserialize(json, segment.GetType(), options);
        if (deserialized is PathSegment result)
            return result;

        return segment;
    }

    public override void Write(Utf8JsonWriter writer, PathSegment value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$type", value.GetType().Name);

        // Serialize all properties
        var json = JsonSerializer.Serialize(value, value.GetType(), options);
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name != "$type") // Skip if already written
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }
}

/// <summary>
/// JSON Converter para EmbroideryObject (polimórfico - usa discriminador $type)
/// </summary>
public sealed class EmbroideryObjectJsonConverter : JsonConverter<EmbroideryObject>
{
    public override EmbroideryObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Expected object for EmbroideryObject");

        // First pass: find the type discriminator
        string? typeDiscriminator = null;
        var properties = new Dictionary<string, JsonElement>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            string prop = reader.GetString() ?? "";
            reader.Read();

            if (prop.Equals("$type", StringComparison.OrdinalIgnoreCase))
            {
                typeDiscriminator = reader.GetString();
            }
            else
            {
                properties[prop] = JsonElement.ParseValue(ref reader);
            }
        }

        // Create the appropriate type
        EmbroideryObject obj = typeDiscriminator switch
        {
            "ShapeObject" or "Shape" => new ShapeObject(),
            "PathObject" or "Path" => new PathObject(),
            "TextObject" or "Text" => throw new NotSupportedException("TextObject not yet implemented"),
            "ImageObject" or "Image" => throw new NotSupportedException("ImageObject not yet implemented"),
            "AppliqueObject" or "Applique" => throw new NotSupportedException("AppliqueObject not yet implemented"),
            "SequinsObject" or "Sequins" => throw new NotSupportedException("SequinsObject not yet implemented"),
            "GroupObject" or "Group" => throw new NotSupportedException("GroupObject not yet implemented"),
            _ => new ShapeObject() // Default
        };

        // Deserialize properties
        var json = JsonSerializer.Serialize(properties);
        var deserialized = JsonSerializer.Deserialize(json, obj.GetType(), options);
        if (deserialized is EmbroideryObject result)
            return result;

        return obj;
    }

    public override void Write(Utf8JsonWriter writer, EmbroideryObject value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("$type", value.GetType().Name);

        // Serialize all properties
        var json = JsonSerializer.Serialize(value, value.GetType(), options);
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name != "$type") // Skip if already written
            {
                prop.WriteTo(writer);
            }
        }
        writer.WriteEndObject();
    }
}