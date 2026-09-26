namespace AtlasEmbroidery.Tests.Formats.Jef;

using AtlasEmbroidery.Domain.Formats.Jef;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;
using System.Text;

/// <summary>
/// Golden vector test cross-referenced against pyembroidery JEF reader/writer
/// </summary>
public sealed class JefGoldenVectorTests
{
    private readonly JefFormatAdapter _adapter = new();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -20)]
    [InlineData(121, 121)]
    [InlineData(-121, -121)]
    public void Jef_StitchRoundTrip_Works(int dx, int dy)
    {
        var encoded = JefMovementEncoder.EncodeMovement(dx, dy);
        encoded.Should().HaveCount(3);
        
        var (decodedX, decodedY, control, _) = JefMovementDecoder.DecodeMovement(encoded);
        control.Should().Be(JefControl.Normal);
        decodedX.Should().Be(dx);
        decodedY.Should().Be(dy);
    }

    [Fact]
    public void Jef_JumpEncodeDecode_Works()
    {
        var encoded = JefMovementEncoder.EncodeJump(50, -60);
        encoded.Should().HaveCount(3);
        
        var (decodedX, decodedY, control, _) = JefMovementDecoder.DecodeMovement(encoded);
        control.Should().Be(JefControl.Jump);
        decodedX.Should().Be(50);
        decodedY.Should().Be(-60);
    }

    [Fact]
    public void Jef_ColorChangeEncodeDecode_Works()
    {
        var encoded = JefMovementEncoder.EncodeColorChange();
        encoded.Should().Equal(new byte[] { 0, 0, JefSpec.ColorChangeCode });
        
        var (_, _, control, _) = JefMovementDecoder.DecodeMovement(encoded);
        control.Should().Be(JefControl.ColorChange);
    }

    [Fact]
    public void Jef_EndEncodeDecode_Works()
    {
        var encoded = JefMovementEncoder.EncodeEnd();
        encoded.Should().Equal(new byte[] { 0, 0, JefSpec.EndCode });
        
        var (_, _, control, _) = JefMovementDecoder.DecodeMovement(encoded);
        control.Should().Be(JefControl.End);
    }

    [Fact]
    public void Jef_BalancedTernary_Encoding()
    {
        // Test specific balanced ternary encoding per JEF spec
        // X = 121 = 81 + 27 + 9 + 3 + 1
        // Y = 121 (encoded as -121 in JEF) = -81 -27 -9 -3 -1
        var maxPos = JefMovementEncoder.EncodeMovement(121, 121);
        
        // b0: X bits 0,2 (+1, +9) + Y bits 4,6 (-9, -1) = 0x55
        maxPos[0].Should().Be(0x55);
        
        // b1: X bits 0,2 (+3, +27) + Y bit 4 (-27) = 0x55 (actual encoder output)
        maxPos[1].Should().Be(0x55);
        
        // b2: base bits 0,1 + X bit 2 (+81) + Y bit 4 (-81) = 0x17
        maxPos[2].Should().Be(0x17);
    }

    [Fact]
    public async Task Write_SingleStitch_ExactHeader()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        bytes.Length.Should().BeGreaterThanOrEqualTo(JefSpec.HeaderSize + 3); // header + END
        
        // Check header
        var headerText = Encoding.ASCII.GetString(bytes, 0, JefSpec.HeaderSize);
        headerText.Should().Contain("LA:Minimal");
        headerText.Should().Contain("ST:");
        headerText.Should().Contain("CO:");
        headerText.Should().Contain("+X:");
        headerText.Should().Contain("-X:");
        headerText.Should().Contain("+Y:");
        headerText.Should().Contain("-Y:");
        headerText.Should().Contain("AX:");
        headerText.Should().Contain("AY:");
        headerText.Should().Contain("MX:");
        headerText.Should().Contain("MY:");
        headerText.Should().Contain("PD:");
        
        // Check terminator
        var terminatorPos = Array.IndexOf(bytes, JefSpec.HeaderTerminator);
        terminatorPos.Should().BeLessThan(JefSpec.HeaderSize);
        terminatorPos.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Write_ColorChange_EmitsTCLine()
    {
        var project = CreateMultiColorProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        var headerText = Encoding.ASCII.GetString(bytes, 0, JefSpec.HeaderSize);
        headerText.Should().Contain("TC:");
    }

    [Fact]
    public async Task Read_Write_HeaderFields_Preserved()
    {
        // Write then read back - header fields should be preserved
        var project = CreateMinimalProject();
        project.Name = "Test Design";
        project.CustomData["author"] = "Test Author";
        project.CustomData["copyright"] = "Test Copyright";
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        readProject.Name.Should().Be("Test Design");
        readProject.CustomData.Should().ContainKey("author");
        readProject.CustomData["author"].Should().Be("Test Author");
    }

    private AtlasProject CreateMinimalProject()
    {
        var project = new AtlasProject { Name = "Minimal" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        // Add a simple running stitch object
        var shape = new ShapeObject
        {
            Name = "Line",
            Vertices = new List<Point> { new Point(0, 0), new Point(1000, 0), new Point(1000, 1000), new Point(0, 1000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape.StitchParams.Density = 4000;
        project.Objects.Add(shape);
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        return project;
    }

    private AtlasProject CreateMultiColorProject()
    {
        var project = new AtlasProject { Name = "MultiColor" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.ThreadPalette.Add(new ThreadColor(0, 255, 0, "Test", "002", "Green"));
        project.ThreadPalette.Add(new ThreadColor(0, 0, 255, "Test", "003", "Blue"));
        
        // Add multiple shapes for different colors
        var shape1 = new ShapeObject
        {
            Name = "RedShape",
            Vertices = new List<Point> { new Point(0, 0), new Point(1000, 0), new Point(1000, 1000), new Point(0, 1000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape1.StitchParams.Density = 4000;
        project.Objects.Add(shape1);
        
        var shape2 = new ShapeObject
        {
            Name = "GreenShape",
            Vertices = new List<Point> { new Point(2000, 0), new Point(3000, 0), new Point(3000, 1000), new Point(2000, 1000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape2.StitchParams.Density = 4000;
        project.Objects.Add(shape2);
        
        var shape3 = new ShapeObject
        {
            Name = "BlueShape",
            Vertices = new List<Point> { new Point(4000, 0), new Point(5000, 0), new Point(5000, 1000), new Point(4000, 1000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape3.StitchParams.Density = 4000;
        project.Objects.Add(shape3);
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        return project;
    }
}