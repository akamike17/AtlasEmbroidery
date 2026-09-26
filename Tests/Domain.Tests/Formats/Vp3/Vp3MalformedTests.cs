namespace AtlasEmbroidery.Tests.Formats.Vp3;

using AtlasEmbroidery.Domain.Formats.Vp3;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using FluentAssertions;
using Xunit;
using System.Text;

/// <summary>
/// Malformed and truncated file tests for VP3 format
/// </summary>
public sealed class Vp3MalformedTests
{
    private readonly Vp3FormatAdapter _adapter = new();

    [Fact]
    public async Task Read_EmptyFile_Throws()
    {
        var data = Array.Empty<byte>();
        var stream = new MemoryStream(data);
        
        // Empty file now throws InvalidDataException due to header size check
        await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_HeaderTooShort_Throws()
    {
        // Header shorter than minimum
        var data = new byte[50];
        var stream = new MemoryStream(data);
        
        await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_InvalidStitchOffset_Throws()
    {
        // Valid header size but stitch offset points past end of file
        var data = new byte[200];
        var stream = new MemoryStream(data);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(500); // stitch offset beyond file
            writer.Write(0x14);
            writer.Write(new byte[20]); // date
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write(0); // color count
            writer.Write(1); // point count
            writer.Write(Vp3Spec.Hoop110x110);
            // ... rest of header
        }
        stream.Position = 0;
        
        await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_TruncatedStitchData_ReturnsPartial()
    {
        // Valid header but truncated stitch data
        var data = new byte[300];
        var stream = new MemoryStream(data);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(116); // stitch offset = header base size
            writer.Write(0x14);
            writer.Write(Encoding.ASCII.GetBytes(DateTime.UtcNow.ToString("yyyyMMddHHmmss").PadRight(20, '\0')));
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write(1); // color count
            writer.Write(5); // point count
            writer.Write(Vp3Spec.Hoop110x110);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            writer.Write(1); // palette index
            writer.Write(0x0D); // color entry
        }
        stream.Position = 116; // at stitch data
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(new byte[] { 0x10, 0x00 }); // one stitch
            writer.Write(new byte[] { 0x80, 0x10 }); // END
        }
        stream.Position = 0;
        
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
        result.Issues.Should().Contain(i => i.RuleId == "VP3.TOO_SHORT");
    }

    [Fact]
    public async Task Validate_InvalidStitchOffset_Warns()
    {
        var data = new byte[200];
        var stream = new MemoryStream(data);
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(500); // invalid stitch offset
            writer.Write(0x14);
            writer.Write(new byte[20]);
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write(0); writer.Write(1);
            writer.Write(Vp3Spec.Hoop110x110);
            // fill rest with zeros
        }
        stream.Position = 0;
        
        var result = await _adapter.ValidateAsync(stream);
        result.IsValid.Should().BeTrue(); // Warning, not critical
        result.Issues.Should().Contain(i => i.RuleId == "VP3.INVALID_STITCH_OFFSET");
    }
}