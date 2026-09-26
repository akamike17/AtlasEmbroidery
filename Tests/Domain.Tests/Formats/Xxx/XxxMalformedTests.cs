namespace AtlasEmbroidery.Tests.Formats.Xxx;

using AtlasEmbroidery.Domain.Formats.Xxx;
using AtlasEmbroidery.Domain.Formats;
using FluentAssertions;
using Xunit;
using System.Text;

/// <summary>
/// Malformed and truncated XXX file tests
/// </summary>
public sealed class XxxMalformedTests
{
    private readonly XxxFormatAdapter _adapter = new();

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
        var data = new byte[50]; // Less than 0x100 header
        var stream = new MemoryStream(data);
        
        await Assert.ThrowsAsync<InvalidDataException>(() => _adapter.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_InvalidSignature_ReturnsInvalid()
    {
        var data = new byte[0x200];
        // Fill with zeros but no XXX signature at 0xA0
        var stream = new MemoryStream(data);
        
        var result = await _adapter.ValidateAsync(stream);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Read_TruncatedStitchData_HandlesGracefully()
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Write valid header
        for (int i = 0; i < 0x17; i++) writer.Write((byte)0x00);
        writer.Write(10); // stitch count
        for (int i = 0; i < 0x0C; i++) writer.Write((byte)0x00);
        writer.Write(1); // thread count
        writer.Write((short)0);
        
        // Bounds
        writer.Write((short)10);
        writer.Write((short)10);
        writer.Write((short)5);
        writer.Write((short)-5);
        writer.Write((short)0);
        writer.Write((short)10);
        writer.Write((short)0);
        writer.Write((short)0);

        // Pad to signature
        for (long i = stream.Position; i < XxxSpec.SignatureOffset; i++) writer.Write((byte)0x00);
        writer.Write(Encoding.ASCII.GetBytes("XXX"));
        for (long i = stream.Position; i < XxxSpec.HeaderVariantBSize; i++) writer.Write((byte)0x00);

        // Stitch data: incomplete record (only 1 byte)
        writer.Write((byte)1);
        // Missing second byte - truncated

        stream.Position = 0;
        
        // Should not throw, just return what it can parse
        var project = await _adapter.ReadAsync(stream);
        project.Should().NotBeNull();
    }

    [Fact]
    public async Task Read_UnknownControlCode_SkipsGracefully()
    {
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Write valid header
        for (int i = 0; i < 0x17; i++) writer.Write((byte)0x00);
        writer.Write(2); // stitch count
        for (int i = 0; i < 0x0C; i++) writer.Write((byte)0x00);
        writer.Write(1); // thread count
        writer.Write((short)0);
        
        // Bounds
        writer.Write((short)10);
        writer.Write((short)10);
        writer.Write((short)5);
        writer.Write((short)-5);
        writer.Write((short)0);
        writer.Write((short)10);
        writer.Write((short)0);
        writer.Write((short)0);

        // Pad to signature
        for (long i = stream.Position; i < XxxSpec.SignatureOffset; i++) writer.Write((byte)0x00);
        writer.Write(Encoding.ASCII.GetBytes("XXX"));
        for (long i = stream.Position; i < XxxSpec.HeaderVariantBSize; i++) writer.Write((byte)0x00);

        // Stitch data: one normal stitch + unknown control + end
        writer.Write((byte)1);
        writer.Write((byte)1);
        // Unknown control 0x7F 0x99
        writer.Write((byte)0x7F);
        writer.Write((byte)0x99);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        // End marker
        writer.Write(XxxSpec.EndSequence);

        // Color table
        for (int i = 0; i < XxxSpec.MaxColors; i++)
        {
            writer.Write((byte)0x00);
            writer.Write((byte)255);
            writer.Write((byte)0);
            writer.Write((byte)0);
        }
        writer.Write((uint)0xFFFFFF00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x01);

        stream.Position = 0;
        
        var project = await _adapter.ReadAsync(stream);
        project.Should().NotBeNull();
    }

    [Fact]
    public async Task Validate_EmptyFile_ReturnsInvalid()
    {
        var stream = new MemoryStream();
        
        var result = await _adapter.ValidateAsync(stream);
        
        result.IsValid.Should().BeFalse();
        result.Issues.Should().Contain(i => i.RuleId == "XXX.FILE_TOO_SMALL");
    }
}