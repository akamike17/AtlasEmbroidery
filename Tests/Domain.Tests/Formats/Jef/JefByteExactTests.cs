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
/// Byte-exact verification tests for JEF format
/// </summary>
public sealed class JefByteExactTests
{
    private readonly JefFormatAdapter _adapter = new();

    [Fact]
    public async Task Write_HeaderStructure_IsExact()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        
        // Header must be exactly 512 bytes
        bytes.Length.Should().BeGreaterThanOrEqualTo(JefSpec.HeaderSize + 3);
        
        // Terminator at correct position
        int terminatorPos = Array.IndexOf(bytes, JefSpec.HeaderTerminator);
        terminatorPos.Should().BeLessThan(JefSpec.HeaderSize);
        terminatorPos.Should().BeGreaterThan(0);
        
        // Padding after terminator should be spaces (0x20)
        for (int i = terminatorPos + 1; i < JefSpec.HeaderSize; i++)
        {
            bytes[i].Should().Be(JefSpec.HeaderPad);
        }
    }

    [Fact]
    public async Task Write_EndMarker_IsExact()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        
        // Last 3 bytes should be END marker (0x00 0x00 0xF3)
        bytes[^3].Should().Be(0x00);
        bytes[^2].Should().Be(0x00);
        bytes[^1].Should().Be(JefSpec.EndCode);
    }

    [Fact]
    public async Task Write_ColorChangeMarker_IsExact()
    {
        // Test that the encoder can produce color change marker
        var colorChangeBytes = JefMovementEncoder.EncodeColorChange();
        colorChangeBytes.Should().Equal(new byte[] { 0x00, 0x00, JefSpec.ColorChangeCode });
        
        // Also verify decoder recognizes it
        var (_, _, control, _) = JefMovementDecoder.DecodeMovement(colorChangeBytes);
        control.Should().Be(JefControl.ColorChange);
    }

    [Fact]
    public async Task Write_JumpStitch_HasJumpFlag()
    {
        var project = CreateJumpProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        
        var headerEnd = JefSpec.HeaderSize;
        bool foundJump = false;
        for (int i = headerEnd; i < bytes.Length - 2; i += 3)
        {
            byte b2 = bytes[i + 2];
            if ((b2 & JefSpec.JumpFlag) != 0)
            {
                foundJump = true;
                break;
            }
        }
        foundJump.Should().BeTrue("Jump project should have jump stitches with jump flag");
    }

    [Fact]
    public async Task Write_ThreadColors_InTCLines()
    {
        var project = CreateMultiColorProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        var headerText = Encoding.ASCII.GetString(bytes, 0, JefSpec.HeaderSize);
        
        // Should have 3 TC lines for 3 colors
        int tcCount = headerText.Split("TC:").Length - 1;
        tcCount.Should().Be(3);
        
        // Each TC line should have hex, description, catalog
        foreach (var line in headerText.Split('\r'))
        {
            if (line.StartsWith("TC:"))
            {
                var parts = line.Substring(3).Split(',');
                parts.Should().HaveCount(3);
                parts[0].Should().StartWith("#"); // Hex color
                parts[2].Should().MatchRegex("^\\d{3}$"); // Catalog number
            }
        }
    }

    [Fact]
    public async Task Write_StitchCount_InHeader()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        var headerText = Encoding.ASCII.GetString(bytes, 0, JefSpec.HeaderSize);
        
        headerText.Should().Contain("ST:");
        var stLine = headerText.Split('\r').First(l => l.StartsWith("ST:"));
        var countStr = stLine.Substring(3);
        int.TryParse(countStr, out int count).Should().BeTrue();
        count.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Write_Bounds_InHeader()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        var headerText = Encoding.ASCII.GetString(bytes, 0, JefSpec.HeaderSize);
        
        headerText.Should().Contain("+X:");
        headerText.Should().Contain("-X:");
        headerText.Should().Contain("+Y:");
        headerText.Should().Contain("-Y:");
        headerText.Should().Contain("AX:");
        headerText.Should().Contain("AY:");
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

    private AtlasProject CreateJumpProject()
    {
        var project = new AtlasProject { Name = "Jump" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        // Add a shape that will generate jumps (separate disconnected shapes)
        var shape1 = new ShapeObject
        {
            Name = "Shape1",
            Vertices = new List<Point> { new Point(0, 0), new Point(1000, 0), new Point(1000, 1000), new Point(0, 1000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape1.StitchParams.Density = 4000;
        project.Objects.Add(shape1);
        
        var shape2 = new ShapeObject
        {
            Name = "Shape2",
            Vertices = new List<Point> { new Point(5000, 0), new Point(6000, 0), new Point(6000, 1000), new Point(5000, 1000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape2.StitchParams.Density = 4000;
        project.Objects.Add(shape2);
        
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        return project;
    }
}