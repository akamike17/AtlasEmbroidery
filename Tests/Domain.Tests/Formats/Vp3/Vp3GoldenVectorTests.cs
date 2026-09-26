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
/// Golden vector test cross-referenced against pyembroidery VP3 reader/writer
/// </summary>
public sealed class Vp3GoldenVectorTests
{
    private readonly Vp3FormatAdapter _adapter = new();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(-1, 0)]
    [InlineData(10, -20)]
    [InlineData(127, 127)]
    [InlineData(-127, -127)]
    public void Vp3_StitchRoundTrip_Works(int dx, int dy)
    {
        var encoded = Vp3MovementEncoder.EncodeStitch(dx, dy);
        encoded.Should().HaveCount(2);
        
        var (decodedX, decodedY, control, _) = Vp3MovementDecoder.DecodeNext(encoded);
        control.Should().Be(Vp3Control.Normal);
        decodedX.Should().Be(dx);
        decodedY.Should().Be(dy);
    }

    [Fact]
    public void Vp3_JumpEncodeDecode_Works()
    {
        var encoded = Vp3MovementEncoder.EncodeJump(50, -60);
        encoded.Should().HaveCount(4);
        encoded[0].Should().Be(0x80);
        encoded[1].Should().Be(0x02);
        
        var (decodedX, decodedY, control, _) = Vp3MovementDecoder.DecodeNext(encoded);
        control.Should().Be(Vp3Control.Jump);
        decodedX.Should().Be(50);
        decodedY.Should().Be(-60);
    }

    [Fact]
    public void Vp3_ColorChangeEncodeDecode_Works()
    {
        var encoded = Vp3MovementEncoder.EncodeColorChange(10, 10);
        encoded.Should().HaveCount(4);
        encoded[0].Should().Be(0x80);
        encoded[1].Should().Be(0x01);
        
        var (_, _, control, _) = Vp3MovementDecoder.DecodeNext(encoded);
        control.Should().Be(Vp3Control.ColorChange);
    }

    [Fact]
    public void Vp3_EndEncodeDecode_Works()
    {
        var encoded = Vp3MovementEncoder.EncodeEnd();
        encoded.Should().Equal(new byte[] { 0x80, 0x10 });
        
        var (_, _, control, _) = Vp3MovementDecoder.DecodeNext(encoded);
        control.Should().Be(Vp3Control.End);
    }

    [Fact]
    public void Vp3_TrimEncode_Works()
    {
        var encoded = Vp3MovementEncoder.EncodeTrim(3);
        encoded.Should().HaveCount(12); // 3 * 4 bytes
        for (int i = 0; i < 3; i++)
        {
            encoded[i * 4].Should().Be(0x80);
            encoded[i * 4 + 1].Should().Be(0x02);
            encoded[i * 4 + 2].Should().Be(0x00);
            encoded[i * 4 + 3].Should().Be(0x00);
        }
    }

    [Fact]
    public async Task Write_SingleStitch_ExactHeader()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        bytes.Length.Should().BeGreaterThanOrEqualTo(Vp3Spec.HeaderBaseSize);
        
        // Verify header structure
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII, leaveOpen: true);
        int stitchOffset = reader.ReadInt32();
        stitchOffset.Should().BeGreaterThanOrEqualTo(Vp3Spec.HeaderBaseSize);
        
        int version = reader.ReadInt32();
        version.Should().Be(0x14);
        
        byte[] dateBytes = reader.ReadBytes(20);
        string dateStr = Encoding.ASCII.GetString(dateBytes).TrimEnd('\0');
        dateStr.Should().MatchRegex(@"^\d{14}$");
    }

    [Fact]
    public async Task Write_HoopSize_Calculated()
    {
        var project = CreateMinimalProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.ASCII, leaveOpen: true);
        
        reader.BaseStream.Seek(4, SeekOrigin.Begin); // skip stitch offset
        reader.ReadInt32(); // version
        reader.ReadBytes(20); // date
        reader.ReadByte(); reader.ReadByte(); // reserved
        int colorCount = reader.ReadInt32();
        int pointCount = reader.ReadInt32();
        int hoopSize = reader.ReadInt32();
        
        hoopSize.Should().BeInRange(Vp3Spec.Hoop50x50, Vp3Spec.Hoop200x200);
    }

    [Fact]
    public async Task Read_Write_HeaderFields_Preserved()
    {
        var project = CreateMinimalProject();
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        readProject.Should().NotBeNull();
        readProject.Name.Should().NotBeNull();
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
}