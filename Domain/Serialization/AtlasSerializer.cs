namespace AtlasEmbroidery.Domain.Serialization;

using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Stitching;

/// <summary>
/// Serializador para formato ATB nativo (AtlasBordado)
/// Usa System.Text.JSON con opciones optimizadas
/// byte[] se serializa como base64 string (NO array JSON)
/// </summary>
public static class AtlasSerializer
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            new PointJsonConverter(),
            new RectangleJsonConverter(),
            new ThreadColorJsonConverter(),
            new StitchPointJsonConverter(),
            new StitchTypeJsonConverter(),
            new PathSegmentJsonConverter(),
            new EmbroideryObjectJsonConverter()
        },
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString | System.Text.Json.Serialization.JsonNumberHandling.WriteAsString
    };

    private static readonly JsonSerializerOptions _compactOptions = new(_options)
    {
        WriteIndented = false
    };

    /// <summary>
    /// Serializa proyecto a JSON string (formato legible)
    /// </summary>
    public static string SerializeToJson(AtlasProject project, bool pretty = true)
    {
        var options = pretty ? _options : _compactOptions;
        return JsonSerializer.Serialize(project, options);
    }

    /// <summary>
    /// Serializa proyecto a bytes (UTF-8 JSON)
    /// </summary>
    public static byte[] SerializeToBytes(AtlasProject project, bool pretty = true)
    {
        var json = SerializeToJson(project, pretty);
        return Encoding.UTF8.GetBytes(json);
    }

    /// <summary>
    /// Serializa y guarda a archivo .atlas
    /// </summary>
    public static void SaveToFile(AtlasProject project, string filePath, bool pretty = true)
    {
        var bytes = SerializeToBytes(project, pretty);
        File.WriteAllBytes(filePath, bytes);
    }

    /// <summary>
    /// Deserializa desde JSON string
    /// </summary>
    public static AtlasProject? DeserializeFromJson(string json)
    {
        return JsonSerializer.Deserialize<AtlasProject>(json, _options);
    }

    /// <summary>
    /// Deserializa desde bytes
    /// </summary>
    public static AtlasProject? DeserializeFromBytes(byte[] bytes)
    {
        var json = Encoding.UTF8.GetString(bytes);
        return DeserializeFromJson(json);
    }

    /// <summary>
    /// Carga desde archivo .atlas
    /// </summary>
    public static AtlasProject? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        var bytes = File.ReadAllBytes(filePath);
        return DeserializeFromBytes(bytes);
    }

    /// <summary>
    /// Calcula hash SHA256 del contenido serializable
    /// </summary>
    public static string ComputeContentHash(AtlasProject project)
    {
        // Serializar sin campos volátiles (fechas, IDs únicos)
        var clone = project.DeepClone();
        clone.Id = Guid.Empty;
        clone.CreatedAt = DateTime.MinValue;
        clone.ModifiedAt = DateTime.MinValue;
        clone.LastSavedAt = null;
        clone.ContentHash = null;
        clone.StitchPlanHash = null;

        foreach (var obj in clone.Objects)
        {
            obj.Id = Guid.Empty;
            obj.CreatedAt = DateTime.MinValue;
            obj.ModifiedAt = DateTime.MinValue;
        }

        var bytes = SerializeToBytes(clone, pretty: false);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Verifica integridad del archivo
    /// </summary>
    public static bool VerifyIntegrity(string filePath, out string? computedHash, out string? storedHash)
    {
        computedHash = null;
        storedHash = null;

        var project = LoadFromFile(filePath);
        if (project == null) return false;

        storedHash = project.ContentHash;
        computedHash = ComputeContentHash(project);

        return string.Equals(computedHash, storedHash, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Serializador binario compacto para planes de puntada (transferencia a máquina)
/// </summary>
public static class BinaryStitchSerializer
{
    // Magic bytes: "ATB1"
    private static readonly byte[] Magic = { 0x41, 0x54, 0x42, 0x31 };
    private const ushort Version = 1;

    /// <summary>
    /// Serializa StitchPlan a formato binario compacto
    /// </summary>
    public static byte[] Serialize(StitchPlan plan)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        // Header
        bw.Write(Magic);
        bw.Write(Version);
        bw.Write(plan.ProjectId.ToByteArray());
        bw.Write(plan.ProjectName ?? "");
        bw.Write(plan.CompiledAt.ToBinary());
        bw.Write(plan.CanvasWidth);
        bw.Write(plan.CanvasHeight);
        bw.Write(plan.CanvasOrigin.X);
        bw.Write(plan.CanvasOrigin.Y);

        // Paleta de hilos
        bw.Write(plan.ThreadPalette.Count);
        foreach (var color in plan.ThreadPalette)
        {
            bw.Write(color.R);
            bw.Write(color.G);
            bw.Write(color.B);
            bw.Write(color.Brand ?? "");
            bw.Write(color.Code ?? "");
            bw.Write(color.Name ?? "");
            bw.Write(color.Description ?? "");
        }

        // Color -> Needle map
        bw.Write(plan.ColorToNeedleMap.Count);
        foreach (var kvp in plan.ColorToNeedleMap)
        {
            bw.Write(kvp.Key);
            bw.Write(kvp.Value);
        }

        // Machine profile (simplificado)
        if (plan.MachineProfile != null)
        {
            bw.Write(true);
            bw.Write(plan.MachineProfile.Id.ToByteArray());
            bw.Write(plan.MachineProfile.Name ?? "");
            bw.Write(plan.MachineProfile.MaxWidth);
            bw.Write(plan.MachineProfile.MaxHeight);
            bw.Write(plan.MachineProfile.NeedleCount);
            bw.Write(plan.MachineProfile.MaxStitchLength);
            bw.Write(plan.MachineProfile.MaxJumpLength);
        }
        else
        {
            bw.Write(false);
        }

        // Hoop profile
        if (plan.HoopProfile != null)
        {
            bw.Write(true);
            bw.Write(plan.HoopProfile.Id.ToByteArray());
            bw.Write(plan.HoopProfile.Name ?? "");
            bw.Write(plan.HoopProfile.Width);
            bw.Write(plan.HoopProfile.Height);
            bw.Write(plan.HoopProfile.UsableWidth);
            bw.Write(plan.HoopProfile.UsableHeight);
        }
        else
        {
            bw.Write(false);
        }

        // Puntadas globales (secuencia aplanada)
        var allStitches = plan.GetAllStitches();
        bw.Write(allStitches.Count);

        // Escribir puntadas en formato compacto
        // Deltas relativos para compresión
        int lastX = 0, lastY = 0;
        foreach (var stitch in allStitches)
        {
            int deltaX = stitch.X - lastX;
            int deltaY = stitch.Y - lastY;

            // Varint encoding para deltas pequeños
            WriteVarInt(bw, deltaX);
            WriteVarInt(bw, deltaY);
            bw.Write((byte)stitch.Type);
            bw.Write(stitch.Needle);
            bw.Write(stitch.ColorIndex);
            bw.Write(stitch.Flags);

            lastX = stitch.X;
            lastY = stitch.Y;
        }

        // Métricas
        bw.Write(plan.TotalStitches);
        bw.Write(plan.TotalJumps);
        bw.Write(plan.TotalTrims);
        bw.Write(plan.TotalColorChanges);
        bw.Write(plan.TotalStops);
        bw.Write(plan.EstimatedTimeSeconds);
        bw.Write(plan.EstimatedThreadMeters);
        bw.Write(plan.DesignBounds.X);
        bw.Write(plan.DesignBounds.Y);
        bw.Write(plan.DesignBounds.Width);
        bw.Write(plan.DesignBounds.Height);

        bw.Flush();
        return ms.ToArray();
    }

    /// <summary>
    /// Deserializa plan binario
    /// </summary>
    public static StitchPlan? Deserialize(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

        // Verificar magic
        var magic = br.ReadBytes(4);
        if (!magic.SequenceEqual(Magic)) return null;

        var version = br.ReadUInt16();
        if (version > Version) return null; // Versión futura no soportada

        var plan = new StitchPlan
        {
            ProjectId = new Guid(br.ReadBytes(16)),
            ProjectName = br.ReadString(),
            CompiledAt = DateTime.FromBinary(br.ReadInt64()),
            CanvasWidth = br.ReadInt32(),
            CanvasHeight = br.ReadInt32(),
            CanvasOrigin = new Point(br.ReadInt32(), br.ReadInt32())
        };

        // Paleta
        int paletteCount = br.ReadInt32();
        plan.ThreadPalette = new List<ThreadColor>(paletteCount);
        for (int i = 0; i < paletteCount; i++)
        {
            plan.ThreadPalette.Add(new ThreadColor(
                br.ReadByte(), br.ReadByte(), br.ReadByte(),
                br.ReadString(), br.ReadString(), br.ReadString(),
                br.ReadString()));
        }

        // Color -> Needle
        int mapCount = br.ReadInt32();
        plan.ColorToNeedleMap = new Dictionary<int, int>(mapCount);
        for (int i = 0; i < mapCount; i++)
        {
            plan.ColorToNeedleMap[br.ReadInt32()] = br.ReadInt32();
        }

        // Machine profile
        if (br.ReadBoolean())
        {
            plan.MachineProfile = new MachineProfile
            {
                Id = new Guid(br.ReadBytes(16)),
                Name = br.ReadString(),
                MaxWidth = br.ReadInt32(),
                MaxHeight = br.ReadInt32(),
                NeedleCount = br.ReadInt32(),
                MaxStitchLength = br.ReadInt32(),
                MaxJumpLength = br.ReadInt32()
            };
        }

        // Hoop profile
        if (br.ReadBoolean())
        {
            plan.HoopProfile = new HoopProfile
            {
                Id = new Guid(br.ReadBytes(16)),
                Name = br.ReadString(),
                Width = br.ReadInt32(),
                Height = br.ReadInt32(),
                UsableWidth = br.ReadInt32(),
                UsableHeight = br.ReadInt32()
            };
        }

        // Puntadas
        int stitchCount = br.ReadInt32();
        var stitches = new List<StitchPoint>(stitchCount);
        int lastX = 0, lastY = 0;

        for (int i = 0; i < stitchCount; i++)
        {
            int deltaX = ReadVarInt(br);
            int deltaY = ReadVarInt(br);
            var type = (StitchType)br.ReadByte();
            byte needle = br.ReadByte();
            byte colorIndex = br.ReadByte();
            ushort flags = br.ReadUInt16();

            int x = lastX + deltaX;
            int y = lastY + deltaY;

            stitches.Add(new StitchPoint(x, y, type, needle, colorIndex, flags));
            lastX = x;
            lastY = y;
        }

        plan.ObjectStitches[Guid.Empty] = stitches; // Todas en una entrada

        // Métricas
        plan.TotalStitches = br.ReadInt64();
        plan.TotalJumps = br.ReadInt64();
        plan.TotalTrims = br.ReadInt64();
        plan.TotalColorChanges = br.ReadInt64();
        plan.TotalStops = br.ReadInt64();
        plan.EstimatedTimeSeconds = br.ReadDouble();
        plan.EstimatedThreadMeters = br.ReadDouble();
        plan.DesignBounds = new Rectangle(
            br.ReadInt32(), br.ReadInt32(), br.ReadInt32(), br.ReadInt32());

        return plan;
    }

    /// <summary>
    /// Varint encoding para enteros (compresión delta)
    /// </summary>
    private static void WriteVarInt(BinaryWriter bw, int value)
    {
        // Zigzag encoding para negativos
        uint zigzag = (uint)((value << 1) ^ (value >> 31));
        while (zigzag >= 0x80)
        {
            bw.Write((byte)(zigzag | 0x80));
            zigzag >>= 7;
        }
        bw.Write((byte)zigzag);
    }

    private static int ReadVarInt(BinaryReader br)
    {
        uint result = 0;
        int shift = 0;
        byte b;
        do
        {
            b = br.ReadByte();
            result |= (uint)(b & 0x7F) << shift;
            shift += 7;
        } while ((b & 0x80) != 0);

        // Zigzag decode
        return (int)(result >> 1) ^ -(int)(result & 1);
    }
}