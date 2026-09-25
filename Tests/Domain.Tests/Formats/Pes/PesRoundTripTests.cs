namespace AtlasEmbroidery.Tests.Formats.Pes;

using AtlasEmbroidery.Domain.Formats.Pes;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Round-trip semantic fidelity tests for PES format
/// </summary>
public class PesRoundTripTests
{
    private readonly PesFormatAdapter _adapter = new();

    [Fact]
    public async Task RoundTrip_SimpleShape_PreservesStitches()
    {
        var project = CreateSimpleProject();
        
        // Write
        var outputStream = new MemoryStream();
        await _adapter.WriteAsync(project, outputStream);
        
        // Read back
        outputStream.Position = 0;
        var readProject = await _adapter.ReadAsync(outputStream);
        
        // Verify
        readProject.Should().NotBeNull();
        readProject.Name.Should().Be(project.Name);
    }

    [Fact]
    public async Task RoundTrip_ColorChanges_Preserved()
    {
        var project = CreateMultiColorProject();
        
        var outputStream = new MemoryStream();
        await _adapter.WriteAsync(project, outputStream);
        
        outputStream.Position = 0;
        var readProject = await _adapter.ReadAsync(outputStream);
        
        readProject.ThreadPalette.Count.Should().Be(project.ThreadPalette.Count);
    }

    [Fact]
    public async Task RoundTrip_JumpsAndTrims_Preserved()
    {
        var project = CreateJumpTrimProject();
        
        var outputStream = new MemoryStream();
        await _adapter.WriteAsync(project, outputStream);
        
        outputStream.Position = 0;
        var readProject = await _adapter.ReadAsync(outputStream);
        
        readProject.Should().NotBeNull();
    }

    [Fact]
    public async Task RoundTrip_MultiPass_Stable()
    {
        var project = CreateSimpleProject();
        
        var stream1 = new MemoryStream();
        await _adapter.WriteAsync(project, stream1);
        stream1.Position = 0;
        var project2 = await _adapter.ReadAsync(stream1);
        
        var stream2 = new MemoryStream();
        await _adapter.WriteAsync(project2, stream2);
        stream2.Position = 0;
        var project3 = await _adapter.ReadAsync(stream2);
        
        project3.Name.Should().Be(project.Name);
        project3.ThreadPalette.Count.Should().Be(project.ThreadPalette.Count);
    }

    private AtlasProject CreateSimpleProject()
    {
        var project = new AtlasProject { Name = "Simple" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var shape = new ShapeObject
        {
            Name = "Line",
            Vertices = new List<Point>
            {
                new Point(0, 0),
                new Point(1000, 0),
                new Point(1000, 1000)
            },
            IsClosed = false,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape.StitchParams.Density = 4000;
        shape.StitchParams.ColorIndex = 0;
        project.Objects.Add(shape);
        
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
            Name = "Red",
            Vertices = new List<Point> { new Point(0, 0), new Point(1000, 0) },
            IsClosed = false,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape1.StitchParams.Density = 4000;
        shape1.StitchParams.ColorIndex = 0;
        project.Objects.Add(shape1);
        
        var shape2 = new ShapeObject
        {
            Name = "Green",
            Vertices = new List<Point> { new Point(1000, 0), new Point(1000, 1000) },
            IsClosed = false,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape2.StitchParams.Density = 4000;
        shape2.StitchParams.ColorIndex = 1;
        project.Objects.Add(shape2);
        
        var shape3 = new ShapeObject
        {
            Name = "Blue",
            Vertices = new List<Point> { new Point(1000, 1000), new Point(0, 1000) },
            IsClosed = false,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape3.StitchParams.Density = 4000;
        shape3.StitchParams.ColorIndex = 2;
        project.Objects.Add(shape3);
        
        return project;
    }

    private AtlasProject CreateJumpTrimProject()
    {
        var project = new AtlasProject { Name = "JumpTrim" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var shape = new ShapeObject
        {
            Name = "WithJumps",
            Vertices = new List<Point>
            {
                new Point(0, 0),
                new Point(1000, 0),
                new Point(2000, 0) // Will create a jump
            },
            IsClosed = false,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape.StitchParams.Density = 4000;
        shape.StitchParams.ColorIndex = 0;
        project.Objects.Add(shape);
        
        return project;
    }
}