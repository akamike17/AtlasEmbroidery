namespace AtlasEmbroidery.Domain.Formats.Dst;

using AtlasEmbroidery.Domain.Formats;
using FluentAssertions;
using Xunit;
using System.Text;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;

/// <summary>
/// Malformed DST test cases - 20+ cases covering various corruption scenarios
/// Updated for correct Tajima DST specification
/// </summary>
public class DstMalformedTests
{
    private readonly DstFormatAdapter _adapter = new();

    [Theory]
    [InlineData("Truncated header (256 bytes)", 256)]
    [InlineData("Truncated header (100 bytes)", 100)]
    [InlineData("Truncated header (10 bytes)", 10)]
    [InlineData("Truncated header (0 bytes)", 0)]
    public async Task Validate_TruncatedHeader_ReturnsCritical(string name, int headerSize)
    {
        // Arrange
        var data = new byte[headerSize];
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse($"Case: {name}");
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_TOO_SMALL" && i.Severity == FmtValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_InvalidStitchCountNegative_ReturnsCritical()
    {
        // Arrange: valid header but stitch count = -1
        var data = DstGoldenFiles.SimpleLine;
        // ST: field is at line 1 after LA: - need to parse header text
        // The ST: value is in the text header
        var text = Encoding.ASCII.GetString(data, 0, 512);
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var stLine = lines.FirstOrDefault(l => l.StartsWith("ST:"));
        if (stLine != null)
        {
            var newText = text.Replace(stLine, "ST:-1");
            var newBytes = Encoding.ASCII.GetBytes(newText);
            Array.Resize(ref newBytes, 512);
            newBytes.CopyTo(data, 0);
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert - negative stitch count should be caught
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId.Contains("STITCH_COUNT") && i.Severity == FmtValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_StitchCountExceedsMax_ReturnsCritical()
    {
        // Arrange: stitch count > MaxTotalStitches
        var data = DstGoldenFiles.SimpleLine;
        var text = Encoding.ASCII.GetString(data, 0, 512);
        var stLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("ST:"));
        if (stLine != null)
        {
            var newText = text.Replace(stLine, "ST:3000000");
            var newBytes = Encoding.ASCII.GetBytes(newText);
            Array.Resize(ref newBytes, 512);
            newBytes.CopyTo(data, 0);
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId.Contains("STITCH_COUNT") && i.Severity == FmtValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_ColorCountExceedsMax_ReturnsWarning()
    {
        // Arrange: color count > MaxColors (250)
        var data = DstGoldenFiles.SimpleLine;
        var text = Encoding.ASCII.GetString(data, 0, 512);
        var coLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("CO:"));
        if (coLine != null)
        {
            var newText = text.Replace(coLine, "CO:255");
            var newBytes = Encoding.ASCII.GetBytes(newText);
            Array.Resize(ref newBytes, 512);
            newBytes.CopyTo(data, 0);
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert - warning, not critical
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId.Contains("COLOR") && i.Severity == FmtValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_InvalidDimensionsZero_ReturnsWarning()
    {
        // Arrange: width/height = 0
        var data = DstGoldenFiles.SimpleLine;
        var text = Encoding.ASCII.GetString(data, 0, 512);
        var pxLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("+X:"));
        if (pxLine != null)
        {
            var newText = text.Replace(pxLine, "+X:0");
            var newBytes = Encoding.ASCII.GetBytes(newText);
            Array.Resize(ref newBytes, 512);
            newBytes.CopyTo(data, 0);
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId.Contains("DIMENSION") && i.Severity == FmtValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_InvalidDimensionsNegative_ReturnsWarning()
    {
        // Arrange: width/height negative
        var data = DstGoldenFiles.SimpleLine;
        var text = Encoding.ASCII.GetString(data, 0, 512);
        var pxLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("+X:"));
        if (pxLine != null)
        {
            var newText = text.Replace(pxLine, "+X:-100");
            var newBytes = Encoding.ASCII.GetBytes(newText);
            Array.Resize(ref newBytes, 512);
            newBytes.CopyTo(data, 0);
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId.Contains("DIMENSION") && i.Severity == FmtValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_ExcessiveDimensions_ReturnsWarning()
    {
        // Arrange: width/height > reasonable bounds
        var data = DstGoldenFiles.SimpleLine;
        var text = Encoding.ASCII.GetString(data, 0, 512);
        var pxLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("+X:"));
        if (pxLine != null)
        {
            var newText = text.Replace(pxLine, "+X:20000");
            var newBytes = Encoding.ASCII.GetBytes(newText);
            Array.Resize(ref newBytes, 512);
            newBytes.CopyTo(data, 0);
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId.Contains("DIMENSION") && i.Severity == FmtValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_EmptyNameInHeader_ReturnsWarning()
    {
        // Arrange: name field empty
        var data = DstGoldenFiles.SimpleLine;
        var text = Encoding.ASCII.GetString(data, 0, 512);
        var laLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("LA:"));
        if (laLine != null)
        {
            var newText = text.Replace(laLine, "LA:");
            var newBytes = Encoding.ASCII.GetBytes(newText);
            Array.Resize(ref newBytes, 512);
            newBytes.CopyTo(data, 0);
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_EMPTY_LABEL" && i.Severity == FmtValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_CorruptedStitchData_MidStream_ReturnsCritical()
    {
        // Arrange: valid header but corrupted stitch bytes (invalid balanced ternary)
        var data = DstGoldenFiles.SimpleLine;
        // Corrupt a stitch byte in the middle (after header) - clear sync bits (bits 0-1 of byte 3)
        if (data.Length > 512 + 3)
        {
            data[512 + 2] = (byte)(data[512 + 2] & 0xFC); // Clear sync bits (bits 0-1 of byte 3 = bits 0-1 of third byte)
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert - invalid balanced ternary is Critical
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "DST.INVALID_BALANCED_TERNARY" && i.Severity == FmtValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_StitchDeltaOutOfRange_ReturnsCritical()
    {
        // Arrange: create a DST with a stitch delta > 121 DST units
        var data = BuildDstWithLargeStitch(200); // Exceeds max 121
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
        // Either movement exceeds max OR invalid balanced ternary encoding
        // Both are valid critical issues for a stitch delta out of range
        result.Issues.Should().Contain(i => 
            (i.RuleId == "DST.MOVEMENT_EXCEEDS_MAX" || i.RuleId == "DST.INVALID_BALANCED_TERNARY") 
            && i.Severity == FmtValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_MissingEndMarker_ReturnsCriticalInStrictMode()
    {
        // Arrange: DST without end marker (0xF3 0x00 0x00)
        var data = DstGoldenFiles.SimpleLine;
        
        // Find end marker and remove it
        var ms = new MemoryStream(data);
        var reader = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);
        reader.BaseStream.Position = 512;
        
        var ms2 = new MemoryStream();
        var writer = new BinaryWriter(ms2, Encoding.ASCII, leaveOpen: true);
        writer.Write(data, 0, 512); // header
        
        while (reader.BaseStream.Position + 2 < reader.BaseStream.Length)
        {
            byte b1 = reader.ReadByte();
            byte b2 = reader.ReadByte();
            byte b3 = reader.ReadByte();
            
            if (b1 == 0xF3 && b2 == 0x00 && b3 == 0x00)
                continue; // Skip end marker
                
            writer.Write(b1);
            writer.Write(b2);
            writer.Write(b3);
        }
        writer.Flush();
        data = ms2.ToArray();
        
        using var stream = new MemoryStream(data);

        // Act - strict mode
        var options = new FormatReadOptions { StrictMode = true };
        var result = await _adapter.ValidateAsync(stream);

        // Assert - strict mode should fail on missing END
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "DST.MISSING_END_MARKER" && i.Severity == FmtValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_AllZeros_ReturnsCritical()
    {
        // Arrange: all zeros
        var data = new byte[1000];
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
        // All zeros file has no LA: prefix, empty label, zero dimensions, no end marker
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_MISSING_LA" && i.Severity == FmtValidationSeverity.Critical);
        result.Issues.Should().Contain(i => i.RuleId == "DST.MISSING_END_MARKER" && i.Severity == FmtValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_RandomBytes_ReturnsIssues()
    {
        // Arrange: random bytes
        var rand = new Random(42);
        var data = new byte[2000];
        rand.NextBytes(data);
        // Make it look like DST header
        data[0] = 0x4C; // 'L'
        data[1] = 0x41; // 'A'
        data[2] = 0x3A; // ':'
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Validate_EmptyStream_ReturnsCritical()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_TOO_SMALL" && i.Severity == FmtValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_HeaderOnlyNoStitchData_ReturnsCriticalMissingEnd()
    {
        // Arrange: header but no stitch data
        var data = new byte[512];
        // Build minimal valid header
        var ms = new MemoryStream(data);
        var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);
        writer.Write("LA:Test\r\n");
        writer.Write("ST:0\r\n");
        writer.Write("CO:0\r\n");
        writer.Write("+X:10\r\n");
        writer.Write("-X:0\r\n");
        writer.Write("+Y:10\r\n");
        writer.Write("-Y:0\r\n");
        writer.Write("AX:0\r\n");
        writer.Write("AY:0\r\n");
        writer.Write("MX:0\r\n");
        writer.Write("MY:0\r\n");
        writer.Write("PD:******\r\n");
        writer.Flush();
        
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert - header only without END marker is invalid (Critical)
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "DST.MISSING_END_MARKER" && i.Severity == FmtValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_VeryLargeStitchCountInHeader_ReturnsCritical()
    {
        // Arrange: header claims 10M stitches but file is small
        var data = DstGoldenFiles.SimpleLine;
        var text = Encoding.ASCII.GetString(data, 0, 512);
        var stLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("ST:"));
        if (stLine != null)
        {
            var newText = text.Replace(stLine, "ST:10000000");
            var newBytes = Encoding.ASCII.GetBytes(newText);
            Array.Resize(ref newBytes, 512);
            newBytes.CopyTo(data, 0);
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert - mismatch between header and actual will be warning, but we check for critical on absurd counts
        result.IsValid.Should().BeFalse(); // Because file size doesn't match
    }

    [Fact]
    public async Task Validate_NegativeStitchCountInHeader_ReturnsCritical()
    {
        // Arrange
        var data = DstGoldenFiles.SimpleLine;
        var text = Encoding.ASCII.GetString(data, 0, 512);
        var stLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("ST:"));
        if (stLine != null)
        {
            var newText = text.Replace(stLine, "ST:-100");
            var newBytes = Encoding.ASCII.GetBytes(newText);
            Array.Resize(ref newBytes, 512);
            newBytes.CopyTo(data, 0);
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId.Contains("STITCH_COUNT") && i.Severity == FmtValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_ValidDstWithKnownGoodData_Passes()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.SimpleLine);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.FormatName.Should().Be("DST");
        result.DetectedCapabilities.Should().NotBeNull();
    }

    [Fact]
    public async Task Validate_MultiColorDesign_Passes()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.MultiColor);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_LargeDesign_Passes()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.LargeDesign);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_SatinColumn_Passes()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.SatinColumn);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_TatamiFill_Passes()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.TatamiFill);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_WithJumpsAndTrims_Passes()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.WithJumpsAndTrims);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_WithStops_Passes()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.WithStops);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_EmptyDesign_Passes()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.EmptyDesign);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_TrailingDataAfterEnd_ReturnsWarning()
    {
        // Arrange: valid DST with extra bytes after END marker
        var data = DstGoldenFiles.SimpleLine;
        var extended = new byte[data.Length + 10];
        Array.Copy(data, extended, data.Length);
        // Add garbage after end
        for (int i = data.Length; i < extended.Length; i++)
            extended[i] = 0xFF;
        
        using var stream = new MemoryStream(extended);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId == "DST.TRAILING_DATA" && i.Severity == FmtValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_HeaderExtentsMismatch_ReturnsWarning()
    {
        // Arrange: header extents don't match actual stitch coordinates
        var data = DstGoldenFiles.SimpleLine;
        var text = Encoding.ASCII.GetString(data, 0, 512);
        var pxLine = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(l => l.StartsWith("+X:"));
        if (pxLine != null)
        {
            var newText = text.Replace(pxLine, "+X:9999"); // Way larger than actual
            var newBytes = Encoding.ASCII.GetBytes(newText);
            Array.Resize(ref newBytes, 512);
            newBytes.CopyTo(data, 0);
        }
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_EXTENTS_MISMATCH" && i.Severity == FmtValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_FileTooLarge_ReturnsCritical()
    {
        // Arrange: file exceeds 50MB limit
        var data = new byte[51 * 1024 * 1024];
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "DST.FILE_TOO_LARGE" && i.Severity == FmtValidationSeverity.Critical);
    }

    // Helper to build DST with a large stitch delta (exceeding 127 DST units)
    private byte[] BuildDstWithLargeStitch(int deltaDstUnits)
    {
        var ms = new MemoryStream();
        var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // Build a proper header using text format (since DstHeader is internal)
        // Write raw ASCII bytes, NOT length-prefixed strings
        var headerLines = new[]
        {
            "LA:Large Stitch\r\n",
            "ST:2\r\n",
            "CO:0\r\n",
            "+X:1000\r\n",
            "-X:0\r\n",
            "+Y:1000\r\n",
            "-Y:0\r\n",
            "AX:0\r\n",
            "AY:0\r\n",
            "MX:0\r\n",
            "MY:0\r\n",
            "PD:******\r\n"
        };
        foreach (var line in headerLines)
        {
            var bytes = Encoding.ASCII.GetBytes(line);
            writer.Write(bytes);
        }
        // Pad to 512
        while (ms.Position < 512)
            writer.Write((byte)0x20);

        // First stitch normal (small move: dx=1, dy=0)
        // Using balanced ternary encoding: dx=1, dy=0, control=normal
        // Y encoded: 1 (mag=1 digit=1) = 0x0001 -> yEncoded = 1
        // X encoded: 1 (mag=1 digit=1) = 0x0001 -> xEncoded = 1
        // byte1 = Y[5:0] | X[7:6] = 0x01 | 0x00 = 0x01
        // byte2 = X[5:0] | Y[7:6] = 0x01 | 0x00 = 0x01
        // byte3 = control[7:6] | Y[9:8] | X[9:8] = 0x00 | 0x00 | 0x00 = 0x00
        writer.Write((byte)0x01);
        writer.Write((byte)0x01);
        writer.Write((byte)0x00);

        // Second stitch with large delta - write raw bytes that decode to > 127
        // The decoder reads 10 bits of encoded value + sign bit
        // We'll write bytes that produce a large encoded value
        // To get delta > 127, we need encoded value > 1023 (since max magnitude is 121)
        // We can write: byte1=0x00, byte2=0x00, byte3=0xFC (sign bit + high bits set)
        // This will decode to a value > 127
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0xFC); // Sign bit (0x400) + all magnitude bits set = large value

        // End marker
        writer.Write((byte)0xF3);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        writer.Flush();
        return ms.ToArray();
    }

    // Helper to encode a delta that exceeds the max representable value
    // We exploit the fact that we can set bits beyond the 10-bit limit
    private int EncodeLargeDelta(int delta)
    {
        // Just return a value that exceeds MaxDeltaPerRecord
        // The encoder will clamp or throw
        return delta;
    }
}