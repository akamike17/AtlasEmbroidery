namespace AtlasEmbroidery.Domain.Formats.Exp;

using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FmtValidationIssue = AtlasEmbroidery.Domain.Formats.ValidationIssue;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;
using System.Text;

/// <summary>
/// Melco EXP format constants and specifications
/// </summary>
public static class ExpSpec
{
    public const string Extension = ".exp";
    public const int MaxDeltaPerRecord = 127; // 8-bit signed
    public const int MicronsPerExpUnit = 100; // 0.1mm = 100 microns (same as PEC/PES)
    public const int MaxColors = 255;

    // Control codes
    public const byte ControlPrefix = 0x80;
    public const byte TrimControl = 0x80;     // 0x80 0x80 0x07 0x00
    public const byte JumpControl = 0x04;     // 0x80 0x04 + delta bytes
    public const byte ColorChangeControl = 0x01; // 0x80 0x01 + 2 bytes (00 00)
    public const byte StopControl = 0x01;     // Same as color change

    // Trim sequence
    public static readonly byte[] TrimSequence = { 0x80, 0x80, 0x07, 0x00 };
    public static readonly byte[] ColorChangeSequence = { 0x80, 0x01, 0x00, 0x00 };
    public static readonly byte[] JumpPrefix = { 0x80, 0x04 };
}

/// <summary>
/// EXP stitch encoder/decoder using 8-bit signed deltas
/// </summary>
public static class ExpMovementEncoder
{
    /// <summary>
    /// Encodes a delta into EXP format (8-bit signed)
    /// </summary>
    public static byte[] EncodeMovement(int deltaX, int deltaY)
    {
        if (deltaX < -128 || deltaX > 127)
            throw new ArgumentOutOfRangeException(nameof(deltaX), $"Delta X {deltaX} exceeds 8-bit signed range [-128, 127]");
        if (deltaY < -128 || deltaY > 127)
            throw new ArgumentOutOfRangeException(nameof(deltaY), $"Delta Y {deltaY} exceeds 8-bit signed range [-128, 127]");

        // EXP negates Y per spec
        byte x = (byte)(deltaX & 0xFF);
        byte y = (byte)((-deltaY) & 0xFF);
        return new byte[] { x, y };
    }

    /// <summary>
    /// Encodes a jump move
    /// </summary>
    public static byte[] EncodeJump(int deltaX, int deltaY)
    {
        var move = EncodeMovement(deltaX, deltaY);
        return new byte[] { ExpSpec.ControlPrefix, ExpSpec.JumpControl, move[0], move[1] };
    }

    /// <summary>
    /// Encodes a trim
    /// </summary>
    public static byte[] EncodeTrim()
    {
        return ExpSpec.TrimSequence;
    }

    /// <summary>
    /// Encodes a color change
    /// </summary>
    public static byte[] EncodeColorChange()
    {
        return ExpSpec.ColorChangeSequence;
    }

    /// <summary>
    /// Encodes a stop (same as color change in EXP)
    /// </summary>
    public static byte[] EncodeStop()
    {
        return ExpSpec.ColorChangeSequence;
    }
}

/// <summary>
/// EXP stitch decoder
/// </summary>
public static class ExpMovementDecoder
{
    public static (int deltaX, int deltaY, ExpControl control, int bytesConsumed) DecodeMovement(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for EXP record");

        byte val1 = data[offset];

        // Regular stitch (first byte != 0x80)
        if (val1 != ExpSpec.ControlPrefix)
        {
            if (offset + 1 >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for stitch record");

            byte val2 = data[offset + 1];
            int deltaX = DecodeSigned8(val1);
            int deltaY = -DecodeSigned8(val2); // EXP negates Y
            return (deltaX, deltaY, ExpControl.Normal, 2);
        }

        // Control code
        if (offset + 1 >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for control code");

        byte control = data[offset + 1];

        switch (control)
        {
            case ExpSpec.TrimControl:
                if (offset + 3 >= data.Length)
                    throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for trim");
                // Expect 07 00
                if (data[offset + 2] != 0x07 || data[offset + 3] != 0x00)
                    throw new InvalidDataException("Invalid trim sequence");
                return (0, 0, ExpControl.Trim, 4);

            case ExpSpec.JumpControl:
                if (offset + 3 >= data.Length)
                    throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for jump");
                byte jx = data[offset + 2];
                byte jy = data[offset + 3];
                int jDeltaX = DecodeSigned8(jx);
                int jDeltaY = -DecodeSigned8(jy);
                return (jDeltaX, jDeltaY, ExpControl.Jump, 4);

            case ExpSpec.ColorChangeControl:
                if (offset + 3 >= data.Length)
                    throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for color change");
                byte ccX = data[offset + 2];
                byte ccY = data[offset + 3];
                int ccDeltaX = DecodeSigned8(ccX);
                int ccDeltaY = -DecodeSigned8(ccY);
                return (ccDeltaX, ccDeltaY, ExpControl.ColorChange, 4);

            default:
                throw new InvalidDataException($"Unknown EXP control code: 0x{control:X2}");
        }
    }

    private static int DecodeSigned8(byte b) => b <= 127 ? b : b - 256;
}

[Flags]
public enum ExpControl : byte
{
    Normal = 0,
    Jump = 1,
    Trim = 2,
    ColorChange = 4,
    Stop = 8,
    End = 16
}

/// <summary>
/// EXP format adapter - reads and writes Melco EXP embroidery files
/// Simple format: no header, just stitch data with 8-bit signed deltas
/// </summary>
public sealed class ExpFormatAdapter : IEmbroideryFormatReader, IEmbroideryFormatWriter, IEmbroideryFormatValidator
{
    public string FormatName => "EXP";
    public string FileExtension => ".exp";
    public string[] Extensions => new[] { ".exp", ".EXP" };
    public string MimeType => "application/x-melco-exp";
    public string DefaultExtension => ".exp";

    public FormatCapabilities Capabilities => new()
    {
        SupportsReading = true,
        SupportsWriting = true,
        SupportsTrim = true,
        SupportsJump = true,
        SupportsColorChange = true,
        SupportsStop = true,
        MaxStitchLength = ExpSpec.MaxDeltaPerRecord * ExpSpec.MicronsPerExpUnit,
        MaxJumpLength = ExpSpec.MaxDeltaPerRecord * ExpSpec.MicronsPerExpUnit,
        MaxTotalStitches = 1_000_000,
        MaxColors = ExpSpec.MaxColors,
    };

    /// <summary>
    /// Reads an EXP file and returns an AtlasProject
    /// </summary>
    public async Task<AtlasProject> ReadAsync(Stream stream, FormatReadOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatReadOptions();

        var project = new AtlasProject();

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var stitches = new List<StitchPoint>();
        int currentX = 0, currentY = 0;
        int colorIndex = 0;
        bool firstStitch = true;

        while (stream.Position < stream.Length)
                {
                    ct.ThrowIfCancellationRequested();

                    if (stream.Position + 1 >= stream.Length)
                        break;

                    byte val1 = reader.ReadByte();
                    byte val2 = reader.ReadByte();

                    // Regular stitch (including 0x00 which is a valid delta)
                    if (val1 != ExpSpec.ControlPrefix)
                    {
                        int deltaX = DecodeSigned8(val1);
                        int deltaY = -DecodeSigned8(val2);

                        currentX += deltaX;
                        currentY += deltaY;

                        var stitch = new StitchPoint(
                            currentX * ExpSpec.MicronsPerExpUnit,
                            -currentY * ExpSpec.MicronsPerExpUnit,
                            StitchType.Running,
                            (byte)(colorIndex + 1),
                            (byte)colorIndex,
                            0
                        );
                        stitches.Add(stitch);

                        if (firstStitch) firstStitch = false;
                        continue;
                    }

                    // Control code
                    if (stream.Position + 1 >= stream.Length)
                        break;

                    byte control = reader.ReadByte();

                    switch (control)
                    {
                        case ExpSpec.TrimControl:
                            // Read 07 00
                            if (stream.Position + 1 >= stream.Length)
                                break;
                            byte t1 = reader.ReadByte();
                            byte t2 = reader.ReadByte();
                            if (t1 != 0x07 || t2 != 0x00)
                                throw new InvalidDataException("Invalid trim sequence in EXP file");
                    
                            var trimStitch = new StitchPoint(
                                currentX * ExpSpec.MicronsPerExpUnit,
                                -currentY * ExpSpec.MicronsPerExpUnit,
                                StitchType.Trim,
                                (byte)(colorIndex + 1),
                                (byte)colorIndex,
                                0
                            );
                            stitches.Add(trimStitch);
                            break;

                        case ExpSpec.JumpControl:
                            if (stream.Position + 1 >= stream.Length)
                                break;
                            byte jx = reader.ReadByte();
                            byte jy = reader.ReadByte();
                            int jDeltaX = DecodeSigned8(jx);
                            int jDeltaY = -DecodeSigned8(jy);

                            currentX += jDeltaX;
                            currentY += jDeltaY;

                            var jumpStitch = new StitchPoint(
                                currentX * ExpSpec.MicronsPerExpUnit,
                                -currentY * ExpSpec.MicronsPerExpUnit,
                                StitchType.Jump,
                                (byte)(colorIndex + 1),
                                (byte)colorIndex,
                                0
                            );
                            stitches.Add(jumpStitch);
                            break;

                        case ExpSpec.ColorChangeControl:
                            // Color change and stop use same control code (0x01) in EXP
                            if (stream.Position + 1 >= stream.Length)
                                break;
                            byte ccX = reader.ReadByte();
                            byte ccY = reader.ReadByte();
                            int ccDeltaX = DecodeSigned8(ccX);
                            int ccDeltaY = -DecodeSigned8(ccY);

                            currentX += ccDeltaX;
                            currentY += ccDeltaY;

                            colorIndex = (colorIndex + 1) % Math.Max(1, project.ThreadPalette.Count);

                            var ccStitch = new StitchPoint(
                                currentX * ExpSpec.MicronsPerExpUnit,
                                -currentY * ExpSpec.MicronsPerExpUnit,
                                StitchType.Stop,
                                (byte)(colorIndex + 1),
                                (byte)colorIndex,
                                0
                            );
                            stitches.Add(ccStitch);
                            break;

                        default:
                            // Skip unknown control codes and their data
                            if (stream.Position + 1 < stream.Length)
                            {
                                reader.ReadByte();
                                reader.ReadByte();
                            }
                            break;
                    }
                }

        // Build project from stitches
        if (stitches.Count > 0)
        {
            var shape = new ShapeObject
            {
                Name = "EXP Import",
                Vertices = stitches.Select(s => new Point(s.X, s.Y)).ToList(),
                IsClosed = false,
                StitchParams = StitchParams.DefaultFor(StitchType.Running)
            };
            shape.StitchParams.Density = 4000;
            project.Objects.Add(shape);
        }

        return project;
    }

    /// <summary>
    /// Writes an AtlasProject to EXP format
    /// </summary>
    public async Task WriteAsync(AtlasProject project, Stream stream, FormatWriteOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatWriteOptions();

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Compile to stitch plan
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        var allStitches = plan.GetAllStitches().ToList();

        // Write stitches directly - no header in EXP
        int lastX = 0, lastY = 0;
        int currentColor = -1;
        bool firstStitch = true;

        foreach (var stitch in allStitches)
        {
            ct.ThrowIfCancellationRequested();

            int targetX = stitch.X / ExpSpec.MicronsPerExpUnit;
            int targetY = stitch.Y / ExpSpec.MicronsPerExpUnit;
            int deltaX = targetX - lastX;
            int deltaY = targetY - lastY;

            // Handle color change
            if (stitch.ColorIndex != currentColor)
            {
                if (!firstStitch)
                {
                    writer.Write(ExpSpec.ColorChangeSequence);
                }
                currentColor = stitch.ColorIndex;
            }

            if (stitch.IsJump)
            {
                writer.Write(ExpMovementEncoder.EncodeJump(deltaX, deltaY));
            }
            else if (stitch.IsTrim)
            {
                writer.Write(ExpSpec.TrimSequence);
            }
            else
            {
                writer.Write(ExpMovementEncoder.EncodeMovement(deltaX, deltaY));
            }

            lastX = targetX;
            lastY = targetY;
            firstStitch = false;
        }
        // No explicit END marker in EXP
    }

    private static int DecodeSigned8(byte b) => b <= 127 ? b : b - 256;

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

            // EXP has no header - just validate we can read something
            if (stream.Length == 0)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "EXP.EMPTY_FILE",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "EXP file is empty",
                    Evidence = "File length is 0",
                    Recommendation = "EXP files must contain at least one stitch record"
                });
                return result;
            }

            // Try to parse as EXP
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            long pos = 0;
            while (pos < stream.Length - 1)
            {
                stream.Position = pos;
                byte val1 = reader.ReadByte();
                byte val2 = reader.ReadByte();
                pos += 2;

                if (val1 == ExpSpec.ControlPrefix)
                {
                    if (pos >= stream.Length) break;
                    byte control = reader.ReadByte();
                    pos++;

                    if (control == ExpSpec.TrimControl)
                    {
                        if (pos + 1 >= stream.Length) break;
                        pos += 2;
                    }
                    else if (control == ExpSpec.JumpControl || control == ExpSpec.ColorChangeControl)
                    {
                        if (pos + 1 >= stream.Length) break;
                        pos += 2;
                    }
                }
            }

            stream.Position = originalPosition;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "EXP.VALIDATION_EXCEPTION",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Validation failed: {ex.Message}",
                Evidence = ex.ToString(),
                Recommendation = "File is not a valid EXP format"
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

        if (project.ThreadPalette.Count > ExpSpec.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "EXP.TOO_MANY_COLORS",
                Severity = FmtValidationSeverity.Critical,
                Message = $"EXP supports maximum {ExpSpec.MaxColors} colors",
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
            int deltaX = Math.Abs(stitch.X / ExpSpec.MicronsPerExpUnit - lastX);
            int deltaY = Math.Abs(stitch.Y / ExpSpec.MicronsPerExpUnit - lastY);
            int maxDelta = Math.Max(deltaX, deltaY);

            if (maxDelta > ExpSpec.MaxDeltaPerRecord)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "EXP.MOVEMENT_EXCEEDS_MAX",
                    Severity = FmtValidationSeverity.Warning,
                    Message = $"Movement exceeds EXP max delta per record ({ExpSpec.MaxDeltaPerRecord})",
                    Evidence = $"Delta: ({deltaX}, {deltaY}) at ({stitch.X}, {stitch.Y})",
                    Recommendation = "Long movements will be split into multiple records"
                });
            }
            lastX = stitch.X / ExpSpec.MicronsPerExpUnit;
            lastY = stitch.Y / ExpSpec.MicronsPerExpUnit;
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
                RuleId = "EXP.TOO_MANY_STITCHES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalStitches} stitches, EXP supports max {Capabilities.MaxTotalStitches}",
                Evidence = $"Total stitches: {plan.TotalStitches}",
                Recommendation = "Simplify design or split into multiple files"
            });
        }

        if (plan.TotalColorChanges > ExpSpec.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "EXP.TOO_MANY_COLOR_CHANGES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalColorChanges} color changes, EXP supports max {ExpSpec.MaxColors}",
                Evidence = $"Color changes: {plan.TotalColorChanges}",
                Recommendation = "Reduce color changes or split design"
            });
        }

        return result;
    }
}