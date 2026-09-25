namespace AtlasEmbroidery.Domain.Formats.Pec;

using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FmtValidationIssue = AtlasEmbroidery.Domain.Formats.ValidationIssue;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;
using System.Text;

/// <summary>
/// Brother PEC format adapter - reads and writes PEC embroidery files
/// </summary>
public sealed class PecFormatAdapter : IEmbroideryFormatReader, IEmbroideryFormatWriter, IEmbroideryFormatValidator
{
    public string FormatName => "PEC";
    public string FileExtension => ".pec";
    public string[] Extensions => new[] { ".pec", ".PEC" };
    public string MimeType => "application/x-pec";
    public string DefaultExtension => ".pec";

    public FormatCapabilities Capabilities => new()
    {
        SupportsReading = true,
        SupportsWriting = true,
        SupportsTrim = true,
        SupportsJump = true,
        SupportsColorChange = true,
        SupportsStop = true,
        MaxStitchLength = PecSpec.MaxDeltaPerRecord * PecSpec.MicronsPerPecUnit,
        MaxJumpLength = PecSpec.MaxDeltaPerRecord * PecSpec.MicronsPerPecUnit,
        MaxTotalStitches = 2_000_000,
        MaxColors = PecSpec.MaxColors,
    };

    /// <summary>
    /// Reads a PEC file and returns an AtlasProject
    /// </summary>
    public async Task<AtlasProject> ReadAsync(Stream stream, FormatReadOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatReadOptions();

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var project = new AtlasProject();

        // Read signature
        byte[] signatureBytes = reader.ReadBytes(8);
        string signature = Encoding.ASCII.GetString(signatureBytes).TrimEnd('\0', ' ');
        if (signature != PecSpec.Signature)
        {
            throw new InvalidDataException($"Invalid PEC signature: expected '{PecSpec.Signature}', got '{signature}'");
        }

        // Skip "LA:" (3 bytes)
        reader.BaseStream.Seek(3, SeekOrigin.Current);

        // Read label (16 bytes)
        byte[] labelBytes = reader.ReadBytes(16);
        string label = Encoding.ASCII.GetString(labelBytes).TrimEnd('\0', ' ').Trim();
        if (!string.IsNullOrEmpty(label))
        {
            project.Name = label;
        }

        // Skip 0xF bytes
        reader.BaseStream.Seek(0xF, SeekOrigin.Current);

        // Read icon stride and height
        byte stride = reader.ReadByte();
        byte height = reader.ReadByte();

        // Skip 0xC bytes
        reader.BaseStream.Seek(0xC, SeekOrigin.Current);

        // Read color changes
        byte colorChanges = reader.ReadByte();
        int colorCount = colorChanges + 1;
        if (colorChanges == 0xFF) colorCount = 0;

        // Read color bytes
        byte[] colorBytes = reader.ReadBytes(colorCount);

        // Map PEC colors to thread palette
        var threadSet = PecThreadSet.GetThreadSet();
        for (int i = 0; i < colorBytes.Length; i++)
        {
            int colorIdx = colorBytes[i] % threadSet.Count;
            var thread = threadSet[colorIdx];
            project.ThreadPalette.Add(new ThreadColor(thread.R, thread.G, thread.B, "Brother", thread.Code, thread.Name, thread.Description));
            project.ColorToNeedleMap[i] = i + 1;
        }

        // Skip to stitch block end position
        reader.BaseStream.Seek(0x1D0 - colorChanges, SeekOrigin.Current);

        // Read stitch block end position (24-bit little endian)
        int stitchBlockEnd = ReadInt24LE(reader) - 5 + (int)reader.BaseStream.Position;

        // Skip 0x0B bytes (31 0xFF 0xF0 + 4 shorts = 11 bytes)
        reader.BaseStream.Seek(0x0B, SeekOrigin.Current);

        // Read stitches
        var stitches = new List<StitchPoint>();
        int currentX = 0, currentY = 0;
        int colorIndex = 0;
        bool firstStitch = true;

        while (reader.BaseStream.Position < reader.BaseStream.Length && reader.BaseStream.Position < stitchBlockEnd)
        {
            ct.ThrowIfCancellationRequested();

            if (reader.BaseStream.Position + 1 >= reader.BaseStream.Length)
                break;

            byte val1 = reader.ReadByte();
            byte val2 = reader.ReadByte();

            // END marker
            if (val1 == PecSpec.EndMarker && val2 == 0x00)
                break;

            // Color change marker
            if (val1 == PecSpec.ColorChangeMarker1 && val2 == PecSpec.ColorChangeMarker2)
            {
                if (reader.BaseStream.Position >= reader.BaseStream.Length)
                    break;
                byte ccIndex = reader.ReadByte();
                colorIndex = ccIndex % project.ThreadPalette.Count;
                continue;
            }

            bool xLong = (val1 & PecSpec.FlagLong) != 0;

            int deltaX, deltaY;
            PecControl control;

            if (xLong)
            {
                if (reader.BaseStream.Position + 1 >= reader.BaseStream.Length)
                    break;
                byte val3 = reader.ReadByte();
                byte val4 = reader.ReadByte();

                int xCode = (val1 << 8) | val2;
                int yCode = (val3 << 8) | val4;

                deltaX = Decode12Bit(xCode);
                deltaY = Decode12Bit(yCode);

                bool jump = (val1 & PecSpec.JumpCode) != 0 || (val3 & PecSpec.JumpCode) != 0;
                bool trim = (val1 & PecSpec.TrimCode) != 0 || (val3 & PecSpec.TrimCode) != 0;

                control = jump ? PecControl.Jump : (trim ? PecControl.Trim : PecControl.Normal);
            }
            else
            {
                deltaX = Decode7Bit(val1);
                deltaY = Decode7Bit(val2);
                control = PecControl.Normal;
            }

            currentX += deltaX;
            currentY += deltaY;

            var stitchType = control switch
            {
                PecControl.Jump => StitchType.Jump,
                PecControl.Trim => StitchType.Trim,
                _ => StitchType.Running
            };

            var stitch = new StitchPoint(
                currentX * PecSpec.MicronsPerPecUnit,
                currentY * PecSpec.MicronsPerPecUnit,
                stitchType,
                (byte)(colorIndex + 1),
                (byte)colorIndex,
                0
            );
            stitches.Add(stitch);

            if (firstStitch)
            {
                firstStitch = false;
            }
        }

        // Build project from stitches
        if (stitches.Count > 0)
        {
            var shapeObj = new ShapeObject
            {
                Name = "Imported PEC",
                Vertices = stitches.Select(s => new Point(s.X, s.Y)).ToList(),
                IsClosed = false,
                StitchParams = StitchParams.DefaultFor(StitchType.Running)
            };
            shapeObj.StitchParams.Density = 4000;
            project.Objects.Add(shapeObj);
        }

        return project;
    }

    private static int Decode7Bit(byte b) => b <= 63 ? b : b - 128;
    private static int Decode12Bit(int code)
    {
        int value = code & 0xFFF;
        return value > 0x7FF ? value - 0x1000 : value;
    }

    private static int ReadInt24LE(BinaryReader reader)
    {
        byte b1 = reader.ReadByte();
        byte b2 = reader.ReadByte();
        byte b3 = reader.ReadByte();
        return b1 | (b2 << 8) | (b3 << 16);
    }

    /// <summary>
    /// Writes an AtlasProject to PEC format
    /// </summary>
    public async Task WriteAsync(AtlasProject project, Stream stream, FormatWriteOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatWriteOptions();

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Compile to stitch plan
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        var allStitches = plan.GetAllStitches().ToList();

        // Write signature
        writer.Write(Encoding.ASCII.GetBytes(PecSpec.Signature));

        // Write label (16 bytes, padded)
        string name = project.Name ?? "Untitled";
        byte[] labelBytes = Encoding.ASCII.GetBytes(name.Length > 8 ? name[..8] : name.PadRight(8));
        writer.Write(labelBytes);
        writer.Write(new byte[8]); // Pad to 16 bytes

        // Write fixed bytes
        writer.Write(new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0xFF, 0x00 });

        // Write icon stride and height
        writer.Write((byte)PecSpec.IconByteStride);
        writer.Write((byte)PecSpec.IconHeight);

        // Skip 0xC bytes
        writer.Write(new byte[12]);

        // Write color changes
        int colorChanges = Math.Max(0, plan.ThreadPalette.Count - 1);
        if (colorChanges > 254) colorChanges = 254;
        writer.Write((byte)colorChanges);

        // Write color bytes
        for (int i = 0; i < plan.ThreadPalette.Count; i++)
        {
            writer.Write((byte)i);
        }

        // Pad to 0x1D0
        int written = (int)writer.BaseStream.Position;
        int padCount = 0x1D0 - written;
        if (padCount > 0)
        {
            writer.Write(new byte[padCount]);
        }

        // Write stitch block end placeholder
        long stitchBlockStartPosition = writer.BaseStream.Position;
        writer.Write((ushort)0);
        writer.WriteInt24LE(0);

        // Write block header
        writer.Write(PecSpec.BlockHeader);

        // Calculate bounds
        int minX = 0, maxX = 0, minY = 0, maxY = 0;
        foreach (var stitch in allStitches)
        {
            int x = stitch.X / PecSpec.MicronsPerPecUnit;
            int y = stitch.Y / PecSpec.MicronsPerPecUnit;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }

        int width = maxX - minX;
        int height = maxY - minY;

        writer.WriteInt16LE(width);
        writer.WriteInt16LE(height);
        writer.WriteInt16LE(0x1E0);
        writer.WriteInt16LE(0x1B0);

        // Encode stitches
        int lastX = 0, lastY = 0;
        int currentColor = -1;
        bool firstStitch = true;
        bool jumping = true;

        foreach (var stitch in allStitches)
        {
            ct.ThrowIfCancellationRequested();

            int targetX = stitch.X / PecSpec.MicronsPerPecUnit;
            int targetY = stitch.Y / PecSpec.MicronsPerPecUnit;

            int deltaX = targetX - lastX;
            int deltaY = targetY - lastY;

            // Handle color change
            if (stitch.ColorIndex != currentColor)
            {
                if (!firstStitch)
                {
                    writer.Write(PecSpec.ColorChangeMarker1);
                    writer.Write(PecSpec.ColorChangeMarker2);
                    writer.Write((byte)stitch.ColorIndex);
                }
                currentColor = stitch.ColorIndex;
                jumping = true;
            }

            if (stitch.IsJump)
            {
                jumping = true;
            }

            if (stitch.IsJump || jumping)
            {
                byte[] jumpData = PecMovementEncoder.EncodeMovement(deltaX, deltaY, PecSpec.JumpCode);
                writer.Write(jumpData);
                jumping = false;
            }
            else if (stitch.IsTrim)
            {
                byte[] trimData = PecMovementEncoder.EncodeMovement(deltaX, deltaY, PecSpec.TrimCode);
                writer.Write(trimData);
            }
            else
            {
                byte[] stitchData = PecMovementEncoder.EncodeMovement(deltaX, deltaY);
                writer.Write(stitchData);
            }

            lastX = targetX;
            lastY = targetY;
            firstStitch = false;
        }

        // Write END marker
        writer.Write(PecSpec.EndMarker);

        // Backfill stitch block length
        long currentPosition = writer.BaseStream.Position;
        int stitchBlockLength = (int)(currentPosition - stitchBlockStartPosition);
        writer.BaseStream.Seek(stitchBlockStartPosition, SeekOrigin.Begin);
        writer.Write((ushort)0);
        writer.WriteInt24LE(stitchBlockLength);
        writer.BaseStream.Seek(currentPosition, SeekOrigin.Begin);

        // Write graphics (blank icon)
        int graphicsSize = PecSpec.GraphicsByteCount;
        for (int i = 0; i < plan.ThreadPalette.Count + 1; i++)
        {
            writer.Write(new byte[graphicsSize]);
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

            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

            if (stream.Length < 8)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "PEC.TOO_SHORT",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "PEC file too short for signature",
                    Evidence = $"File length: {stream.Length}",
                    Recommendation = "File must be at least 8 bytes for PEC signature"
                });
                return result;
            }

            byte[] signatureBytes = reader.ReadBytes(8);
            string signature = Encoding.ASCII.GetString(signatureBytes).TrimEnd('\0', ' ');
            if (signature != PecSpec.Signature)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "PEC.INVALID_SIGNATURE",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "Invalid PEC signature",
                    Evidence = $"Expected '{PecSpec.Signature}', got '{signature}'",
                    Recommendation = "Ensure file is a valid PEC format"
                });
            }

            if (stream.Length < PecSpec.HeaderSize)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "PEC.HEADER_TOO_SMALL",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "PEC header too small",
                    Evidence = $"File length: {stream.Length}, minimum: {PecSpec.HeaderSize}",
                    Recommendation = "PEC file must have at least 520 bytes header"
                });
            }

            stream.Position = originalPosition;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "PEC.VALIDATION_EXCEPTION",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Validation failed: {ex.Message}",
                Evidence = ex.ToString(),
                Recommendation = "File is not a valid PEC format"
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

        if (project.ThreadPalette.Count > PecSpec.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "PEC.TOO_MANY_COLORS",
                Severity = FmtValidationSeverity.Critical,
                Message = $"PEC supports maximum {PecSpec.MaxColors} colors",
                Evidence = $"Project has {project.ThreadPalette.Count} colors",
                Recommendation = "Reduce color count or split design"
            });
        }

        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        var allStitches = plan.GetAllStitches().ToList();

        int lastX = 0, lastY = 0;
        foreach (var stitch in allStitches)
        {
            int deltaX = Math.Abs(stitch.X / PecSpec.MicronsPerPecUnit - lastX);
            int deltaY = Math.Abs(stitch.Y / PecSpec.MicronsPerPecUnit - lastY);
            int maxDelta = Math.Max(deltaX, deltaY);

            if (maxDelta > PecSpec.MaxDeltaPerRecord)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "PEC.MOVEMENT_EXCEEDS_MAX",
                    Severity = FmtValidationSeverity.Warning,
                    Message = $"Movement exceeds PEC max delta per record ({PecSpec.MaxDeltaPerRecord})",
                    Evidence = $"Delta: ({deltaX}, {deltaY}) at ({stitch.X}, {stitch.Y})",
                    Recommendation = "Long movements will be split into multiple records"
                });
            }
            lastX = stitch.X / PecSpec.MicronsPerPecUnit;
            lastY = stitch.Y / PecSpec.MicronsPerPecUnit;
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
                RuleId = "PEC.TOO_MANY_STITCHES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalStitches} stitches, PEC supports max {Capabilities.MaxTotalStitches}",
                Evidence = $"Total stitches: {plan.TotalStitches}",
                Recommendation = "Simplify design or split into multiple files"
            });
        }

        if (plan.TotalColorChanges > Capabilities.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "PEC.TOO_MANY_COLOR_CHANGES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalColorChanges} color changes, PEC supports max {Capabilities.MaxColors}",
                Evidence = $"Color changes: {plan.TotalColorChanges}",
                Recommendation = "Reduce color changes or split design"
            });
        }

        return result;
    }
}

// Extension methods for BinaryWriter
public static class BinaryWriterExtensions
{
    public static void WriteInt16LE(this BinaryWriter writer, int value)
    {
        writer.Write((byte)(value & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
    }

    public static void WriteInt24LE(this BinaryWriter writer, int value)
    {
        writer.Write((byte)(value & 0xFF));
        writer.Write((byte)((value >> 8) & 0xFF));
        writer.Write((byte)((value >> 16) & 0xFF));
    }
}