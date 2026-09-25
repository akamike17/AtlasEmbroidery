namespace AtlasEmbroidery.Tests.Formats.Pec;

using AtlasEmbroidery.Domain.Formats.Pec;
using FluentAssertions;
using Xunit;

/// <summary>
/// Golden vector tests: cross-reference against pyembroidery reference implementation
/// These vectors are INDEPENDENT - derived from pyembroidery's write_value() logic
/// 
/// IMPORTANT: Our encoder takes SCREEN coordinates and negates Y internally (per PEC spec).
/// pyembroidery's write_value() takes PRE-NEGATED coordinates.
/// So our encoder(input) == pyembroidery.write_value(input_x, -input_y)
/// </summary>
public class PecGoldenVectorTests
{
    private static readonly (int dx, int dy, byte flags, byte[] expected)[] GoldenVectors = new[]
    {
        // Short form (7-bit): normal stitches
        (0, 0, (byte)0, new byte[] { 0x00, 0x00 }),
        (1, 0, (byte)0, new byte[] { 0x01, 0x00 }),
        (-1, 0, (byte)0, new byte[] { 0x7F, 0x00 }),
        (10, 0, (byte)0, new byte[] { 0x0A, 0x00 }),
        (-10, 0, (byte)0, new byte[] { 0x76, 0x00 }),
        (62, 0, (byte)0, new byte[] { 0x3E, 0x00 }),
        (-63, 0, (byte)0, new byte[] { 0x41, 0x00 }),
        
        // Y axis short form (our encoder negates Y)
        (0, 1, (byte)0, new byte[] { 0x00, 0x7F }),   // dy=1 -> negate to -1 -> 0x7F
        (0, -1, (byte)0, new byte[] { 0x00, 0x01 }),  // dy=-1 -> negate to 1 -> 0x01
        (0, 10, (byte)0, new byte[] { 0x00, 0x76 }),  // dy=10 -> negate to -10 -> 0x76
        (0, -10, (byte)0, new byte[] { 0x00, 0x0A }), // dy=-10 -> negate to 10 -> 0x0A
        
        // Mixed short form
        (10, -20, (byte)0, new byte[] { 0x0A, 0x14 }),
        (-30, 40, (byte)0, new byte[] { 0x62, 0x58 }), // dx=-30=0x62, dy=40->-40=0xD8... wait 40 & 0x7F = 40=0x28
        
        // Long form (12-bit): jump/trim or large deltas
        // Jump flag = 0x10. Our encoder applies flag to both X and Y.
        // Input (100, 0): X=100, Y=0 (negated stays 0) -> both with FLAG_LONG|JUMP
        (100, 0, PecSpec.JumpCode, new byte[] { 0x90, 0x64, 0x90, 0x00 }),
        // Input (0, -100): X=0, Y=-100->100 -> both with FLAG_LONG|JUMP
        (0, -100, PecSpec.JumpCode, new byte[] { 0x90, 0x00, 0x90, 0x64 }),
    };

    private static readonly (byte colorIndex, byte[] expected)[] ColorChangeVectors = new[]
    {
        ((byte)0x00, new byte[] { 0xFE, 0xB0, 0x00 }),
        ((byte)0x01, new byte[] { 0xFE, 0xB0, 0x01 }),
        ((byte)0x0F, new byte[] { 0xFE, 0xB0, 0x0F }),
    };

    private static readonly byte[] EndVector = new byte[] { 0xFF };

    [Theory]
    [MemberData(nameof(GetGoldenVectors))]
    public void Encode_GoldenVector_MatchesPyembroidery(int dx, int dy, byte flags, byte[] expected)
    {
        var encoded = PecMovementEncoder.EncodeMovement(dx, dy, flags);
        encoded.Should().Equal(expected, $"Golden vector mismatch for dx={dx}, dy={dy}, flags=0x{flags:X2}");
    }

    [Theory]
    [MemberData(nameof(GetDecodeVectors))]
    public void Decode_GoldenVector_RoundTrips(int dx, int dy, byte[] expected)
    {
        var decoded = PecMovementDecoder.DecodeMovement(expected);
        decoded.deltaX.Should().Be(dx, $"Round-trip dx mismatch");
        decoded.deltaY.Should().Be(dy, $"Round-trip dy mismatch");
    }

    [Theory]
    [MemberData(nameof(GetColorChangeVectors))]
    public void Encode_ColorChange_MatchesPyembroidery(byte colorIndex, byte[] expected)
    {
        var encoded = PecMovementEncoder.EncodeColorChange(colorIndex);
        encoded.Should().Equal(expected, $"Color change mismatch for index={colorIndex}");
    }

    [Theory]
    [MemberData(nameof(GetColorChangeDecodeVectors))]
    public void Decode_ColorChange_RoundTrips(byte[] expected)
    {
        var decoded = PecMovementDecoder.DecodeMovement(expected);
        decoded.control.Should().Be(PecControl.ColorChange, $"Color change control mismatch");
        decoded.bytesConsumed.Should().Be(3, $"Color change should consume 3 bytes");
    }

    [Fact]
    public void Encode_End_MatchesPyembroidery()
    {
        var encoded = PecMovementEncoder.EncodeEnd();
        encoded.Should().Equal(EndVector, "END marker mismatch");
    }

    [Fact]
    public void Decode_End_RoundTrips()
    {
        var decoded = PecMovementDecoder.DecodeMovement(EndVector);
        decoded.control.Should().Be(PecControl.End, "END control mismatch");
        decoded.bytesConsumed.Should().Be(1, "END should consume 1 byte");
    }

    public static IEnumerable<object[]> GetGoldenVectors()
    {
        foreach (var (dx, dy, flags, expected) in GoldenVectors)
        {
            yield return new object[] { dx, dy, flags, expected };
        }
    }

    public static IEnumerable<object[]> GetDecodeVectors()
    {
        foreach (var (dx, dy, flags, expected) in GoldenVectors)
        {
            yield return new object[] { dx, dy, expected };
        }
    }

    public static IEnumerable<object[]> GetColorChangeVectors()
    {
        foreach (var (colorIndex, expected) in ColorChangeVectors)
        {
            yield return new object[] { colorIndex, expected };
        }
    }

    public static IEnumerable<object[]> GetColorChangeDecodeVectors()
    {
        foreach (var (colorIndex, expected) in ColorChangeVectors)
        {
            yield return new object[] { expected };
        }
    }
}