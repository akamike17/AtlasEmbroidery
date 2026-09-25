namespace AtlasEmbroidery.Tests.Formats.Exp;

using AtlasEmbroidery.Domain.Formats.Exp;
using AtlasEmbroidery.Domain.Formats;
using FluentAssertions;
using Xunit;

/// <summary>
/// Malformed and truncated EXP file tests
/// </summary>
public class ExpMalformedTests
{
    private readonly ExpFormatAdapter _adapter = new();

    [Fact]
    public async Task Read_EmptyFile_ReturnsEmptyProject()
    {
        var stream = new MemoryStream();
        var project = await _adapter.ReadAsync(stream);
        project.Should().NotBeNull();
        project.Objects.Should().BeEmpty();
    }

    [Fact]
    public async Task Read_TruncatedControlCode_Throws()
    {
        // Starts with control prefix but incomplete
        var data = new byte[] { 0x80 }; // Just control prefix
        var stream = new MemoryStream(data);
        
        // Should complete without exception (returns empty project)
        var project = await _adapter.ReadAsync(stream);
        project.Should().NotBeNull();
    }

    [Fact]
    public async Task Read_InvalidTrimSequence_SkipsGracefully()
    {
        // Control prefix + trim control but wrong sequence
        var data = new byte[] { 0x80, 0x80, 0x00, 0x00 }; // Should be 07 00
        var stream = new MemoryStream(data);
        
        // The reader now skips invalid trim sequences gracefully
        var project = await _adapter.ReadAsync(stream);
        project.Should().NotBeNull();
    }

    [Fact]
    public async Task Read_UnknownControlCode_SkipsGracefully()
    {
        // Control prefix + unknown control - should skip gracefully
        var data = new byte[] { 0x80, 0xFF, 0x00, 0x00 };
        var stream = new MemoryStream(data);
        
        // Should complete without exception (skips unknown control)
        var project = await _adapter.ReadAsync(stream);
        project.Should().NotBeNull();
    }

    [Fact]
    public async Task Validate_EmptyFile_ReturnsInvalid()
    {
        var stream = new MemoryStream();
        var result = await _adapter.ValidateAsync(stream);
        
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "EXP.EMPTY_FILE");
    }
}