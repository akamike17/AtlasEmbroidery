namespace AtlasEmbroidery.Domain.Formats.Pes;

using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Domain.Formats.Pec;
using FmtValidationIssue = AtlasEmbroidery.Domain.Formats.ValidationIssue;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;
using System.Text;

/// <summary>
/// Brother PES format constants and specifications
/// </summary>
public static class PesSpec
{
    // Signatures for different versions
    public const string SignatureV1 = "#PES0001";
    public const string SignatureV6 = "#PES0060";
    public const string SignatureV9 = "#PES0090";
    public const string SignatureV10 = "#PES0100";
    
    public static readonly string[] SupportedSignatures = { SignatureV1, SignatureV6, "#PES0030", "#PES0040", "#PES0050", "#PES0055", "#PES0056", "#PES0070", "#PES0080", SignatureV9, SignatureV10 };

    public const int MaxDeltaPerRecord = 2047; // Same as PEC
    public const int MicronsPerPesUnit = 100; // 0.1mm = 100 microns
    public const int MaxColors = 255;
    
    // PES v1 constants
    public const ushort PesV1Hoop100x100 = 0x01;
    public const ushort PesV1Hoop130x180 = 0x02;
    
    // PES v6+ constants
    public const ushort PesV6ScaleToFit = 0x01;
    public const string EmbOne = "CEmbOne";
    public const string EmbSeg = "CSewSeg";
    
    // Default hoop sizes (in 0.1mm units)
    public const int DefaultHoopWidth = 1300;  // 130mm
    public const int DefaultHoopHeight = 1800; // 180mm
    public const int DesignArea100x100 = 1000; // 100mm
    public const int DesignArea130x180 = 1800; // 180mm
    
    // Section end marker
    public const ushort SectionEnd = 0x8003;
    
    // Thread record size in addendum
    public const int ThreadAddendumBlockSize = 0x90; // 144 bytes per thread
}

/// <summary>
/// PES thread color information (from PES thread format)
/// </summary>
public sealed class PesThread
{
    public string CatalogNumber { get; set; } = "";
    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public string Description { get; set; } = "";
    public string Brand { get; set; } = "";
    public string Chart { get; set; } = "";
    
    public ThreadColor ToThreadColor() => new ThreadColor(R, G, B, Brand, CatalogNumber, Description);
}

/// <summary>
/// PES format adapter - reads and writes PES embroidery files
/// PES is a wrapper around PEC stitch data with version-specific headers
/// </summary>
public sealed class PesFormatAdapter : IEmbroideryFormatReader, IEmbroideryFormatWriter, IEmbroideryFormatValidator
{
    public string FormatName => "PES";
    public string FileExtension => ".pes";
    public string[] Extensions => new[] { ".pes", ".PES" };
    public string MimeType => "application/x-pes";
    public string DefaultExtension => ".pes";

    public FormatCapabilities Capabilities => new()
    {
        SupportsReading = true,
        SupportsWriting = true,
        SupportsTrim = true,
        SupportsJump = true,
        SupportsColorChange = true,
        SupportsStop = true,
        MaxStitchLength = PesSpec.MaxDeltaPerRecord * PesSpec.MicronsPerPesUnit,
        MaxJumpLength = PesSpec.MaxDeltaPerRecord * PesSpec.MicronsPerPesUnit,
        MaxTotalStitches = 2_000_000,
        MaxColors = PesSpec.MaxColors,
    };

    /// <summary>
    /// Reads a PES file and returns an AtlasProject
    /// </summary>
    public async Task<AtlasProject> ReadAsync(Stream stream, FormatReadOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatReadOptions();

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var project = new AtlasProject();

        // Read signature (8 bytes)
        byte[] signatureBytes = reader.ReadBytes(8);
        string signature = Encoding.ASCII.GetString(signatureBytes).TrimEnd('\0');
        
        if (!IsValidSignature(signature))
        {
            throw new InvalidDataException($"Invalid PES signature: '{signature}'");
        }

        // Read PEC block position (32-bit LE)
        int pecBlockPosition = reader.ReadInt32();
        
        // Parse version-specific header
        ParsePesHeader(reader, signature, project);
        
        // Seek to PEC block and read stitches using PEC reader
        stream.Position = pecBlockPosition;
        await ReadPecStitchesAsync(stream, project, ct);
        
        return project;
    }

    private static bool IsValidSignature(string sig)
    {
        return sig.StartsWith("#PES") || sig == "#PEC0001"; // Some PES files start with PEC signature
    }

    private void ParsePesHeader(BinaryReader reader, string signature, AtlasProject project)
    {
        // Determine version from signature
        double version = GetVersionFromSignature(signature);
        project.CustomData["pes_version"] = version;

        if (version <= 1.0)
        {
            ParsePesV1Header(reader, project);
        }
        else
        {
            ParsePesV6PlusHeader(reader, project, version);
        }
    }

    private static double GetVersionFromSignature(string sig)
    {
        return sig switch
        {
            "#PES0001" => 1.0,
            "#PES0020" => 2.0,
            "#PES0022" => 2.2,
            "#PES0030" => 3.0,
            "#PES0040" => 4.0,
            "#PES0050" or "#PES0055" or "#PES0056" => 5.0,
            "#PES0060" => 6.0,
            "#PES0070" => 7.0,
            "#PES0080" => 8.0,
            "#PES0090" => 9.0,
            "#PES0100" => 10.0,
            _ => 1.0
        };
    }

    private void ParsePesV1Header(BinaryReader reader, AtlasProject project)
    {
        // V1 header: scale (2), hoop (2), distinct_blocks (2)
        reader.ReadInt16(); // scale
        ushort hoop = reader.ReadUInt16();
        project.CustomData["hoop_type"] = hoop;
        reader.ReadInt16(); // distinct_blocks
    }

    private void ParsePesV6PlusHeader(BinaryReader reader, AtlasProject project, double version)
    {
        // Skip 4 bytes (unknown: scale + version string)
        reader.BaseStream.Seek(4, SeekOrigin.Current);

        // Read metadata strings (length-prefixed)
        project.Name = ReadPesString(reader) ?? "Untitled";
        project.CustomData["category"] = ReadPesString(reader) ?? "";
        project.CustomData["author"] = ReadPesString(reader) ?? "";
        project.CustomData["keywords"] = ReadPesString(reader) ?? "";
        project.CustomData["comments"] = ReadPesString(reader) ?? "";

        if (version >= 6.0)
        {
            // Writer writes 18 ushorts (36 bytes) + 1 byte + 24 bytes (transform) + 3 ushorts (6 bytes)
            // Read/seek the 18 ushorts (OptimizeHoopChange, DesignPageIsCustom, Hoop Width, Hoop Height,
            // UseExistingDesignArea, Design Width, Design Height, Design Page Section Width,
            // Design Page Section Height, p6, Background Color, Foreground Color,
            // Show Grid, With Axes, Snap To Grid, Grid Interval, p9, OptimizeEntryExitPoints)
            reader.BaseStream.Seek(36, SeekOrigin.Current); // 18 * 2 bytes

            reader.ReadByte(); // fromImageStringLength

            // Transform matrix (6 floats = 24 bytes)
            reader.BaseStream.Seek(24, SeekOrigin.Current);

            // Pattern counts (3 ushorts = 6 bytes) - programmable fills, motifs, feather patterns
            reader.BaseStream.Seek(6, SeekOrigin.Current);
        }

        if (version >= 9.0)
        {
            reader.BaseStream.Seek(14, SeekOrigin.Current); // hoop name
            reader.BaseStream.Seek(38, SeekOrigin.Current); // image file
        }
        else if (version >= 6.0)
        {
            // Writer doesn't have image file here - it has pattern counts (already consumed above)
            // No additional seek needed for v6
        }
        else if (version >= 5.0)
        {
            reader.BaseStream.Seek(24, SeekOrigin.Current); // image
        }

        // Skip programmable fills, motifs, feather patterns (already skipped above for v6+)
            // For v5, these would be here
            if (version < 6.0)
            {
                ushort countProgrammableFills = reader.ReadUInt16();
                if (countProgrammableFills != 0) return;

                // Skip pattern counts
                reader.ReadUInt16(); // programmable fills
                reader.ReadUInt16(); // motifs
                reader.ReadUInt16(); // feather patterns
            }

            // Read threads
            ushort countThreads = reader.ReadUInt16();
            var threads = new List<PesThread>();
            for (int i = 0; i < countThreads; i++)
            {
                var thread = ReadPesThread(reader);
                threads.Add(thread);
                project.ThreadPalette.Add(thread.ToThreadColor());
                project.ColorToNeedleMap[i] = i + 1;
            }

            // Distinct block objects (v6+)
            if (version >= 6.0)
            {
                reader.ReadInt16();
            }
        }

    private string? ReadPesString(BinaryReader reader)
    {
        byte length = reader.ReadByte();
        if (length == 0) return null;
        byte[] bytes = reader.ReadBytes(length);
        return Encoding.UTF8.GetString(bytes);
    }

    private PesThread ReadPesThread(BinaryReader reader)
    {
        var thread = new PesThread();
        thread.CatalogNumber = ReadPesString(reader) ?? "";
        thread.R = reader.ReadByte();
        thread.G = reader.ReadByte();
        thread.B = reader.ReadByte();
        reader.BaseStream.Seek(5, SeekOrigin.Current); // unknown (1) + custom color flag (4)
        thread.Description = ReadPesString(reader) ?? "";
        thread.Brand = ReadPesString(reader) ?? "";
        thread.Chart = ReadPesString(reader) ?? "";
        return thread;
    }

    private async Task ReadPecStitchesAsync(Stream stream, AtlasProject project, CancellationToken ct)
    {
        // Reuse PEC reading logic for stitch block
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        
        // PEC block starts with 31 FF F0
        byte[] blockHeader = reader.ReadBytes(3);
        if (blockHeader[0] != 0x31 || blockHeader[1] != 0xFF || blockHeader[2] != 0xF0)
        {
            throw new InvalidDataException("Invalid PEC block header in PES file");
        }

        // Read bounds (4 int16)
        int width = reader.ReadInt16();
        int height = reader.ReadInt16();
        reader.ReadInt16(); // hoop width
        reader.ReadInt16(); // hoop height

        // Read stitches using PEC decoder
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

            // END marker
            if (val1 == 0xFF)
                break;

            // Color change marker
            if (val1 == 0xFE && val2 == 0xB0)
            {
                if (stream.Position >= stream.Length)
                    break;
                byte ccIndex = reader.ReadByte();
                colorIndex = ccIndex % project.ThreadPalette.Count;
                continue;
            }

            bool xLong = (val1 & 0x80) != 0;

            int deltaX, deltaY;
            var control = PecControl.Normal;

            if (xLong)
            {
                if (stream.Position + 1 >= stream.Length)
                    break;
                byte val3 = reader.ReadByte();
                byte val4 = reader.ReadByte();

                int xCode = (val1 << 8) | val2;
                int yCode = (val3 << 8) | val4;

                deltaX = Decode12Bit(xCode);
                deltaY = Decode12Bit(yCode);

                bool jump = (val1 & 0x10) != 0 || (val3 & 0x10) != 0;
                bool trim = (val1 & 0x20) != 0 || (val3 & 0x20) != 0;

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
                currentX * PesSpec.MicronsPerPesUnit,
                -currentY * PesSpec.MicronsPerPesUnit, // PEC negates Y
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
            var shape = new ShapeObject
            {
                Name = "PES Import",
                Vertices = stitches.Select(s => new Point(s.X, s.Y)).ToList(),
                IsClosed = false,
                StitchParams = StitchParams.DefaultFor(StitchType.Running)
            };
            shape.StitchParams.Density = 4000;
            project.Objects.Add(shape);
        }
    }

    private static int Decode7Bit(byte b) => b <= 63 ? b : b - 128;
    private static int Decode12Bit(int code)
    {
        int value = code & 0xFFF;
        return value > 0x7FF ? value - 0x1000 : value;
    }

    /// <summary>
    /// Writes an AtlasProject to PES format (v6 by default)
    /// </summary>
    public async Task WriteAsync(AtlasProject project, Stream stream, FormatWriteOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatWriteOptions();

        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Compile to stitch plan
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        var allStitches = plan.GetAllStitches().ToList();

        // Write PES v6 signature
        writer.Write(Encoding.ASCII.GetBytes(PesSpec.SignatureV6));

        // Placeholder for PEC block position
        long pecBlockPlaceholder = writer.BaseStream.Position;
        writer.Write(0); // Will backfill

        // Write PES v6 header
        WritePesV6Header(writer, project, plan, allStitches);

        // Write stitch blocks (EMB_ONE + EMB_SEG sections)
        WritePesStitchBlocks(writer, project, plan, allStitches);

        // Backfill PEC block position
        long pecBlockPosition = writer.BaseStream.Position;
        writer.BaseStream.Seek(pecBlockPlaceholder, SeekOrigin.Begin);
        writer.Write((int)pecBlockPosition);
        writer.BaseStream.Seek(pecBlockPosition, SeekOrigin.Begin);

        // Write PEC stitch data
        WritePecStitchData(writer, plan, allStitches, ct);

        // Write PES addendum (thread color info)
        WritePesAddendum(writer, project, plan);

        // Final terminator
        writer.Write((ushort)0x0000);
    }

    private void WritePesV6Header(BinaryWriter writer, AtlasProject project, StitchPlan plan, List<StitchPoint> allStitches)
    {
        // Scale to fit
        writer.Write((ushort)PesSpec.PesV6ScaleToFit);
        
        // Version string "02"
        writer.Write(Encoding.ASCII.GetBytes("02"));
        
        // Metadata strings
        WritePesString8(writer, project.Name ?? "Untitled");
        WritePesString8(writer, project.CustomData.GetValueOrDefault("category")?.ToString() ?? "");
        WritePesString8(writer, project.CustomData.GetValueOrDefault("author")?.ToString() ?? "");
        WritePesString8(writer, project.CustomData.GetValueOrDefault("keywords")?.ToString() ?? "");
        WritePesString8(writer, project.CustomData.GetValueOrDefault("comments")?.ToString() ?? "");
        
        // Booleans and hoop settings
        writer.Write((ushort)0); // OptimizeHoopChange
        writer.Write((ushort)0); // DesignPageIsCustom
        writer.Write((ushort)PesSpec.DefaultHoopWidth);
        writer.Write((ushort)PesSpec.DefaultHoopHeight);
        writer.Write((ushort)0); // UseExistingDesignArea
        writer.Write((ushort)PesSpec.DesignArea130x180);
        writer.Write((ushort)PesSpec.DesignArea130x180);
        writer.Write((ushort)PesSpec.DesignArea100x100);
        writer.Write((ushort)PesSpec.DesignArea100x100);
        writer.Write((ushort)0x64); // p6
        writer.Write((ushort)0x07); // Background color
        writer.Write((ushort)0x13); // Foreground color
        writer.Write((ushort)0x01); // ShowGrid
        writer.Write((ushort)0x01); // WithAxes
        writer.Write((ushort)0x00); // SnapToGrid
        writer.Write((ushort)100); // GridInterval
        writer.Write((ushort)0x01); // p9
        writer.Write((ushort)0x00); // OptimizeEntryExitPoints
        writer.Write((byte)0); // fromImageStringLength
        
        // Transform matrix (6 floats)
        writer.Write(1.0f); writer.Write(0.0f); writer.Write(0.0f);
        writer.Write(1.0f); writer.Write(0.0f); writer.Write(0.0f);
        
        // Pattern counts (all zero for now)
        writer.Write((ushort)0); // Programmable fills
        writer.Write((ushort)0); // Motifs
        writer.Write((ushort)0); // Feather patterns
        
        // Thread colors
        writer.Write((ushort)plan.ThreadPalette.Count);
        foreach (var thread in plan.ThreadPalette)
        {
            WritePesThread(writer, thread);
        }
        
        // Distinct block objects
        writer.Write((ushort)1);
    }

    private void WritePesString8(BinaryWriter writer, string? str)
    {
        if (string.IsNullOrEmpty(str))
        {
            writer.Write((byte)0);
            return;
        }
        byte[] bytes = Encoding.UTF8.GetBytes(str);
        if (bytes.Length > 255) bytes = bytes[..255];
        writer.Write((byte)bytes.Length);
        writer.Write(bytes);
    }

    private void WritePesThread(BinaryWriter writer, ThreadColor thread)
    {
        WritePesString8(writer, thread.Code ?? "");       // CatalogNumber
        writer.Write(thread.R);
        writer.Write(thread.G);
        writer.Write(thread.B);
        writer.Write(new byte[5]); // unknown (1) + custom color flag (4)
        WritePesString8(writer, thread.Description ?? ""); // Description
        WritePesString8(writer, thread.Brand ?? "");       // Brand
        WritePesString8(writer, thread.Code ?? "");        // Chart (use Code as fallback)
    }

    private void WritePesStitchBlocks(BinaryWriter writer, AtlasProject project, StitchPlan plan, List<StitchPoint> allStitches)
    {
        if (allStitches.Count == 0) return;

        // Calculate bounds
        int minX = allStitches.Min(s => s.X);
        int maxX = allStitches.Max(s => s.X);
        int minY = allStitches.Min(s => s.Y);
        int maxY = allStitches.Max(s => s.Y);

        // Center coordinates
        double cx = (maxX + minX) / 2.0;
        double cy = (maxY + minY) / 2.0;

        double left = minX - cx;
        double top = minY - cy;
        double right = maxX - cx;
        double bottom = maxY - cy;

        // EMB_ONE block
        writer.Write(Encoding.ASCII.GetBytes(PesSpec.EmbOne));
        long sewSegPlaceholder = WritePesSewSegHeader(writer, left, top, right, bottom);
        
        // Section count placeholder (will be patched)
        writer.Write((ushort)0xFFFF);
        writer.Write((ushort)0x0000);

        // EMB_SEG block
        writer.Write(Encoding.ASCII.GetBytes(PesSpec.EmbSeg));
        var (sections, colorLog) = WritePesEmbSegSegments(writer, project, plan, allStitches, left, bottom, cx, cy);

        // Patch section count
        long currentPos = writer.BaseStream.Position;
        writer.BaseStream.Seek(sewSegPlaceholder, SeekOrigin.Begin);
        writer.Write((ushort)sections);
        writer.BaseStream.Seek(currentPos, SeekOrigin.Begin);

        // Terminator for blocks
        writer.Write((ushort)0x0000);
        writer.Write((ushort)0x0000);
    }

    private long WritePesSewSegHeader(BinaryWriter writer, double left, double top, double right, double bottom)
    {
        double width = right - left;
        double height = bottom - top;
        double hoopHeight = PesSpec.DefaultHoopHeight;
        double hoopWidth = PesSpec.DefaultHoopWidth;

        // Bounding boxes (8 int16 = 0s)
        for (int i = 0; i < 8; i++) writer.Write((short)0);

        // Transform matrix
        double transX = 350 + hoopWidth / 2 - width / 2;
        double transY = 100 + height + hoopHeight / 2 - height / 2;
        
        writer.Write(1.0f); writer.Write(0.0f); writer.Write(0.0f);
        writer.Write(1.0f); writer.Write((float)transX); writer.Write((float)transY);

        writer.Write((ushort)1);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((short)width);
        writer.Write((short)height);
        writer.Write(new byte[8]);

        // Section count placeholder
        long placeholder = writer.BaseStream.Position;
        writer.Write((ushort)0);
        return placeholder;
    }

    private (int sections, List<(int section, int color)> colorLog) WritePesEmbSegSegments(
        BinaryWriter writer, AtlasProject project, StitchPlan plan, 
        List<StitchPoint> allStitches, double left, double bottom, double cx, double cy)
    {
        int section = 0;
        var colorLog = new List<(int section, int color)>();
        int previousColorCode = -1;
        int flag = -1;

        double adjustX = left + cx;
        double adjustY = bottom + cy;

        // Group stitches by color segments
        var colorSegments = GroupStitchesByColor(allStitches);

        foreach (var (segments, colorCode, segFlag) in colorSegments)
        {
            if (flag != -1)
            {
                writer.Write(PesSpec.SectionEnd); // Section end
            }

            if (previousColorCode != colorCode)
            {
                colorLog.Add((section, colorCode));
                previousColorCode = colorCode;
            }

            writer.Write((ushort)segFlag);
            writer.Write((ushort)colorCode);
            writer.Write((ushort)segments.Count);

            foreach (var (x, y) in segments)
            {
                int stitchX = (int)Math.Round(x - adjustX);
                int stitchY = (int)Math.Round(y - adjustY);
                writer.Write((short)stitchX);
                writer.Write((short)stitchY);
            }

            section++;
            flag = segFlag;
        }

        // Final section end
        writer.Write(PesSpec.SectionEnd);

        // Color log
        writer.Write((ushort)colorLog.Count);
        foreach (var (sec, color) in colorLog)
        {
            writer.Write((ushort)sec);
            writer.Write((ushort)color);
        }

        return (section, colorLog);
    }

    private IEnumerable<(List<(int x, int y)> segments, int colorCode, int flag)> GroupStitchesByColor(List<StitchPoint> allStitches)
    {
        int currentColorCode = 0;
        var currentSegment = new List<(int x, int y)>();

        foreach (var stitch in allStitches)
        {
            if (stitch.ColorIndex != currentColorCode)
            {
                if (currentSegment.Count > 0)
                {
                    yield return (currentSegment, currentColorCode, stitch.IsJump ? 1 : 0);
                    currentSegment = new List<(int x, int y)>();
                }
                currentColorCode = stitch.ColorIndex;
            }

            currentSegment.Add((stitch.X, stitch.Y));
        }

        if (currentSegment.Count > 0)
        {
            yield return (currentSegment, currentColorCode, 0);
        }
    }

    private void WritePecStitchData(BinaryWriter writer, StitchPlan plan, List<StitchPoint> allStitches, CancellationToken ct = default)
    {
        // PEC block header
        writer.Write(new byte[] { 0x31, 0xFF, 0xF0 });

        // Bounds - handle empty stitch list
        int minX = 0, maxX = 0, minY = 0, maxY = 0;
        if (allStitches.Count > 0)
        {
            minX = allStitches.Min(s => s.X / PesSpec.MicronsPerPesUnit);
            maxX = allStitches.Max(s => s.X / PesSpec.MicronsPerPesUnit);
            minY = allStitches.Min(s => s.Y / PesSpec.MicronsPerPesUnit);
            maxY = allStitches.Max(s => s.Y / PesSpec.MicronsPerPesUnit);
        }

        writer.WriteInt16LE(maxX - minX);
        writer.WriteInt16LE(maxY - minY);
        writer.WriteInt16LE(PesSpec.DefaultHoopWidth);
        writer.WriteInt16LE(PesSpec.DefaultHoopHeight);

        // Encode stitches using PEC encoder logic
        int lastX = 0, lastY = 0;
        int currentColor = -1;
        bool firstStitch = true;
        bool jumping = true;

        foreach (var stitch in allStitches)
        {
            ct.ThrowIfCancellationRequested();

            int targetX = stitch.X / PesSpec.MicronsPerPesUnit;
            int targetY = stitch.Y / PesSpec.MicronsPerPesUnit;
            int deltaX = targetX - lastX;
            int deltaY = targetY - lastY;

            // Handle color change
            if (stitch.ColorIndex != currentColor)
            {
                if (!firstStitch)
                {
                    writer.Write(new byte[] { 0xFE, 0xB0, (byte)stitch.ColorIndex });
                }
                currentColor = stitch.ColorIndex;
                jumping = true;
            }

            if (stitch.IsJump) jumping = true;

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

        // END marker
        writer.Write(new byte[] { 0xFF });
    }

    private void WritePesAddendum(BinaryWriter writer, AtlasProject project, StitchPlan plan)
    {
        // Color index list (thread indices)
        for (int i = 0; i < plan.ThreadPalette.Count; i++)
        {
            writer.Write((byte)i);
        }
        // Pad to 128
        for (int i = plan.ThreadPalette.Count; i < 128; i++)
        {
            writer.Write((byte)0x20);
        }

        // Thread color blocks (144 bytes each)
        for (int i = 0; i < plan.ThreadPalette.Count; i++)
        {
            writer.Write(new byte[PesSpec.ThreadAddendumBlockSize]);
        }

        // RGB values (24-bit each)
        foreach (var thread in plan.ThreadPalette)
        {
            writer.WriteInt24LE((thread.R << 16) | (thread.G << 8) | thread.B);
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

            if (stream.Length < 12)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "PES.TOO_SHORT",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "PES file too short for signature and PEC block position",
                    Evidence = $"File length: {stream.Length}",
                    Recommendation = "File must be at least 12 bytes for PES signature + PEC block position"
                });
                return result;
            }

            byte[] signatureBytes = reader.ReadBytes(8);
            string signature = Encoding.ASCII.GetString(signatureBytes).TrimEnd('\0');
            if (!IsValidSignature(signature))
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "PES.INVALID_SIGNATURE",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "Invalid PES signature",
                    Evidence = $"Expected '#PESxxxx' or '#PEC0001', got '{signature}'",
                    Recommendation = "Ensure file is a valid PES format"
                });
            }

            stream.Position = originalPosition;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "PES.VALIDATION_EXCEPTION",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Validation failed: {ex.Message}",
                Evidence = ex.ToString(),
                Recommendation = "File is not a valid PES format"
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

        if (project.ThreadPalette.Count > PesSpec.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "PES.TOO_MANY_COLORS",
                Severity = FmtValidationSeverity.Critical,
                Message = $"PES supports maximum {PesSpec.MaxColors} colors",
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
            int deltaX = Math.Abs(stitch.X / PesSpec.MicronsPerPesUnit - lastX);
            int deltaY = Math.Abs(stitch.Y / PesSpec.MicronsPerPesUnit - lastY);
            int maxDelta = Math.Max(deltaX, deltaY);

            if (maxDelta > PesSpec.MaxDeltaPerRecord)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "PES.MOVEMENT_EXCEEDS_MAX",
                    Severity = FmtValidationSeverity.Warning,
                    Message = $"Movement exceeds PES max delta per record ({PesSpec.MaxDeltaPerRecord})",
                    Evidence = $"Delta: ({deltaX}, {deltaY}) at ({stitch.X}, {stitch.Y})",
                    Recommendation = "Long movements will be split into multiple records"
                });
            }
            lastX = stitch.X / PesSpec.MicronsPerPesUnit;
            lastY = stitch.Y / PesSpec.MicronsPerPesUnit;
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
                RuleId = "PES.TOO_MANY_STITCHES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalStitches} stitches, PES supports max {Capabilities.MaxTotalStitches}",
                Evidence = $"Total stitches: {plan.TotalStitches}",
                Recommendation = "Simplify design or split into multiple files"
            });
        }

        if (plan.TotalColorChanges > Capabilities.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "PES.TOO_MANY_COLOR_CHANGES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalColorChanges} color changes, PES supports max {Capabilities.MaxColors}",
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