namespace AtlasEmbroidery.Domain.Tests.Validation;

using AtlasEmbroidery.Domain.Validation;
using AtlasEmbroidery.Domain.Models;
using FluentAssertions;
using Xunit;

public class AtlasValidatorTests
{
    private readonly AtlasValidator _validator = new();

    [Fact]
    public void AtlasValidator_EmptyProject_ReturnsWarning()
    {
        var project = new AtlasProject { Name = "Empty" };
        
        var result = _validator.Validate(project);
        
        result.OverallStatus.Should().Be(ValidationStatus.Warning);
        result.Issues.Should().Contain(i => i.Code == "GEOM_EMPTY");
    }

    [Fact]
    public void AtlasValidator_DesignExceedsHoop_ReturnsCritical()
    {
        var project = new AtlasProject 
        { 
            Name = "Large Design",
            SelectedHoop = HoopProfile.CreateStandard("Small Hoop", 100000, 100000, 50000, 50000)
        };

        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 200000, 200000), "Large Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.RecalculateBounds();
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.OverallStatus.Should().Be(ValidationStatus.Fail);
        result.Issues.Should().Contain(i => i.Code == "GEOM_HOOP_FIT" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public void AtlasValidator_DesignFitsHoop_ReturnsPass()
    {
        var project = new AtlasProject 
        { 
            Name = "Fitting Design",
            SelectedHoop = HoopProfile.CreateStandard("Large Hoop", 400000, 400000, 350000, 350000)
        };

        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 100000, 100000), "Small Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.RecalculateBounds();
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.OverallStatus.Should().Be(ValidationStatus.Pass);
    }

    [Fact]
    public void AtlasValidator_StitchLengthTooShort_ReturnsCritical()
    {
        var project = new AtlasProject { Name = "Short Stitch" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.StitchParams.MinStitchLength = 10; // 0.01mm - below absolute minimum
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "STITCH_LEN_MIN" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public void AtlasValidator_StitchLengthTooLong_ReturnsWarning()
    {
        var project = new AtlasProject { Name = "Long Stitch" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.StitchParams.MaxStitchLength = 150000; // 15mm - above DST limit
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "STITCH_LEN_MAX" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void AtlasValidator_DensityTooLow_ReturnsWarning()
    {
        var project = new AtlasProject { Name = "Low Density" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape.StitchParams.Density = 50; // 0.05mm - very sparse
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "STITCH_DENSITY_LOW");
    }

    [Fact]
    public void AtlasValidator_DensityTooHigh_ReturnsWarning()
    {
        var project = new AtlasProject { Name = "High Density" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape.StitchParams.Density = 1500; // 0.15mm - very dense
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "STITCH_DENSITY_HIGH");
    }

    [Fact]
    public void AtlasValidator_SatinColumnTooNarrow_ReturnsCritical()
    {
        var project = new AtlasProject { Name = "Narrow Satin" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Satin);
        shape.StitchParams.Satin = new SatinParams { ColumnWidth = 500 }; // 0.5mm
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "SATIN_WIDTH_MIN" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public void AtlasValidator_SatinColumnTooWide_ReturnsWarning()
    {
        var project = new AtlasProject { Name = "Wide Satin" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Satin);
        shape.StitchParams.Satin = new SatinParams { ColumnWidth = 150000 }; // 15mm
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "SATIN_WIDTH_MAX" && i.Severity == ValidationSeverity.Warning);
    }

    [Fact]
    public void AtlasValidator_PullCompensationLow_ReturnsWarning()
    {
        var project = new AtlasProject { Name = "Low Pull Comp" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape.StitchParams.PullCompensation = 50; // 0.05mm
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "REG_PULL_COMP_LOW");
    }

    [Fact]
    public void AtlasValidator_LargeTatamiArea_ReturnsWarning()
    {
        var project = new AtlasProject { Name = "Large Tatami" };
        
        // 500mm x 500mm = 2500 cm² - exceeds 100 cm² threshold
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 5000000, 5000000), "Huge Tatami");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape.RecalculateBounds();
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "REG_LARGE_TATAMI");
    }

    [Fact]
    public void AtlasValidator_MoireAngles_ReturnsWarning()
    {
        var project = new AtlasProject { Name = "Moire Test" };
        
        var shape1 = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect 1");
        shape1.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape1.StitchParams.Angle = 0; // 0 degrees
        shape1.StitchParams.ColorIndex = 0;
        shape1.RecalculateBounds();
        project.Objects.Add(shape1);

        var shape2 = ShapeObject.CreateRectangle(new Rectangle(15000, 0, 10000, 10000), "Rect 2");
        shape2.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape2.StitchParams.Angle = 300; // 30 degrees - moire risk with 0 deg
        shape2.StitchParams.ColorIndex = 0; // Same color
        shape2.RecalculateBounds();
        project.Objects.Add(shape2);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "REG_ANGLE_MOIRE");
    }

    [Fact]
    public void AtlasValidator_MetallicThread_RequiresMetallicNeedle()
    {
        var project = new AtlasProject { Name = "Metallic Thread" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.RecalculateBounds();
        project.Objects.Add(shape);

        project.WorkProfile = new WorkProfile
        {
            Thread = new MaterialProfile
            {
                Name = "Metallic 40wt",
                ThreadMaterial = ThreadMaterial.Metallic,
                ThreadWeight = 40
            },
            Needle = new MaterialProfile
            {
                Name = "Regular 75/11",
                NeedlePoint = NeedlePoint.Sharp,
                NeedleSize = 75
            }
        };

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "MAT_METALLIC_NEEDLE");
    }

    [Fact]
    public void AtlasValidator_TowelFabric_RequiresTopping()
    {
        var project = new AtlasProject { Name = "Towel Fabric" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.RecalculateBounds();
        project.Objects.Add(shape);

        project.WorkProfile = new WorkProfile
        {
            Fabric = new MaterialProfile
            {
                Name = "Terry Cloth",
                FabricType = FabricType.Towel,
                WeightGsm = 400
            },
            Thread = new MaterialProfile { Name = "Polyester 40wt", ThreadMaterial = ThreadMaterial.Polyester },
            Needle = new MaterialProfile { Name = "75/11 Sharp", NeedlePoint = NeedlePoint.Sharp, NeedleSize = 75 },
            Stabilizer = new MaterialProfile { Name = "Tear Away", StabilizerType = StabilizerType.TearAway },
            Topping = null // Missing topping
        };

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "MAT_TOWEL_TOPPING");
    }

    [Fact]
    public void AtlasValidator_KnitFabric_RequiresCutAwayStabilizer()
    {
        var project = new AtlasProject { Name = "Knit Fabric" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.RecalculateBounds();
        project.Objects.Add(shape);

        project.WorkProfile = new WorkProfile
        {
            Fabric = new MaterialProfile
            {
                Name = "Jersey",
                FabricType = FabricType.Jersey
            },
            Stabilizer = new MaterialProfile
            {
                Name = "Tear Away",
                StabilizerType = StabilizerType.TearAway // Should be CutAway
            }
        };

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "MAT_STABILIZER_KNIT");
    }

    [Fact]
    public void AtlasValidator_ElasticFabric_RequiresHigherPullComp()
    {
        var project = new AtlasProject { Name = "Elastic Fabric" };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape.StitchParams.PullCompensation = 150; // Below MinPullCompensation * 2 (200)
        shape.RecalculateBounds();
        project.Objects.Add(shape);

        project.WorkProfile = new WorkProfile
        {
            Fabric = new MaterialProfile
            {
                Name = "Spandex Blend",
                FabricType = FabricType.Jersey,
                Elasticity = 0.5 // High elasticity
            }
        };

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "MAT_ELASTIC_PULL");
    }

    [Fact]
    public void AtlasValidator_SingleNeedleMultipleColors_ReturnsWarning()
    {
        var project = new AtlasProject { Name = "Single Needle Multi Color" };
        project.TargetMachine = MachineProfile.CreateBrotherDefault();
        project.TargetMachine.NeedleCount = 1;

        var shape1 = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect 1");
        shape1.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape1.StitchParams.ColorIndex = 0;
        shape1.RecalculateBounds();
        project.Objects.Add(shape1);

        var shape2 = ShapeObject.CreateRectangle(new Rectangle(15000, 0, 10000, 10000), "Rect 2");
        shape2.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape2.StitchParams.ColorIndex = 1;
        shape2.RecalculateBounds();
        project.Objects.Add(shape2);

        project.ThreadPalette.Add(ThreadColor.Red);
        project.ThreadPalette.Add(ThreadColor.Green);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "MACH_SINGLE_NEEDLE_COLORS");
    }

    [Fact]
    public void AtlasValidator_ExceedsMachineColorChanges_ReturnsCritical()
    {
        var project = new AtlasProject { Name = "Too Many Colors" };
        project.TargetMachine = MachineProfile.CreateBrotherDefault();
        project.TargetMachine.MaxColorChanges = 5;

        for (int i = 0; i < 10; i++)
        {
            var shape = ShapeObject.CreateRectangle(new Rectangle(i * 15000, 0, 10000, 10000), $"Rect {i}");
            shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
            shape.StitchParams.ColorIndex = i;
            shape.RecalculateBounds();
            project.Objects.Add(shape);
            project.ThreadPalette.Add(new ThreadColor((byte)(i * 25), (byte)(i * 10), (byte)(255 - i * 10), "Test", i.ToString("D3"), $"Color {i}"));
        }

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "MACH_COLOR_CHANGES_EXCEED" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public void AtlasValidator_DesignFitsMachineArea_ReturnsPass()
    {
        var project = new AtlasProject { Name = "Machine Fit" };
        project.TargetMachine = MachineProfile.CreateTajimaDefault();
        project.TargetMachine.MaxWidth = 400000;
        project.TargetMachine.MaxHeight = 400000;

        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 100000, 100000), "Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.RecalculateBounds();
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.OverallStatus.Should().Be(ValidationStatus.Pass);
    }

    [Fact]
    public void AtlasValidator_DesignExceedsMachineArea_ReturnsCritical()
    {
        var project = new AtlasProject { Name = "Machine Exceed" };
        project.TargetMachine = MachineProfile.CreateTajimaDefault();
        project.TargetMachine.MaxWidth = 100000;
        project.TargetMachine.MaxHeight = 100000;

        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 200000, 200000), "Large Rect");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.RecalculateBounds();
        project.Objects.Add(shape);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "GEOM_MACHINE_AREA" && i.Severity == ValidationSeverity.Critical);
    }

    [Fact]
    public void AtlasValidator_OverlappingObjects_ReturnsWarning()
    {
        var project = new AtlasProject { Name = "Overlap Test" };
        
        var shape1 = ShapeObject.CreateRectangle(new Rectangle(0, 0, 100000, 100000), "Rect 1");
        shape1.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape1.RecalculateBounds();
        project.Objects.Add(shape1);

        var shape2 = ShapeObject.CreateRectangle(new Rectangle(50000, 50000, 100000, 100000), "Rect 2");
        shape2.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape2.RecalculateBounds();
        project.Objects.Add(shape2);

        var result = _validator.Validate(project);
        
        result.Issues.Should().Contain(i => i.Code == "GEOM_COLLISION");
    }
}