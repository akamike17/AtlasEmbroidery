namespace AtlasEmbroidery.Domain.Formats.Jef;

using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FmtValidationIssue = AtlasEmbroidery.Domain.Formats.ValidationIssue;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;
using System.Text;

/// <summary>
/// Janome JEF format constants and specifications
/// </summary>
public static class JefSpec
{
    public const string Extension = ".jef";
    public const int HeaderSize = 512; // Fixed header size
    public const int MaxStitchDistance = 121;
    public const int MaxJumpDistance = 121;
    public const int MicronsPerJefUnit = 100; // 0.1mm = 100 microns
    public const int MaxColors = 255;

    // Header field prefixes
    public const string PrefixName = "LA";
    public const string PrefixStitches = "ST";
    public const string PrefixColors = "CO";
    public const string PrefixPosX = "+X";
    public const string PrefixNegX = "-X";
    public const string PrefixPosY = "+Y";
    public const string PrefixNegY = "-Y";
    public const string PrefixAx = "AX";
    public const string PrefixAy = "AY";
    public const string PrefixMx = "MX";
    public const string PrefixMy = "MY";
    public const string PrefixPd = "PD";
    public const string PrefixAuthor = "AU";
    public const string PrefixCopyright = "CP";
    public const string PrefixThreadColor = "TC";

    // Header terminator
    public const byte HeaderTerminator = 0x1A;
    public const byte HeaderPad = 0x20; // Space

    // Control codes in byte 2 (b2)
    public const byte StitchBase = 0b00000011;    // 0x03 - stitch with no jump
    public const byte JumpFlag = 0b10000000;      // 0x80 - bit 7: jump
    public const byte ColorChangeCode = 0b11000011; // 0xC3 - color change
    public const byte StopCode = 0b11000011;      // 0xC3 - same as color change
    public const byte EndCode = 0b11110011;       // 0xF3 - end marker
    public const byte SequinModeCode = 0b01000011; // 0x43 - sequin mode

    // Bit positions for coordinate encoding
    // b0 bits: 0=+1, 1=-1, 2=+9, 3=-9, 4=-9(y), 5=+9(y), 6=-3(y), 7=+3(y)
    // b1 bits: 0=+3, 1=-3, 2=+27, 3=-27, 4=-27(y), 5=+27(y), 6=-3(y), 7=+3(y)
    // b2 bits: 2=+81, 3=-81, 4=-81(y), 5=+81(y), 7=jump flag, 0=1, 1=1 (base)

    // Encoding values
    public static readonly int[] XValues = { 1, -1, 9, -9, 27, -27, 81, -81 };
    public static readonly int[] YValues = { 1, -1, 9, -9, 27, -27, 81, -81 };
}

/// <summary>
/// JEF thread color information
/// </summary>
public sealed class JefThread
{
    public string HexColor { get; set; } = "";
    public string Description { get; set; } = "";
    public string CatalogNumber { get; set; } = "";
    
    public ThreadColor ToThreadColor()
    {
        if (HexColor.StartsWith("#") && HexColor.Length == 7)
        {
            var r = byte.Parse(HexColor.Substring(1, 2), System.Globalization.NumberStyles.HexNumber);
            var g = byte.Parse(HexColor.Substring(3, 2), System.Globalization.NumberStyles.HexNumber);
            var b = byte.Parse(HexColor.Substring(5, 2), System.Globalization.NumberStyles.HexNumber);
            return new ThreadColor(r, g, b, "Janome", CatalogNumber, Description);
        }
        return new ThreadColor(0, 0, 0, "Janome", CatalogNumber, Description);
    }
}

/// <summary>
/// JEF stitch encoder using balanced ternary-like encoding
/// </summary>
public static class JefMovementEncoder
{
    /// <summary>
    /// Encodes a delta into JEF format (3 bytes)
    /// </summary>
    public static byte[] EncodeMovement(int deltaX, int deltaY, byte flags = JefSpec.StitchBase)
    {
        if (Math.Abs(deltaX) > JefSpec.MaxStitchDistance)
            throw new ArgumentOutOfRangeException(nameof(deltaX), $"Delta X {deltaX} exceeds max ±{JefSpec.MaxStitchDistance}");
        if (Math.Abs(deltaY) > JefSpec.MaxStitchDistance)
            throw new ArgumentOutOfRangeException(nameof(deltaY), $"Delta Y {deltaY} exceeds max ±{JefSpec.MaxStitchDistance}");

        // JEF negates Y per spec
        deltaY = -deltaY;

        byte b0 = 0;
        byte b1 = 0;
        byte b2 = flags;

        // X encoding
        int x = deltaX;
        if (flags == JefSpec.JumpFlag || flags == JefSpec.StitchBase)
        {
            b2 |= (1 << 0); // bit 0
            b2 |= (1 << 1); // bit 1
            
            if (x > 40) { b2 |= (1 << 2); x -= 81; }
            if (x < -40) { b2 |= (1 << 3); x += 81; }
            if (x > 13) { b1 |= (1 << 2); x -= 27; }
            if (x < -13) { b1 |= (1 << 3); x += 27; }
            if (x > 4) { b0 |= (1 << 2); x -= 9; }
            if (x < -4) { b0 |= (1 << 3); x += 9; }
            if (x > 1) { b1 |= (1 << 0); x -= 3; }
            if (x < -1) { b1 |= (1 << 1); x += 3; }
            if (x > 0) { b0 |= (1 << 0); x -= 1; }
            if (x < 0) { b0 |= (1 << 1); x += 1; }
            if (x != 0) throw new ArgumentException($"X value {deltaX} exceeds maximum allowed");
        }

        // Y encoding
        int y = deltaY;
        if (flags == JefSpec.JumpFlag || flags == JefSpec.StitchBase)
        {
            if (y > 40) { b2 |= (1 << 5); y -= 81; }
            if (y < -40) { b2 |= (1 << 4); y += 81; }
            if (y > 13) { b1 |= (1 << 5); y -= 27; }
            if (y < -13) { b1 |= (1 << 4); y += 27; }
            if (y > 4) { b0 |= (1 << 5); y -= 9; }
            if (y < -4) { b0 |= (1 << 4); y += 9; }
            if (y > 1) { b1 |= (1 << 7); y -= 3; }
            if (y < -1) { b1 |= (1 << 6); y += 3; }
            if (y > 0) { b0 |= (1 << 7); y -= 1; }
            if (y < 0) { b0 |= (1 << 6); y += 1; }
            if (y != 0) throw new ArgumentException($"Y value {deltaY} exceeds maximum allowed");
        }

        return new byte[] { b0, b1, b2 };
    }

    /// <summary>
    /// Encodes a jump move
    /// </summary>
    public static byte[] EncodeJump(int deltaX, int deltaY)
    {
        return EncodeMovement(deltaX, deltaY, JefSpec.JumpFlag);
    }

    /// <summary>
    /// Encodes a color change
    /// </summary>
    public static byte[] EncodeColorChange()
    {
        return new byte[] { 0, 0, JefSpec.ColorChangeCode };
    }

    /// <summary>
    /// Encodes a stop (same as color change in JEF)
    /// </summary>
    public static byte[] EncodeStop()
    {
        return new byte[] { 0, 0, JefSpec.StopCode };
    }

    /// <summary>
    /// Encodes the END marker
    /// </summary>
    public static byte[] EncodeEnd()
    {
        return new byte[] { 0, 0, JefSpec.EndCode };
    }
}

/// <summary>
/// JEF stitch decoder
/// </summary>
public static class JefMovementDecoder
{
    public static (int deltaX, int deltaY, JefControl control, int bytesConsumed) DecodeMovement(ReadOnlySpan<byte> data, int offset = 0)
    {
        if (offset + 2 >= data.Length)
            throw new ArgumentOutOfRangeException(nameof(data), "Insufficient data for JEF record (need 3 bytes)");

        byte b0 = data[offset];
        byte b1 = data[offset + 1];
        byte b2 = data[offset + 2];

        // Check for END marker
        if ((b2 & 0b11110011) == JefSpec.EndCode)
        {
            return (0, 0, JefControl.End, 3);
        }

        // Check for color change / stop
        if ((b2 & 0b11000011) == JefSpec.ColorChangeCode)
        {
            return (0, 0, JefControl.ColorChange, 3);
        }

        // Check for sequin mode
        if ((b2 & 0b11000011) == JefSpec.SequinModeCode)
        {
            return (0, 0, JefControl.SequinMode, 3);
        }

        bool isJump = (b2 & JefSpec.JumpFlag) != 0;

        int deltaX = DecodeDx(b0, b1, b2);
        int deltaY = DecodeDy(b0, b1, b2);

        var control = isJump ? JefControl.Jump : JefControl.Normal;
        return (deltaX, deltaY, control, 3);
    }

    private static int GetBit(byte b, int pos) => (b >> pos) & 1;

    private static int DecodeDx(byte b0, byte b1, byte b2)
    {
        int x = 0;
        x += GetBit(b2, 2) * (+81);
        x += GetBit(b2, 3) * (-81);
        x += GetBit(b1, 2) * (+27);
        x += GetBit(b1, 3) * (-27);
        x += GetBit(b0, 2) * (+9);
        x += GetBit(b0, 3) * (-9);
        x += GetBit(b1, 0) * (+3);
        x += GetBit(b1, 1) * (-3);
        x += GetBit(b0, 0) * (+1);
        x += GetBit(b0, 1) * (-1);
        return x;
    }

    private static int DecodeDy(byte b0, byte b1, byte b2)
    {
        int y = 0;
        y += GetBit(b2, 5) * (+81);
        y += GetBit(b2, 4) * (-81);
        y += GetBit(b1, 5) * (+27);
        y += GetBit(b1, 4) * (-27);
        y += GetBit(b0, 5) * (+9);
        y += GetBit(b0, 4) * (-9);
        y += GetBit(b1, 7) * (+3);
        y += GetBit(b1, 6) * (-3);
        y += GetBit(b0, 7) * (+1);
        y += GetBit(b0, 6) * (-1);
        return -y; // JEF negates Y
    }
}

[Flags]
public enum JefControl : byte
{
    Normal = 0,
    Jump = 1,
    ColorChange = 2,
    Stop = 4,
    SequinMode = 8,
    End = 16
}

/// <summary>
/// JEF format adapter - reads and writes Janome JEF embroidery files
/// </summary>
public sealed class JefFormatAdapter : IEmbroideryFormatReader, IEmbroideryFormatWriter, IEmbroideryFormatValidator
{
    public string FormatName => "JEF";
    public string FileExtension => ".jef";
    public string[] Extensions => new[] { ".jef", ".JEF" };
    public string MimeType => "application/x-janome-jef";
    public string DefaultExtension => ".jef";

    public FormatCapabilities Capabilities => new()
    {
        SupportsReading = true,
        SupportsWriting = true,
        SupportsTrim = false, // JEF doesn't have native trim, uses jump sequences
        SupportsJump = true,
        SupportsColorChange = true,
        SupportsStop = true,
        MaxStitchLength = JefSpec.MaxStitchDistance * JefSpec.MicronsPerJefUnit,
        MaxJumpLength = JefSpec.MaxJumpDistance * JefSpec.MicronsPerJefUnit,
        MaxTotalStitches = 500_000,
        MaxColors = JefSpec.MaxColors,
    };

    /// <summary>
    /// Reads a JEF file and returns an AtlasProject
    /// </summary>
    public async Task<AtlasProject> ReadAsync(Stream stream, FormatReadOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatReadOptions();

        var project = new AtlasProject();

        // Read header (512 bytes)
        byte[] header = new byte[JefSpec.HeaderSize];
        int bytesRead = await stream.ReadAsync(header, 0, header.Length, ct);
        if (bytesRead < JefSpec.HeaderSize)
            throw new InvalidDataException($"JEF header too short: {bytesRead} bytes");

        // Parse header
        ParseHeader(header, project);

        // Read stitches (3 bytes each)
        var stitches = new List<StitchPoint>();
        int currentX = 0, currentY = 0;
        int colorIndex = 0;
        bool firstStitch = true;

        byte[] buffer = new byte[3];
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            
            int read = await stream.ReadAsync(buffer, 0, 3, ct);
            if (read != 3) break;

            var (deltaX, deltaY, control, _) = JefMovementDecoder.DecodeMovement(buffer, 0);

            if (control == JefControl.End)
                break;

            if (control == JefControl.ColorChange)
            {
                colorIndex = (colorIndex + 1) % Math.Max(1, project.ThreadPalette.Count);
                continue;
            }

            currentX += deltaX;
            currentY += deltaY;

            var stitchType = control == JefControl.Jump ? StitchType.Jump : StitchType.Running;

            var stitch = new StitchPoint(
                currentX * JefSpec.MicronsPerJefUnit,
                -currentY * JefSpec.MicronsPerJefUnit, // JEF negates Y in decode, but we negate again for model coords
                stitchType,
                (byte)(colorIndex + 1),
                (byte)colorIndex,
                0
            );
            stitches.Add(stitch);

            if (firstStitch) firstStitch = false;
        }

        // Build project from stitches
        if (stitches.Count > 0)
        {
            var shape = new ShapeObject
            {
                Name = "JEF Import",
                Vertices = stitches.Select(s => new Point(s.X, s.Y)).ToList(),
                IsClosed = false,
                StitchParams = StitchParams.DefaultFor(StitchType.Running)
            };
            shape.StitchParams.Density = 4000;
            project.Objects.Add(shape);
        }

        return project;
    }

    private void ParseHeader(byte[] header, AtlasProject project)
    {
        // Convert to string and parse line by line
        string headerText = Encoding.ASCII.GetString(header);
        string[] lines = headerText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        int stitchCount = 0;
        int colorCount = 0;
        int minX = 0, maxX = 0, minY = 0, maxY = 0;
        int ax = 0, ay = 0;

        foreach (string line in lines)
        {
            if (line.Length < 3) continue;
            if (line[2] != ':') continue;

            string prefix = line.Substring(0, 2).Trim();
            string value = line.Substring(3).Trim();

            switch (prefix)
            {
                case JefSpec.PrefixName:
                    project.Name = value;
                    break;
                case JefSpec.PrefixStitches:
                    int.TryParse(value, out stitchCount);
                    break;
                case JefSpec.PrefixColors:
                    int.TryParse(value, out colorCount);
                    break;
                case JefSpec.PrefixPosX:
                    int.TryParse(value, out maxX);
                    break;
                case JefSpec.PrefixNegX:
                    int.TryParse(value, out minX);
                    minX = -minX;
                    break;
                case JefSpec.PrefixPosY:
                    int.TryParse(value, out maxY);
                    break;
                case JefSpec.PrefixNegY:
                    int.TryParse(value, out minY);
                    minY = -minY;
                    break;
                case JefSpec.PrefixAx:
                    ax = ParseSignedValue(value);
                    break;
                case JefSpec.PrefixAy:
                    ay = ParseSignedValue(value);
                    break;
                case JefSpec.PrefixAuthor:
                    project.CustomData["author"] = value;
                    break;
                case JefSpec.PrefixCopyright:
                    project.CustomData["copyright"] = value;
                    break;
                case JefSpec.PrefixThreadColor:
                    ParseThreadColor(value, project);
                    break;
            }
        }

        project.CustomData["jef_stitches"] = stitchCount;
        project.CustomData["jef_colors"] = colorCount;
    }

    private int ParseSignedValue(string value)
    {
        if (value.StartsWith("+"))
            return int.Parse(value.Substring(1));
        if (value.StartsWith("-"))
            return -int.Parse(value.Substring(1));
        return int.Parse(value);
    }

    private void ParseThreadColor(string value, AtlasProject project)
    {
        var parts = value.Split(',');
        if (parts.Length >= 3)
        {
            var thread = new JefThread
            {
                HexColor = parts[0].Trim(),
                Description = parts[1].Trim(),
                CatalogNumber = parts[2].Trim()
            };
            project.ThreadPalette.Add(thread.ToThreadColor());
        }
    }

    /// <summary>
    /// Writes an AtlasProject to JEF format
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
        int minX = allStitches.Count > 0 ? allStitches.Min(s => s.X / JefSpec.MicronsPerJefUnit) : 0;
        int maxX = allStitches.Count > 0 ? allStitches.Max(s => s.X / JefSpec.MicronsPerJefUnit) : 0;
        int minY = allStitches.Count > 0 ? allStitches.Min(s => s.Y / JefSpec.MicronsPerJefUnit) : 0;
        int maxY = allStitches.Count > 0 ? allStitches.Max(s => s.Y / JefSpec.MicronsPerJefUnit) : 0;

        // Last position
        int lastX = 0, lastY = 0;
        if (allStitches.Count > 0)
        {
            var last = allStitches[^1];
            lastX = last.X / JefSpec.MicronsPerJefUnit;
            lastY = -last.Y / JefSpec.MicronsPerJefUnit; // JEF negates Y
        }

        // Write header
        WriteHeader(writer, project, plan, minX, maxX, minY, maxY, lastX, lastY);

        // Write stitches
        int currentX = 0, currentY = 0;
        int currentColor = -1;
        bool firstStitch = true;

        foreach (var stitch in allStitches)
        {
            ct.ThrowIfCancellationRequested();

            int targetX = stitch.X / JefSpec.MicronsPerJefUnit;
            int targetY = -stitch.Y / JefSpec.MicronsPerJefUnit; // JEF negates Y
            int deltaX = targetX - currentX;
            int deltaY = targetY - currentY;

            // Handle color change
            if (stitch.ColorIndex != currentColor)
            {
                if (!firstStitch)
                {
                    writer.Write(JefMovementEncoder.EncodeColorChange());
                }
                currentColor = stitch.ColorIndex;
            }

            if (stitch.IsJump)
            {
                writer.Write(JefMovementEncoder.EncodeJump(deltaX, deltaY));
            }
            else
            {
                writer.Write(JefMovementEncoder.EncodeMovement(deltaX, deltaY));
            }

            currentX = targetX;
            currentY = targetY;
            firstStitch = false;
        }

        // Write END marker
        writer.Write(JefMovementEncoder.EncodeEnd());
    }

    private void WriteHeader(BinaryWriter writer, AtlasProject project, StitchPlan plan, 
        int minX, int maxX, int minY, int maxY, int lastX, int lastY)
    {
        // Header lines
        WriteLine(writer, $"{JefSpec.PrefixName}:{project.Name ?? "Untitled"}", 16);
        WriteLine(writer, $"{JefSpec.PrefixStitches}:{plan.TotalStitches:D7}");
        WriteLine(writer, $"{JefSpec.PrefixColors}:{plan.ThreadPalette.Count:D3}");
        WriteLine(writer, $"{JefSpec.PrefixPosX}:{maxX:D5}");
        WriteLine(writer, $"{JefSpec.PrefixNegX}:{Math.Abs(minX):D5}");
        WriteLine(writer, $"{JefSpec.PrefixPosY}:{maxY:D5}");
        WriteLine(writer, $"{JefSpec.PrefixNegY}:{Math.Abs(minY):D5}");
        WriteLine(writer, $"{JefSpec.PrefixAx}:{(lastX >= 0 ? "+" : "")}{lastX:D5}");
        WriteLine(writer, $"{JefSpec.PrefixAy}:{(lastY >= 0 ? "+" : "")}{lastY:D5}");
        WriteLine(writer, $"{JefSpec.PrefixMx}:+00000");
        WriteLine(writer, $"{JefSpec.PrefixMy}:+00000");
        WriteLine(writer, $"{JefSpec.PrefixPd}:******");

        // Extended header info
        if (project.CustomData.TryGetValue("author", out var authorObj) && authorObj is string author)
            WriteLine(writer, $"{JefSpec.PrefixAuthor}:{author}");
        
        if (project.CustomData.TryGetValue("copyright", out var copyrightObj) && copyrightObj is string copyright)
            WriteLine(writer, $"{JefSpec.PrefixCopyright}:{copyright}");

        // Thread colors
        for (int i = 0; i < plan.ThreadPalette.Count; i++)
        {
            var thread = plan.ThreadPalette[i];
            string hex = $"#{thread.R:X2}{thread.G:X2}{thread.B:X2}";
            string desc = thread.Description ?? thread.Name ?? $"Color {i}";
            string catalog = thread.Code ?? $"{i:D3}";
            WriteLine(writer, $"{JefSpec.PrefixThreadColor}:{hex},{desc},{catalog}");
        }

        // Header terminator
        writer.Write(JefSpec.HeaderTerminator);

        // Pad to 512 bytes
        long currentPos = writer.BaseStream.Position;
        for (long i = currentPos; i < JefSpec.HeaderSize; i++)
        {
            writer.Write(JefSpec.HeaderPad);
        }
    }

    private void WriteLine(BinaryWriter writer, string line, int valueWidth = 0)
    {
        string formatted = valueWidth > 0 ? line : line;
        byte[] bytes = Encoding.ASCII.GetBytes(formatted + "\r");
        writer.Write(bytes);
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

            if (stream.Length < JefSpec.HeaderSize)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "JEF.TOO_SHORT",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "JEF file too short for header",
                    Evidence = $"File length: {stream.Length}, expected at least {JefSpec.HeaderSize}",
                    Recommendation = "JEF files must have 512-byte header"
                });
                return result;
            }

            // Check header terminator
            byte[] header = new byte[JefSpec.HeaderSize];
            await stream.ReadAsync(header, 0, header.Length);
            
            int terminatorPos = Array.IndexOf(header, JefSpec.HeaderTerminator);
            if (terminatorPos < 0)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "JEF.MISSING_TERMINATOR",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "JEF header missing terminator (0x1A)",
                    Evidence = "No 0x1A found in first 512 bytes",
                    Recommendation = "File may be truncated or corrupt"
                });
            }

            // Check for LA: prefix
            string headerText = Encoding.ASCII.GetString(header, 0, Math.Min(20, header.Length));
            if (!headerText.StartsWith("LA:"))
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "JEF.MISSING_NAME_PREFIX",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "JEF header missing LA: prefix",
                    Evidence = $"Header starts with: {headerText}",
                    Recommendation = "Valid JEF files start with LA:name"
                });
            }

            stream.Position = originalPosition;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "JEF.VALIDATION_EXCEPTION",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Validation failed: {ex.Message}",
                Evidence = ex.ToString(),
                Recommendation = "File is not a valid JEF format"
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

        if (project.ThreadPalette.Count > JefSpec.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "JEF.TOO_MANY_COLORS",
                Severity = FmtValidationSeverity.Critical,
                Message = $"JEF supports maximum {JefSpec.MaxColors} colors",
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
            int deltaX = Math.Abs(stitch.X / JefSpec.MicronsPerJefUnit - lastX);
            int deltaY = Math.Abs(stitch.Y / JefSpec.MicronsPerJefUnit - lastY);
            int maxDelta = Math.Max(deltaX, deltaY);

            if (maxDelta > JefSpec.MaxStitchDistance)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "JEF.MOVEMENT_EXCEEDS_MAX",
                    Severity = FmtValidationSeverity.Warning,
                    Message = $"Movement exceeds JEF max delta per record ({JefSpec.MaxStitchDistance})",
                    Evidence = $"Delta: ({deltaX}, {deltaY}) at ({stitch.X}, {stitch.Y})",
                    Recommendation = "Long movements will be split into multiple records"
                });
            }
            lastX = stitch.X / JefSpec.MicronsPerJefUnit;
            lastY = stitch.Y / JefSpec.MicronsPerJefUnit;
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
                RuleId = "JEF.TOO_MANY_STITCHES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalStitches} stitches, JEF supports max {Capabilities.MaxTotalStitches}",
                Evidence = $"Total stitches: {plan.TotalStitches}",
                Recommendation = "Simplify design or split into multiple files"
            });
        }

        if (plan.TotalColorChanges > JefSpec.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "JEF.TOO_MANY_COLOR_CHANGES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalColorChanges} color changes, JEF supports max {JefSpec.MaxColors}",
                Evidence = $"Color changes: {plan.TotalColorChanges}",
                Recommendation = "Reduce color changes or split design"
            });
        }

        return result;
    }
}