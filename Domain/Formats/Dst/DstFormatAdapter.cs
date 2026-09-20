namespace AtlasEmbroidery.Domain.Formats.Dst;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Domain.Formats;
using FmtValidationIssue = AtlasEmbroidery.Domain.Formats.ValidationIssue;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;
using System.Text;

/// <summary>
/// DST (Tajima) format adapter - Reader/Writer/Normalization/Read-back/Semantic diff
/// DST es el formato nativo de máquinas Tajima, ampliamente soportado
/// </summary>
public sealed class DstFormatAdapter : IEmbroideryFormatReader, IEmbroideryFormatWriter, IEmbroideryNormalizer, IEmbroideryFormatValidator
{
    public string FormatName => "DST";
    public string FileExtension => ".dst";
    public string[] Extensions => new[] { ".dst", ".DST" };
    public string MimeType => "application/x-dst";
    public string DefaultExtension => ".dst";
    public FormatCapabilities Capabilities => new()
    {
        SupportsReading = true,
        SupportsWriting = true,
        SupportsTrim = true,
        SupportsJump = true,
        SupportsColorChange = true,
        SupportsStop = true,
        SupportsSequins = false,
        SupportsPuff3D = false,
        MaxStitchLength = 1270, // 12.7mm en décimas de mm (0.1mm units)
        MaxJumpLength = 1270,
        MaxStitchesPerColor = 65535,
        MaxTotalStitches = 2000000,
        MaxColors = 250,
    };

    private const byte StitchControlByte = 0x80;
    private const byte TrimMask = 0x04;
    private const byte StopMask = 0x08;
    private const byte ColorChangeMask = 0x01;
    private const byte JumpMask = 0x02;
    private const byte EndOfDataMask = 0x03;

    /// <summary>
    /// Lee un archivo DST y convierte a AtlasProject
    /// </summary>
    public async Task<AtlasProject> ReadAsync(Stream stream, FormatReadOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatReadOptions();
        
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);

        // Header DST: 512 bytes
        var header = reader.ReadBytes(512);
        if (header.Length < 512)
            throw new InvalidDataException("DST file too small for header");

        // Validate if requested
        if (options.ValidateOnly)
        {
            var validation = Validate(stream);
            if (!validation.IsValid)
                throw new InvalidDataException($"DST validation failed: {string.Join("; ", validation.Issues.Select(i => i.Message))}");
            // Return minimal project for validation-only
            return new AtlasProject { Name = "Validation Only" };
        }

        // Parse header
        var project = ParseHeader(header);
        
        // Check size limits
        if (options.MaxStitches > 0)
        {
            // We'll check during reading
        }

        // Read stitch data
        var stitches = new List<StitchPoint>();
        var currentX = 0;
        var currentY = 0;
        var colorIndex = 0;
        var needleIndex = 1;
        int stitchCount = 0;

        // DST stitch records are 3 bytes each
        while (stream.Position < stream.Length)
        {
            ct.ThrowIfCancellationRequested();

            if (stitchCount >= options.MaxStitches)
                throw new InvalidDataException($"Stitch count exceeds maximum allowed: {options.MaxStitches}");

            byte b1 = reader.ReadByte();
            byte b2 = reader.ReadByte();
            byte b3 = reader.ReadByte();

            // Check for end of data (0xF3, 0x00, 0x00 or similar)
            if (b1 == 0xF3 && b2 == 0x00 && b3 == 0x00)
                break;

            // Parse DST stitch format
            var (dx, dy, flags) = DecodeStitch(b1, b2, b3);
            
            currentX += dx;
            currentY += dy;

            var stitch = new StitchPoint(currentX, currentY, StitchType.Running, (byte)needleIndex, (byte)colorIndex, (ushort)flags);

            // Handle control commands
            if ((flags & (ushort)DstFlags.ColorChange) != 0)
            {
                colorIndex++;
                if (colorIndex >= options.MaxColors)
                    throw new InvalidDataException($"Color count exceeds maximum allowed: {options.MaxColors}");
                needleIndex = (colorIndex % Capabilities.MaxColors) + 1;
            }

            if ((flags & (ushort)DstFlags.End) != 0)
                break;

            stitches.Add(stitch);
            stitchCount++;
        }

        // Create a single shape object with all stitches
        var shapeObj = new ShapeObject
        {
            Name = "Imported DST",
            Vertices = stitches.Select(s => s.Position).ToList(),
            IsClosed = false,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };

        project.Objects.Add(shapeObj);
        project.ThreadPalette = GenerateDefaultPalette(colorIndex + 1);
        
        // Update ColorToNeedleMap
        for (int i = 0; i <= colorIndex; i++)
        {
            project.ColorToNeedleMap[i] = (i % Capabilities.MaxColors) + 1;
        }

        project.RecalculateBounds();
        project.Touch();

        return project;
    }

    /// <summary>
    /// Escribe AtlasProject a formato DST
    /// </summary>
    public async Task WriteAsync(AtlasProject project, Stream stream, FormatWriteOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatWriteOptions();
        
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Compile to stitch plan
        var engine = new StitchEngine();
        var plan = engine.Compile(project);

        // Build header
        var header = BuildHeader(project, plan);
        writer.Write(header);

        // Write stitch data
        var allStitches = plan.GetAllStitches();
        var lastX = 0;
        var lastY = 0;
        var currentColor = -1;

        foreach (var stitch in allStitches)
        {
            ct.ThrowIfCancellationRequested();

            int dx = stitch.X - lastX;
            int dy = stitch.Y - lastY;

            // Clamp to DST limits (±1270 in 0.1mm units = ±12.7mm)
            dx = Math.Clamp(dx, -Capabilities.MaxStitchLength, Capabilities.MaxStitchLength);
            dy = Math.Clamp(dy, -Capabilities.MaxStitchLength, Capabilities.MaxStitchLength);

            byte flags = 0;
            
            if (stitch.IsTrim) flags |= (byte)DstFlags.Trim;
            if (stitch.IsJump) flags |= (byte)DstFlags.Jump;
            if (stitch.IsStop) flags |= (byte)DstFlags.Stop;
            
            // Color change detection
            if (stitch.ColorIndex != currentColor)
            {
                currentColor = stitch.ColorIndex;
                if (currentColor > 0)
                    flags |= (byte)DstFlags.ColorChange;
            }

            var (b1, b2, b3) = EncodeStitch(dx, dy, flags);
            writer.Write(b1);
            writer.Write(b2);
            writer.Write(b3);

            lastX = stitch.X;
            lastY = stitch.Y;
        }

        // End of data marker
        writer.Write((byte)0xF3);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        // Pad to 3-byte boundary if needed
        while (stream.Position % 3 != 0)
        {
            writer.Write((byte)0x00);
        }
    }

    /// <summary>
    /// Normaliza un proyecto DST (limpia, valida, corrige)
    /// </summary>
    public AtlasProject Normalize(AtlasProject project, NormalizationProfile? profile = null)
    {
        profile ??= new NormalizationProfile();
        var normalized = project.DeepClone();

        // Remove stitches beyond machine limits
        foreach (var obj in normalized.Objects)
        {
            var param = obj.StitchParams;
            if (profile.ClampToMachineLimits)
            {
                param.MaxStitchLength = Math.Min(param.MaxStitchLength, profile.MaxStitchLengthMicrons);
                param.MaxJumpDistance = Math.Min(param.MaxJumpDistance, profile.MaxJumpLengthMicrons);
            }
        }

        // Ensure color palette doesn't exceed limits
        if (profile.EnsureValidColorPalette && normalized.ThreadPalette.Count > profile.MaxColors)
        {
            normalized.ThreadPalette = normalized.ThreadPalette.Take(profile.MaxColors).ToList();
        }

        // Remove duplicate consecutive stitches
        if (profile.RemoveDuplicateStitches)
        {
            // Placeholder - actual deduplication would need StitchPlan
            // This is handled at the StitchPlan level during compilation
        }

        normalized.RecalculateBounds();
        normalized.Touch();
        
        return normalized;
    }

    /// <summary>
    /// Validates a DST file stream (synchronous - implements IEmbroideryFormatReader)
    /// </summary>
    public FormatValidationResult Validate(Stream stream)
    {
        return ValidateAsync(stream).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Validates a DST file stream (async)
    /// </summary>
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
            var originalPosition = stream.Position;
            stream.Position = 0;
            
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            
            // Check minimum size
            if (stream.Length < 512)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_TOO_SMALL",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "DST file too small for header (minimum 512 bytes)",
                    Evidence = $"File size: {stream.Length} bytes",
                    Recommendation = "Ensure file is a valid DST format"
                });
                stream.Position = originalPosition;
                return result;
            }

            var header = reader.ReadBytes(512);
            
            // Check magic bytes (DST files typically start with spaces or specific signature)
            // DST doesn't have a strong magic, but check for reasonable header
            var name = Encoding.ASCII.GetString(header, 2, 16).TrimEnd('\0', ' ');
            if (string.IsNullOrWhiteSpace(name))
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_EMPTY_NAME",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "DST header has empty design name",
                    Evidence = "Bytes 2-17 are empty/whitespace",
                    Recommendation = "Design name should be populated"
                });
            }

            // Check dimensions
            int width = BitConverter.ToInt16(header, 90);
            int height = BitConverter.ToInt16(header, 92);
            if (width <= 0 || height <= 0 || width > 10000 || height > 10000)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_INVALID_DIMENSIONS",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "DST header contains suspicious dimensions",
                    Evidence = $"Width: {width}, Height: {height} (in 0.1mm units)",
                    Recommendation = "Verify design dimensions are reasonable"
                });
            }

            // Check stitch count
            int stitchCount = BitConverter.ToInt32(header, 98);
            if (stitchCount < 0 || stitchCount > Capabilities.MaxTotalStitches)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_INVALID_STITCH_COUNT",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "DST header stitch count out of valid range",
                    Evidence = $"Stitch count: {stitchCount}, Max: {Capabilities.MaxTotalStitches}",
                    Recommendation = "File may be corrupted or not a valid DST"
                });
            }

            // Check color count
            int colorCount = header[102];
            if (colorCount > Capabilities.MaxColors)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_TOO_MANY_COLORS",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "DST header color count exceeds format maximum",
                    Evidence = $"Colors: {colorCount}, Max: {Capabilities.MaxColors}",
                    Recommendation = "Reduce color count or split design"
                });
            }

            // Validate stitch data (sample check)
            stream.Position = 512;
            int validStitches = 0;
            int maxCheck = Math.Min(1000, (int)(stream.Length - 512) / 3);
            
            for (int i = 0; i < maxCheck && stream.Position + 2 < stream.Length; i++)
            {
                try
                {
                    byte b1 = reader.ReadByte();
                    byte b2 = reader.ReadByte();
                    byte b3 = reader.ReadByte();
                    
                    // Check for end marker
                    if (b1 == 0xF3 && b2 == 0x00 && b3 == 0x00)
                        break;
                    
                    var (dx, dy, flags) = DecodeStitch(b1, b2, b3);
                    
                    // Check for reasonable stitch lengths
                    if (Math.Abs(dx) > 2047 || Math.Abs(dy) > 2047)
                    {
                        result.Issues.Add(new FmtValidationIssue
                        {
                            RuleId = "DST.STITCH_OUT_OF_RANGE",
                            Severity = FmtValidationSeverity.Warning,
                            Message = "Stitch delta exceeds 12-bit signed range",
                            Evidence = $"Stitch {i}: dx={dx}, dy={dy}",
                            Position = stream.Position - 3,
                            Recommendation = "Stitch will be clamped during read"
                        });
                    }
                    
                    validStitches++;
                }
                catch
                {
                    result.Issues.Add(new FmtValidationIssue
                    {
                        RuleId = "DST.STITCH_READ_ERROR",
                        Severity = FmtValidationSeverity.Warning,
                        Message = "Failed to parse stitch data",
                        Evidence = $"At stitch index {i}",
                        Position = stream.Position,
                        Recommendation = "File may have corrupted stitch data"
                    });
                }
            }
            
            result.DetectedCapabilities = Capabilities;
            stream.Position = originalPosition;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "DST.VALIDATION_EXCEPTION",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Validation failed with exception: {ex.Message}",
                Evidence = ex.ToString(),
                Recommendation = "File is not a valid DST format"
            });
        }

        return result;
    }

    /// <summary>
    /// Validates an AtlasProject for DST compatibility
    /// </summary>
    public FormatValidationResult Validate(AtlasProject project, MachineProfile? machine = null, HoopProfile? hoop = null)
    {
        var result = new FormatValidationResult 
        { 
            FormatName = FormatName,
            IsValid = true,
            Issues = new List<FmtValidationIssue>()
        };

        // Check color count
        if (project.ThreadPalette.Count > Capabilities.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "DST.TOO_MANY_COLORS",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Project has {project.ThreadPalette.Count} colors, DST supports max {Capabilities.MaxColors}",
                Evidence = $"Color count: {project.ThreadPalette.Count}",
                Recommendation = "Reduce color palette or split design"
            });
        }

        // Check bounds against machine/hoop
        var bounds = project.GetDesignBounds();
        if (machine != null)
        {
            if (bounds.Width > machine.MaxWidth || bounds.Height > machine.MaxHeight)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.EXCEEDS_MACHINE_FIELD",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "Design exceeds machine maximum field size",
                    Evidence = $"Design: {bounds.Width}x{bounds.Height}µm, Machine: {machine.MaxWidth}x{machine.MaxHeight}µm",
                    Recommendation = "Resize design or use larger machine"
                });
            }
        }

        if (hoop != null)
        {
            if (bounds.Width > hoop.UsableWidth || bounds.Height > hoop.UsableHeight)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.EXCEEDS_HOOP",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "Design exceeds hoop usable area",
                    Evidence = $"Design: {bounds.Width}x{bounds.Height}µm, Hoop usable: {hoop.UsableWidth}x{hoop.UsableHeight}µm",
                    Recommendation = "Use larger hoop or reposition design"
                });
            }
        }

        // Check stitch lengths
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        var longStitches = plan.GetAllStitches().Where(s => s.IsSewing && 
            Math.Max(Math.Abs(s.X), Math.Abs(s.Y)) > Capabilities.MaxStitchLength * 10).ToList();
        
        if (longStitches.Count > 0)
        {
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "DST.STITCH_LENGTH_EXCEEDS_LIMIT",
                Severity = FmtValidationSeverity.Warning,
                Message = $"{longStitches.Count} stitches exceed DST max stitch length (12.7mm)",
                Evidence = "Stitches will be clamped during write",
                Recommendation = "Consider reducing stitch length in digitization"
            });
        }

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

        // Check total stitches
        if (plan.TotalStitches > Capabilities.MaxTotalStitches)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "DST.TOO_MANY_STITCHES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalStitches} stitches, DST supports max {Capabilities.MaxTotalStitches}",
                Evidence = $"Total stitches: {plan.TotalStitches}",
                Recommendation = "Simplify design or split into multiple files"
            });
        }

        return result;
    }

    /// <summary>
    /// Round-trip test: Read -> Write -> Read and compare
    /// </summary>
    public async Task<RoundTripResult> RoundTripTestAsync(Stream originalStream, CancellationToken ct = default)
    {
        var result = new RoundTripResult { FormatName = FormatName };

        try
        {
            // First read
            originalStream.Position = 0;
            var project1 = await ReadAsync(originalStream, null, ct);

            // Write to memory
            using var ms = new MemoryStream();
            await WriteAsync(project1, ms, null, ct);

            // Read back
            ms.Position = 0;
            var project2 = await ReadAsync(ms, null, ct);

            // Semantic diff
            result.Differences = SemanticDiff(project1, project2);
            result.Success = result.Differences.Count == 0;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Semantic diff entre dos proyectos
    /// </summary>
    public List<SemanticDifference> SemanticDiff(AtlasProject a, AtlasProject b)
    {
        var diffs = new List<SemanticDifference>();

        // Compare stitch counts
        var engine = new StitchEngine();
        var planA = engine.Compile(a);
        var planB = engine.Compile(b);

        if (planA.TotalStitches != planB.TotalStitches)
        {
            diffs.Add(new SemanticDifference
            {
                Type = DifferenceType.StitchCount,
                Description = $"Stitch count differs: {planA.TotalStitches} vs {planB.TotalStitches}",
                ValueA = planA.TotalStitches.ToString(),
                ValueB = planB.TotalStitches.ToString()
            });
        }

        if (planA.TotalJumps != planB.TotalJumps)
        {
            diffs.Add(new SemanticDifference
            {
                Type = DifferenceType.JumpCount,
                Description = $"Jump count differs: {planA.TotalJumps} vs {planB.TotalJumps}",
                ValueA = planA.TotalJumps.ToString(),
                ValueB = planB.TotalJumps.ToString()
            });
        }

        if (planA.TotalTrims != planB.TotalTrims)
        {
            diffs.Add(new SemanticDifference
            {
                Type = DifferenceType.TrimCount,
                Description = $"Trim count differs: {planA.TotalTrims} vs {planB.TotalTrims}",
                ValueA = planA.TotalTrims.ToString(),
                ValueB = planB.TotalTrims.ToString()
            });
        }

        if (planA.TotalColorChanges != planB.TotalColorChanges)
        {
            diffs.Add(new SemanticDifference
            {
                Type = DifferenceType.ColorChangeCount,
                Description = $"Color change count differs: {planA.TotalColorChanges} vs {planB.TotalColorChanges}",
                ValueA = planA.TotalColorChanges.ToString(),
                ValueB = planB.TotalColorChanges.ToString()
            });
        }

        // Compare bounds
        if (!planA.DesignBounds.Equals(planB.DesignBounds))
        {
            diffs.Add(new SemanticDifference
            {
                Type = DifferenceType.Bounds,
                Description = $"Design bounds differ: {planA.DesignBounds} vs {planB.DesignBounds}",
                ValueA = planA.DesignBounds.ToString(),
                ValueB = planB.DesignBounds.ToString()
            });
        }

        // Compare color palette
        if (a.ThreadPalette.Count != b.ThreadPalette.Count)
        {
            diffs.Add(new SemanticDifference
            {
                Type = DifferenceType.ColorPalette,
                Description = $"Color count differs: {a.ThreadPalette.Count} vs {b.ThreadPalette.Count}",
                ValueA = a.ThreadPalette.Count.ToString(),
                ValueB = b.ThreadPalette.Count.ToString()
            });
        }

        return diffs;
    }

    /// <summary>
    /// Fuzz testing - genera archivos DST válidos aleatorios
    /// </summary>
    public byte[] GenerateFuzzInput(int seed = 0)
    {
        var rand = new Random(seed);
        var ms = new MemoryStream();
        var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // Valid header
        var header = new byte[512];
        rand.NextBytes(header);
        // Ensure valid header fields
        Encoding.ASCII.GetBytes("LA:").CopyTo(header, 0);
        Encoding.ASCII.GetBytes("Fuzz Test").CopyTo(header, 2);
        writer.Write(header);

        // Random stitch data
        int stitchCount = rand.Next(100, 5000);
        int x = 0, y = 0;

        for (int i = 0; i < stitchCount; i++)
        {
            int dx = rand.Next(-100, 101);
            int dy = rand.Next(-100, 101);
            byte flags = (byte)rand.Next(0, 16);

            var (b1, b2, b3) = EncodeStitch(dx, dy, flags);
            writer.Write(b1);
            writer.Write(b2);
            writer.Write(b3);

            x += dx;
            y += dy;
        }

        // End marker
        writer.Write((byte)0xF3);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        writer.Flush();
        return ms.ToArray();
    }

    #region Private Implementation

    private AtlasProject ParseHeader(byte[] header)
    {
        var project = new AtlasProject
        {
            Name = Encoding.ASCII.GetString(header, 2, 16).TrimEnd('\0', ' '),
            SourceFileHash = ComputeHeaderHash(header)
        };

        // Parse dimensions (bytes 90-97 typically)
        // DST stores dimensions in 0.1mm units
        int width = BitConverter.ToInt16(header, 90);
        int height = BitConverter.ToInt16(header, 92);
        
        project.CanvasWidth = width * 100; // Convert 0.1mm to microns
        project.CanvasHeight = height * 100;

        // Stitch count
        int stitchCount = BitConverter.ToInt32(header, 98);
        
        // Color count
        int colorCount = header[102];

        // Generate default palette
        project.ThreadPalette = GenerateDefaultPalette(colorCount);
        for (int i = 0; i < colorCount; i++)
        {
            project.ColorToNeedleMap[i] = (i % 15) + 1;
        }

        return project;
    }

    private byte[] BuildHeader(AtlasProject project, StitchPlan plan)
    {
        var header = new byte[512];
        
        // Magic bytes
        header[0] = 0x20; // Space
        header[1] = 0x20; // Space
        
        // Name (16 bytes at offset 2)
        var nameBytes = Encoding.ASCII.GetBytes(project.Name.PadRight(16).Substring(0, 16));
        nameBytes.CopyTo(header, 2);
        
        // Dimensions at offset 90-97 (in 0.1mm)
        var bounds = project.GetDesignBounds();
        short width = (short)(bounds.Width / 100);
        short height = (short)(bounds.Height / 100);
        BitConverter.GetBytes(width).CopyTo(header, 90);
        BitConverter.GetBytes(height).CopyTo(header, 92);
        
        // Stitch count at offset 98
        BitConverter.GetBytes((int)plan.TotalStitches).CopyTo(header, 98);
        
        // Color count at offset 102
        header[102] = (byte)Math.Min(project.ThreadPalette.Count, 255);
        
        return header;
    }

    private string ComputeHeaderHash(byte[] header)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(header)).ToLowerInvariant();
    }

    private List<ThreadColor> GenerateDefaultPalette(int count)
    {
        var palette = new List<ThreadColor>();
        var hues = new[] { 0, 30, 60, 120, 180, 240, 270, 300 };
        
        for (int i = 0; i < count; i++)
        {
            int hue = hues[i % hues.Length] + (i / hues.Length) * 15;
            var color = HsvToRgb(hue % 360, 0.8, 0.9);
            palette.Add(new ThreadColor(color.R, color.G, color.B, "DST", i.ToString("D3"), $"Color {i + 1}"));
        }
        
        return palette;
    }

    private static (byte R, byte G, byte B) HsvToRgb(int h, double s, double v)
    {
        int hi = (h / 60) % 6;
        double f = h / 60.0 - hi;
        double p = v * (1 - s);
        double q = v * (1 - f * s);
        double t = v * (1 - (1 - f) * s);
        double r, g, b;
        switch (hi)
        {
            case 0: r = v; g = t; b = p; break;
            case 1: r = q; g = v; b = p; break;
            case 2: r = p; g = v; b = t; break;
            case 3: r = p; g = q; b = v; break;
            case 4: r = t; g = p; b = v; break;
            default: r = v; g = p; b = q; break;
        }
        
        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    /// <summary>
    /// Decodifica 3 bytes DST a dx, dy, flags
    /// Formato DST: cada coordenada es 12 bits con signo, más flags de control
    /// </summary>
    private static (int dx, int dy, int flags) DecodeStitch(byte b1, byte b2, byte b3)
    {
        // DST encoding:
        // b1: YYYY YYXX (Y high 6 bits, X high 2 bits)
        // b2: XXXX XXYY (X mid 6 bits, Y mid 2 bits) 
        // b3: YYXX XXFF (Y low 2 bits, X low 2 bits, Flags 4 bits)
        
        int x = ((b1 & 0x03) << 10) | ((b2 & 0x3F) << 4) | ((b3 & 0xC0) >> 2);
        int y = ((b1 & 0xFC) << 4) | ((b2 & 0xC0) >> 2) | ((b3 & 0x30) >> 4);
        
        // Sign extend 12-bit values
        if ((x & 0x800) != 0) x |= ~0xFFF;
        if ((y & 0x800) != 0) y |= ~0xFFF;
        
        int flags = b3 & 0x0F;
        
        return (x, -y, flags); // Y is inverted in DST
    }

    /// <summary>
    /// Codifica dx, dy, flags a 3 bytes DST
    /// </summary>
    private static (byte b1, byte b2, byte b3) EncodeStitch(int dx, int dy, int flags)
    {
        // Clamp to 12-bit signed range
        dx = Math.Clamp(dx, -2048, 2047);
        dy = Math.Clamp(dy, -2048, 2047);
        
        // DST uses inverted Y
        int x = dx & 0xFFF;
        int y = (-dy) & 0xFFF;
        
        byte b1 = (byte)(((y >> 4) & 0xFC) | ((x >> 10) & 0x03));
        byte b2 = (byte)(((x >> 4) & 0x3F) | ((y >> 2) & 0xC0));
        byte b3 = (byte)(((y & 0x03) << 4) | ((x & 0x03) << 2) | (flags & 0x0F));
        
        return (b1, b2, b3);
    }

    #endregion
}

/// <summary>
/// Flags específicos de DST
/// </summary>
[Flags]
internal enum DstFlags : ushort
{
    None = 0,
    Jump = 0x01,          // Jump stitch
    ColorChange = 0x02,   // Color change
    Trim = 0x04,          // Trim
    Stop = 0x08,          // Stop
    End = 0x03,           // End of data (Jump + ColorChange)
}