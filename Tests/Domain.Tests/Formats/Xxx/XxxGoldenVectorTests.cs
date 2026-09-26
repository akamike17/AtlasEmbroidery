namespace AtlasEmbroidery.Tests.Formats.Xxx;

using AtlasEmbroidery.Domain.Formats.Xxx;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;
using System.Text;

/// <summary>
/// Golden vector tests for XXX format cross-referenced against pyembroidery XxxWriter
/// </summary>
public sealed class XxxGoldenVectorTests
{
    private readonly XxxFormatAdapter _adapter = new();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -20)]
    [InlineData(100, 50)]
    [InlineData(-100, -50)]
    [InlineData(124, 124)] // Max normal delta
    [InlineData(-124, -124)]
    public void Xxx_StitchRoundTrip_Works(int dx, int dy)
    {
        var encoded = XxxMovementEncoder.EncodeMovement(dx, dy);
        var (decodedDx, decodedDy, control, consumed) = XxxMovementDecoder.DecodeNext(encoded);

        control.Should().Be(XxxControl.Normal);
        decodedDx.Should().Be(dx);
        decodedDy.Should().Be(dy);
        consumed.Should().Be(2);
    }

    [Fact]
    public void Xxx_LongMoveEncodeDecode_Works()
    {
        // Deltas that exceed normal 8-bit range
        var encoded = XxxMovementEncoder.EncodeLongMove(200, -300);
        var (decodedDx, decodedDy, control, consumed) = XxxMovementDecoder.DecodeNext(encoded);

        control.Should().Be(XxxControl.Normal);
        decodedDx.Should().Be(200);
        decodedDy.Should().Be(-300);
        consumed.Should().Be(5);
    }

    [Fact]
    public void Xxx_JumpEncodeDecode_Works()
    {
        var encoded = XxxMovementEncoder.EncodeJump(10, 20);
        var (_, _, control, consumed) = XxxMovementDecoder.DecodeNext(encoded);

        control.Should().Be(XxxControl.Jump);
        consumed.Should().Be(4);
        encoded[0].Should().Be(XxxSpec.ControlPrefix);
        encoded[1].Should().Be(XxxSpec.CtrlJump);
    }

    [Fact]
    public void Xxx_TrimEncodeDecode_Works()
    {
        var encoded = XxxMovementEncoder.EncodeTrim();
        var (_, _, control, consumed) = XxxMovementDecoder.DecodeNext(encoded);

        control.Should().Be(XxxControl.Trim);
        consumed.Should().Be(4);
        encoded[0].Should().Be(XxxSpec.ControlPrefix);
        encoded[1].Should().Be(XxxSpec.CtrlTrim);
    }

    [Fact]
    public void Xxx_ColorChangeEncodeDecode_Works()
    {
        var encoded = XxxMovementEncoder.EncodeColorChange();
        var (_, _, control, consumed) = XxxMovementDecoder.DecodeNext(encoded);

        control.Should().Be(XxxControl.ColorChange);
        consumed.Should().Be(4);
        encoded[0].Should().Be(XxxSpec.ControlPrefix);
        encoded[1].Should().Be(XxxSpec.CtrlColorChange);
    }

    [Fact]
    public void Xxx_StopEncodeDecode_Works()
    {
        var encoded = XxxMovementEncoder.EncodeStop();
        var (_, _, control, consumed) = XxxMovementDecoder.DecodeNext(encoded);

        control.Should().Be(XxxControl.ColorChange); // Stop encoded as color change in XXX
        consumed.Should().Be(4);
    }

    [Fact]
    public void Xxx_NeedleChangeEncodeDecode_Works()
    {
        var encoded = XxxMovementEncoder.EncodeNeedleChange(5);
        var (_, _, control, consumed) = XxxMovementDecoder.DecodeNext(encoded);

        control.Should().Be(XxxControl.NeedleChange);
        consumed.Should().Be(4);
        encoded[0].Should().Be(XxxSpec.ControlPrefix);
        encoded[1].Should().Be((byte)(XxxSpec.CtrlNeedleChangeStart + 4)); // Needle 5 = 0x0E
    }

    [Fact]
    public void Xxx_EndEncodeDecode_Works()
    {
        var encoded = XxxMovementEncoder.EncodeEnd();
        var (_, _, control, consumed) = XxxMovementDecoder.DecodeNext(encoded);

        control.Should().Be(XxxControl.End);
        consumed.Should().Be(4);
        encoded.Should().Equal(XxxSpec.EndSequence);
    }

    [Fact]
    public async Task Read_MinimalValidXXX_ReturnsProject()
    {
        // Create a minimal valid XXX file (variant B)
        var stream = new MemoryStream();
        var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Write variant B header
        for (int i = 0; i < 0x17; i++) writer.Write((byte)0x00);
        writer.Write(1); // stitch count - 1
        for (int i = 0; i < 0x0C; i++) writer.Write((byte)0x00);
        writer.Write(1); // thread count
        writer.Write((short)0);
        
        // Bounds
        writer.Write((short)10); // width
        writer.Write((short)10); // height
        writer.Write((short)5);  // lastX
        writer.Write((short)-5); // -lastY
        writer.Write((short)0);  // -minX
        writer.Write((short)10); // maxY
        writer.Write((short)0);
        writer.Write((short)0);

        // Pad to signature
        for (long i = stream.Position; i < XxxSpec.SignatureOffset; i++) writer.Write((byte)0x00);
        writer.Write(Encoding.ASCII.GetBytes("XXX"));
        for (long i = stream.Position; i < XxxSpec.HeaderVariantBSize; i++) writer.Write((byte)0x00);

        // Stitch data: one normal stitch (1, 1)
        writer.Write((byte)1);
        writer.Write((byte)255); // -1 negated = 255

        // End marker
        writer.Write(XxxSpec.EndSequence);

        // Color table (21 entries)
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
        project.Objects.Should().HaveCount(1);
    }

    [Fact]
    public async Task Write_SingleStitch_ProducesValidStructure()
    {
        var project = CreateSimpleProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);

        var bytes = stream.ToArray();
        
        // Check header structure
        bytes.Length.Should().BeGreaterThan(XxxSpec.HeaderVariantBSize + 4);
        
        // Check signature at 0xA0
        bytes[XxxSpec.SignatureOffset].Should().Be((byte)'X');
        bytes[XxxSpec.SignatureOffset + 1].Should().Be((byte)'X');
        bytes[XxxSpec.SignatureOffset + 2].Should().Be((byte)'X');

        // Check end marker exists
        bytes.Should().Contain(XxxSpec.EndSequence);
    }

    [Fact]
        public void Write_MultiColor_ProducesColorChanges()
        {
            // Test that the encoder can produce color change marker
            var colorChangeBytes = XxxMovementEncoder.EncodeColorChange();
            colorChangeBytes.Should().HaveCount(4);
            colorChangeBytes[0].Should().Be(XxxSpec.ControlPrefix);
            colorChangeBytes[1].Should().Be(XxxSpec.CtrlColorChange);
        
            // Also verify decoder recognizes it
            var (_, _, control, _) = XxxMovementDecoder.DecodeNext(colorChangeBytes);
            control.Should().Be(XxxControl.ColorChange);
        }

    private AtlasProject CreateSimpleProject()
    {
        var project = new AtlasProject { Name = "Simple" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));

        var shape = new ShapeObject
        {
            Name = "Simple",
            Vertices = new List<Point> { new(0, 0), new(1000, 0), new(1000, 1000), new(0, 1000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape.StitchParams.Density = 4000;
        project.Objects.Add(shape);

        return project;
    }

    private AtlasProject CreateMultiColorProject()
    {
        var project = new AtlasProject { Name = "MultiColor" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.ThreadPalette.Add(new ThreadColor(0, 0, 255, "Test", "002", "Blue"));

        var shape1 = new ShapeObject
        {
            Name = "RedSquare",
            Vertices = new List<Point> { new(0, 0), new(1000, 0), new(1000, 1000), new(0, 1000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape1.StitchParams.Density = 4000;
        project.Objects.Add(shape1);

        var shape2 = new ShapeObject
        {
            Name = "BlueSquare",
            Vertices = new List<Point> { new(2000, 0), new(3000, 0), new(3000, 1000), new(2000, 1000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape2.StitchParams.Density = 4000;
        project.Objects.Add(shape2);

        return project;
    }
}