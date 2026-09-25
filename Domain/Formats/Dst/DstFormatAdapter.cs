namespace AtlasEmbroidery.Domain.Formats.Dst;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Domain.Formats;
using FmtValidationIssue = AtlasEmbroidery.Domain.Formats.ValidationIssue;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;
using System.Text;

/// <summary>
/// Tajima DST format constants and specifications
/// </summary>
public static class DstSpec
{
    public const int HeaderSize = 512;
    public const int MaxDeltaPerRecord = 121; // DST units (12.1mm) - balanced ternary max: 1+3+9+27+81=121
    public const int MicronsPerDstUnit = 100; // 1 DST unit = 0.1mm = 100 microns
    
    // Control byte values (bits 7-6 of byte 3) - raw values per Tajima spec
    // Per spec: bit 7 = Jump, bit 6 = Stop/ColorChange
    public const byte StitchNormal = 0x00;      // 00xxxxxx (bits 7,6 = 00)
    public const byte StitchJump = 0x80;        // 10xxxxxx (bit 7 = Jump = 1)
    public const byte StitchColorChange = 0xC0; // 11xxxxxx (bits 7,6 = Stop/ColorChange = 11)
    public const byte StitchEnd = 0xF0;         // 1111xxxx (End marker uses fixed 0xF3 0x00 0x00, not this)
    
    // End marker: 3 bytes (0xF3 0x00 0x00)
    public static readonly byte[] EndMarker = { 0xF3, 0x00, 0x00 };
    
    // Movement encoding: balanced ternary with magnitudes 1,3,9,27,81
    public static readonly int[] MoveMagnitudes = { 1, 3, 9, 27, 81 };
    
    // Header field labels
    public static readonly string[] HeaderLabels = 
    {
        "LA:", "ST:", "CO:", "+X:", "-X:", "+Y:", "-Y:",
        "AX:", "AY:", "MX:", "MY:", "PD:"
    };
}

/// <summary>
/// DST movement encoder using Tajima ternary bit encoding
/// Per KDE Community Wiki / file-extensions.com specification:
/// Each axis uses 10 bits: ±1, ±3, ±9, ±27, ±81 (balanced ternary magnitudes)
/// Bits are interleaved: Y[1], Y[-1], Y[9], Y[-9], X[-9], X[9], X[-1], X[1] in byte 1, etc.
/// </summary>
public static class DstMovementEncoder
{
    /// <summary>
    /// Encodes a delta in DST units (max ±121) into 3 bytes per Tajima spec
    /// Returns (byte1, byte2, byte3) where byte3 contains control bits in bits 7-6
    /// </summary>
    public static (byte b1, byte b2, byte b3) EncodeMovement(int deltaX, int deltaY, byte controlByte)
    {
        if (deltaX < -121 || deltaX > 121)
            throw new ArgumentOutOfRangeException(nameof(deltaX), $"Delta X {deltaX} exceeds max ±121");
        if (deltaY < -121 || deltaY > 121)
            throw new ArgumentOutOfRangeException(nameof(deltaY), $"Delta Y {deltaY} exceeds max ±121");

        // Tajima DST bit encoding (big-endian 24-bit):
        // Byte 1 (bits 23-16): Y+=1(23), Y-=1(22), Y+=9(21), Y-=9(20), X-=9(19), X+=9(18), X-=1(17), X+=1(16)
        // Byte 2 (bits 15-8): Y+=3(15), Y-=3(14), Y+=27(13), Y-=27(12), X-=27(11), X+=27(10), X-=3(9), X+=3(8)
        // Byte 3 (bits 7-0): Jump(7), Stop/ColorChange(6), Y+=81(5), Y-=81(4), X-=81(3), X+=81(2), sync(1), sync(0)
        //
        // Note: Y axis is NEGATED per Tajima spec (y = -y before encoding)
        // Control byte mapping per spec:
        // Normal=0x00 (bits 7,6=00), Jump=0x80 (bit 7=1), ColorChange/Stop=0xC0 (bits 7,6=11), End=0xF0 (bits 7,6,5,4=1111)
        // But End marker is SPECIAL: fixed 3-byte sequence 0xF3 0x00 0x00 (not encoded via normal path)
        // ColorChange/Stop records have dx=0, dy=0 with byte3=0xC3

        // Negate Y per Tajima spec
        deltaY = -deltaY;

        int bits24 = 0;
        
        // Encode X axis
        EncodeAxisBits(deltaX, true, ref bits24);
        // Encode Y axis
        EncodeAxisBits(deltaY, false, ref bits24);
        
        // Set control bits (bits 7-6 of byte 3, which are bits 7-6 of the 24-bit value)
        // Map control values to spec bits:
        // StitchNormal (0x00) -> 00 in bits 7-6
        // StitchJump (0x80) -> 10 in bits 7-6 -> bit 7 = Jump (per spec)
        // StitchColorChange (0xC0) -> 11 in bits 7-6 -> bits 7,6 = Stop/ColorChange (per spec)
        // StitchEnd not used here - END is written as raw 0xF3 0x00 0x00

        // Control byte constants already have bits 7-6 set correctly per spec, so OR directly
        bits24 |= (controlByte & 0xC0);
        
        // Set sync bits (bits 0-1 of byte 3)
        bits24 |= 0x03;
        
        // Extract bytes (big-endian)
        byte b1 = (byte)((bits24 >> 16) & 0xFF);
        byte b2 = (byte)((bits24 >> 8) & 0xFF);
        byte b3 = (byte)(bits24 & 0xFF);
        
        return (b1, b2, b3);
    }
    
    private static void EncodeAxisBits(int delta, bool isX, ref int bits24)
    {
        // Balanced ternary encoding using standard algorithm
        // Each magnitude can be -1, 0, +1
        // Algorithm: repeatedly divide by 3, adjusting remainders 2->-1, -2->+1
        
        int n = delta;
        int[] magnitudes = { 1, 3, 9, 27, 81 };  // 3^0, 3^1, 3^2, 3^3, 3^4
        
        for (int i = 0; i < magnitudes.Length; i++)
        {
            int mag = magnitudes[i];
            int r = n % 3;
            n = n / 3;
            
            // Adjust for balanced ternary: digits must be -1, 0, +1
            if (r == 2)
            {
                r = -1;
                n += 1;
            }
            else if (r == -2)
            {
                r = 1;
                n -= 1;
            }
            
            // Set the bit for this magnitude
            if (r == 1)
            {
                SetAxisBit(mag, true, isX, ref bits24);
            }
            else if (r == -1)
            {
                SetAxisBit(mag, false, isX, ref bits24);
            }
            // r == 0: no bit set
        }
        
        // After processing all 5 trits, n should be 0
        if (n != 0)
            throw new InvalidOperationException($"Failed to encode delta {delta}: overflow (remaining {n})");
    }
    
    private static void SetAxisBit(int magnitude, bool positive, bool isX, ref int bits24)
    {
        int bitPosition = (magnitude, positive, isX) switch
        {
            // X axis bits
            (81, true, true) => 2,   // X += 81
            (81, false, true) => 3,  // X -= 81
            (27, true, true) => 10,  // X += 27
            (27, false, true) => 11, // X -= 27
            (9, true, true) => 18,   // X += 9
            (9, false, true) => 19,  // X -= 9
            (3, true, true) => 8,    // X += 3
            (3, false, true) => 9,   // X -= 3
            (1, true, true) => 16,   // X += 1
            (1, false, true) => 17,  // X -= 1
            
            // Y axis bits (per pyembroidery/Tajima spec)
            // Encoder NEGATES Y first, then balanced ternary
            // positive=true  -> Y += magnitude in encoding
            // positive=false -> Y -= magnitude in encoding
            (81, true, false) => 5,  // Y += 81 (byte 3 bit 5)
            (81, false, false) => 4, // Y -= 81 (byte 3 bit 4)
            (27, true, false) => 13, // Y += 27 (byte 2 bit 5)
            (27, false, false) => 12,// Y -= 27 (byte 2 bit 4)
            (9, true, false) => 21,  // Y += 9 (byte 1 bit 5)
            (9, false, false) => 20, // Y -= 9 (byte 1 bit 4)
            (3, true, false) => 15,  // Y += 3 (byte 2 bit 7)
            (3, false, false) => 14, // Y -= 3 (byte 2 bit 6)
            (1, true, false) => 23,  // Y += 1 (byte 1 bit 7)
            (1, false, false) => 22, // Y -= 1 (byte 1 bit 6)
            
            _ => throw new ArgumentException($"Invalid bit specification: mag={magnitude}, pos={positive}, isX={isX}")
        };
        
        bits24 |= (1 << bitPosition);
    }
    
    /// <summary>
    /// Decodes 3 bytes into deltaX, deltaY, controlByte per Tajima spec
    /// </summary>
    public static (int deltaX, int deltaY, byte control) DecodeMovement(byte b1, byte b2, byte b3)
    {
        // Combine bytes (big-endian)
        int bits24 = (b1 << 16) | (b2 << 8) | b3;
        
        // Check sync bits (bits 0-1 must be set)
        if ((bits24 & 0x03) != 0x03)
        {
            // Check if this is END marker (0xF3 0x00 0x00)
            if (b1 == 0xF3 && b2 == 0x00 && b3 == 0x00)
            {
                return (0, 0, (byte)DstControl.End);
            }
            throw new InvalidDataException($"Invalid DST record: sync bits not set (b3=0x{b3:X2})");
        }
        
        // Decode X axis
        int deltaX = DecodeAxisBits(bits24, true);
        // Decode Y axis
        int deltaY = DecodeAxisBits(bits24, false);
        
        // Extract control bits (bits 7-6 of byte 3)
        byte control = (byte)((b3 >> 6) & 0x03);
        
        return (deltaX, deltaY, control);
    }
    
    private static int DecodeAxisBits(int bits24, bool isX)
    {
        int result = 0;
        
        if (isX)
        {
            // X axis bits
            if ((bits24 & (1 << 2)) != 0) result += 81;   // X += 81
            if ((bits24 & (1 << 3)) != 0) result -= 81;   // X -= 81
            if ((bits24 & (1 << 10)) != 0) result += 27;  // X += 27
            if ((bits24 & (1 << 11)) != 0) result -= 27;  // X -= 27
            if ((bits24 & (1 << 18)) != 0) result += 9;   // X += 9
            if ((bits24 & (1 << 19)) != 0) result -= 9;   // X -= 9
            if ((bits24 & (1 << 8)) != 0) result += 3;    // X += 3
            if ((bits24 & (1 << 9)) != 0) result -= 3;    // X -= 3
            if ((bits24 & (1 << 16)) != 0) result += 1;   // X += 1
            if ((bits24 & (1 << 17)) != 0) result -= 1;   // X -= 1
        }
        else
        {
            // Y axis bits (per pyembroidery/Tajima spec)
            // Encoder: Y is NEGATED first, then encoded
            // Decoder: decode Y from bits, then NEGATE
            int yEncoded = 0;
            if ((bits24 & (1 << 5)) != 0) yEncoded += 81;   // bit 5 -> Y += 81 in encoding
            if ((bits24 & (1 << 4)) != 0) yEncoded -= 81;   // bit 4 -> Y -= 81 in encoding
            if ((bits24 & (1 << 13)) != 0) yEncoded += 27;  // bit 13 -> Y += 27 in encoding
            if ((bits24 & (1 << 12)) != 0) yEncoded -= 27;  // bit 12 -> Y -= 27 in encoding
            if ((bits24 & (1 << 21)) != 0) yEncoded += 9;   // bit 21 -> Y += 9 in encoding
            if ((bits24 & (1 << 20)) != 0) yEncoded -= 9;   // bit 20 -> Y -= 9 in encoding
            if ((bits24 & (1 << 15)) != 0) yEncoded += 3;   // bit 15 -> Y += 3 in encoding
            if ((bits24 & (1 << 14)) != 0) yEncoded -= 3;   // bit 14 -> Y -= 3 in encoding
            if ((bits24 & (1 << 23)) != 0) yEncoded += 1;   // bit 23 -> Y += 1 in encoding
            if ((bits24 & (1 << 22)) != 0) yEncoded -= 1;   // bit 22 -> Y -= 1 in encoding
            
            // Negate per Tajima spec (encoder negated Y before encoding)
            result = -yEncoded;
        }
        
        return result;
    }
}

/// <summary>
/// DST Header parser and builder
/// </summary>
public sealed class DstHeader
{
    public string Label { get; set; } = "";
    public int StitchCount { get; set; }
    public int ColorChanges { get; set; }
    public int MinX { get; set; }
    public int MaxX { get; set; }
    public int MinY { get; set; }
    public int MaxY { get; set; }
    public int StartX { get; set; }
    public int StartY { get; set; }
    public int EndX { get; set; }
    public int EndY { get; set; }
    
    public static DstHeader Parse(byte[] headerBytes)
    {
        var header = new DstHeader();
        string text = Encoding.ASCII.GetString(headerBytes);
        
        // Parse line by line
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("LA:"))
                header.Label = trimmed.Substring(3).Trim();
            else if (trimmed.StartsWith("ST:"))
            {
                if (int.TryParse(trimmed.Substring(3).Trim(), out var stitchCount))
                    header.StitchCount = stitchCount;
            }
            else if (trimmed.StartsWith("CO:"))
            {
                if (int.TryParse(trimmed.Substring(3).Trim(), out var colorChanges))
                    header.ColorChanges = colorChanges;
            }
            else if (trimmed.StartsWith("+X:"))
            {
                if (int.TryParse(trimmed.Substring(3).Trim(), out var maxX))
                    header.MaxX = maxX;
            }
            else if (trimmed.StartsWith("-X:"))
            {
                if (int.TryParse(trimmed.Substring(3).Trim(), out var minX))
                    header.MinX = minX;
            }
            else if (trimmed.StartsWith("+Y:"))
            {
                if (int.TryParse(trimmed.Substring(3).Trim(), out var maxY))
                    header.MaxY = maxY;
            }
            else if (trimmed.StartsWith("-Y:"))
            {
                if (int.TryParse(trimmed.Substring(3).Trim(), out var minY))
                    header.MinY = minY;
            }
            else if (trimmed.StartsWith("AX:"))
            {
                if (int.TryParse(trimmed.Substring(3).Trim(), out var startX))
                    header.StartX = startX;
            }
            else if (trimmed.StartsWith("AY:"))
            {
                if (int.TryParse(trimmed.Substring(3).Trim(), out var startY))
                    header.StartY = startY;
            }
            else if (trimmed.StartsWith("MX:"))
            {
                if (int.TryParse(trimmed.Substring(3).Trim(), out var endX))
                    header.EndX = endX;
            }
            else if (trimmed.StartsWith("MY:"))
            {
                if (int.TryParse(trimmed.Substring(3).Trim(), out var endY))
                    header.EndY = endY;
            }
        }
        
        return header;
    }
    
    public byte[] ToBytes()
    {
        var lines = new List<string>
        {
            $"LA:{Label}",
            $"ST:{StitchCount}",
            $"CO:{ColorChanges}",
            $"+X:{MaxX}",
            $"-X:{MinX}",
            $"+Y:{MaxY}",
            $"-Y:{MinY}",
            $"AX:{StartX}",
            $"AY:{StartY}",
            $"MX:{EndX}",
            $"MY:{EndY}",
            "PD:******"
        };
        
        var text = string.Join("\r\n", lines) + "\r\n";
        var bytes = Encoding.ASCII.GetBytes(text);
        
        var result = new byte[DstSpec.HeaderSize];
        Array.Copy(bytes, result, Math.Min(bytes.Length, DstSpec.HeaderSize));
        return result;
    }
}

/// <summary>
/// Represents a parsed DST stitch record
/// </summary>
public sealed class DstStitchRecord
{
    public int DeltaX { get; set; } // DST units
    public int DeltaY { get; set; } // DST units
    public DstControl Control { get; set; }
    public int AbsoluteX { get; set; } // DST units
    public int AbsoluteY { get; set; } // DST units
}

[Flags]
public enum DstControl : byte
{
    None = 0,
    Normal = 0x00,      // Bits 7-6 = 00
    Jump = 0x80,        // Bits 7-6 = 10 (bit 7 = Jump)
    ColorChange = 0xC0, // Bits 7-6 = 11 (bits 7,6 = Stop/ColorChange)
    End = 0xF0          // End marker (fixed 0xF3 0x00 0x00)
}

/// <summary>
/// Tajima DST format adapter - Correct implementation per Tajima specification
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
        SupportsTrim = true, // DST uses jumps for trim (no native trim command)
        SupportsJump = true,
        SupportsColorChange = true,
        SupportsStop = true, // Color change acts as stop
        SupportsSequins = false,
        SupportsPuff3D = false,
        MaxStitchLength = DstSpec.MaxDeltaPerRecord * DstSpec.MicronsPerDstUnit, // 12100 microns = 12.1mm
        MaxJumpLength = DstSpec.MaxDeltaPerRecord * DstSpec.MicronsPerDstUnit,
        MaxStitchesPerColor = 65535,
        MaxTotalStitches = 2000000,
        MaxColors = 250,
    };

    private const long MaxDocumentBytes = 50 * 1024 * 1024; // 50 MB

    /// <summary>
    /// Reads a DST file and converts to AtlasProject
    /// </summary>
    public async Task<AtlasProject> ReadAsync(Stream stream, FormatReadOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatReadOptions();
        
        if (stream.Length > MaxDocumentBytes)
            throw new InvalidDataException($"DST file exceeds maximum size of {MaxDocumentBytes} bytes");
        
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        
        // Read header
        var headerBytes = reader.ReadBytes(DstSpec.HeaderSize);
        if (headerBytes.Length < DstSpec.HeaderSize)
            throw new InvalidDataException("DST file too small for header (512 bytes required)");
        
        var header = DstHeader.Parse(headerBytes);
        
        if (!headerBytes.Take(3).SequenceEqual(Encoding.ASCII.GetBytes("LA:")))
            throw new InvalidDataException("Invalid DST header: missing LA: prefix");
        
        if (options.ValidateOnly)
        {
            var validation = await ValidateAsync(stream);
            if (!validation.IsValid)
                throw new InvalidDataException($"DST validation failed: {string.Join("; ", validation.Issues.Select(i => i.Message))}");
            return new AtlasProject { Name = "Validation Only" };
        }
        
        // Read stitch records
        var records = new List<DstStitchRecord>();
        int currentX = 0, currentY = 0;
        int colorIndex = 0;
        int recordCount = 0;
        int colorChangeCount = 0;
        
        while (stream.Position + 2 < stream.Length)
        {
            ct.ThrowIfCancellationRequested();
            
            if (recordCount >= options.MaxStitches)
                throw new InvalidDataException($"Stitch count exceeds maximum allowed: {options.MaxStitches}");
            if (colorChangeCount >= options.MaxColors)
                throw new InvalidDataException($"Color change count exceeds maximum allowed: {options.MaxColors}");
            
            // Check for end marker BEFORE decoding (0xF3 0x00 0x00)
            if (stream.Position + 2 < stream.Length)
            {
                long pos = stream.Position;
                byte peek1 = reader.ReadByte();
                byte peek2 = reader.ReadByte();
                byte peek3 = reader.ReadByte();
                
                if (peek1 == 0xF3 && peek2 == 0x00 && peek3 == 0x00)
                {
                    // End marker found - don't consume it, leave position for caller
                    stream.Position = pos;
                    break;
                }
                
                // Not end marker, rewind and decode normally
                stream.Position = pos;
            }
            
            byte b1 = reader.ReadByte();
            byte b2 = reader.ReadByte();
            byte b3 = reader.ReadByte();
            
            var (deltaX, deltaY, control) = DstMovementEncoder.DecodeMovement(b1, b2, b3);
            var dstControl = (DstControl)control;
            
            currentX += deltaX;
            currentY += deltaY;
            
            var record = new DstStitchRecord
            {
                DeltaX = deltaX,
                DeltaY = deltaY,
                Control = dstControl,
                AbsoluteX = currentX,
                AbsoluteY = currentY
            };
            records.Add(record);
            recordCount++;
            
            if (dstControl == DstControl.ColorChange)
            {
                colorIndex++;
                colorChangeCount++;
            }
        }
        
        // Convert to AtlasProject
        var project = new AtlasProject
        {
            Name = header.Label,
            SourceFileHash = ComputeHash(headerBytes)
        };
        
        // Convert DST units to microns
        var stitchPoints = new List<StitchPoint>();
        int needleIndex = 1;
        colorIndex = 0;
        
        foreach (var record in records)
        {
            int xMicrons = record.AbsoluteX * DstSpec.MicronsPerDstUnit;
            int yMicrons = record.AbsoluteY * DstSpec.MicronsPerDstUnit;
            
            var flags = (ushort)0;
            if (record.Control == DstControl.Jump) flags |= 0x01;
            if (record.Control == DstControl.ColorChange) flags |= 0x02;
            
            var stitch = new StitchPoint(
                xMicrons, yMicrons, 
                StitchType.Running, 
                (byte)needleIndex, 
                (byte)colorIndex, 
                flags);
            stitchPoints.Add(stitch);
            
            if (record.Control == DstControl.ColorChange)
            {
                colorIndex++;
                needleIndex = (colorIndex % 15) + 1;
            }
        }
        
        // Create shape object from stitch points
        var shapeObj = new ShapeObject
        {
            Name = "Imported DST",
            Vertices = stitchPoints.Select(s => s.Position).ToList(),
            IsClosed = false,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        
        project.Objects.Add(shapeObj);
        
        // Build thread palette (DST doesn't store RGB - generate defaults)
        project.ThreadPalette = GenerateDefaultPalette(colorIndex + 1);
        for (int i = 0; i <= colorIndex; i++)
        {
            project.ColorToNeedleMap[i] = (i % 15) + 1;
        }
        
        project.RecalculateBounds();
        project.Touch();
        
        return project;
    }

    /// <summary>
    /// Writes AtlasProject to DST format
    /// </summary>
    public async Task WriteAsync(AtlasProject project, Stream stream, FormatWriteOptions? options = null, CancellationToken ct = default)
    {
        options ??= new FormatWriteOptions();
        
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        
        // Compile to stitch plan
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        var allStitches = plan.GetAllStitches().ToList();
        
        if (!allStitches.Any())
        {
            // Empty design - write minimal header
            var emptyHeader = new DstHeader
            {
                Label = project.Name ?? "Empty",
                StitchCount = 0,
                ColorChanges = 0
            }.ToBytes();
            writer.Write(emptyHeader);
            // End marker (Tajima spec: 0xF3 0x00 0x00)
            writer.Write(0xF3);
            writer.Write((byte)0x00);
            writer.Write((byte)0x00);
            return;
        }
        
        // Build stitch records with proper DST encoding
        var records = new List<DstStitchRecord>();
        int lastX = 0, lastY = 0;
        int currentColor = -1;
        int colorChangeCount = 0;
        
        foreach (var stitch in allStitches)
        {
            ct.ThrowIfCancellationRequested();
            
            // Convert microns to DST units
            int targetX = stitch.X / DstSpec.MicronsPerDstUnit;
            int targetY = stitch.Y / DstSpec.MicronsPerDstUnit;
            
            int deltaX = targetX - lastX;
            int deltaY = targetY - lastY;
            
            // Decompose large movements into multiple records
            var decomposedRecords = DecomposeMovement(deltaX, deltaY, stitch, currentColor != stitch.ColorIndex);
            
            foreach (var rec in decomposedRecords)
            {
                records.Add(rec);
                lastX += rec.DeltaX;
                lastY += rec.DeltaY;
                
                if (rec.Control == DstControl.ColorChange)
                {
                    colorChangeCount++;
                    currentColor = stitch.ColorIndex;
                }
            }
        }
        
        // Build header
        var header = BuildHeader(project, records, colorChangeCount);
        writer.Write(header.ToBytes());
        
        // Write stitch records
        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            
            byte controlByte = (byte)record.Control;
            var (b1, b2, b3) = DstMovementEncoder.EncodeMovement(record.DeltaX, record.DeltaY, controlByte);
            writer.Write(b1);
            writer.Write(b2);
            writer.Write(b3);
        }
        
        // End marker (Tajima spec: 0xF3 0x00 0x00)
        writer.Write(0xF3);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
    }

    /// <summary>
    /// Decomposes a large movement into multiple DST records (max ±121 per axis)
    /// </summary>
    private static List<DstStitchRecord> DecomposeMovement(int deltaX, int deltaY, StitchPoint stitch, bool isColorChange)
    {
        var records = new List<DstStitchRecord>();
        int remainingX = deltaX;
        int remainingY = deltaY;
        bool firstRecord = true;
        
        while (Math.Abs(remainingX) > DstSpec.MaxDeltaPerRecord || Math.Abs(remainingY) > DstSpec.MaxDeltaPerRecord)
        {
            int stepX = Math.Clamp(remainingX, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
            int stepY = Math.Clamp(remainingY, -DstSpec.MaxDeltaPerRecord, DstSpec.MaxDeltaPerRecord);
            
            remainingX -= stepX;
            remainingY -= stepY;
            
            var control = firstRecord && isColorChange ? DstControl.ColorChange : 
                         (stitch.IsJump ? DstControl.Jump : DstControl.Normal);
            
            records.Add(new DstStitchRecord
            {
                DeltaX = stepX,
                DeltaY = stepY,
                Control = control
            });
            
            firstRecord = false;
        }
        
        // Final record
        if (remainingX != 0 || remainingY != 0 || firstRecord)
        {
            var control = firstRecord && isColorChange ? DstControl.ColorChange :
                         (stitch.IsJump ? DstControl.Jump : DstControl.Normal);
            
            records.Add(new DstStitchRecord
            {
                DeltaX = remainingX,
                DeltaY = remainingY,
                Control = control
            });
        }
        
        return records;
    }

    /// <summary>
    /// Normalizes a DST project
    /// </summary>
    public AtlasProject Normalize(AtlasProject project, NormalizationProfile? profile = null)
    {
        profile ??= new NormalizationProfile();
        var normalized = project.DeepClone();
        
        foreach (var obj in normalized.Objects)
        {
            var param = obj.StitchParams;
            if (profile.ClampToMachineLimits)
            {
                int maxStitchDst = Capabilities.MaxStitchLength;
                int maxJumpDst = Capabilities.MaxJumpLength;
                param.MaxStitchLength = Math.Min(param.MaxStitchLength, maxStitchDst);
                param.MaxJumpDistance = Math.Min(param.MaxJumpDistance, maxJumpDst);
            }
        }
        
        if (profile.EnsureValidColorPalette && normalized.ThreadPalette.Count > profile.MaxColors)
        {
            normalized.ThreadPalette = normalized.ThreadPalette.Take(profile.MaxColors).ToList();
        }
        
        if (profile.RemoveDuplicateStitches)
        {
            // This requires StitchPlan - delegate to StitchEngine level
            // For now, mark as not implemented at this level
        }
        
        if (profile.FixInvalidCoordinates)
        {
            // Coordinates are already validated during read/write
        }
        
        normalized.RecalculateBounds();
        normalized.Touch();
        return normalized;
    }

    /// <summary>
    /// Validates a DST file stream (synchronous)
    /// </summary>
    public FormatValidationResult Validate(Stream stream)
    {
        return ValidateAsync(stream).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Validates a DST file stream (async) - scans complete body
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
            
            if (stream.Length > MaxDocumentBytes)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.FILE_TOO_LARGE",
                    Severity = FmtValidationSeverity.Critical,
                    Message = $"DST file exceeds maximum size of {MaxDocumentBytes} bytes",
                    Evidence = $"File size: {stream.Length} bytes",
                    Recommendation = "Reduce file size or split design"
                });
                stream.Position = originalPosition;
                return result;
            }
            
            if (stream.Length < DstSpec.HeaderSize)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_TOO_SMALL",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "DST file too small for header (512 bytes required)",
                    Evidence = $"File size: {stream.Length} bytes",
                    Recommendation = "Ensure file is a valid DST format"
                });
                stream.Position = originalPosition;
                return result;
            }
            
            using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
            var headerBytes = reader.ReadBytes(DstSpec.HeaderSize);
            var header = DstHeader.Parse(headerBytes);
            
            // Validate header
            if (!headerBytes.Take(3).SequenceEqual(Encoding.ASCII.GetBytes("LA:")))
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_MISSING_LA",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "DST header missing required LA: prefix",
                    Evidence = "First 3 bytes are not 'LA:'",
                    Recommendation = "File is not a valid Tajima DST format"
                });
            }

            if (string.IsNullOrWhiteSpace(header.Label))
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_EMPTY_LABEL",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "DST header has empty design label",
                    Evidence = "LA: field is empty",
                    Recommendation = "Design label should be populated"
                });
            }

            // Check for negative stitch count
            if (header.StitchCount < 0)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.INVALID_STITCH_COUNT",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "DST header contains negative stitch count",
                    Evidence = $"ST:{header.StitchCount}",
                                        Recommendation = "Stitch count must be non-negative"
                                    });
                                }

                                // Check for negative color changes
                                if (header.ColorChanges < 0)
                                {
                                    result.IsValid = false;
                                    result.Issues.Add(new FmtValidationIssue
                                    {
                                        RuleId = "DST.INVALID_COLOR_CHANGE_COUNT",
                                        Severity = FmtValidationSeverity.Critical,
                                        Message = "DST header contains negative color change count",
                                        Evidence = $"CO:{header.ColorChanges}",
                                        Recommendation = "Color change count must be non-negative"
                                    });
                                }

                                // Check for excessive stitch count in header (beyond format capabilities)
                                if (header.StitchCount > Capabilities.MaxTotalStitches)
                                {
                                    result.IsValid = false;
                                    result.Issues.Add(new FmtValidationIssue
                                    {
                                        RuleId = "DST.HEADER_STITCH_COUNT_EXCEEDS_MAX",
                                        Severity = FmtValidationSeverity.Critical,
                                        Message = "DST header stitch count exceeds format maximum",
                                        Evidence = $"ST:{header.StitchCount} > Max:{Capabilities.MaxTotalStitches}",
                                        Recommendation = "Design exceeds format capabilities; split into multiple files"
                                    });
                                }

                                // Check for excessive color changes in header
                                                            if (header.ColorChanges > Capabilities.MaxColors)
                                                            {
                                                                // Warning, not critical - design may still be readable
                                                                result.Issues.Add(new FmtValidationIssue
                                                                {
                                                                    RuleId = "DST.HEADER_COLOR_CHANGES_EXCEEDS_MAX",
                                                                    Severity = FmtValidationSeverity.Warning,
                                                                    Message = "DST header color change count exceeds format maximum",
                                                                    Evidence = $"CO:{header.ColorChanges} > Max:{Capabilities.MaxColors}",
                                                                    Recommendation = "Design exceeds format capabilities; reduce color changes"
                                                                });
                                                            }

                                                            // Check for zero dimensions in header (may indicate empty or corrupted design)
                                                                                                                        if (header.MaxX == 0 && header.MinX == 0 && header.MaxY == 0 && header.MinY == 0)
                                                                                                                        {
                                                                                                                            result.Issues.Add(new FmtValidationIssue
                                                                                                                            {
                                                                                                                                RuleId = "DST.HEADER_ZERO_DIMENSIONS",
                                                                                                                                Severity = FmtValidationSeverity.Warning,
                                                                                                                                Message = "DST header has zero dimensions (no extent)",
                                                                                                                                Evidence = "Header: X[0,0] Y[0,0]",
                                                                                                                                Recommendation = "Design may be empty or header extents not calculated"
                                                                                                                            });
                                                                                                                        }
                                                            
                                                                                                                        // Check for negative dimensions in header
                                                                                                                        if (header.MinX < 0 || header.MinY < 0 || header.MaxX < 0 || header.MaxY < 0)
                                                                                                                        {
                                                                                                                            result.Issues.Add(new FmtValidationIssue
                                                                                                                            {
                                                                                                                                RuleId = "DST.HEADER_NEGATIVE_DIMENSIONS",
                                                                                                                                Severity = FmtValidationSeverity.Warning,
                                                                                                                                Message = "DST header contains negative dimensions",
                                                                                                                                Evidence = $"Header: X[{header.MinX},{header.MaxX}] Y[{header.MinY},{header.MaxY}]",
                                                                                                                                Recommendation = "Negative extents are invalid; header may be corrupted"
                                                                                                                            });
                                                                                                                        }
                                                            
                                                                                                                        // Validate body - scan ALL records
                                int recordCount = 0;
                                int observedColorChanges = 0;
                                int maxCoordX = 0, minCoordX = 0, maxCoordY = 0, minCoordY = 0;
                                int currentX = 0, currentY = 0;
                                bool foundEnd = false;
            
            while (stream.Position + 2 < stream.Length)
            {
                // Check for end marker BEFORE decoding (0xF3 0x00 0x00)
                if (stream.Position + 2 < stream.Length)
                {
                    long pos = stream.Position;
                    byte peek1 = reader.ReadByte();
                    byte peek2 = reader.ReadByte();
                    byte peek3 = reader.ReadByte();
                    
                    if (peek1 == 0xF3 && peek2 == 0x00 && peek3 == 0x00)
                    {
                        foundEnd = true;
                        // Don't advance past end marker - leave it for caller
                        stream.Position = pos;
                        break;
                    }
                    
                    // Not end marker, rewind and decode normally
                    stream.Position = pos;
                }
                
                byte b1 = reader.ReadByte();
                byte b2 = reader.ReadByte();
                byte b3 = reader.ReadByte();
                
                int deltaX, deltaY;
                byte control;
                try
                {
                    var decoded = DstMovementEncoder.DecodeMovement(b1, b2, b3);
                    deltaX = decoded.deltaX;
                    deltaY = decoded.deltaY;
                    control = decoded.control;
                }
                catch (InvalidDataException ex)
                {
                    result.IsValid = false;
                    result.Issues.Add(new FmtValidationIssue
                    {
                        RuleId = "DST.INVALID_BALANCED_TERNARY",
                        Severity = FmtValidationSeverity.Critical,
                        Message = "Invalid balanced ternary encoding in stitch record",
                        Evidence = ex.Message,
                        Position = stream.Position - 3,
                        Recommendation = "Stitch data contains invalid encoding"
                    });
                    continue; // Skip this record, continue validation
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    result.IsValid = false;
                    result.Issues.Add(new FmtValidationIssue
                    {
                        RuleId = "DST.MOVEMENT_EXCEEDS_MAX",
                        Severity = FmtValidationSeverity.Critical,
                        Message = "Movement exceeds DST maximum per record",
                        Evidence = ex.Message,
                        Position = stream.Position - 3,
                        Recommendation = $"Long movements must be decomposed into multiple records (max ±{DstSpec.MaxDeltaPerRecord} DST units)"
                    });
                    continue; // Skip this record, continue validation
                }
                
                var dstControl = (DstControl)control;
                
                currentX += deltaX;
                currentY += deltaY;
                
                maxCoordX = Math.Max(maxCoordX, currentX);
                minCoordX = Math.Min(minCoordX, currentX);
                maxCoordY = Math.Max(maxCoordY, currentY);
                minCoordY = Math.Min(minCoordY, currentY);
                
                if (dstControl == DstControl.ColorChange)
                    observedColorChanges++;
                
                recordCount++;
                
                // Check movement bounds
                if (Math.Abs(deltaX) > DstSpec.MaxDeltaPerRecord || Math.Abs(deltaY) > DstSpec.MaxDeltaPerRecord)
                {
                    result.Issues.Add(new FmtValidationIssue
                    {
                        RuleId = "DST.MOVEMENT_EXCEEDS_MAX",
                        Severity = FmtValidationSeverity.Critical,
                        Message = $"Movement exceeds DST maximum per record (±{DstSpec.MaxDeltaPerRecord} units)",
                        Evidence = $"Record {recordCount}: deltaX={deltaX}, deltaY={deltaY}",
                        Position = stream.Position - 3,
                        Recommendation = "Long movements must be decomposed into multiple records"
                    });
                    result.IsValid = false;
                }
            }
            
            if (!foundEnd)
            {
                // Cannot access options in ValidateAsync - check stream capabilities instead
                bool strictMode = true; // Default to strict for validation
                if (strictMode)
                {
                    result.IsValid = false;
                    result.Issues.Add(new FmtValidationIssue
                    {
                        RuleId = "DST.MISSING_END_MARKER",
                        Severity = FmtValidationSeverity.Critical,
                        Message = "DST file missing END marker (0xF3 0x00 0x00)",
                        Evidence = "Reached end of stream without finding END record",
                        Recommendation = "File is truncated or corrupted"
                    });
                }
                else
                {
                    result.Issues.Add(new FmtValidationIssue
                    {
                        RuleId = "DST.MISSING_END_MARKER",
                        Severity = FmtValidationSeverity.Warning,
                        Message = "DST file missing END marker (0xF3 0x00 0x00)",
                        Evidence = "Reached end of stream without finding END record",
                        Recommendation = "File may be truncated"
                    });
                }
            }
            
            // Header/body consistency checks
            if (header.StitchCount > 0 && Math.Abs(header.StitchCount - recordCount) > 1) // Allow ±1 for counting convention
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_STITCH_COUNT_MISMATCH",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "Header ST count differs from actual record count",
                    Evidence = $"Header ST={header.StitchCount}, Actual={recordCount}",
                    Recommendation = "Header stitch count may use different counting convention"
                });
            }
            
            if (header.ColorChanges > 0 && header.ColorChanges != observedColorChanges)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_COLOR_CHANGE_MISMATCH",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "Header CO count differs from observed color changes",
                    Evidence = $"Header CO={header.ColorChanges}, Observed={observedColorChanges}",
                    Recommendation = "Verify color change sequence"
                });
            }
            
            // Check extents
            if (header.MaxX != maxCoordX || header.MinX != minCoordX || header.MaxY != maxCoordY || header.MinY != minCoordY)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_EXTENTS_MISMATCH",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "Header extents differ from observed coordinates",
                    Evidence = $"Header: X[{header.MinX},{header.MaxX}] Y[{header.MinY},{header.MaxY}] vs Observed: X[{minCoordX},{maxCoordX}] Y[{minCoordY},{maxCoordY}]",
                    Recommendation = "Header extents may be approximate"
                });
            }
            
            // Check for excessive dimensions in header (beyond DST practical limits)
            const int MaxReasonableDimension = 10000; // DST units = 1000mm
            if (Math.Abs(header.MaxX) > MaxReasonableDimension || Math.Abs(header.MinX) > MaxReasonableDimension ||
                Math.Abs(header.MaxY) > MaxReasonableDimension || Math.Abs(header.MinY) > MaxReasonableDimension)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.HEADER_EXCESSIVE_DIMENSIONS",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "Header extents exceed practical DST limits",
                    Evidence = $"Header: X[{header.MinX},{header.MaxX}] Y[{header.MinY},{header.MaxY}]",
                    Recommendation = "Verify design extents are correct; excessive values may indicate corruption"
                });
            }
            
            // Check trailing data after END
            if (foundEnd && stream.Position < stream.Length)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.TRAILING_DATA",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "Data found after END marker",
                    Evidence = $"{stream.Length - stream.Position} trailing bytes",
                    Recommendation = "Trailing data will be ignored"
                });
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

        // Color change count (not palette colors)
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        int colorChanges = (int)plan.TotalColorChanges;
        
        if (colorChanges > Capabilities.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "DST.TOO_MANY_COLOR_CHANGES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Project has {colorChanges} color changes, DST supports max {Capabilities.MaxColors}",
                Evidence = $"Color changes: {colorChanges}",
                Recommendation = "Reduce color changes or split design"
            });
        }

        // Bounds check
        var bounds = project.GetDesignBounds();
        if (machine != null)
        {
            int maxXDst = machine.MaxWidth / DstSpec.MicronsPerDstUnit;
            int maxYDst = machine.MaxHeight / DstSpec.MicronsPerDstUnit;
            int designWidthDst = bounds.Width / DstSpec.MicronsPerDstUnit;
            int designHeightDst = bounds.Height / DstSpec.MicronsPerDstUnit;
            
            if (designWidthDst > maxXDst || designHeightDst > maxYDst)
            {
                result.IsValid = false;
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.EXCEEDS_MACHINE_FIELD",
                    Severity = FmtValidationSeverity.Critical,
                    Message = "Design exceeds machine maximum field size",
                    Evidence = $"Design: {designWidthDst}x{designHeightDst} DST units, Machine: {maxXDst}x{maxYDst} DST units",
                    Recommendation = "Resize design or use larger machine"
                });
            }
        }

        if (hoop != null)
        {
            int hoopWidthDst = hoop.UsableWidth / DstSpec.MicronsPerDstUnit;
            int hoopHeightDst = hoop.UsableHeight / DstSpec.MicronsPerDstUnit;
            int designWidthDst = bounds.Width / DstSpec.MicronsPerDstUnit;
            int designHeightDst = bounds.Height / DstSpec.MicronsPerDstUnit;
            
            if (designWidthDst > hoopWidthDst || designHeightDst > hoopHeightDst)
            {
                result.Issues.Add(new FmtValidationIssue
                {
                    RuleId = "DST.EXCEEDS_HOOP",
                    Severity = FmtValidationSeverity.Warning,
                    Message = "Design exceeds hoop usable area",
                    Evidence = $"Design: {designWidthDst}x{designHeightDst} DST units, Hoop: {hoopWidthDst}x{hoopHeightDst} DST units",
                    Recommendation = "Use larger hoop or reposition design"
                });
            }
        }

        // Check for movements that exceed single-record limit
        var longMovements = plan.GetAllStitches()
            .Where(s => s.IsSewing)
            .Select((s, i) => new { Stitch = s, Index = i })
            .Skip(1)
            .Where(x => 
            {
                var prev = plan.GetAllStitches().ElementAt(x.Index - 1);
                int dx = Math.Abs((x.Stitch.X - prev.X) / DstSpec.MicronsPerDstUnit);
                int dy = Math.Abs((x.Stitch.Y - prev.Y) / DstSpec.MicronsPerDstUnit);
                return dx > DstSpec.MaxDeltaPerRecord || dy > DstSpec.MaxDeltaPerRecord;
            })
            .ToList();
        
        if (longMovements.Any())
        {
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "DST.LONG_MOVEMENTS_WILL_BE_DECOMPOSED",
                Severity = FmtValidationSeverity.Info,
                Message = $"{longMovements.Count} movements exceed single-record limit and will be decomposed",
                Evidence = "Movements > 121 DST units (12.1mm) will be split into multiple records",
                Recommendation = "This is handled automatically during write; no action required"
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

        if (plan.TotalColorChanges > Capabilities.MaxColors)
        {
            result.IsValid = false;
            result.Issues.Add(new FmtValidationIssue
            {
                RuleId = "DST.TOO_MANY_COLOR_CHANGES",
                Severity = FmtValidationSeverity.Critical,
                Message = $"Plan has {plan.TotalColorChanges} color changes, DST supports max {Capabilities.MaxColors}",
                Evidence = $"Color changes: {plan.TotalColorChanges}",
                Recommendation = "Reduce color changes or split design"
            });
        }

        return result;
    }

    /// <summary>
    /// Round-trip test: Read → Write → Read with semantic comparison
    /// </summary>
    public async Task<RoundTripResult> RoundTripTestAsync(Stream originalStream, CancellationToken ct = default)
    {
        var result = new RoundTripResult { FormatName = FormatName };

        try
        {
            // First read
            originalStream.Position = 0;
            var project1 = await ReadAsync(originalStream, null, ct);
            var engine = new StitchEngine();
            var plan1 = engine.Compile(project1);

            // Write to memory
            using var ms = new MemoryStream();
            await WriteAsync(project1, ms, null, ct);

            // Read back
            ms.Position = 0;
            var project2 = await ReadAsync(ms, null, ct);
            var plan2 = engine.Compile(project2);

            // Semantic comparison on StitchPlans
            result.Differences = CompareStitchPlans(plan1, plan2);
            result.Success = result.Differences.Count == 0;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }

        return result;
    }

    private List<SemanticDifference> CompareStitchPlans(StitchPlan a, StitchPlan b)
    {
        var diffs = new List<SemanticDifference>();

        if (a.TotalStitches != b.TotalStitches)
            diffs.Add(new SemanticDifference { Type = DifferenceType.StitchCount, Description = $"Stitch count: {a.TotalStitches} vs {b.TotalStitches}", ValueA = a.TotalStitches.ToString(), ValueB = b.TotalStitches.ToString() });
        
        if (a.TotalJumps != b.TotalJumps)
            diffs.Add(new SemanticDifference { Type = DifferenceType.JumpCount, Description = $"Jump count: {a.TotalJumps} vs {b.TotalJumps}", ValueA = a.TotalJumps.ToString(), ValueB = b.TotalJumps.ToString() });
        
        if (a.TotalTrims != b.TotalTrims)
            diffs.Add(new SemanticDifference { Type = DifferenceType.TrimCount, Description = $"Trim count: {a.TotalTrims} vs {b.TotalTrims}", ValueA = a.TotalTrims.ToString(), ValueB = b.TotalTrims.ToString() });
        
        if (a.TotalColorChanges != b.TotalColorChanges)
            diffs.Add(new SemanticDifference { Type = DifferenceType.ColorChangeCount, Description = $"Color changes: {a.TotalColorChanges} vs {b.TotalColorChanges}", ValueA = a.TotalColorChanges.ToString(), ValueB = b.TotalColorChanges.ToString() });
        
        if (!a.DesignBounds.Equals(b.DesignBounds))
            diffs.Add(new SemanticDifference { Type = DifferenceType.Bounds, Description = $"Bounds: {a.DesignBounds} vs {b.DesignBounds}", ValueA = a.DesignBounds.ToString(), ValueB = b.DesignBounds.ToString() });

        return diffs;
    }

    /// <summary>
    /// Semantic diff between two AtlasProjects (backward compatibility)
    /// </summary>
    public List<SemanticDifference> SemanticDiff(AtlasProject a, AtlasProject b)
    {
        var engine = new StitchEngine();
        var diffs = CompareStitchPlans(engine.Compile(a), engine.Compile(b));
        
        // Also compare thread palettes at project level
        if (a.ThreadPalette.Count != b.ThreadPalette.Count)
        {
            diffs.Add(new SemanticDifference 
            { 
                Type = DifferenceType.ColorPalette, 
                Description = $"Thread palette count: {a.ThreadPalette.Count} vs {b.ThreadPalette.Count}", 
                ValueA = a.ThreadPalette.Count.ToString(), 
                ValueB = b.ThreadPalette.Count.ToString() 
            });
        }
        else
        {
            // Compare individual colors
            for (int i = 0; i < a.ThreadPalette.Count; i++)
            {
                var ca = a.ThreadPalette[i];
                var cb = b.ThreadPalette[i];
                if (ca.R != cb.R || ca.G != cb.G || ca.B != cb.B)
                {
                    diffs.Add(new SemanticDifference 
                    { 
                        Type = DifferenceType.ColorPalette, 
                        Description = $"Thread color {i}: RGB({ca.R},{ca.G},{ca.B}) vs RGB({cb.R},{cb.G},{cb.B})", 
                        ValueA = $"RGB({ca.R},{ca.G},{ca.B})", 
                        ValueB = $"RGB({cb.R},{cb.G},{cb.B})" 
                    });
                }
            }
        }
        
        return diffs;
    }

    /// <summary>
        /// Generates a deterministic valid DST file for testing
        /// </summary>
        public byte[] GenerateFuzzInput(int seed = 0)
        {
            var rand = new Random(seed);
            var ms = new MemoryStream();
            var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

            // Deterministic header with bounds matching the stitch data we'll generate
            // Use a simple bounded pattern
            var header = new DstHeader
            {
                Label = "Fuzz Test",
                StitchCount = 100,
                ColorChanges = 1,
                MinX = -100, MaxX = 100,
                MinY = -100, MaxY = 100,
                StartX = 0, StartY = 0,
                EndX = 0, EndY = 0
            }.ToBytes();
            writer.Write(header);

            // Deterministic stitch data - use small deltas that stay within bounds
            int x = 0, y = 0;
            for (int i = 0; i < 100; i++)
            {
                // Small deltas that keep us within [-100, 100] bounds
                int dx = rand.Next(-5, 6);
                int dy = rand.Next(-5, 6);

                // Clamp to ensure we don't exceed bounds
                if (x + dx > 100) dx = 100 - x;
                if (x + dx < -100) dx = -100 - x;
                if (y + dy > 100) dy = 100 - y;
                if (y + dy < -100) dy = -100 - y;

                byte control = (byte)(i == 50 ? DstControl.ColorChange : DstControl.Normal);

                var (b1, b2, b3) = DstMovementEncoder.EncodeMovement(dx, dy, control);
                writer.Write(b1);
                writer.Write(b2);
                writer.Write(b3);

                x += dx;
                y += dy;
            }

            // End marker (Tajima spec: 0xF3 0x00 0x00)
            writer.Write(0xF3);
            writer.Write((byte)0x00);
            writer.Write((byte)0x00);
            writer.Flush();
            return ms.ToArray();
        }

        #region Private Helpers

    private DstHeader BuildHeader(AtlasProject project, List<DstStitchRecord> records, int colorChanges)
    {
        if (!records.Any())
        {
            return new DstHeader { Label = project.Name ?? "Empty", StitchCount = 0, ColorChanges = 0 };
        }

        int minX = records.Min(r => r.AbsoluteX);
        int maxX = records.Max(r => r.AbsoluteX);
        int minY = records.Min(r => r.AbsoluteY);
        int maxY = records.Max(r => r.AbsoluteY);
        int startX = records.First().AbsoluteX - records.First().DeltaX;
        int startY = records.First().AbsoluteY - records.First().DeltaY;
        int endX = records.Last().AbsoluteX;
        int endY = records.Last().AbsoluteY;

        return new DstHeader
        {
            Label = project.Name ?? "Untitled",
            StitchCount = records.Count,
            ColorChanges = colorChanges,
            MinX = minX, MaxX = maxX,
            MinY = minY, MaxY = maxY,
            StartX = startX, StartY = startY,
            EndX = endX, EndY = endY
        };
    }

    private string ComputeHash(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data)).ToLowerInvariant();
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

    #endregion
}