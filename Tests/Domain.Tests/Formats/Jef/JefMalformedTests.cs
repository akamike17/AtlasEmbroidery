namespace AtlasEmbroidery.Tests.Formats.Jef;

using AtlasEmbroidery.Domain.Formats.Jef;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using FluentAssertions;
using Xunit;
using System.Text;

/// <summary>
/// Malformed and truncated file tests for JEF format
/// </summary>
public sealed class JefMalformedTests
{
    private readonly JefFormatAdapter _adapter = new();

    [Fact]
    public async Task Read_EmptyFile_Throws()
    {
        var data = Array.Empty<byte>();
        var stream = new MemoryStream(data);
        
        await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_HeaderTooShort_Throws()
    {
        // Header shorter than 512 bytes
        var data = new byte[100];
        var stream = new MemoryStream(data);
        
        await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_MissingTerminator_StillParses()
    {
        // Valid header without terminator - should still parse what it can
        var data = Encoding.ASCII.GetBytes("LA:Test\rST:0000001\rCO:001\r" + new string(' ', 400));
        // Pad to 512 bytes
        var padded = new byte[512];
        Array.Copy(data, padded, Math.Min(data.Length, 512));
        var stream = new MemoryStream(padded);
        
        var project = await _adapter.ReadAsync(stream);
        project.Should().NotBeNull();
        project.Name.Should().Be("Test");
    }

    [Fact]
    public async Task Read_InvalidStitchData_SkipsGracefully()
    {
        // Valid header + invalid stitch data
        var header = Encoding.ASCII.GetBytes("LA:Test\rST:0000001\rCO:001\r" + new string(' ', 400));
        var headerBytes = new byte[512];
        Array.Copy(header, headerBytes, Math.Min(header.Length, 512));
        
        var badStitch = new byte[] { 0xFF, 0xFF, 0xFF }; // Invalid
        var data = new byte[headerBytes.Length + badStitch.Length];
        Array.Copy(headerBytes, data, headerBytes.Length);
        Array.Copy(badStitch, 0, data, headerBytes.Length, badStitch.Length);
        
        var stream = new MemoryStream(data);
        var project = await _adapter.ReadAsync(stream);
        project.Should().NotBeNull();
    }

    [Fact]
    public async Task Validate_EmptyFile_ReturnsInvalid()
    {
        var data = Array.Empty<byte>();
        var stream = new MemoryStream(data);
        
        var result = _adapter.Validate(stream);
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "JEF.TOO_SHORT");
    }

    [Fact]
    public async Task Validate_MissingHeaderTerminator_Warns()
    {
        var header = Encoding.ASCII.GetBytes("LA:Test\rST:0000001\rCO:001\r" + new string(' ', 400));
        var headerBytes = new byte[512];
        Array.Copy(header, headerBytes, Math.Min(header.Length, 512));
        
        var stream = new MemoryStream(headerBytes);
        var result = await _adapter.ValidateAsync(stream);
        
        result.IsValid.Should().BeTrue(); // Not critical
        result.Issues.Should().Contain(i => i.RuleId == "JEF.MISSING_TERMINATOR");
    }

    [Fact]
    public async Task Validate_MissingNamePrefix_Warns()
    {
        var header = Encoding.ASCII.GetBytes("ST:0000001\rCO:001\r" + new string(' ', 400));
        var headerBytes = new byte[512];
        Array.Copy(header, headerBytes, Math.Min(header.Length, 512));
        
        var stream = new MemoryStream(headerBytes);
        var result = await _adapter.ValidateAsync(stream);
        
        result.IsValid.Should().BeTrue();
        result.Issues.Should().Contain(i => i.RuleId == "JEF.MISSING_NAME_PREFIX");
    }
}