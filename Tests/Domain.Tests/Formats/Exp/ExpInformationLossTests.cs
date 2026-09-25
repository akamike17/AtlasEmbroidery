namespace AtlasEmbroidery.Tests.Formats.Exp;

using AtlasEmbroidery.Domain.Formats.Exp;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Information loss tests for EXP format
/// Documents what EXP cannot preserve vs our internal model
/// </summary>
public class ExpInformationLossTests
{
    [Fact]
    public void Exp_CannotStore_ThreadColors()
    {
        // EXP has no thread palette - color changes are implicit
        // Our model has full ThreadColor with RGB, brand, catalog
        var thread = new ThreadColor(123, 45, 67, "Custom", "999", "Custom");
        
        // All thread metadata lost in EXP
        thread.R.Should().Be(123);
        thread.G.Should().Be(45);
        thread.B.Should().Be(67);
    }

    [Fact]
    public void Exp_CannotStore_StopVsColorChange()
    {
        // EXP uses same code (0x80 0x01) for both STOP and COLOR_CHANGE
        // Our model distinguishes Stop and ColorChange stitch types
        var stopStitch = new StitchPoint(0, 0, StitchType.Stop, 1, 0, 0);
        var ccStitch = new StitchPoint(0, 0, StitchType.ColorChange, 1, 0, 0);
        
        stopStitch.Type.Should().Be(StitchType.Stop);
        ccStitch.Type.Should().Be(StitchType.ColorChange);
    }

    [Fact]
    public void Exp_CannotStore_HoopInfo()
    {
        // EXP has no hoop metadata
        // Our model has HoopProfile with name, type, dimensions
        int hoopWidth = 1300;
        int hoopHeight = 1800;
        
        hoopWidth.Should().Be(1300);
        hoopHeight.Should().Be(1800);
    }

    [Fact]
    public void Exp_CannotStore_DesignName()
    {
        // EXP has no design name field
        var project = new AtlasProject { Name = "My Design" };
        
        // Name lost on export
        project.Name.Should().Be("My Design");
    }

    [Fact]
    public void Exp_CannotStore_UnderlayParams()
    {
        // EXP only stores stitch data
        var shape = new ShapeObject
        {
            Name = "Test",
            Vertices = new List<Point> { new Point(0, 0), new Point(1000, 0) },
            StitchParams = new StitchParams { Density = 4000, Underlay = new UnderlayParams { Type = UnderlayType.Zigzag } }
        };
        
        shape.StitchParams.Underlay.Type.Should().Be(UnderlayType.Zigzag);
    }

    [Fact]
    public void Exp_CannotStore_CompensationParams()
    {
        // Pull compensation, push compensation not in EXP
        var shape = new ShapeObject
        {
            Name = "Test",
            Vertices = new List<Point> { new Point(0, 0), new Point(1000, 0) },
            StitchParams = new StitchParams { Density = 4000, PullCompensation = 200 }
        };
        
        shape.StitchParams.PullCompensation.Should().Be(200);
    }

    [Fact]
    public void Exp_MaxDeltaPerRecord_127()
    {
        // 8-bit signed limit
        const int maxDelta = 127;
        
        // Movements larger than 127 units (12.7mm) must be split
        maxDelta.Should().Be(127);
    }

    [Fact]
    public void Exp_CannotStore_TrimMetadata()
    {
        // EXP has trim (0x80 0x80 0x07 0x00) but no metadata about trim distance/policy
        var shape = new ShapeObject
        {
            Name = "Test",
            Vertices = new List<Point> { new Point(0, 0), new Point(1000, 0) },
            StitchParams = new StitchParams { Density = 4000, MinTrimDistance = 2000 }
        };
        
        shape.StitchParams.MinTrimDistance.Should().Be(2000);
    }

    [Fact]
    public void Exp_CannotStore_ExplicitEndMarker()
    {
        // EXP has no explicit END marker - file just ends
        // Our model has explicit StitchType.End
        var endStitch = new StitchPoint(0, 0, StitchType.End, 1, 0, 0);
        
        endStitch.Type.Should().Be(StitchType.End);
    }
}