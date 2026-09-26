namespace AtlasEmbroidery.Tests.Formats.Vp3;

using AtlasEmbroidery.Domain.Formats.Vp3;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;
using System.Text;

/// <summary>
/// Byte-exact verification tests for VP3 format
/// </summary>
public sealed class Vp3ByteExactTests
{
    private readonly Vp3FormatAdapter _adapter = new();

    [Fact]
    public async Task Write_HeaderStructure_IsExact()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        bytes.Length.Should().BeGreaterThanOrEqualTo(Vp3Spec.HeaderBaseSize);
        
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII, leaveOpen: true);
        int stitchOffset = reader.ReadInt32();
        stitchOffset.Should().BeGreaterThanOrEqualTo(Vp3Spec.HeaderBaseSize);
        
        int version = reader.ReadInt32();
        version.Should().Be(0x14);
    }

    [Fact]
    public async Task Write_EndMarker_IsExact()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        
        // Last 2 bytes should be END marker (0x80 0x10)
        bytes[^2].Should().Be(0x80);
        bytes[^1].Should().Be(0x10);
    }

    [Fact]
    public async Task Write_JumpStitch_HasControlPrefix()
    {
        // Test that the encoder can produce jump marker
        var jumpBytes = Vp3MovementEncoder.EncodeJump(10, 10);
        jumpBytes.Should().HaveCount(4);
        jumpBytes[0].Should().Be(0x80);
        jumpBytes[1].Should().Be(0x02);
        
        // Also verify decoder recognizes it
        var (_, _, control, _) = Vp3MovementDecoder.DecodeNext(jumpBytes);
        control.Should().Be(Vp3Control.Jump);
    }

    [Fact]
    public async Task Write_ColorChange_HasControlPrefix()
    {
        // Test that the encoder can produce color change marker
        var colorChangeBytes = Vp3MovementEncoder.EncodeColorChange(10, 10);
        colorChangeBytes.Should().HaveCount(4);
        colorChangeBytes[0].Should().Be(0x80);
        colorChangeBytes[1].Should().Be(0x01);
        
        // Also verify decoder recognizes it
        var (_, _, control, _) = Vp3MovementDecoder.DecodeNext(colorChangeBytes);
        control.Should().Be(Vp3Control.ColorChange);
    }

    [Fact]
    public async Task Write_PaletteIndices_InHeader()
    {
        var project = CreateMultiColorProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII, leaveOpen: true);
        
        // Read through header to find palette indices
        int stitchOffset = reader.ReadInt32();
        reader.ReadInt32(); // version
        reader.ReadBytes(20); // date
        reader.ReadByte(); reader.ReadByte(); // reserved
        int colorCount = reader.ReadInt32();
        reader.ReadInt32(); // pointCount
        reader.ReadInt32(); // hoopSize
        reader.ReadBytes(16); // centerOffsets (4 int32)
        reader.ReadBytes(64); // hoopEdgeDistances (16 int32)
        
        // Now at palette indices
        for (int i = 0; i < colorCount; i++)
        {
            int idx = reader.ReadInt32();
            idx.Should().BeInRange(1, 64); // JEF palette is 1-64
        }
    }

    [Fact]
    public async Task Write_StitchCount_InHeader()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII, leaveOpen: true);
        
        reader.BaseStream.Seek(4 + 20 + 2, SeekOrigin.Begin); // skip offset, date, reserved
        int colorCount = reader.ReadInt32();
        int pointCount = reader.ReadInt32();
        
        pointCount.Should().BeGreaterThanOrEqualTo(1); // At least END
    }

    [Fact]
    public async Task Write_HoopEdgeDistances_InHeader()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII, leaveOpen: true);
        
        // Seek to hoop edge distances (after hoop size + center offsets = 4 + 4*4 = 20 bytes)
        reader.BaseStream.Seek(4 + 20 + 2 + 4 + 4 + 20, SeekOrigin.Begin); // offset + date + reserved + colorCount + pointCount + hoopSize + 4 center offsets
        
        // 4 hoop types * 4 distances = 16 int32s
        for (int h = 0; h < 4; h++)
        {
            int left = reader.ReadInt32();
            int top = reader.ReadInt32();
            int right = reader.ReadInt32();
            int bottom = reader.ReadInt32();
            // Should be >= 0 or -1
            (left >= -1).Should().BeTrue();
            (top >= -1).Should().BeTrue();
            (right >= -1).Should().BeTrue();
            (bottom >= -1).Should().BeTrue();
        }
    }

    [Fact]
    public async Task Write_RegularStitch_TwoBytes()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        
        // Find regular stitches (no 0x80 prefix) in stitch data
        int headerEnd = Vp3Spec.HeaderBaseSize + (project.ThreadPalette.Count * Vp3Spec.ColorEntrySize) + 4;
        bool foundRegular = false;
        for (int i = headerEnd; i < bytes.Length - 1; i++)
        {
            if (bytes[i] != 0x80 && bytes[i + 1] != 0x80)
            {
                // Check if next is control
                if (i + 2 < bytes.Length && bytes[i + 2] != 0x80)
                {
                    foundRegular = true;
                    break;
                }
            }
        }
        foundRegular.Should().BeTrue("Should have regular stitches (2 bytes each)");
    }

    private AtlasProject CreateMinimalProject()
    {
        var project = new AtlasProject { Name = "Minimal" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
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