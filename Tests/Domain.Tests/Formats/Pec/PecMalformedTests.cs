namespace AtlasEmbroidery.Tests.Formats.Pec;

using AtlasEmbroidery.Domain.Formats.Pec;
using AtlasEmbroidery.Domain.Formats;
using FluentAssertions;
using Xunit;
using System.Text;

/// <summary>
/// Malformed and truncated PEC file tests
/// </summary>
public class PecMalformedTests
{
    private readonly PecFormatAdapter _adapter = new();

    [Fact]
    public async Task Read_TooShort_Throws()
    {
        var stream = new MemoryStream(new byte[4]);
        await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_InvalidSignature_Throws()
    {
        var stream = new MemoryStream(Encoding.ASCII.GetBytes("INVALID\0\0\0\0\0\0\0\0"));
        await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_TruncatedHeader_Throws()
    {
        // Valid signature but too short
        var data = new byte[100];
        Encoding.ASCII.GetBytes(PecSpec.Signature).CopyTo(data, 0);
        var stream = new MemoryStream(data);
        await Assert.ThrowsAsync<EndOfStreamException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_TruncatedStitchBlock_ReturnsPartial()
    {
        // Create minimal valid PEC then truncate
        var pattern = new MemoryStream();
        await WriteMinimalValidPec(pattern);
        pattern.Position = 0;
        
        // Read and truncate after header
        var bytes = pattern.ToArray();
        var truncated = new MemoryStream(bytes[..Math.Min(550, bytes.Length)]);
        
        var project = await _adapter.ReadAsync(truncated);
        project.Should().NotBeNull();
    }

    [Fact]
    public async Task Validate_InvalidSignature_ReturnsCritical()
    {
        var stream = new MemoryStream(Encoding.ASCII.GetBytes("NOTPEC\0\0"));
        var result = await _adapter.ValidateAsync(stream);
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PEC.INVALID_SIGNATURE" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public async Task Validate_TooShort_ReturnsCritical()
    {
        var stream = new MemoryStream(new byte[4]);
        var result = await _adapter.ValidateAsync(stream);
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PEC.TOO_SHORT");
    }

    [Fact]
    public async Task Validate_HeaderTooSmall_ReturnsCritical()
    {
        var data = new byte[100];
        Encoding.ASCII.GetBytes(PecSpec.Signature).CopyTo(data, 0);
        var stream = new MemoryStream(data);
        var result = await _adapter.ValidateAsync(stream);
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PEC.HEADER_TOO_SMALL");
    }

    [Fact]
    public async Task Validate_ValidPEC_ReturnsValid()
    {
        var pattern = new MemoryStream();
        await WriteMinimalValidPec(pattern);
        pattern.Position = 0;
        var result = await _adapter.ValidateAsync(pattern);
        result.IsValid.Should().BeTrue();
    }

    private async Task WriteMinimalValidPec(MemoryStream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        
        // Signature
        writer.Write(Encoding.ASCII.GetBytes(PecSpec.Signature));
        
        // Label (16 bytes)
        writer.Write(Encoding.ASCII.GetBytes("TEST".PadRight(16, '\0')));
        
        // Fixed bytes (0xF bytes)
        writer.Write(new byte[] { 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0xFF, 0x00 });
        
        // Icon stride and height
        writer.Write((byte)PecSpec.IconByteStride);
        writer.Write((byte)PecSpec.IconHeight);
        
        // 0xC bytes
        writer.Write(new byte[12]);
        
        // Color changes (0)
        writer.Write((byte)0xFF); // 0 colors
        
        // Pad to 0x1D0
        int written = (int)writer.BaseStream.Position;
        int padCount = 0x1D0 - written;
        if (padCount > 0) writer.Write(new byte[padCount]);
        
        // Stitch block length placeholder
        writer.Write((ushort)0);
        writer.WriteInt24LE(20); // Small block length
        
        // Block header
        writer.Write(PecSpec.BlockHeader);
        
        // Bounds (4 shorts)
        writer.WriteInt16LE(100);
        writer.WriteInt16LE(100);
        writer.WriteInt16LE(0x1E0);
        writer.WriteInt16LE(0x1B0);
        
        // Single stitch (0,0)
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        
        // END marker
        writer.Write(PecSpec.EndMarker);
        
        // Graphics (one blank)
        writer.Write(new byte[PecSpec.GraphicsByteCount]);
    }
}