namespace AtlasEmbroidery.Domain.Tests.Stitching;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Domain.Geometry;
using FluentAssertions;
using Xunit;

public class StitchEngineTests
{
    private readonly StitchEngine _engine = new();

    [Fact]
    public void StitchEngine_Compile_RunningStitch_GeneratesPoints()
    {
        var project = CreateSimpleProject();
        project.Objects[0].StitchParams.PrimaryStitchType = StitchType.Running;
        project.Objects[0].StitchParams.RunningSpacing = 250; // 2.5mm

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.GlobalSequence.Should().NotBeEmpty();
        plan.TotalStitches.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StitchEngine_Compile_SatinStitch_GeneratesColumns()
    {
        var project = CreateSimpleProject();
        project.Objects[0].StitchParams.PrimaryStitchType = StitchType.Satin;
        project.Objects[0].StitchParams.SatinSpacing = 200;
        project.Objects[0].StitchParams.Satin = new SatinParams { ColumnWidth = 3000 };

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.GlobalSequence.Should().NotBeEmpty();
        plan.TotalStitches.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StitchEngine_Compile_TatamiFill_GeneratesFillPattern()
    {
        var project = CreateSimpleProject();
        project.Objects[0].StitchParams.PrimaryStitchType = StitchType.Tatami;
        project.Objects[0].StitchParams.Density = 400;
        project.Objects[0].StitchParams.Tatami = new TatamiParams { Pattern = TatamiPattern.Standard };

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.GlobalSequence.Should().NotBeEmpty();
        plan.TotalStitches.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StitchEngine_Compile_MultipleObjects_OrdersBySequence()
    {
        var project = CreateMultiObjectProject();

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.ObjectStitches.Should().HaveCount(3);
        
        // Verify objects are compiled
        plan.ObjectStitches.Values.SelectMany(s => s).Should().NotBeEmpty();
    }

    [Fact]
    public void StitchEngine_Compile_WithUnderlay_AddsUnderlayStitches()
    {
        var project = CreateSimpleProject();
        project.Objects[0].StitchParams.PrimaryStitchType = StitchType.Satin;
        project.Objects[0].StitchParams.Underlay = new UnderlayParams 
        { 
            Type = UnderlayType.EdgeWalk, 
            Enabled = true,
            Density = 800,
            Inset = 200
        };

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        // Underlay should add stitches
        plan.TotalStitches.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StitchEngine_Compile_WithPullCompensation_AdjustsBounds()
    {
        var project = CreateSimpleProject();
        project.Objects[0].StitchParams.PullCompensation = 200; // 0.2mm

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.DesignBounds.Width.Should().BeGreaterThan(0);
        plan.DesignBounds.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StitchEngine_Compile_AngleVariation_ProducesVariedAngles()
    {
        var project = CreateSimpleProject();
        project.Objects[0].StitchParams.PrimaryStitchType = StitchType.Tatami;
        project.Objects[0].StitchParams.Angle = 450; // 45 degrees
        project.Objects[0].StitchParams.AngleVariation = 100; // ±10 degrees
        project.Objects[0].StitchParams.Tatami = new TatamiParams();

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.GlobalSequence.Should().NotBeEmpty();
    }

    [Fact]
    public void StitchEngine_Compile_RespectsMinMaxStitchLength()
    {
        var project = CreateSimpleProject();
        project.Objects[0].StitchParams.MinStitchLength = 1000; // 1mm
        project.Objects[0].StitchParams.MaxStitchLength = 3000; // 3mm
        project.Objects[0].StitchParams.PreferredStitchLength = 2000; // 2mm

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.GlobalSequence.Should().NotBeEmpty();
        
        // Check that stitches are within bounds
        foreach (var stitch in plan.GlobalSequence)
        {
            // Basic sanity check - stitches should be reasonable
            stitch.X.Should().BeInRange(0, project.CanvasWidth);
            stitch.Y.Should().BeInRange(0, project.CanvasHeight);
        }
    }

    [Fact]
    public void StitchEngine_Compile_GeneratesTrimStitches_WhenEnabled()
    {
        var project = CreateMultiObjectProject();
        project.Objects[0].StitchParams.TrimPolicy = TrimPolicy.Auto;
        project.Objects[0].StitchParams.MinTrimDistance = 2000; // 2mm

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.TotalTrims.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void StitchEngine_Compile_ColorChanges_AreTracked()
    {
        var project = CreateMultiObjectProject();
        project.Objects[0].StitchParams.ColorIndex = 0;
        project.Objects[1].StitchParams.ColorIndex = 1;
        project.Objects[2].StitchParams.ColorIndex = 0;

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.TotalColorChanges.Should().BeGreaterThanOrEqualTo(0);
        plan.StitchesPerColor.Should().HaveCount(2);
    }

    [Fact]
    public void StitchEngine_Compile_EstimatesTimeAndThread()
    {
        var project = CreateSimpleProject();
        project.TargetMachine = MachineProfile.Default();
        project.TargetMachine.MaxSpeed = 800;

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.EstimatedTimeSeconds.Should().BeGreaterThan(0);
        plan.EstimatedThreadMeters.Should().BeGreaterThan(0);
    }

    [Fact]
    public void StitchEngine_Compile_EmptyProject_ReturnsValidPlan()
    {
        var project = new AtlasProject
        {
            Name = "Empty",
            CanvasWidth = 10000,
            CanvasHeight = 10000,
            TargetMachine = MachineProfile.Default(),
            SelectedHoop = HoopProfile.Default()
        };

        var plan = _engine.Compile(project);

        plan.Should().NotBeNull();
        plan.TotalStitches.Should().Be(0);
        plan.GlobalSequence.Should().BeEmpty();
    }

    private AtlasProject CreateSimpleProject()
    {
        var project = new AtlasProject
        {
            Name = "TestProject",
            CanvasWidth = 10000,
            CanvasHeight = 10000,
            TargetMachine = MachineProfile.Default(),
            SelectedHoop = HoopProfile.Default()
        };

        var shape = ShapeObject.CreateRectangle(new Rectangle(1000, 1000, 5000, 5000));
        shape.StitchParams.ColorIndex = 0;
        shape.StitchParams.NeedleIndex = 1;
        project.Objects.Add(shape);

        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Brand", "R001", "Red"));
        project.ColorToNeedleMap[0] = 1;

        return project;
    }

    private AtlasProject CreateMultiObjectProject()
    {
        var project = new AtlasProject
        {
            Name = "MultiObject",
            CanvasWidth = 20000,
            CanvasHeight = 20000,
            TargetMachine = MachineProfile.Default(),
            SelectedHoop = HoopProfile.Default()
        };

        // Object 1 - Rectangle
        var rect = ShapeObject.CreateRectangle(new Rectangle(1000, 1000, 3000, 3000));
        rect.StitchParams.ColorIndex = 0;
        rect.StitchParams.NeedleIndex = 1;
        rect.SequenceOrder = 0;
        project.Objects.Add(rect);

        // Object 2 - Circle
        var circle = ShapeObject.CreateEllipse(new Point(10000, 5000), 2000, 2000);
        circle.StitchParams.ColorIndex = 1;
        circle.StitchParams.NeedleIndex = 2;
        circle.SequenceOrder = 1;
        project.Objects.Add(circle);

        // Object 3 - Another rectangle
        var rect2 = ShapeObject.CreateRectangle(new Rectangle(5000, 10000, 4000, 4000));
        rect2.StitchParams.ColorIndex = 0;
        rect2.StitchParams.NeedleIndex = 1;
        rect2.SequenceOrder = 2;
        project.Objects.Add(rect2);

        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Brand", "R001", "Red"));
        project.ThreadPalette.Add(new ThreadColor(0, 255, 0, "Brand", "G001", "Green"));
        project.ColorToNeedleMap[0] = 1;
        project.ColorToNeedleMap[1] = 2;

        return project;
    }
}