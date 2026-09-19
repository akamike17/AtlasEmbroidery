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

        // CORRECCIÓN 4: Budget global del binario
        private const int MAX_BINARY_DOCUMENT_BYTES = 50_000_000; // 50 MB máximo

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

            // CORRECCIÓN 4: Budget check en serialización
            // Estimar tamaño mínimo del documento
            long estimatedSize = ms.Position + (long)allStitches.Count * 9 + 500; // 9 bytes/stitch + métricas
            if (estimatedSize > MAX_BINARY_DOCUMENT_BYTES)
                throw new InvalidOperationException($"Document exceeds maximum size: {estimatedSize} > {MAX_BINARY_DOCUMENT_BYTES}");

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
                bw.Write(stitch.SequenceIndex); // Version 1+

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
        if (data == null || data.Length < 6) // Magic(4) + Version(2)
            return null;

        try
        {
            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms, Encoding.UTF8, leaveOpen: true);

            // Verificar magic
            var magic = br.ReadBytes(4);
            if (!magic.SequenceEqual(Magic)) return null;

            var version = br.ReadUInt16();
            // CORRECCIÓN 1: Solo aceptar versión 1 explícitamente
            if (version != Version) return null; // Reject version 0, future versions, etc.

            var plan = new StitchPlan
            {
                ProjectId = new Guid(br.ReadBytes(16)),
                ProjectName = ReadStringSafe(br),
                CompiledAt = DateTime.FromBinary(br.ReadInt64()),
                CanvasWidth = br.ReadInt32(),
                CanvasHeight = br.ReadInt32(),
                CanvasOrigin = new Point(br.ReadInt32(), br.ReadInt32())
            };

            // Paleta - límite defensivo
            int paletteCount = br.ReadInt32();
            if (paletteCount < 0 || paletteCount > 10000) return null;
            plan.ThreadPalette = new List<ThreadColor>(paletteCount);
            for (int i = 0; i < paletteCount; i++)
            {
                plan.ThreadPalette.Add(new ThreadColor(
                    br.ReadByte(), br.ReadByte(), br.ReadByte(),
                    ReadStringSafe(br), ReadStringSafe(br), ReadStringSafe(br),
                    ReadStringSafe(br)));
            }

            // Color -> Needle
            int mapCount = br.ReadInt32();
            if (mapCount < 0 || mapCount > 10000) return null;
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
                    Name = ReadStringSafe(br),
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
                    Name = ReadStringSafe(br),
                    Width = br.ReadInt32(),
                    Height = br.ReadInt32(),
                    UsableWidth = br.ReadInt32(),
                    UsableHeight = br.ReadInt32()
                };
            }

            // Puntadas - límite defensivo + CORRECCIÓN 5: Budget check
            int stitchCount = br.ReadInt32();
            if (stitchCount < 0 || stitchCount > 10000000) return null; // 10M max
            
            // Budget check: estimación conservadora de bytes mínimos por puntada
            long minBytesNeeded = (long)stitchCount * 9;
            long remainingBytes = ms.Length - ms.Position;
            if (remainingBytes < minBytesNeeded) return null; // Archivo truncado o count inflado

            // CORRECCIÓN 4: Check global document size budget
            if (ms.Length > MAX_BINARY_DOCUMENT_BYTES) return null;

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
                ushort sequenceIndex = br.ReadUInt16(); // Version 1+

                int x = lastX + deltaX;
                int y = lastY + deltaY;

                stitches.Add(new StitchPoint(x, y, type, needle, colorIndex, flags, sequenceIndex));
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

            // CORRECCIÓN 6: Trailing data check - para versión cerrada, stream debe estar consumido completamente
            if (ms.Position != ms.Length)
                return null; // Datos trailing no permitidos

            // CORRECCIÓN 5: Validación de métricas binarias
            if (plan.TotalStitches < 0 || plan.TotalJumps < 0 || plan.TotalTrims < 0 || 
                plan.TotalColorChanges < 0 || plan.TotalStops < 0)
                return null;

            if (!double.IsFinite(plan.EstimatedTimeSeconds) || !double.IsFinite(plan.EstimatedThreadMeters) ||
                plan.EstimatedTimeSeconds < 0 || plan.EstimatedThreadMeters < 0)
                return null;

            // CORRECCIÓN 6: Consistencia de métricas - TotalStitches no debe exceder stitchCount
            // stitchCount incluye todas las puntadas serializadas (incluyendo jumps, trims, tie-in, tie-off, underlay)
            // TotalStitches es el contador de puntadas de costura "reales"
            // Validación: TotalStitches <= stitchCount
            if (plan.TotalStitches > stitchCount)
                return null;

            return plan;
        }
        catch (EndOfStreamException)
        {
            return null; // Datos truncados en cualquier parte
        }
        catch (InvalidDataException)
        {
            return null; // Datos de formato inválido
        }
        // CORRECCIÓN 1: Eliminado catch (Exception) genérico - bugs internos deben propagarse
    }

    /// <summary>
    /// Lee string con validación explícita de longitud ANTES de materializar (CORRECCIÓN 2)
    /// UTF-8 estricto con DecoderFallback.ExceptionFallback (CORRECCIÓN 2)
    /// </summary>
    private static string ReadStringSafe(BinaryReader br)
    {
        try
        {
            // Leer longitud 7-bit encoded Int32 (igual que BinaryWriter.Write(string))
            int length = 0;
            int shift = 0;
            byte b;
            do
            {
                if (shift >= 35) // Máx 5 bytes para Int32 7-bit encoded
                    throw new InvalidDataException("String length exceeds maximum encoding");
                
                b = br.ReadByte();
                length |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);

            // Validar longitud ANTES de allocation (CORRECCIÓN 3)
            const int MAX_STRING_BYTES = 10000;
            if (length < 0 || length > MAX_STRING_BYTES)
                throw new InvalidDataException($"Invalid string length: {length}");

            // Verificar bytes disponibles
            long remaining = br.BaseStream.Length - br.BaseStream.Position;
            if (remaining < length)
                throw new EndOfStreamException($"Insufficient bytes for string: need {length}, have {remaining}");

            // Leer exactamente N bytes
            byte[] bytes = br.ReadBytes(length);
            if (bytes.Length != length)
                throw new EndOfStreamException("Premature end of stream reading string");

            // CORRECCIÓN 2: UTF-8 estricto - rechazar secuencias inválidas
            var decoder = Encoding.UTF8.GetDecoder();
            // Usar decoder con ExceptionFallback para rechazar UTF-8 inválido
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return encoding.GetString(bytes);
        }
        catch (EndOfStreamException)
        {
            throw; // Propagar para que Deserialize lo capture
        }
        catch (InvalidDataException)
        {
            throw; // Propagar
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException("Invalid UTF-8 sequence in string");
        }
        catch (Exception)
        {
            throw new InvalidDataException("Failed to decode string");
        }
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
            int bytesRead = 0;
            do
            {
                if (bytesRead >= 5) // Max 5 bytes for int32 varint (prevents infinite loop on corrupt data)
                    throw new InvalidDataException("Varint exceeds maximum length");
                
                b = br.ReadByte();
            
                // CORRECCIÓN 4: Validar el 5to byte - para int32, el 5to byte solo puede tener los 4 bits bajos válidos
                if (bytesRead == 4 && (b & 0xF0) != 0)
                    throw new InvalidDataException("Invalid varint: 5th byte has invalid high bits");
            
                result |= (uint)(b & 0x7F) << shift;
                shift += 7;
                bytesRead++;
            } while ((b & 0x80) != 0);

            // Zigzag decode
            return (int)(result >> 1) ^ -(int)(result & 1);
        }
}