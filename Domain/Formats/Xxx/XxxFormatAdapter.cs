namespace AtlasEmbroidery.Domain.Formats.Xxx;

using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FmtValidationIssue = AtlasEmbroidery.Domain.Formats.ValidationIssue;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;
using System.Text;

/// <summary>
/// XXX format adapter - reads and writes Singer XXX embroidery files
/// Two header variants (A and B), stitch data with signed 8-bit deltas, 0x7D long prefix, 0x7F control prefix
/// </summary>
public sealed class XxxFormatAdapter : IEmbroideryFormatReader, IEmbroideryFormatWriter, IEmbroideryFormatValidator
{
    public string FormatName => "XXX";
    public string FileExtension => ".xxx";
    public string[] Extensions => new[] { ".xxx", ".XXX" };
    public string MimeType => "application/x-singer-xxx";
    public string DefaultExtension => ".xxx";

    public FormatCapabilities Capabilities => new()
    {
        SupportsReading = true,
        SupportsWriting = true,
        SupportsTrim = true,
        SupportsJump = true,
        SupportsColorChange = true,
        SupportsStop = true,
        MaxStitchLength = XxxSpec.MaxDeltaPerRecord * XxxSpec.MicronsPerXxxUnit,
        MaxJumpLength = XxxSpec.MaxDeltaLong * XxxSpec.MicronsPerXxxUnit,
        MaxTotalStitches = 1_000_000,
        MaxColors = XxxSpec.MaxColors,
    };

    /// <summary>
    /// Reads an XXX file and returns an AtlasProject
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

        // Check minimum file size (need at least 0x100 bytes for header)
        if (stream.Length < XxxSpec.HeaderVariantASize)
        {
            throw new InvalidDataException($"XXX file too small: expected at least {XxxSpec.HeaderVariantASize} bytes for header, got {stream.Length}");
        }

        // Detect header variant and skip
        stream.Position = 0;
        byte[] headerCheck = reader.ReadBytes(4);
        stream.Position = 0;

        // XXX header is 0x100 bytes for both variants
        // Variant B has "XXX" signature at offset 0xA0
        stream.Seek(XxxSpec.SignatureOffset, SeekOrigin.Begin);
        byte[] sig = reader.ReadBytes(3);
        stream.Position = 0;

        bool isVariantB = sig[0] == 'X' && sig[1] == 'X' && sig[2] == 'X';

        // Skip header (0x100 bytes)
        stream.Seek(XxxSpec.HeaderVariantASize, SeekOrigin.Begin);

        // Read stitch data
        while (stream.Position < stream.Length)
        {
            ct.ThrowIfCancellationRequested();

            if (stream.Position >= stream.Length)
                break;

            byte b1 = reader.ReadByte();

            // Normal stitch (no control prefix)
            if (b1 != XxxSpec.ControlPrefix && b1 != XxxSpec.LongMovePrefix)
            {
                if (stream.Position >= stream.Length)
                    break;
                byte b2 = reader.ReadByte();

                int deltaX = DecodeSigned8(b1);
                int deltaY = -DecodeSigned8(b2);

                currentX += deltaX;
                currentY += deltaY;

                var stitch = new StitchPoint(
                    currentX * XxxSpec.MicronsPerXxxUnit,
                    -currentY * XxxSpec.MicronsPerXxxUnit,
                    StitchType.Running,
                    (byte)(colorIndex + 1),
                    (byte)colorIndex,
                    0
                );
                stitches.Add(stitch);

                if (firstStitch) firstStitch = false;
                continue;
            }

            // Long move (0x7D prefix)
            if (b1 == XxxSpec.LongMovePrefix)
            {
                if (stream.Position + 3 >= stream.Length)
                    break;
                byte xLo = reader.ReadByte();
                byte xHi = reader.ReadByte();
                byte yLo = reader.ReadByte();
                byte yHi = reader.ReadByte();

                int deltaX = (int)(xLo | (xHi << 8));
                int deltaY = -((int)(yLo | (yHi << 8)));

                currentX += deltaX;
                currentY += deltaY;

                var stitch = new StitchPoint(
                    currentX * XxxSpec.MicronsPerXxxUnit,
                    -currentY * XxxSpec.MicronsPerXxxUnit,
                    StitchType.Running,
                    (byte)(colorIndex + 1),
                    (byte)colorIndex,
                    0
                );
                stitches.Add(stitch);

                if (firstStitch) firstStitch = false;
                continue;
            }

            // Control code (0x7F prefix)
            if (stream.Position >= stream.Length)
                break;
            byte ctrl = reader.ReadByte();

            switch (ctrl)
            {
                case XxxSpec.CtrlJump:
                    if (stream.Position + 1 >= stream.Length)
                        break;
                    byte jx = reader.ReadByte();
                    byte jy = reader.ReadByte();
                    int jDeltaX = DecodeSigned8(jx);
                    int jDeltaY = -DecodeSigned8(jy);

                    currentX += jDeltaX;
                    currentY += jDeltaY;

                    var jumpStitch = new StitchPoint(
                        currentX * XxxSpec.MicronsPerXxxUnit,
                        -currentY * XxxSpec.MicronsPerXxxUnit,
                        StitchType.Jump,
                        (byte)(colorIndex + 1),
                        (byte)colorIndex,
                        0
                    );
                    stitches.Add(jumpStitch);
                    break;

                case XxxSpec.CtrlTrim:
                    if (stream.Position + 1 >= stream.Length)
                        break;
                    byte tx = reader.ReadByte();
                    byte ty = reader.ReadByte();
                    int tDeltaX = DecodeSigned8(tx);
                    int tDeltaY = -DecodeSigned8(ty);

                    currentX += tDeltaX;
                    currentY += tDeltaY;

                    var trimStitch = new StitchPoint(
                        currentX * XxxSpec.MicronsPerXxxUnit,
                        -currentY * XxxSpec.MicronsPerXxxUnit,
                        StitchType.Trim,
                        (byte)(colorIndex + 1),
                        (byte)colorIndex,
                        0
                    );
                    stitches.Add(trimStitch);
                    break;

                case XxxSpec.CtrlColorChange:
                    if (stream.Position + 1 >= stream.Length)
                        break;
                    byte ccX = reader.ReadByte();
                    byte ccY = reader.ReadByte();
                    int ccDeltaX = DecodeSigned8(ccX);
                    int ccDeltaY = -DecodeSigned8(ccY);

                    currentX += ccDeltaX;
                    currentY += ccDeltaY;

                    colorIndex = (colorIndex + 1) % Math.Max(1, XxxSpec.MaxColors);

                    var ccStitch = new StitchPoint(
                        currentX * XxxSpec.MicronsPerXxxUnit,
                        -currentY * XxxSpec.MicronsPerXxxUnit,
                        StitchType.Stop,
                        (byte)(colorIndex + 1),
                        (byte)colorIndex,
                        0
                    );
                    stitches.Add(ccStitch);
                    break;

                case XxxSpec.CtrlEnd:
                    // End marker reached - read remaining 2 bytes and stop
                    if (stream.Position + 1 < stream.Length)
                    {
                        reader.ReadByte();
                        reader.ReadByte();
                    }
                    goto EndRead;

                default:
                    // Needle change (0x0A-0x17)
                    if (ctrl >= XxxSpec.CtrlNeedleChangeStart && ctrl <= XxxSpec.CtrlNeedleChangeEnd)
                    {
                        if (stream.Position + 1 >= stream.Length)
                            break;
                        byte nx = reader.ReadByte();
                        byte ny = reader.ReadByte();
                        int nDeltaX = DecodeSigned8(nx);
                        int nDeltaY = -DecodeSigned8(ny);

                        currentX += nDeltaX;
                        currentY += nDeltaY;

                        int needle = ctrl - XxxSpec.CtrlNeedleChangeStart + 1;
                        colorIndex = needle - 1; // Map needle to color index

                        var needleStitch = new StitchPoint(
                            currentX * XxxSpec.MicronsPerXxxUnit,
                            -currentY * XxxSpec.MicronsPerXxxUnit,
                            StitchType.Stop,
                            (byte)(colorIndex + 1),
                            (byte)colorIndex,
                            0
                        );
                        stitches.Add(needleStitch);
                    }
                    else
                    {
                        // Unknown control - skip 2 data bytes if available
                        if (stream.Position + 1 < stream.Length)
                        {
                            reader.ReadByte();
                            reader.ReadByte();
                        }
                    }
                    break;
            }
        }

    EndRead:
        // Read color table (21 entries of RGB + reserved) at end of file
        // After END marker, there should be color data
        // In practice, XXX files often have color table after stitch data
        // For now, create basic palette from stitches
        if (stitches.Count > 0)
        {
            var shape = new ShapeObject
            {
                Name = "XXX Import",
                Vertices = stitches.Select(s => new Point(s.X, s.Y)).ToList(),
                IsClosed = false,
                StitchParams = StitchParams.DefaultFor(StitchType.Running)
            };
            shape.StitchParams.Density = 4000;
            project.Objects.Add(shape);

            // Add basic thread colors based on color indices used
            int maxColorIndex = stitches.Max(s => s.ColorIndex);
            for (int i = 0; i <= maxColorIndex; i++)
            {
                project.ThreadPalette.Add(new ThreadColor(255, 0, 0, $"Color {i + 1}", $"C{i + 1:D3}", $"Thread {i + 1}"));
            }
        }

        return project;
    }

    /// <summary>
    /// Writes an AtlasProject to XXX format (variant B with "XXX" signature)
    /// </summary>
    public async Task WriteAsync(AtlasProject project, Stream stream, FormatWriteOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatWriteOptions();

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Compile to stitch plan
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        var allStitches = plan.GetAllStitches().ToList();

        // Write variant B header (0x100 bytes with "XXX" signature)
        WriteVariantBHeader(writer, allStitches, project);

        // Placeholder for end of stitches offset (filled at end)
        long stitchEndPlaceholderPos = stream.Position;
        writer.Write(0); // placeholder for end of stitches offset

        // Write stitch data
        int lastX = 0, lastY = 0;
        int currentColor = -1;
        bool firstStitch = true;

        foreach (var stitch in allStitches)
        {
            ct.ThrowIfCancellationRequested();

            int targetX = stitch.X / XxxSpec.MicronsPerXxxUnit;
            int targetY = stitch.Y / XxxSpec.MicronsPerXxxUnit;
            int deltaX = targetX - lastX;
            int deltaY = targetY - lastY;

            // Handle color change
            if (stitch.ColorIndex != currentColor)
            {
                if (!firstStitch)
                {
                    writer.Write(XxxMovementEncoder.EncodeColorChange());
                }
                currentColor = stitch.ColorIndex;
            }

            if (stitch.IsJump)
            {
                writer.Write(XxxMovementEncoder.EncodeJump(deltaX, deltaY));
            }
            else if (stitch.IsTrim)
            {
                writer.Write(XxxMovementEncoder.EncodeTrim());
            }
            else
            {
                // Check if we need long move encoding
                if (Math.Abs(deltaX) > XxxSpec.MaxDeltaPerRecord || Math.Abs(deltaY) > XxxSpec.MaxDeltaPerRecord)
                {
                    writer.Write(XxxMovementEncoder.EncodeLongMove(deltaX, deltaY));
                }
                else
                {
                    writer.Write(XxxMovementEncoder.EncodeMovement(deltaX, deltaY));
                }
            }

            lastX = targetX;
            lastY = targetY;
            firstStitch = false;
        }

        // Write END marker
        writer.Write(XxxMovementEncoder.EncodeEnd());

        // Fill in the end of stitches offset
        long endOfStitches = stream.Position;
        stream.Position = stitchEndPlaceholderPos;
        writer.Write((int)endOfStitches);
        stream.Position = endOfStitches;

        // Write color table (21 entries: 0x00 R G B)
        for (int i = 0; i < XxxSpec.MaxColors; i++)
        {
            writer.Write((byte)0x00);
            if (i < project.ThreadPalette.Count)
            {
                var thread = project.ThreadPalette[i];
                writer.Write((byte)thread.R);
                writer.Write((byte)thread.G);
                writer.Write((byte)thread.B);
            }
            else
            {
                writer.Write((byte)0x00);
                writer.Write((byte)0x00);
                writer.Write((byte)0x00);
            }
        }

        // Final marker
        writer.Write((uint)0xFFFFFF00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x01);
    }

    private void WriteVariantBHeader(BinaryWriter writer, List<StitchPoint> stitches, AtlasProject project)
    {
        // Variant B header (0x100 bytes)
        // 0x00-0x16: 23 bytes zeros
        for (int i = 0; i < 0x17; i++)
            writer.Write((byte)0x00);

        // 0x14: Stitch count (little-endian int32) - pyembroidery writes at 0x14
        writer.Write(stitches.Count > 0 ? stitches.Count - 1 : 0);

        // 0x18-0x1F: 12 bytes zeros
        for (int i = 0; i < 0x0C; i++)
            writer.Write((byte)0x00);

        // 0x20: Thread count (little-endian int32)
        writer.Write(project.ThreadPalette.Count);
        writer.Write((short)0x0000);

        // 0x24: Bounds (8 x int16 LE)
        var bounds = project.GetDesignBounds();
        // pyembroidery writes: width, height, lastX, -lastY, -minX, maxY
        int width = bounds.Width;
        int height = bounds.Height;
        int lastX = stitches.Count > 0 ? (int)(stitches[^1].X / XxxSpec.MicronsPerXxxUnit) : 0;
        int lastY = stitches.Count > 0 ? (int)(stitches[^1].Y / XxxSpec.MicronsPerXxxUnit) : 0;

        writer.Write((short)(width / XxxSpec.MicronsPerXxxUnit));
        writer.Write((short)(height / XxxSpec.MicronsPerXxxUnit));
        writer.Write((short)(lastX));
        writer.Write((short)(-lastY));
        writer.Write((short)(-(int)(bounds.X / (long)XxxSpec.MicronsPerXxxUnit)));
        writer.Write((short)((int)(bounds.Y / (long)XxxSpec.MicronsPerXxxUnit)));
        writer.Write((short)0);
        writer.Write((short)0);

        // 0x34-0xB5: zeros until XXX signature at 0xA0
        for (long i = writer.BaseStream.Position; i < XxxSpec.SignatureOffset; i++)
            writer.Write((byte)0x00);

        // 0xA0: "XXX" signature
        writer.Write(Encoding.ASCII.GetBytes("XXX"));

        // 0xA3-0xD9: zeros
        for (long i = writer.BaseStream.Position; i < XxxSpec.HeaderVariantBSize; i++)
            writer.Write((byte)0x00);
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

            // Check file size
            if (stream.Length < XxxSpec.HeaderVariantASize + 4)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "XXX.FILE_TOO_SMALL",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "XXX file too small for valid header",
                    Evidence = $"File length: {stream.Length}, minimum: {XxxSpec.HeaderVariantASize + 4}",
                    Recommendation = "File is not a valid XXX format"
                });
                return result;
            }

            // Check for variant B signature
            stream.Seek(XxxSpec.SignatureOffset, SeekOrigin.Begin);
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            byte[] sig = reader.ReadBytes(3);
            if (!(sig[0] == 'X' && sig[1] == 'X' && sig[2] == 'X'))
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "XXX.NO_XXX_SIGNATURE",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "XXX signature not found at expected offset (variant B)",
                    Evidence = $"Bytes at 0x{XxxSpec.SignatureOffset:X}: {sig[0]:X2} {sig[1]:X2} {sig[2]:X2}",
                    Recommendation = "File is not a valid XXX variant B format"
                });
            }

            stream.Position = originalPosition;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "XXX.VALIDATION_EXCEPTION",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Validation failed: {ex.Message}",
                Evidence = ex.ToString(),
                Recommendation = "File is not a valid XXX format"
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

        if (project.ThreadPalette.Count > XxxSpec.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "XXX.TOO_MANY_COLORS",
                Severity = FmtValidationSeverity.Critical,
                Message = $"XXX supports maximum {XxxSpec.MaxColors} colors",
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
            int deltaX = Math.Abs(stitch.X / XxxSpec.MicronsPerXxxUnit - lastX);
            int deltaY = Math.Abs(stitch.Y / XxxSpec.MicronsPerXxxUnit - lastY);
            int maxDelta = Math.Max(deltaX, deltaY);

            if (maxDelta > XxxSpec.MaxDeltaLong)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "XXX.MOVEMENT_EXCEEDS_MAX_LONG",
                    Severity = FmtValidationSeverity.Warning,
                    Message = $"Movement exceeds XXX max delta for long move ({XxxSpec.MaxDeltaLong})",
                    Evidence = $"Delta: ({deltaX}, {deltaY}) at ({stitch.X}, {stitch.Y})",
                    Recommendation = "Long movements will be split into multiple records"
                });
            }
            else if (maxDelta > XxxSpec.MaxDeltaPerRecord)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "XXX.MOVEMENT_EXCEEDS_MAX_NORMAL",
                    Severity = FmtValidationSeverity.Info,
                    Message = $"Movement exceeds XXX normal max delta ({XxxSpec.MaxDeltaPerRecord}), will use long encoding",
                    Evidence = $"Delta: ({deltaX}, {deltaY}) at ({stitch.X}, {stitch.Y})",
                    Recommendation = "Normal - long move encoding will be used"
                });
            }
            lastX = stitch.X / XxxSpec.MicronsPerXxxUnit;
            lastY = stitch.Y / XxxSpec.MicronsPerXxxUnit;
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
                RuleId = "XXX.TOO_MANY_STITCHES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalStitches} stitches, XXX supports max {Capabilities.MaxTotalStitches}",
                Evidence = $"Total stitches: {plan.TotalStitches}",
                Recommendation = "Simplify design or split into multiple files"
            });
        }

        if (plan.TotalColorChanges > XxxSpec.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "XXX.TOO_MANY_COLOR_CHANGES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalColorChanges} color changes, XXX supports max {XxxSpec.MaxColors}",
                Evidence = $"Color changes: {plan.TotalColorChanges}",
                Recommendation = "Reduce color changes or split design"
            });
        }

        return result;
    }
}