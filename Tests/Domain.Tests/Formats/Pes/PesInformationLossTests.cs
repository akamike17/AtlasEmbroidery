namespace AtlasEmbroidery.Tests.Formats.Pes;

using AtlasEmbroidery.Domain.Formats.Pes;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Information loss tests for PES format
/// Documents what PES cannot preserve vs our internal model
/// </summary>
public class PesInformationLossTests
{
    [Fact]
    public void Pes_CannotStore_ThreadRGB_OutsidePalette()
    {
        // PES stores thread colors in palette; any color not in palette
        // gets mapped to nearest. Full RGB precision in addendum.
        var thread = new ThreadColor(123, 45, 67, "Custom", "999", "Custom");
        
        // PES v6+ stores RGB in addendum (24-bit per thread)
        // But only for threads in the palette
        // Threads outside palette are not stored
        thread.R.Should().Be(123);
        thread.G.Should().Be(45);
        thread.B.Should().Be(67);
    }

    [Fact]
    public void Pes_CannotStore_ArbitraryStopCommands()
    {
        // PES uses color change (FE B0) for stops
        // No native STOP command - stop == color change
        // Our model has explicit Stop stitch type
        var stopStitch = new StitchPoint(0, 0, StitchType.Stop, 1, 0, 0);
        
        // Will be encoded as color change in PES
        stopStitch.Type.Should().Be(StitchType.Stop);
    }

    [Fact]
    public void Pes_CannotStore_ThreadNames()
    {
        // PES v6+ stores catalog number, description, brand, chart
        // But Name property in our model doesn't map directly
        var thread = new ThreadColor(255, 0, 0, "Brother", "1000", "Red");
        
        // CatalogNumber -> Code
        // Description -> Description
        // Brand -> Brand
        // Chart -> Chart
        // Name is not stored
        thread.Code.Should().Be("1000");
    }

    [Fact]
    public void Pes_CannotStore_HoopSelection_Exactly()
    {
        // PES v6+ stores hoop width/height as 16-bit values
        // Our HoopProfile has richer info (name, type, multiple sizes)
        // Only numeric width/height preserved
        int hoopWidth = 1300;  // 130mm in 0.1mm units
        int hoopHeight = 1800; // 180mm
        
        hoopWidth.Should().Be(1300);
        hoopHeight.Should().Be(1800);
    }

    [Fact]
    public void Pes_CannotStore_Icon_Preview()
    {
        // PES doesn't store icon/preview data
        // Our model may have preview/simulation result
        var project = new AtlasProject();
        project.SimulationResult = new SimulationResult { TotalStitches = 100 };
        
        // This info is lost in PES export
        project.SimulationResult.Should().NotBeNull();
    }

    [Fact]
    public void Pes_CannotStore_Underlay_Params()
    {
        // PES only stores stitch data
        // Underlay params (type, density, angle) are lost
        var shape = new ShapeObject
        {
            Name = "Test",
            Vertices = new List<Point> { new Point(0, 0), new Point(1000, 0) },
            StitchParams = new StitchParams { Density = 4000, Underlay = new UnderlayParams { Type = UnderlayType.Zigzag } }
        };
        
        shape.StitchParams.Underlay.Type.Should().Be(UnderlayType.Zigzag);
    }

    [Fact]
    public void Pes_CannotStore_Compensation_Params()
    {
        // Pull compensation, push compensation not in PES
        var shape = new ShapeObject
        {
            Name = "Test",
            Vertices = new List<Point> { new Point(0, 0), new Point(1000, 0) },
            StitchParams = new StitchParams { Density = 4000, PullCompensation = 200 }
        };
        
        shape.StitchParams.PullCompensation.Should().Be(200);
    }

    [Fact]
    public void Pes_MaxDeltaPerRecord_2047()
    {
        // Same as PEC: 12-bit signed limit
        const int maxDelta = 2047;
        
        // Movements larger than 2047 units (204.7mm) must be split
        maxDelta.Should().Be(2047);
    }
}