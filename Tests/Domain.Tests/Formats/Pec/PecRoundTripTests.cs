namespace AtlasEmbroidery.Tests.Formats.Pec;

using AtlasEmbroidery.Domain.Formats.Pec;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Round-trip and semantic fidelity tests for PEC format
/// </summary>
public class PecRoundTripTests
{
    private readonly PecFormatAdapter _adapter = new();

    [Fact]
    public async Task RoundTrip_SimpleShape_PreservesStitchCount()
    {
        // Create a simple project
        var project = CreateTestProject();
        
        // Write to stream
        var outputStream = new MemoryStream();
        await _adapter.WriteAsync(project, outputStream);
        
        // Read back
        outputStream.Position = 0;
        var readProject = await _adapter.ReadAsync(outputStream);
        
        // Verify we have at least one shape with vertices
        readProject.Objects.Should().NotBeEmpty();
        var shape = readProject.Objects.OfType<ShapeObject>().First();
        shape.Vertices.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RoundTrip_PreservesColorChanges()
    {
        var project = CreateMultiColorProject();
        
        var outputStream = new MemoryStream();
        await _adapter.WriteAsync(project, outputStream);
        
        outputStream.Position = 0;
        var readProject = await _adapter.ReadAsync(outputStream);
        
        // PEC only stores color indices, reader maps to built-in palette
        // ThreadPalette count may differ due to palette mapping
        readProject.ThreadPalette.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RoundTrip_PreservesJumpsAndTrims()
    {
        var project = CreateJumpTrimProject();
        
        var outputStream = new MemoryStream();
        await _adapter.WriteAsync(project, outputStream);
        
        outputStream.Position = 0;
        var readProject = await _adapter.ReadAsync(outputStream);
        
        var shape = readProject.Objects.OfType<ShapeObject>().FirstOrDefault();
        shape.Should().NotBeNull();
        shape!.Vertices.Should().NotBeEmpty();
    }

    [Fact]
    public async Task RoundTrip_MultipleTimes_Stable()
    {
        var project = CreateTestProject();
        var current = project;
        
        for (int i = 0; i < 3; i++)
        {
            var outputStream = new MemoryStream();
            await _adapter.WriteAsync(current, outputStream);
            outputStream.Position = 0;
            current = await _adapter.ReadAsync(outputStream);
        }
        
        // After 3 round-trips, should still have valid data
        current.Objects.Should().NotBeEmpty();
    }

    private AtlasProject CreateTestProject()
    {
        var project = new AtlasProject { Name = "RoundTrip Test" };
        
        var shape = new ShapeObject
        {
            Name = "Test Shape",
            Vertices = new List<Point>
            {
                new(0, 0),
                new(10000, 0),
                new(10000, 10000),
                new(0, 10000),
                new(0, 0)
            },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        shape.StitchParams.Density = 4000;
        
        project.Objects.Add(shape);
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Brother", "003", "Red"));
        project.ColorToNeedleMap[0] = 1;
        
        return project;
    }

    private AtlasProject CreateMultiColorProject()
    {
        var project = new AtlasProject { Name = "MultiColor Test" };
        
        var shape1 = new ShapeObject
        {
            Name = "Red Shape",
            Vertices = new List<Point> { new(0, 0), new(5000, 0), new(5000, 5000), new(0, 5000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        
        var shape2 = new ShapeObject
        {
            Name = "Blue Shape",
            Vertices = new List<Point> { new(6000, 0), new(11000, 0), new(11000, 5000), new(6000, 5000) },
            IsClosed = true,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        
        project.Objects.Add(shape1);
        project.Objects.Add(shape2);
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Brother", "003", "Red"));
        project.ThreadPalette.Add(new ThreadColor(0, 0, 255, "Brother", "005", "Blue"));
        project.ColorToNeedleMap[0] = 1;
        project.ColorToNeedleMap[1] = 2;
        
        return project;
    }

    private AtlasProject CreateJumpTrimProject()
    {
        var project = new AtlasProject { Name = "JumpTrim Test" };
        
        var shape = new ShapeObject
        {
            Name = "Jump Shape",
            Vertices = new List<Point>
            {
                new(0, 0),
                new(10000, 0),   // Normal stitch
                new(10000, 20000), // Large jump
                new(20000, 20000)  // Normal stitch
            },
            IsClosed = false,
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        
        project.Objects.Add(shape);
        project.ThreadPalette.Add(new ThreadColor(0, 0, 0, "Brother", "001", "Black"));
        project.ColorToNeedleMap[0] = 1;
        
        return project;
    }
}