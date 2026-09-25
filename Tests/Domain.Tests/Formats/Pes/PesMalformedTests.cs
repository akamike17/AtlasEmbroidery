namespace AtlasEmbroidery.Tests.Formats.Pes;

using AtlasEmbroidery.Domain.Formats.Pes;
using AtlasEmbroidery.Domain.Formats;
using FluentAssertions;
using Xunit;
using System.Text;

/// <summary>
/// Malformed and truncated PES file tests
/// </summary>
public class PesMalformedTests
{
    private readonly PesFormatAdapter _adapter = new();

    [Fact]
    public async Task Read_InvalidSignature_Throws()
    {
        var data = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        var stream = new MemoryStream(data);
        
        await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_TooShort_Throws()
    {
        var data = Encoding.ASCII.GetBytes("#PES0060");
        var stream = new MemoryStream(data);
        
        await Assert.ThrowsAsync<EndOfStreamException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_ValidSignatureMissingPecBlock_Throws()
    {
        // Valid PES v6 signature but no PEC block position / data
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        
        writer.Write(Encoding.ASCII.GetBytes("#PES0060"));
        writer.Write(0); // PEC block position (invalid)
        
        writer.Flush();
        stream.Position = 0;
        
        await Assert.ThrowsAsync<EndOfStreamException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Validate_InvalidSignature_ReturnsInvalid()
    {
        // 12+ bytes of invalid signature to pass length check but fail signature check
        var data = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B };
        var stream = new MemoryStream(data);
        
        var result = await _adapter.ValidateAsync(stream);
        
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PES.INVALID_SIGNATURE");
    }

    [Fact]
    public async Task Validate_TooShort_ReturnsInvalid()
    {
        var data = Encoding.ASCII.GetBytes("#PES0060");
        var stream = new MemoryStream(data);
        
        var result = await _adapter.ValidateAsync(stream);
        
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "PES.TOO_SHORT");
    }

    [Fact]
    public async Task Read_TruncatedHeader_Throws()
    {
        // Valid signature but too short for full header
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        
        writer.Write(Encoding.ASCII.GetBytes("#PES0060"));
        writer.Write((int)100); // PEC block at position 100
        // Not enough data for header
        
        writer.Flush();
        stream.Position = 0;
        
        await Assert.ThrowsAsync<EndOfStreamException>(() => _adapter.ReadAsync(stream));
    }
}