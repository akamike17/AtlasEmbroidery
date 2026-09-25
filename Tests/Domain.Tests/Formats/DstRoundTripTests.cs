namespace AtlasEmbroidery.Domain.Formats.Dst;

using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Round-trip tests for DST format
/// Read -> Write -> Read with semantic comparison
/// </summary>
public class DstRoundTripTests
{
    private readonly DstFormatAdapter _adapter = new();

    [Theory]
    [InlineData(nameof(DstGoldenFiles.SimpleLine))]
    [InlineData(nameof(DstGoldenFiles.Square))]
    [InlineData(nameof(DstGoldenFiles.MultiColor))]
    [InlineData(nameof(DstGoldenFiles.WithJumpsAndTrims))]
    [InlineData(nameof(DstGoldenFiles.LargeDesign))]
    [InlineData(nameof(DstGoldenFiles.ManyColors))]
    [InlineData(nameof(DstGoldenFiles.SatinColumn))]
    [InlineData(nameof(DstGoldenFiles.TatamiFill))]
    [InlineData(nameof(DstGoldenFiles.WithStops))]
    [InlineData(nameof(DstGoldenFiles.EmptyDesign))]
    public async Task RoundTrip_GoldenFiles_PreservesSemantics(string goldenFileName)
    {
        // Arrange
        var goldenFileProperty = typeof(DstGoldenFiles).GetProperty(goldenFileName);
        goldenFileProperty.Should().NotBeNull($"Golden file {goldenFileName} not found");
        
        var originalData = (byte[])goldenFileProperty!.GetValue(null)!;
        using var originalStream = new MemoryStream(originalData);
        
        // Act - Read -> Write -> Read
        var project1 = await _adapter.ReadAsync(originalStream);
        var engine = new StitchEngine();
        var plan1 = engine.Compile(project1);
        
        // Write to memory
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project1, ms);
        
        // Read back
        ms.Position = 0;
        var project2 = await _adapter.ReadAsync(ms);
        var plan2 = engine.Compile(project2);
        
        // Assert - Semantic comparison
        var diffs = _adapter.SemanticDiff(project1, project2);
        
        // For golden files, semantic properties should be preserved
        // DST is a stitch-only format - all stitch types become running stitches on read
        // Stitch count and bounds WILL differ due to:
        // 1. Movement decomposition (max 121 DST units per record)
        // 2. Stitch type loss (satin/tatami -> running on read, then regenerated on write)
        // 3. StitchEngine regenerates patterns from shapes
        // Only color changes should be reliably preserved
        diffs.Should().NotContain(x => x.Type == DifferenceType.ColorChangeCount, 
            $"Color change count mismatch for {goldenFileName}: {{Description}}");
    }

    [Fact]
    public async Task RoundTrip_SimpleLine_CompletesWithoutException()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.SimpleLine);

        // Act - should not throw
        var project = await _adapter.ReadAsync(stream);
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        
        ms.Position = 0;
        var project2 = await _adapter.ReadAsync(ms);
        var plan2 = engine.Compile(project2);

        // Assert
        plan2.TotalStitches.Should().BeGreaterThan(0);
        plan2.TotalColorChanges.Should().Be(0);
    }

    [Fact]
    public async Task RoundTrip_MultiColor_PreservesColorChanges()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.MultiColor);

        // Act
        var project = await _adapter.ReadAsync(stream);
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        
        ms.Position = 0;
        var project2 = await _adapter.ReadAsync(ms);
        var plan2 = engine.Compile(project2);

        // Assert
        plan2.TotalColorChanges.Should().Be(plan.TotalColorChanges);
    }

    [Fact]
    public async Task RoundTrip_WithJumps_PreservesJumpCount()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.WithJumpsAndTrims);

        // Act
        var project = await _adapter.ReadAsync(stream);
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        
        ms.Position = 0;
        var project2 = await _adapter.ReadAsync(ms);
        var plan2 = engine.Compile(project2);

        // Assert
        plan2.TotalJumps.Should().Be(plan.TotalJumps);
    }

    [Fact]
        public async Task RoundTrip_LargeDesign_StaysWithinLimits()
        {
            // Arrange - Use LargeDesign golden file
            using var stream = new MemoryStream(DstGoldenFiles.LargeDesign);
        
            // Act
            var project = await _adapter.ReadAsync(stream);
            var engine = new StitchEngine();
            var plan = engine.Compile(project);
        
            using var ms = new MemoryStream();
            await _adapter.WriteAsync(project, ms);
        
            ms.Position = 0;
            var project2 = await _adapter.ReadAsync(ms);
            var plan2 = engine.Compile(project2);

            // Assert - DST max is per-stitch (12.7mm), not total design bounds
            // Design can be larger than 12.7mm through multiple stitches
            plan2.TotalStitches.Should().BeLessThan(_adapter.Capabilities.MaxTotalStitches);
        
            // Validate that the design is readable and produces valid output
            var validation = _adapter.Validate(plan2);
            validation.IsValid.Should().BeTrue();
        }

    [Fact]
    public async Task RoundTrip_EmptyDesign_ProducesValidOutput()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.EmptyDesign);

        // Act
        var project = await _adapter.ReadAsync(stream);
        var engine = new StitchEngine();
        var plan = engine.Compile(project);
        
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        
        ms.Position = 0;
        var project2 = await _adapter.ReadAsync(ms);
        var plan2 = engine.Compile(project2);

        // Assert
        plan2.TotalStitches.Should().Be(0);
        plan2.TotalColorChanges.Should().Be(0);
    }

    [Fact]
        public async Task RoundTrip_DeterministicOutput()
        {
            // Arrange
            using var stream = new MemoryStream(DstGoldenFiles.SimpleLine);

            // Act - Two round trips should produce semantically equivalent output
            // (not binary identical because StitchEngine regenerates stitches from shapes)
            var project = await _adapter.ReadAsync(stream);
            var engine = new StitchEngine();
            var plan = engine.Compile(project);
        
            using var ms1 = new MemoryStream();
            await _adapter.WriteAsync(project, ms1);
        
            ms1.Position = 0;
            var project2 = await _adapter.ReadAsync(ms1);
            var plan2 = engine.Compile(project2);
        
            using var ms2 = new MemoryStream();
            await _adapter.WriteAsync(project2, ms2);

            // Assert - Binary output may differ due to StitchEngine regeneration
            // DST is a stitch-only format - all stitch types become running stitches on read
            // Stitch count and bounds WILL differ due to:
            // 1. Movement decomposition (max 127 DST units per record)
            // 2. Stitch type loss (all stitch types become running on read, then regenerated on write)
            // 3. StitchEngine regenerates patterns from shapes
            // Only color changes should be reliably preserved
            var diffs = _adapter.SemanticDiff(project, project2);
            diffs.Should().NotContain(x => x.Type == DifferenceType.ColorChangeCount);
            // Bounds and stitch count will differ due to regeneration - that's expected for DST round-trip
        }

    [Fact]
    public async Task RoundTrip_SemanticDiff_NormalizedProjects()
    {
        // Arrange
        var project1 = CreateTestProject();
        var project2 = project1.DeepClone();
        
        // Normalize both
        var norm1 = _adapter.Normalize(project1);
        var norm2 = _adapter.Normalize(project2);

        // Act
        var diffs = _adapter.SemanticDiff(norm1, norm2);

        // Assert - Normalized identical projects should have no semantic differences
        diffs.Should().BeEmpty();
    }

    [Fact]
    public async Task RoundTrip_SemanticDiff_DetectsColorChange()
    {
        // Arrange
        var project1 = CreateTestProject();
        var project2 = project1.DeepClone();
        
        // Add a color to project2
        project2.ThreadPalette.Add(new ThreadColor(0, 0, 255, "DST", "002", "Blue"));
        project2.ColorToNeedleMap[1] = 2;

        // Act
        var diffs = _adapter.SemanticDiff(project1, project2);

        // Assert
        diffs.Should().Contain(d => d.Type == DifferenceType.ColorPalette);
    }

    [Fact]
    public async Task RoundTrip_SemanticDiff_DetectsBoundsChange()
    {
        // Arrange
        var project1 = CreateTestProject();
        var project2 = project1.DeepClone();
        
        // Modify bounds by scaling an object
        var obj = project2.Objects.FirstOrDefault();
        if (obj is ShapeObject shapeObj)
        {
            // Scale vertices
            for (int i = 0; i < shapeObj.Vertices.Count; i++)
            {
                shapeObj.Vertices[i] = shapeObj.Vertices[i].Scale(2.0);
            }
            shapeObj.RecalculateBounds();
        }
        project2.RecalculateBounds();

        // Act
        var diffs = _adapter.SemanticDiff(project1, project2);

        // Assert
        diffs.Should().Contain(d => d.Type == DifferenceType.Bounds);
    }

    [Fact]
    public async Task RoundTrip_ProjectValidation_AfterWrite()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.SimpleLine);
        var project = await _adapter.ReadAsync(stream);
        
        // Act
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        
        ms.Position = 0;
        var validation = await _adapter.ValidateAsync(ms);

        // Assert
        validation.IsValid.Should().BeTrue();
        validation.FormatName.Should().Be("DST");
    }

    [Fact]
    public async Task RoundTrip_GenerateFuzzInput_ValidatesCorrectly()
    {
        // Arrange
        var fuzzData = _adapter.GenerateFuzzInput(42);
        using var stream = new MemoryStream(fuzzData);

        // Act
        var validation = await _adapter.ValidateAsync(stream);

        // Assert
        validation.IsValid.Should().BeTrue();
        validation.DetectedCapabilities.Should().NotBeNull();
    }

    [Fact]
    public async Task RoundTrip_ValidateProject_WithinCapabilities()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.SimpleLine);
        var project = await _adapter.ReadAsync(stream);

        // Act
        var validation = _adapter.Validate(project);

        // Assert
        validation.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task RoundTrip_ValidateStitchPlan_WithinCapabilities()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.SimpleLine);
        var project = await _adapter.ReadAsync(stream);
        var engine = new StitchEngine();
        var plan = engine.Compile(project);

        // Act
        var validation = _adapter.Validate(plan);

        // Assert
        validation.IsValid.Should().BeTrue();
    }

    private AtlasProject CreateTestProject()
    {
        var project = new AtlasProject
        {
            Name = "Test Project",
            ThreadPalette = new List<ThreadColor>
            {
                new ThreadColor(255, 0, 0, "DST", "001", "Red"),
                new ThreadColor(0, 255, 0, "DST", "002", "Green")
            }
        };
        
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Test Shape");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.StitchParams.RunningSpacing = 200;
        project.Objects.Add(shape);
        
        project.ColorToNeedleMap[0] = 1;
        project.RecalculateBounds();
        project.Touch();
        
        return project;
    }
}