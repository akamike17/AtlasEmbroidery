namespace AtlasEmbroidery.Tests.Formats.Exp;

using AtlasEmbroidery.Domain.Formats.Exp;
using AtlasEmbroidery.Domain.Formats;
using FluentAssertions;
using Xunit;

/// <summary>
/// Golden vector tests for EXP format
/// Cross-referenced against pyembroidery EXP reader/writer
/// </summary>
public class ExpGoldenVectorTests
{
    [Fact]
    public void Exp_StitchEncoding_MatchesPyembroidery()
    {
        // pyembroidery: dx & 0xFF, -dy & 0xFF
        // Delta (0,0) -> 0x00, 0x00
        ExpMovementEncoder.EncodeMovement(0, 0).Should().Equal(new byte[] { 0x00, 0x00 });
        
        // Delta (1,0) -> 0x01, 0x00
        ExpMovementEncoder.EncodeMovement(1, 0).Should().Equal(new byte[] { 0x01, 0x00 });
        
        // Delta (-1,0) -> 0xFF, 0x00
        ExpMovementEncoder.EncodeMovement(-1, 0).Should().Equal(new byte[] { 0xFF, 0x00 });
        
        // Delta (10, -20) -> 0x0A, 0x14
        ExpMovementEncoder.EncodeMovement(10, -20).Should().Equal(new byte[] { 0x0A, 0x14 });
        
        // Delta (127, 127) -> 0x7F, 0x81
        ExpMovementEncoder.EncodeMovement(127, 127).Should().Equal(new byte[] { 0x7F, 0x81 });
        
        // Delta (-128, -128) -> 0x80, 0x80
        ExpMovementEncoder.EncodeMovement(-128, -128).Should().Equal(new byte[] { 0x80, 0x80 });
    }

    [Fact]
    public void Exp_JumpEncoding_IsExact()
    {
        // Jump: 0x80 0x04 + delta bytes
        var encoded = ExpMovementEncoder.EncodeJump(10, 5);
        encoded.Should().Equal(new byte[] { 0x80, 0x04, 0x0A, 0xFB }); // 5 negated = -5 = 0xFB
    }

    [Fact]
    public void Exp_TrimEncoding_IsExact()
    {
        // Trim: 0x80 0x80 0x07 0x00
        ExpMovementEncoder.EncodeTrim().Should().Equal(new byte[] { 0x80, 0x80, 0x07, 0x00 });
    }

    [Fact]
    public void Exp_ColorChangeEncoding_IsExact()
    {
        // Color change: 0x80 0x01 0x00 0x00
        ExpMovementEncoder.EncodeColorChange().Should().Equal(new byte[] { 0x80, 0x01, 0x00, 0x00 });
    }

    [Fact]
    public void Exp_StopEncoding_IsExact()
    {
        // Stop same as color change in EXP
        ExpMovementEncoder.EncodeStop().Should().Equal(new byte[] { 0x80, 0x01, 0x00, 0x00 });
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -20)]
    [InlineData(127, 127)]
    [InlineData(-127, -127)]
    public void Exp_StitchRoundTrip_Works(int dx, int dy)
    {
        var encoded = ExpMovementEncoder.EncodeMovement(dx, dy);
        var decoded = ExpMovementDecoder.DecodeMovement(encoded);
        decoded.deltaX.Should().Be(dx);
        decoded.deltaY.Should().Be(dy);
        decoded.control.Should().Be(ExpControl.Normal);
    }

    [Fact]
    public void Exp_JumpRoundTrip_Works()
    {
        var encoded = ExpMovementEncoder.EncodeJump(10, 5);
        var decoded = ExpMovementDecoder.DecodeMovement(encoded);
        decoded.deltaX.Should().Be(10);
        decoded.deltaY.Should().Be(5);
        decoded.control.Should().Be(ExpControl.Jump);
        decoded.bytesConsumed.Should().Be(4);
    }

    [Fact]
    public void Exp_TrimRoundTrip_Works()
    {
        var encoded = ExpMovementEncoder.EncodeTrim();
        var decoded = ExpMovementDecoder.DecodeMovement(encoded);
        decoded.control.Should().Be(ExpControl.Trim);
        decoded.bytesConsumed.Should().Be(4);
    }

    [Fact]
    public void Exp_ColorChangeRoundTrip_Works()
    {
        var encoded = ExpMovementEncoder.EncodeColorChange();
        var decoded = ExpMovementDecoder.DecodeMovement(encoded);
        decoded.control.Should().Be(ExpControl.ColorChange);
        decoded.bytesConsumed.Should().Be(4);
    }
}