namespace AtlasEmbroidery.Domain.Formats.Vp3;

using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FmtValidationIssue = AtlasEmbroidery.Domain.Formats.ValidationIssue;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;
using System.Text;

/// <summary>
/// Husqvarna/Viking VP3 format constants and specifications
/// </summary>
public static class Vp3Spec
{
    // Header constants
    // pyembroidery header: stitchOffset(4) + version(4) + date(20) + reserved(2) + colorCount(4) + pointCount(4)
    // + hoopSize(4) + centerOffsets(16) + hoopEdgeDistances(64) = 122 bytes
    // Then palette indices (colorCount * 4 bytes)
    // Then color entries (colorCount * 4 bytes, 0x0D each)
    public const int HeaderBaseSize = 122; // Base header size before palette
    public const int ColorEntrySize = 4; // 4 bytes per palette index + 4 bytes per color entry (0x0D)
    public const int MaxStitchDistance = 127; // signed 8-bit
    public const int MaxJumpDistance = 127;
    public const int MicronsPerVp3Unit = 100; // 0.1mm = 100 microns
    public const int MaxColors = 255;

    // Hoop types
    public const int Hoop110x110 = 0;
    public const int Hoop50x50 = 1;
    public const int Hoop140x200 = 2;
    public const int Hoop126x110 = 3;
    public const int Hoop200x200 = 4;

    // Control prefix
    public const byte ControlPrefix = 0x80;

    // Control codes (after 0x80 prefix)
    // Note: In pyembroidery, CtrlJump = 0x02, CtrlColorChange = 0x01, CtrlEnd = 0x10
    // Trim uses same as jump (0x02) with zero coordinates
    public const byte CtrlJump = 0x02;
    public const byte CtrlColorChange = 0x01;
    public const byte CtrlEnd = 0x10;
    // CtrlStop = 0x01 (same as color change)
    // CtrlTrim = 0x02 (same as jump)

    // Regular stitch: no prefix, just 2 bytes (signed dx, signed dy with Y negated)
    // Jump/Color/Stop: 0x80 + ctrl + 2 bytes coords
    // END: 0x80 + 0x10
}

/// <summary>
/// VP3 thread color (maps to JEF thread palette indices)
/// </summary>
public sealed class Vp3Thread
{
    public int PaletteIndex { get; set; }
    public string Description { get; set; } = "";
    public string CatalogNumber { get; set; } = "";
    public ThreadColor? RgbColor { get; set; }
    
    public ThreadColor ToThreadColor()
    {
        if (RgbColor.HasValue)
            return RgbColor.Value;
        return new ThreadColor(0, 0, 0, "Husqvarna", CatalogNumber, Description);
    }
}

/// <summary>
/// VP3 stitch encoder - uses signed 8-bit deltas with 0x80 prefix for controls
/// </summary>
public static class Vp3MovementEncoder
{
    /// <summary>
    /// Encodes a regular stitch (no prefix, 2 bytes)
    /// </summary>
    public static byte[] EncodeStitch(int deltaX, int deltaY)
    {
        if (deltaX < -128 || deltaX > 127)
            throw new ArgumentOutOfRangeException(nameof(deltaX), "Delta X must fit in signed byte");
        if (deltaY < -128 || deltaY > 127)
            throw new ArgumentOutOfRangeException(nameof(deltaY), "Delta Y must fit in signed byte");

        // VP3 negates Y
        byte y = (byte)(-deltaY);
        byte x = (byte)deltaX;
        return new byte[] { x, y };
    }

    /// <summary>
    /// Encodes a jump/move (0x80 0x02 + 2 bytes coords)
    /// </summary>
    public static byte[] EncodeJump(int deltaX, int deltaY)
    {
        if (deltaX < -128 || deltaX > 127)
            throw new ArgumentOutOfRangeException(nameof(deltaX), "Delta X must fit in signed byte");
        if (deltaY < -128 || deltaY > 127)
            throw new ArgumentOutOfRangeException(nameof(deltaY), "Delta Y must fit in signed byte");

        byte y = (byte)(-deltaY);
        byte x = (byte)deltaX;
        return new byte[] { Vp3Spec.ControlPrefix, Vp3Spec.CtrlJump, x, y };
    }

    /// <summary>
    /// Encodes a color change (0x80 0x01 + 2 bytes coords)
    /// </summary>
    public static byte[] EncodeColorChange(int deltaX, int deltaY)
    {
        if (deltaX < -128 || deltaX > 127)
            throw new ArgumentOutOfRangeException(nameof(deltaX), "Delta X must fit in signed byte");
        if (deltaY < -128 || deltaY > 127)
            throw new ArgumentOutOfRangeException(nameof(deltaY), "Delta Y must fit in signed byte");

        byte y = (byte)(-deltaY);
        byte x = (byte)deltaX;
        return new byte[] { Vp3Spec.ControlPrefix, Vp3Spec.CtrlColorChange, x, y };
    }

    /// <summary>
    /// Encodes a stop (same as color change in VP3)
    /// </summary>
    public static byte[] EncodeStop(int deltaX, int deltaY)
    {
        return EncodeColorChange(deltaX, deltaY);
    }

    /// <summary>
    /// Encodes END marker (0x80 0x10)
    /// </summary>
    public static byte[] EncodeEnd()
    {
        return new byte[] { Vp3Spec.ControlPrefix, Vp3Spec.CtrlEnd };
    }

    /// <summary>
    /// Encodes trim sequence (multiple 0x80 0x02 0x00 0x00)
    /// </summary>
    public static byte[] EncodeTrim(int count = 3)
    {
        var result = new List<byte>();
        for (int i = 0; i < count; i++)
        {
            result.AddRange(new byte[] { Vp3Spec.ControlPrefix, Vp3Spec.CtrlJump, 0x00, 0x00 });
        }
        return result.ToArray();
    }
}

/// <summary>
/// VP3 stitch decoder
/// </summary>
public static class Vp3MovementDecoder
{
    public static (int deltaX, int deltaY, Vp3Control control, int bytesConsumed) DecodeNext(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(offset));

        byte first = data[offset];

        if (first != Vp3Spec.ControlPrefix)
        {
            // Regular stitch (2 bytes)
            if (offset + 1 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(data), "Incomplete stitch record");
            
            int dx1 = (sbyte)first;
            int dy1 = -(sbyte)data[offset + 1];
            return (dx1, dy1, Vp3Control.Normal, 2);
        }

        // Control code (4 bytes: 0x80 + ctrl + dx + dy, except END which is 2 bytes)
        if (offset + 1 >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(data), "Incomplete control record");

        byte ctrl = data[offset + 1];

        if (ctrl == Vp3Spec.CtrlEnd)
        {
            return (0, 0, Vp3Control.End, 2);
        }

        if (offset + 3 >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(data), "Incomplete control record with coords");

        int dx2 = (sbyte)data[offset + 2];
        int dy2 = -(sbyte)data[offset + 3];

        Vp3Control control = ctrl switch
        {
            Vp3Spec.CtrlJump => Vp3Control.Jump,
            Vp3Spec.CtrlColorChange => Vp3Control.ColorChange,
            _ => Vp3Control.Unknown
        };

        return (dx2, dy2, control, 4);
    }
}

[Flags]
public enum Vp3Control : byte
{
    Normal = 0,
    Jump = 1,
    ColorChange = 2,
    Stop = 4,
    Trim = 8,
    End = 16,
    Unknown = 32
}

/// <summary>
/// VP3 format adapter - reads and writes Husqvarna/Viking VP3 embroidery files
/// </summary>
public sealed class Vp3FormatAdapter : IEmbroideryFormatReader, IEmbroideryFormatWriter, IEmbroideryFormatValidator
{
    public string FormatName => "VP3";
    public string FileExtension => ".vp3";
    public string[] Extensions => new[] { ".vp3", ".VP3" };
    public string MimeType => "application/x-vp3";
    public string DefaultExtension => ".vp3";

    public FormatCapabilities Capabilities => new()
    {
        SupportsReading = true,
        SupportsWriting = true,
        SupportsTrim = true,
        SupportsJump = true,
        SupportsColorChange = true,
        SupportsStop = true,
        MaxStitchLength = Vp3Spec.MaxStitchDistance * Vp3Spec.MicronsPerVp3Unit,
        MaxJumpLength = Vp3Spec.MaxJumpDistance * Vp3Spec.MicronsPerVp3Unit,
        MaxTotalStitches = 500_000,
        MaxColors = Vp3Spec.MaxColors,
    };

    /// <summary>
    /// Reads a VP3 file and returns an AtlasProject
    /// </summary>
    public async Task<AtlasProject> ReadAsync(Stream stream, FormatReadOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatReadOptions();

        var project = new AtlasProject();

        // Check minimum header size
        if (stream.Length < Vp3Spec.HeaderBaseSize)
            throw new InvalidDataException($"VP3 file too short: {stream.Length} bytes, expected at least {Vp3Spec.HeaderBaseSize}");

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        // Read stitch offset (4 bytes, little endian)
        int stitchOffset = reader.ReadInt32();
        if (stitchOffset < 0 || stitchOffset > stream.Length)
            throw new InvalidDataException($"Invalid stitch offset: {stitchOffset}");

        // Skip 20 bytes
        reader.BaseStream.Seek(20, SeekOrigin.Current);

        // Read color count
        int colorCount = reader.ReadInt32();
        if (colorCount < 0 || colorCount > Vp3Spec.MaxColors)
            throw new InvalidDataException($"Invalid color count: {colorCount}");

        // Skip 88 bytes
        reader.BaseStream.Seek(88, SeekOrigin.Current);

        // Read color palette indices (4 bytes per color index)
        var paletteIndices = new List<int>();
        for (int i = 0; i < colorCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            int idx = reader.ReadInt32();
            paletteIndices.Add(idx);
        }

        // Skip colorCount * 4 bytes (0x0D entries)
        reader.BaseStream.Seek(colorCount * 4, SeekOrigin.Current);

        // Seek to stitch data
        reader.BaseStream.Seek(stitchOffset, SeekOrigin.Begin);

        // Read stitches
        var stitches = new List<StitchPoint>();
        int currentX = 0, currentY = 0;
        int colorIndex = 0;
        bool firstStitchRead = true;

        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            ct.ThrowIfCancellationRequested();

            if (reader.BaseStream.Position + 1 >= reader.BaseStream.Length)
                break;

            byte b1 = reader.ReadByte();
            byte b2 = reader.ReadByte();

            if (b1 != Vp3Spec.ControlPrefix)
            {
                // Regular stitch
                int deltaX = (sbyte)b1;
                int deltaY = -(sbyte)b2;

                currentX += deltaX;
                currentY += deltaY;

                var stitch = new StitchPoint(
                    currentX * Vp3Spec.MicronsPerVp3Unit,
                    -currentY * Vp3Spec.MicronsPerVp3Unit,
                    StitchType.Running,
                    (byte)(colorIndex + 1),
                    (byte)colorIndex,
                    0
                );
                stitches.Add(stitch);

                if (firstStitchRead) firstStitchRead = false;
                continue;
            }

            // Control code
            if (reader.BaseStream.Position >= reader.BaseStream.Length)
                break;
            byte ctrl = reader.ReadByte();

            if (ctrl == Vp3Spec.CtrlEnd)
            {
                break;
            }

            // Read coordinates for control codes
            if (reader.BaseStream.Position + 1 >= reader.BaseStream.Length)
                break;
            byte cx = reader.ReadByte();
            byte cy = reader.ReadByte();
            int cDeltaX = (sbyte)cx;
            int cDeltaY = -(sbyte)cy;

            currentX += cDeltaX;
            currentY += cDeltaY;

            if (ctrl == Vp3Spec.CtrlJump)
            {
                var jumpStitch = new StitchPoint(
                    currentX * Vp3Spec.MicronsPerVp3Unit,
                    -currentY * Vp3Spec.MicronsPerVp3Unit,
                    StitchType.Jump,
                    (byte)(colorIndex + 1),
                    (byte)colorIndex,
                    0
                );
                stitches.Add(jumpStitch);
            }
            else if (ctrl == Vp3Spec.CtrlColorChange)
            {
                colorIndex = (colorIndex + 1) % Math.Max(1, project.ThreadPalette.Count);
                var ccStitch = new StitchPoint(
                    currentX * Vp3Spec.MicronsPerVp3Unit,
                    -currentY * Vp3Spec.MicronsPerVp3Unit,
                    StitchType.Stop,
                    (byte)(colorIndex + 1),
                    (byte)colorIndex,
                    0
                );
                stitches.Add(ccStitch);
            }
            // Trim uses same code as jump (0x02) with zero coords - treated as jump here
        }

        // Build project from stitches
        if (stitches.Count > 0)
        {
            var shape = new ShapeObject
            {
                Name = "VP3 Import",
                Vertices = stitches.Select(s => new Point(s.X, s.Y)).ToList(),
                IsClosed = false,
                StitchParams = StitchParams.DefaultFor(StitchType.Running)
            };
            shape.StitchParams.Density = 4000;
            project.Objects.Add(shape);
        }

        project.CustomData["vp3_stitch_offset"] = stitchOffset;
        project.CustomData["vp3_color_count"] = colorCount;

        return project;
    }

    /// <summary>
    /// Writes an AtlasProject to VP3 format
    /// </summary>
    public async Task WriteAsync(AtlasProject project, Stream stream, FormatWriteOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatWriteOptions();

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Compile to stitch plan
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        var allStitches = plan.GetAllStitches().ToList();

        // Calculate bounds
        int minX = allStitches.Count > 0 ? allStitches.Min(s => s.X / Vp3Spec.MicronsPerVp3Unit) : 0;
        int maxX = allStitches.Count > 0 ? allStitches.Max(s => s.X / Vp3Spec.MicronsPerVp3Unit) : 0;
        int minY = allStitches.Count > 0 ? allStitches.Min(s => s.Y / Vp3Spec.MicronsPerVp3Unit) : 0;
        int maxY = allStitches.Count > 0 ? allStitches.Max(s => s.Y / Vp3Spec.MicronsPerVp3Unit) : 0;

        int designWidth = maxX - minX;
        int designHeight = maxY - minY;
        int halfWidth = designWidth / 2;
        int halfHeight = designHeight / 2;

        // Build color palette (map to JEF thread indices - simplified)
        var paletteIndices = new List<int>();
        int currentColor = -1;
        int lastPaletteIndex = -1;
        bool firstStitchWrite = true;

        foreach (var stitch in allStitches)
        {
            if (stitch.ColorIndex != currentColor)
            {
                if (!firstStitchWrite)
                {
                    // This will be handled in stitch writing
                }
                currentColor = stitch.ColorIndex;
                // Map to JEF palette index (simplified: use color index modulo palette size)
                int jefIndex = (currentColor % 64) + 1; // JEF has 64 colors, 1-based
                paletteIndices.Add(jefIndex);
                lastPaletteIndex = jefIndex;
            }
            firstStitchWrite = false;
        }

        int colorCount = paletteIndices.Count;
        int stitchOffset = Vp3Spec.HeaderBaseSize + (colorCount * 4); // 4 bytes per color index

        // Count total stitch points for header
        int pointCount = 1; // END marker
        foreach (var stitch in allStitches)
        {
            if (stitch.IsJump)
                pointCount += 2;
            else if (stitch.Type == StitchType.Trim)
                pointCount += 6; // 3 trim commands * 2
            else if (stitch.Type == StitchType.Stop || IsColorChange(allStitches, stitch))
                pointCount += 2;
            else
                pointCount += 1;
        }

        // Write header
        writer.Write(stitchOffset); // stitch offset
        writer.Write(0x14); // version/flags
        writer.Write(Encoding.ASCII.GetBytes(DateTime.UtcNow.ToString("yyyyMMddHHmmss").PadRight(20, '\0'))); // date string (20 bytes)
        writer.Write((byte)0); // reserved
        writer.Write((byte)0); // reserved
        writer.Write(colorCount); // color count
        writer.Write(pointCount); // point count

        // Hoop size
        int hoopSize = GetVp3HoopSize(designWidth, designHeight);
        writer.Write(hoopSize);

        // Design center offsets
        writer.Write(halfWidth);
        writer.Write(halfHeight);
        writer.Write(halfWidth);
        writer.Write(halfHeight);

        // Hoop edge distances for 4 hoop types
        WriteHoopEdgeDistance(writer, 550 - halfWidth, 550 - halfHeight); // 110x110
        WriteHoopEdgeDistance(writer, 250 - halfWidth, 250 - halfHeight); // 50x50
        WriteHoopEdgeDistance(writer, 700 - halfWidth, 1000 - halfHeight); // 140x200
        WriteHoopEdgeDistance(writer, 700 - halfWidth, 1000 - halfHeight); // custom

        // Color palette
        foreach (int idx in paletteIndices)
        {
            writer.Write(idx);
        }

        // Color entries (0x0D)
        for (int i = 0; i < colorCount; i++)
        {
            writer.Write(0x0D);
        }

        // Write stitches
        int curX = 0, curY = 0;
        int currentColorIdx = -1;
        bool firstStitch = true;

        foreach (var stitch in allStitches)
        {
            ct.ThrowIfCancellationRequested();

            int targetX = stitch.X / Vp3Spec.MicronsPerVp3Unit;
            int targetY = -stitch.Y / Vp3Spec.MicronsPerVp3Unit; // VP3 negates Y
            int deltaX = targetX - curX;
            int deltaY = targetY - curY;

            // Handle color change
            if (stitch.ColorIndex != currentColorIdx)
            {
                if (!firstStitch)
                {
                    writer.Write(Vp3MovementEncoder.EncodeColorChange(deltaX, deltaY));
                }
                else
                {
                    // First color - just regular stitch
                    writer.Write(Vp3MovementEncoder.EncodeStitch(deltaX, deltaY));
                }
                currentColorIdx = stitch.ColorIndex;
            }
            else if (stitch.IsJump)
            {
                writer.Write(Vp3MovementEncoder.EncodeJump(deltaX, deltaY));
            }
            else if (stitch.Type == StitchType.Trim)
            {
                writer.Write(Vp3MovementEncoder.EncodeTrim(3));
            }
            else
            {
                writer.Write(Vp3MovementEncoder.EncodeStitch(deltaX, deltaY));
            }

            curX = targetX;
            curY = targetY;
            firstStitch = false;
        }

        // Write END marker
        writer.Write(Vp3MovementEncoder.EncodeEnd());
    }

    private bool IsColorChange(List<StitchPoint> allStitches, StitchPoint current)
    {
        int idx = allStitches.IndexOf(current);
        if (idx == 0) return false;
        return allStitches[idx - 1].ColorIndex != current.ColorIndex;
    }

    private int GetVp3HoopSize(int width, int height)
    {
        if (width < 500 && height < 500) return Vp3Spec.Hoop50x50;
        if (width < 1260 && height < 1100) return Vp3Spec.Hoop126x110;
        if (width < 1400 && height < 2000) return Vp3Spec.Hoop140x200;
        if (width < 2000 && height < 2000) return Vp3Spec.Hoop200x200;
        return Vp3Spec.Hoop110x110;
    }

    private void WriteHoopEdgeDistance(BinaryWriter writer, int xEdge, int yEdge)
    {
        if (xEdge >= 0 && yEdge >= 0)
        {
            writer.Write(xEdge); // left
            writer.Write(yEdge); // top
            writer.Write(xEdge); // right
            writer.Write(yEdge); // bottom
        }
        else
        {
            writer.Write(-1);
            writer.Write(-1);
            writer.Write(-1);
            writer.Write(-1);
        }
    }

    public FormatValidationResult Validate(Stream stream)
    {
        return ValidateAsync(stream).GetAwaiter().GetResult();
    }

    public async Task<FormatValidationResult> ValidateAsync(Stream stream)
    {
        var result = new FormatValidationResult
        {
            FormatName = FormatName,
            IsValid = true,
            Issues = new List<FmtValidationIssue>()
        };

        try
        {
            long originalPosition = stream.Position;
            stream.Position = 0;

            if (stream.Length < Vp3Spec.HeaderBaseSize)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "VP3.TOO_SHORT",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "VP3 file too short for header",
                    Evidence = $"File length: {stream.Length}, expected at least {Vp3Spec.HeaderBaseSize}",
                    Recommendation = "VP3 files must have at least 116-byte header"
                });
                return result;
            }

            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            int stitchOffset = reader.ReadInt32();
            
            if (stitchOffset < Vp3Spec.HeaderBaseSize || stitchOffset > stream.Length)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "VP3.INVALID_STITCH_OFFSET",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "Invalid stitch offset in VP3 header",
                    Evidence = $"Stitch offset: {stitchOffset}, file length: {stream.Length}",
                    Recommendation = "File may be truncated or corrupt"
                });
            }

            stream.Position = originalPosition;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "VP3.VALIDATION_EXCEPTION",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Validation failed: {ex.Message}",
                Evidence = ex.ToString(),
                Recommendation = "File is not a valid VP3 format"
            });
        }

        return result;
    }

    public FormatValidationResult Validate(AtlasProject project, MachineProfile? machine = null, HoopProfile? hoop = null)
    {
        var result = new FormatValidationResult
        {
            FormatName = FormatName,
            IsValid = true,
            Issues = new List<FmtValidationIssue>()
        };

        if (project.ThreadPalette.Count > Vp3Spec.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "VP3.TOO_MANY_COLORS",
                Severity = FmtValidationSeverity.Critical,
                Message = $"VP3 supports maximum {Vp3Spec.MaxColors} colors",
                Evidence = $"Project has {project.ThreadPalette.Count} colors",
                Recommendation = "Reduce color count or split design"
            });
        }

        var engine = new StitchEngine();
        var stitchPlan = engine.Compile(project);
        var allStitches = stitchPlan.GetAllStitches().ToList();

        int lastX = 0, lastY = 0;
        foreach (var stitch in allStitches)
        {
            int deltaX = Math.Abs(stitch.X / Vp3Spec.MicronsPerVp3Unit - lastX);
            int deltaY = Math.Abs(stitch.Y / Vp3Spec.MicronsPerVp3Unit - lastY);
            int maxDelta = Math.Max(deltaX, deltaY);

            if (maxDelta > Vp3Spec.MaxStitchDistance)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "VP3.MOVEMENT_EXCEEDS_MAX",
                    Severity = FmtValidationSeverity.Warning,
                    Message = $"Movement exceeds VP3 max delta per record ({Vp3Spec.MaxStitchDistance})",
                    Evidence = $"Delta: ({deltaX}, {deltaY}) at ({stitch.X}, {stitch.Y})",
                    Recommendation = "Long movements will be split into multiple records"
                });
            }
            lastX = stitch.X / Vp3Spec.MicronsPerVp3Unit;
            lastY = stitch.Y / Vp3Spec.MicronsPerVp3Unit;
        }

        result.DetectedCapabilities = Capabilities;
        return result;
    }

    public FormatValidationResult Validate(StitchPlan plan, MachineProfile? machine = null, HoopProfile? hoop = null)
    {
        var result = new FormatValidationResult
        {
            FormatName = FormatName,
            IsValid = true,
            Issues = new List<FmtValidationIssue>()
        };

        if (plan.TotalStitches > Capabilities.MaxTotalStitches)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "VP3.TOO_MANY_STITCHES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalStitches} stitches, VP3 supports max {Capabilities.MaxTotalStitches}",
                Evidence = $"Total stitches: {plan.TotalStitches}",
                Recommendation = "Simplify design or split into multiple files"
            });
        }

        if (plan.TotalColorChanges > Vp3Spec.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "VP3.TOO_MANY_COLOR_CHANGES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalColorChanges} color changes, VP3 supports max {Vp3Spec.MaxColors}",
                Evidence = $"Color changes: {plan.TotalColorChanges}",
                Recommendation = "Reduce color changes or split design"
            });
        }

        return result;
    }
}