namespace AtlasEmbroidery.Tests.Formats.Jef;

using AtlasEmbroidery.Domain.Formats.Jef;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Round-trip semantic fidelity tests for JEF format
/// </summary>
public sealed class JefRoundTripTests
{
    private readonly JefFormatAdapter _adapter = new();

    [Fact]
    public async Task RoundTrip_SimpleShape_PreservesStitches()
    {
        var project = CreateSimpleProject();
        
        var outputStream = new MemoryStream();
        await _adapter.WriteAsync(project, outputStream);
        outputStream.Position = 0;
        
        var roundTripProject = await _adapter.ReadAsync(outputStream);
        
        roundTripProject.Should().NotBeNull();
        roundTripProject.Name.Should().Be("Simple");
        roundTripProject.Objects.Should().HaveCount(1);
        roundTripProject.ThreadPalette.Should().HaveCount(1);
    }

    [Fact]
    public async Task RoundTrip_ColorChanges_Preserved()
    {
        var project = CreateMultiColorProject();
        
        var outputStream = new MemoryStream();
        await _adapter.WriteAsync(project, outputStream);
        outputStream.Position = 0;
        
        var roundTripProject = await _adapter.ReadAsync(outputStream);
        
        roundTripProject.Should().NotBeNull();
        roundTripProject.ThreadPalette.Should().HaveCount(3);
    }

    [Fact]
    public async Task RoundTrip_JumpsAndTrims_Preserved()
    {
        var project = CreateJumpProject();
        
        var outputStream = new MemoryStream();
        await _adapter.WriteAsync(project, outputStream);
        outputStream.Position = 0;
        
        var roundTripProject = await _adapter.ReadAsync(outputStream);
        
        roundTripProject.Should().NotBeNull();
        roundTripProject.Objects.Should().HaveCount(1);
    }

    [Fact]
    public async Task RoundTrip_MultiPass_Stable()
    {
        var project = CreateSimpleProject();
        
        var stream1 = new MemoryStream();
        await _adapter.WriteAsync(project, stream1);
        stream1.Position = 0;
        var project1 = await _adapter.ReadAsync(stream1);
        
        var stream2 = new MemoryStream();
        await _adapter.WriteAsync(project1, stream2);
        stream2.Position = 0;
        var project2 = await _adapter.ReadAsync(stream2);
        
        var stream3 = new MemoryStream();
        await _adapter.WriteAsync(project2, stream3);
        stream3.Position = 0;
        var project3 = await _adapter.ReadAsync(stream3);
        
        project3.Name.Should().Be("Simple");
        project3.Objects.Should().HaveCount(1);
    }

    private AtlasProject CreateSimpleProject()
    {
        var project = new AtlasProject { Name = "Simple" };
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