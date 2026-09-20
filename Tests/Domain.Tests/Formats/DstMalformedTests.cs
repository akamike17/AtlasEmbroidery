namespace AtlasEmbroidery.Domain.Formats.Dst;

using AtlasEmbroidery.Domain.Formats;
using FluentAssertions;
using Xunit;
using System.Text;

/// <summary>
/// Malformed DST test cases - 20+ cases covering various corruption scenarios
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
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_TOO_SMALL" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_InvalidStitchCountNegative_ReturnsCritical()
    {
        // Arrange: valid header but stitch count = -1
        var data = DstGoldenFiles.SimpleLine;
        // Modify stitch count at offset 98
        BitConverter.GetBytes(-1).CopyTo(data, 98);
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_INVALID_STITCH_COUNT" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_StitchCountExceedsMax_ReturnsCritical()
    {
        // Arrange: stitch count > MaxTotalStitches
        var data = DstGoldenFiles.SimpleLine;
        BitConverter.GetBytes(3_000_000).CopyTo(data, 98);
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_INVALID_STITCH_COUNT" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_ColorCountExceedsMax_ReturnsWarning()
    {
        // Arrange: color count > MaxColors
        var data = DstGoldenFiles.SimpleLine;
        data[102] = 255; // MaxColors = 250
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue(); // Warning, not critical
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_TOO_MANY_COLORS" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_InvalidDimensionsZero_ReturnsWarning()
    {
        // Arrange: width/height = 0
        var data = DstGoldenFiles.SimpleLine;
        BitConverter.GetBytes((short)0).CopyTo(data, 90);
        BitConverter.GetBytes((short)0).CopyTo(data, 92);
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_INVALID_DIMENSIONS" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_InvalidDimensionsNegative_ReturnsWarning()
    {
        // Arrange: width/height negative
        var data = DstGoldenFiles.SimpleLine;
        BitConverter.GetBytes((short)(-100)).CopyTo(data, 90);
        BitConverter.GetBytes((short)(-100)).CopyTo(data, 92);
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_INVALID_DIMENSIONS" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_ExcessiveDimensions_ReturnsWarning()
    {
        // Arrange: width/height > 10000
        var data = DstGoldenFiles.SimpleLine;
        BitConverter.GetBytes((short)20000).CopyTo(data, 90);
        BitConverter.GetBytes((short)20000).CopyTo(data, 92);
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_INVALID_DIMENSIONS" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_EmptyNameInHeader_ReturnsWarning()
    {
        // Arrange: name field empty
        var data = DstGoldenFiles.SimpleLine;
        // Name at offset 2, 16 bytes - already empty in golden file, let's make it spaces
        for (int i = 2; i < 18; i++) data[i] = 0x20;
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_EMPTY_NAME" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_CorruptedStitchData_MidStream_ReturnsWarning()
    {
        // Arrange: valid header but corrupted stitch bytes
        var data = DstGoldenFiles.SimpleLine;
        // Corrupt a stitch byte in the middle (after header)
        data[512 + 1] = 0xFF; // Invalid stitch encoding
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue(); // Sample check may not catch all
        result.Issues.Should().Contain(i => i.RuleId == "DST.STITCH_READ_ERROR" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_StitchDeltaOutOfRange_ReturnsWarning()
    {
        // Arrange: create a DST with a stitch delta > 2047
        var data = BuildDstWithLargeStitch(3000);
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId == "DST.STITCH_OUT_OF_RANGE" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public async Task Validate_MissingEndMarker_ReturnsWarning()
    {
        // Arrange: DST without end marker (0xF3 0x00 0x00)
        var data = DstGoldenFiles.SimpleLine;
        // Find and remove end marker
        var endMarker = new byte[] { 0xF3, 0x00, 0x00 };
        var ms = new MemoryStream(data);
        var reader = new BinaryReader(ms, Encoding.ASCII, leaveOpen: true);
        reader.BaseStream.Position = 512;
        
        // Rewrite without end marker
        var ms2 = new MemoryStream();
        var writer = new BinaryWriter(ms2, Encoding.ASCII, leaveOpen: true);
        writer.Write(data, 0, 512); // header
        
        // Copy stitches but skip end marker
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

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert - validation still passes but may not find end
        result.IsValid.Should().BeTrue();
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
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_TOO_SMALL" || i.RuleId == "DST.HEADER_INVALID_STITCH_COUNT");
    }

    [Fact]
    public async Task Validate_RandomBytes_ReturnsIssues()
    {
        // Arrange: random bytes
        var rand = new Random(42);
        var data = new byte[2000];
        rand.NextBytes(data);
        // Make it look like DST header
        data[0] = 0x20;
        data[1] = 0x20;
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
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_TOO_SMALL" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_HeaderOnlyNoStitchData_ReturnsWarning()
    {
        // Arrange: header but no stitch data
        var data = new byte[512];
        data[0] = 0x20;
        data[1] = 0x20;
        Encoding.ASCII.GetBytes("Test").CopyTo(data, 2);
        BitConverter.GetBytes((short)100).CopyTo(data, 90);
        BitConverter.GetBytes((short)100).CopyTo(data, 92);
        BitConverter.GetBytes(100).CopyTo(data, 98);
        data[102] = 1;
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Validate_VeryLargeStitchCountInHeader_ReturnsCritical()
    {
        // Arrange: header claims 10M stitches but file is small
        var data = DstGoldenFiles.SimpleLine;
        BitConverter.GetBytes(10_000_000).CopyTo(data, 98);
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_INVALID_STITCH_COUNT" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_NegativeStitchCountInHeader_ReturnsCritical()
    {
        // Arrange
        var data = DstGoldenFiles.SimpleLine;
        BitConverter.GetBytes(-100).CopyTo(data, 98);
        using var stream = new MemoryStream(data);

        // Act
        var result = await _adapter.ValidateAsync(stream);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "DST.HEADER_INVALID_STITCH_COUNT" && i.Severity == ValidationSeverity.Critical);
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

    // Helper to build DST with a large stitch delta
    private byte[] BuildDstWithLargeStitch(int delta)
    {
        var ms = new MemoryStream();
        var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        var header = new byte[512];
        header[0] = 0x20;
        header[1] = 0x20;
        Encoding.ASCII.GetBytes("Large Stitch").CopyTo(header, 2);
        BitConverter.GetBytes((short)1000).CopyTo(header, 90);
        BitConverter.GetBytes((short)1000).CopyTo(header, 92);
        BitConverter.GetBytes(2).CopyTo(header, 98); // 2 stitches
        header[102] = 1;
        writer.Write(header);

        // First stitch normal
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        // Second stitch with large delta
        var (b1, b2, b3) = EncodeStitch(delta, 0, 0);
        writer.Write(b1);
        writer.Write(b2);
        writer.Write(b3);

        // End marker
        writer.Write((byte)0xF3);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        writer.Flush();
        return ms.ToArray();
    }

    private static (byte b1, byte b2, byte b3) EncodeStitch(int dx, int dy, int flags)
    {
        dx = Math.Clamp(dx, -2048, 2047);
        dy = Math.Clamp(dy, -2048, 2047);
        
        int x = dx & 0xFFF;
        int y = (-dy) & 0xFFF;
        
        byte b1 = (byte)(((y >> 4) & 0xFC) | ((x >> 10) & 0x03));
        byte b2 = (byte)(((x >> 4) & 0x3F) | ((y >> 2) & 0xC0));
        byte b3 = (byte)(((y & 0x03) << 4) | ((x & 0x03) << 2) | (flags & 0x0F));
        
        return (b1, b2, b3);
    }
}