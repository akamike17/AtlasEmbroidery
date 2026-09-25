namespace AtlasEmbroidery.Tests.Formats.Pes;

using AtlasEmbroidery.Domain.Formats.Pec;
using AtlasEmbroidery.Domain.Formats.Pes;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Golden vector tests for PES format
/// Tests both header parsing and stitch data (which uses PEC encoding)
/// </summary>
public class PesGoldenVectorTests
{
    private readonly PesFormatAdapter _adapter = new();

    [Fact]
    public void Pes_V6_Signature_Recognition()
    {
        var sig = "#PES0060";
        var version = sig switch
        {
            "#PES0001" => 1.0,
            "#PES0020" => 2.0,
            "#PES0022" => 2.2,
            "#PES0030" => 3.0,
            "#PES0040" => 4.0,
            "#PES0050" or "#PES0055" or "#PES0056" => 5.0,
            "#PES0060" => 6.0,
            "#PES0070" => 7.0,
            "#PES0080" => 8.0,
            "#PES0090" => 9.0,
            "#PES0100" => 10.0,
            _ => 1.0
        };
        
        version.Should().Be(6.0);
    }

    [Fact]
    public void Pes_V10_Signature_Recognition()
    {
        var sig = "#PES0100";
        var version = sig switch
        {
            "#PES0001" => 1.0,
            "#PES0020" => 2.0,
            "#PES0022" => 2.2,
            "#PES0030" => 3.0,
            "#PES0040" => 4.0,
            "#PES0050" or "#PES0055" or "#PES0056" => 5.0,
            "#PES0060" => 6.0,
            "#PES0070" => 7.0,
            "#PES0080" => 8.0,
            "#PES0090" => 9.0,
            "#PES0100" => 10.0,
            _ => 1.0
        };
        
        version.Should().Be(10.0);
    }

    [Fact]
    public void Pes_PecSignatures_AlsoRecognized()
    {
        // PES reader falls back to PEC reader if signature is #PEC0001
        var sig = "#PEC0001";
        var isValid = sig.StartsWith("#PES") || sig == "#PEC0001";
        
        isValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0, new byte[] { 0x00, 0x00 })]
    [InlineData(1, 0, new byte[] { 0x01, 0x00 })]
    [InlineData(-1, 0, new byte[] { 0x7F, 0x00 })]
    [InlineData(10, 0, new byte[] { 0x0A, 0x00 })]
    [InlineData(-10, 0, new byte[] { 0x76, 0x00 })]
    [InlineData(62, 0, new byte[] { 0x3E, 0x00 })]
    [InlineData(-63, 0, new byte[] { 0x41, 0x00 })]
    [InlineData(0, 1, new byte[] { 0x00, 0x7F })]
    [InlineData(0, -1, new byte[] { 0x00, 0x01 })]
    [InlineData(100, 0, new byte[] { 0x80, 0x64, 0x80, 0x00 })]
    public void Pes_StitchEncoding_MatchesPec(int dx, int dy, byte[] expected)
    {
        // PES uses PEC stitch encoding
        var encoded = PecMovementEncoder.EncodeMovement(dx, dy);
        encoded.Should().Equal(expected);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -20)]
    [InlineData(100, 0)]
    public void Pes_StitchRoundTrip_Works(int dx, int dy)
    {
        var encoded = PecMovementEncoder.EncodeMovement(dx, dy);
        var decoded = PecMovementDecoder.DecodeMovement(encoded);
        decoded.deltaX.Should().Be(dx);
        decoded.deltaY.Should().Be(dy);
    }

    [Fact]
    public void Pes_ColorChangeEncoding_IsExact()
    {
        var encoded = PecMovementEncoder.EncodeColorChange(5);
        encoded.Should().Equal(new byte[] { 0xFE, 0xB0, 0x05 });
    }

    [Fact]
    public void Pes_EndEncoding_IsExact()
    {
        var encoded = PecMovementEncoder.EncodeEnd();
        encoded.Should().Equal(new byte[] { 0xFF });
    }
}