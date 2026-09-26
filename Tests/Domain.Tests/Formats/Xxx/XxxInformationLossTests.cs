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
/// Information loss documentation tests for XXX format
/// </summary>
public sealed class XxxInformationLossTests
{
    private readonly XxxFormatAdapter _adapter = new();

    [Fact]
    public async Task XXX_ThreadColor_MappedToFixed21EntryTable()
    {
        // XXX has exactly 21 color entries in the color table
        // If source has more than 21 colors, they must be mapped/merged
        var project = CreateProjectWithColors(30);
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        
        // Find color table (after end marker)
        int endPos = -1;
        for (int i = 0; i < bytes.Length - 3; i++)
        {
            if (bytes[i] == 0x7F && bytes[i + 1] == 0x7F && bytes[i + 2] == 0x02 && bytes[i + 3] == 0x14)
            {
                endPos = i + 4;
                break;
            }
        }
        
        // Count color entries (each starts with 0x00)
        // But stop before final marker (0xFFFFFF00 = 00 FF FF FF in LE)
        int colorEntries = 0;
        for (int i = endPos; i + 3 < bytes.Length; i += 4)
        {
            if (bytes[i] == 0x00)
            {
                // Check if this is the start of the final marker (0xFFFFFF00 = 00 FF FF FF)
                if (i + 3 < bytes.Length && bytes[i + 1] == 0xFF && bytes[i + 2] == 0xFF && bytes[i + 3] == 0xFF)
                    break; // This is the final marker, not a color entry
                colorEntries++;
            }
            else
                break;
        }
        
        colorEntries.Should().Be(XxxSpec.MaxColors, "XXX color table limited to 21 entries");
    }

    [Fact]
    public void XXX_NoExplicitFastSlowCommands()
    {
        // XXX doesn't have explicit FAST/SLOW commands in stitch stream
        // Speed changes must be handled externally
        var capabilities = _adapter.Capabilities;
        capabilities.SupportsStop.Should().BeTrue();
        capabilities.SupportsJump.Should().BeTrue();
        capabilities.SupportsTrim.Should().BeTrue();
        // No SupportsFast or SupportsSlow property
    }

    [Fact]
    public void XXX_NoExplicitNeedleSetInStitchStream()
    {
        // XXX has needle change commands (0x0A-0x17) but they're mapped to color changes
        // True NEEDLE_SET with thread mapping is not directly represented
        var encoded = XxxMovementEncoder.EncodeNeedleChange(5);
        
        encoded[0].Should().Be(XxxSpec.ControlPrefix);
        encoded[1].Should().Be((byte)(XxxSpec.CtrlNeedleChangeStart + 4));
    }

    [Fact]
    public async Task XXX_NoDesignNameInHeader()
    {
        // XXX variant B header doesn't store design name (only "XXX" signature)
        // Design name from source is lost
        var project = new AtlasProject { Name = "MyDesign" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var shape = new ShapeObject
        {
            Name = "Shape",
            Vertices = new List<Point> { new(0, 0), new(1000, 0) },
            IsClosed = false,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape.StitchParams.Density = 4000;
        project.Objects.Add(shape);
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        
        // Check that "MyDesign" is not in the header
        string headerText = Encoding.ASCII.GetString(bytes, 0, XxxSpec.HeaderVariantBSize);
        headerText.Should().NotContain("MyDesign");
        headerText.Should().Contain("XXX");
    }

    [Fact]
    public void XXX_StopAndColorChangeIndistinguishable()
    {
        // XXX uses same control code (0x7F 0x08) for both stop and color change
        var colorChange = XxxMovementEncoder.EncodeColorChange();
        var stop = XxxMovementEncoder.EncodeStop();
        
        colorChange.Should().Equal(stop, "Color change and stop use identical encoding in XXX");
    }

    [Fact]
    public void XXX_NoTrimSequenceInStitchData()
    {
        // XXX trim is encoded as 0x7F 0x03 with delta bytes
        // But it's not a multi-byte sequence like EXP's 0x80 0x80 0x07 0x00
        var trim = XxxMovementEncoder.EncodeTrim();
        
        trim.Should().Equal(new byte[] { 0x7F, 0x03, 0x00, 0x00 });
    }

    [Fact]
    public void XXX_MaxDelta124ForNormalMoves()
    {
        // Normal moves limited to 124 (0x7C) due to reserved 0xFD,0xFE,0xFF
        // Moves >124 must use long encoding (0x7D prefix)
        XxxSpec.MaxDeltaPerRecord.Should().Be(124);
    }

    [Fact]
    public void XXX_LongMoveUses16BitLE()
    {
        // Long moves use 0x7D + 16-bit little-endian deltas
        var longMove = XxxMovementEncoder.EncodeLongMove(1000, -2000);
        
        longMove[0].Should().Be(XxxSpec.LongMovePrefix);
        longMove.Length.Should().Be(5); // 0x7D + 4 bytes (2 x 16-bit LE)
    }

    [Fact]
    public void XXX_EndMarkerFixed4Bytes()
    {
        // XXX end marker is always 0x7F 0x7F 0x02 0x14
        var end = XxxMovementEncoder.EncodeEnd();
        
        end.Should().Equal(XxxSpec.EndSequence);
    }

    [Fact]
    public async Task XXX_VariantAAndBHeadersDifferent()
    {
        // XXX has two header variants with different structures
        // Writer implements variant B (with "XXX" signature)
        // Variant A has different header layout (no "XXX" signature)
        var project = CreateSimpleProject();
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        
        // Should be variant B with XXX signature
        bytes[0xA0].Should().Be((byte)'X');
        bytes[0xA1].Should().Be((byte)'X');
        bytes[0xA2].Should().Be((byte)'X');
    }

    [Fact]
    public async Task XXX_ColorTableRGBOnly()
    {
        // XXX color table stores only RGB (3 bytes per color + 1 reserved)
        // No thread brand, code, or name metadata preserved
        var project = CreateSimpleProject();
        project.ThreadPalette[0] = new ThreadColor(255, 128, 64, "Madeira", "1001", "Rayon Red");
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        
        // Find first color entry (after end marker)
        int endPos = -1;
        for (int i = 0; i < bytes.Length - 3; i++)
        {
            if (bytes[i] == 0x7F && bytes[i + 1] == 0x7F && bytes[i + 2] == 0x02 && bytes[i + 3] == 0x14)
            {
                endPos = i + 4;
                break;
            }
        }
        
        // First color entry: 0x00 R G B
        bytes[endPos].Should().Be(0x00); // reserved
        bytes[endPos + 1].Should().Be(255); // R
        bytes[endPos + 2].Should().Be(128); // G
        bytes[endPos + 3].Should().Be(64); // B
        
        // "Madeira", "1001", "Rayon Red" are NOT stored
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

    private AtlasProject CreateProjectWithColors(int colorCount)
    {
        var project = new AtlasProject { Name = "ManyColors" };
        for (int i = 0; i < colorCount; i++)
        {
            project.ThreadPalette.Add(new ThreadColor(
                (byte)(i * 10 % 256), 
                (byte)(i * 20 % 256), 
                (byte)(i * 30 % 256),
                "Test", 
                $"{i:D3}", 
                $"Color {i}"));
        }

        var shape = new ShapeObject
        {
            Name = "Shape",
            Vertices = new List<Point> { new(0, 0), new(1000, 0) },
            IsClosed = false,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape.StitchParams.Density = 4000;
        project.Objects.Add(shape);

        return project;
    }
}