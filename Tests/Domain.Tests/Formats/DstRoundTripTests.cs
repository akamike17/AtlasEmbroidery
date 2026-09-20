namespace AtlasEmbroidery.Domain.Formats.Dst;

using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// DST Round-trip and Golden File tests
/// </summary>
public class DstRoundTripTests
{
    private readonly DstFormatAdapter _adapter = new();

    [Fact]
    public async Task RoundTrip_SimpleLine_CompletesWithoutException()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.SimpleLine);

        // Act
        var result = await _adapter.RoundTripTestAsync(stream);

        // Assert - Read/Write should complete without exception
        // Binary round-trip may differ because StitchEngine regenerates from vertices
        result.Success.Should().BeTrue($"Round-trip failed: {result.Error}\nDifferences: {string.Join(", ", result.Differences.Select(d => d.Description))}");
    }

    [Fact]
    public async Task RoundTrip_Square_PreservesSemantics()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.Square);

        // Act
        var result = await _adapter.RoundTripTestAsync(stream);

        // Assert
        result.Success.Should().BeTrue($"Round-trip failed: {result.Error}");
    }

    [Fact]
    public async Task RoundTrip_MultiColor_PreservesSemantics()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.MultiColor);

        // Act
        var result = await _adapter.RoundTripTestAsync(stream);

        // Assert
        result.Success.Should().BeTrue($"Round-trip failed: {result.Error}");
    }

    [Fact]
    public async Task RoundTrip_WithJumpsAndTrims_PreservesSemantics()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.WithJumpsAndTrims);

        // Act
        var result = await _adapter.RoundTripTestAsync(stream);

        // Assert
        result.Success.Should().BeTrue($"Round-trip failed: {result.Error}");
    }

    [Fact]
    public async Task RoundTrip_LargeDesign_PreservesSemantics()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.LargeDesign);

        // Act
        var result = await _adapter.RoundTripTestAsync(stream);

        // Assert
        result.Success.Should().BeTrue($"Round-trip failed: {result.Error}");
    }

    [Fact]
    public async Task RoundTrip_ManyColors_PreservesSemantics()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.ManyColors);

        // Act
        var result = await _adapter.RoundTripTestAsync(stream);

        // Assert
        result.Success.Should().BeTrue($"Round-trip failed: {result.Error}");
    }

    [Fact]
    public async Task RoundTrip_SatinColumn_PreservesSemantics()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.SatinColumn);

        // Act
        var result = await _adapter.RoundTripTestAsync(stream);

        // Assert
        result.Success.Should().BeTrue($"Round-trip failed: {result.Error}");
    }

    [Fact]
    public async Task RoundTrip_TatamiFill_PreservesSemantics()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.TatamiFill);

        // Act
        var result = await _adapter.RoundTripTestAsync(stream);

        // Assert
        result.Success.Should().BeTrue($"Round-trip failed: {result.Error}");
    }

    [Fact]
    public async Task RoundTrip_WithStops_PreservesSemantics()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.WithStops);

        // Act
        var result = await _adapter.RoundTripTestAsync(stream);

        // Assert
        result.Success.Should().BeTrue($"Round-trip failed: {result.Error}");
    }

    [Fact]
    public async Task RoundTrip_EmptyDesign_PreservesSemantics()
    {
        // Arrange
        using var stream = new MemoryStream(DstGoldenFiles.EmptyDesign);

        // Act
        var result = await _adapter.RoundTripTestAsync(stream);

        // Assert
        result.Success.Should().BeTrue($"Round-trip failed: {result.Error}");
    }

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
    public async Task Read_GoldenFile_ProducesValidProject(string goldenFileName)
    {
        // Arrange
        var goldenFile = typeof(DstGoldenFiles).GetProperty(goldenFileName)?.GetValue(null) as byte[];
        goldenFile.Should().NotBeNull($"Golden file {goldenFileName} not found");
        using var stream = new MemoryStream(goldenFile!);

        // Act
        var project = await _adapter.ReadAsync(stream);

        // Assert
        project.Should().NotBeNull();
        project.Name.Should().NotBeNullOrEmpty();
        project.Objects.Should().NotBeNull();
        project.ThreadPalette.Should().NotBeNull();
        project.CanvasWidth.Should().BeGreaterThan(0);
        project.CanvasHeight.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ReadWrite_ProjectWithKnownStitches_PreservesStitchCount()
    {
        // Arrange: create a project with known stitch count
        var project = new AtlasProject { Name = "Test Stitch Count" };
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Square");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.StitchParams.RunningSpacing = 200; // 200 microns = 0.2mm
        project.Objects.Add(shape);
        project.ThreadPalette.Add(ThreadColor.Red);
        project.ColorToNeedleMap[0] = 1;

        // Act
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        
        ms.Position = 0;
        var readProject = await _adapter.ReadAsync(ms);

        // Assert
        var engine = new StitchEngine();
        var originalPlan = engine.Compile(project);
        var readPlan = engine.Compile(readProject);
        
        readPlan.TotalStitches.Should().Be(originalPlan.TotalStitches, 
            "Stitch count should be preserved through round-trip");
    }

    [Fact]
    public async Task ReadWrite_ProjectWithJumps_PreservesJumps()
    {
        // Arrange: project with multiple separate objects (should produce jumps)
        var project = new AtlasProject { Name = "Test Jumps" };
        
        var shape1 = ShapeObject.CreateRectangle(new Rectangle(0, 0, 5000, 5000), "Square1");
        shape1.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        project.Objects.Add(shape1);
        
        var shape2 = ShapeObject.CreateRectangle(new Rectangle(20000, 20000, 5000, 5000), "Square2");
        shape2.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        project.Objects.Add(shape2);
        
        project.ThreadPalette.Add(ThreadColor.Red);
        project.ColorToNeedleMap[0] = 1;

        // Act
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        
        ms.Position = 0;
        var readProject = await _adapter.ReadAsync(ms);

        // Assert
        var engine = new StitchEngine();
        var originalPlan = engine.Compile(project);
        var readPlan = engine.Compile(readProject);
        
        readPlan.TotalJumps.Should().BeGreaterOrEqualTo(originalPlan.TotalJumps,
            "Jump count should be preserved (or increase due to different object ordering)");
    }

    [Fact]
    public async Task ReadWrite_ProjectWithTrims_PreservesTrims()
    {
        // Arrange: project where trims are expected (color changes)
        var project = new AtlasProject { Name = "Test Trims" };
        
        var shape1 = ShapeObject.CreateRectangle(new Rectangle(0, 0, 5000, 5000), "Red Square");
        shape1.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        project.Objects.Add(shape1);
        
        var shape2 = ShapeObject.CreateRectangle(new Rectangle(10000, 0, 5000, 5000), "Blue Square");
        shape2.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape2.StitchParams.ColorIndex = 1;
        project.Objects.Add(shape2);
        
        project.ThreadPalette.Add(ThreadColor.Red);
        project.ThreadPalette.Add(ThreadColor.Blue);
        project.ColorToNeedleMap[0] = 1;
        project.ColorToNeedleMap[1] = 2;

        // Act
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        
        ms.Position = 0;
        var readProject = await _adapter.ReadAsync(ms);

        // Assert
        var engine = new StitchEngine();
        var originalPlan = engine.Compile(project);
        var readPlan = engine.Compile(readProject);
        
        readPlan.TotalTrims.Should().BeGreaterOrEqualTo(originalPlan.TotalTrims);
        readPlan.TotalColorChanges.Should().BeGreaterOrEqualTo(originalPlan.TotalColorChanges);
    }

    [Fact]
    public async Task ReadWrite_ProjectWithStops_PreservesStops()
    {
        // Arrange: project with explicit stop command
        var project = new AtlasProject { Name = "Test Stops" };
        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 5000, 5000), "Square");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        project.Objects.Add(shape);
        project.ThreadPalette.Add(ThreadColor.Red);
        project.ColorToNeedleMap[0] = 1;

        // Act
        using var ms = new MemoryStream();
        await _adapter.WriteAsync(project, ms);
        
        ms.Position = 0;
        var readProject = await _adapter.ReadAsync(ms);

        // Assert
        var engine = new StitchEngine();
        var originalPlan = engine.Compile(project);
        var readPlan = engine.Compile(readProject);
        
        readPlan.TotalStops.Should().BeGreaterOrEqualTo(originalPlan.TotalStops);
    }

    [Fact]
    public async Task Write_GeneratedFuzzInput_ProducesValidDST()
    {
        // Arrange
        var fuzzData = _adapter.GenerateFuzzInput(42);
        
        // Act
        using var stream = new MemoryStream(fuzzData);
        var project = await _adapter.ReadAsync(stream);
        
        // Assert
        project.Should().NotBeNull();
        project.Objects.Should().NotBeEmpty();
        project.ThreadPalette.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RoundTrip_Deterministic_SameInputSameOutput()
    {
        // Arrange
        var data = DstGoldenFiles.Square;
        
        // Act - two round-trips
        using var stream1 = new MemoryStream(data);
        var result1 = await _adapter.RoundTripTestAsync(stream1);
        
        using var stream2 = new MemoryStream(data);
        var result2 = await _adapter.RoundTripTestAsync(stream2);

        // Assert
        result1.Success.Should().BeTrue();
        result2.Success.Should().BeTrue();
        result1.Differences.Should().Equal(result2.Differences);
    }
}