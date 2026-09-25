namespace AtlasEmbroidery.Domain.Tests.Formats;

using AtlasEmbroidery.Domain.Formats.Dst;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;
using FmtValidationSeverity = AtlasEmbroidery.Domain.Formats.ValidationSeverity;

public class DstFormatAdapterTests
{
    private readonly DstFormatAdapter _adapter = new();

    [Fact]
    public void DstFormatAdapter_FormatName_IsDST()
    {
        _adapter.FormatName.Should().Be("DST");
    }

    [Fact]
    public void DstFormatAdapter_FileExtension_IsDst()
    {
        _adapter.FileExtension.Should().Be(".dst");
    }

    [Fact]
    public void DstFormatAdapter_Capabilities_AreCorrect()
    {
        var caps = _adapter.Capabilities;
        
        caps.SupportsReading.Should().BeTrue();
        caps.SupportsWriting.Should().BeTrue();
        caps.SupportsTrim.Should().BeTrue();
        caps.SupportsJump.Should().BeTrue();
        caps.SupportsColorChange.Should().BeTrue();
        caps.SupportsStop.Should().BeTrue();
        caps.MaxStitchLength.Should().Be(12100); // 12.1mm in microns (121 DST units * 100)
        caps.MaxJumpLength.Should().Be(12100); // 12.1mm in microns (121 DST units * 100)
        caps.MaxColors.Should().Be(250);
    }

    [Fact]
    public void DstFormatAdapter_GenerateFuzzInput_ProducesValidData()
    {
        var data = _adapter.GenerateFuzzInput(42);
        
        data.Should().NotBeNullOrEmpty();
        data.Length.Should().BeGreaterThan(512);
        
        // Verify end marker exists
        data.Should().ContainInOrder((byte)0xF3, (byte)0x00, (byte)0x00);
    }

    [Fact]
    public async Task DstFormatAdapter_ReadWrite_RoundTrip_SimpleProject()
    {
        var project = new AtlasProject
        {
            Name = "RoundTrip Test",
            CanvasWidth = 100000,
            CanvasHeight = 100000,
        };

        var shape = ShapeObject.CreateRectangle(new Rectangle(10000, 10000, 50000, 50000), "Test Rectangle");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.StitchParams.Density = 4000;
        shape.StitchParams.ColorIndex = 0;
        shape.StitchParams.NeedleIndex = 1;
        project.Objects.Add(shape);

        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.ThreadPalette.Add(new ThreadColor(0, 255, 0, "Test", "002", "Green"));
        project.ColorToNeedleMap[0] = 1;
        project.ColorToNeedleMap[1] = 2;

        using var ms1 = new MemoryStream();
        await _adapter.WriteAsync(project, ms1);

        ms1.Position = 0;
        var readProject = await _adapter.ReadAsync(ms1);

        readProject.Should().NotBeNull();
        readProject.Name.Should().Be("RoundTrip Test");
        readProject.Objects.Should().HaveCount(1);
        readProject.ThreadPalette.Count.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void DstFormatAdapter_SemanticDiff_DetectsDifferences()
    {
        var project1 = new AtlasProject { Name = "A" };
        var project2 = new AtlasProject { Name = "B" };

        var shape1 = ShapeObject.CreateRectangle(new Rectangle(0, 0, 10000, 10000), "Rect");
        shape1.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        project1.Objects.Add(shape1);

        var shape2 = ShapeObject.CreateRectangle(new Rectangle(0, 0, 20000, 20000), "Rect");
        shape2.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        project2.Objects.Add(shape2);

        project1.ThreadPalette.Add(ThreadColor.Red);
        project2.ThreadPalette.Add(ThreadColor.Red);
        project2.ThreadPalette.Add(ThreadColor.Green);

        var diffs = _adapter.SemanticDiff(project1, project2);

        diffs.Should().NotBeEmpty();
        diffs.Should().Contain(d => d.Type == DifferenceType.StitchCount || d.Type == DifferenceType.ColorPalette);
    }

    [Fact]
    public void DstFormatAdapter_Normalize_RespectsLimits()
    {
        var project = new AtlasProject { Name = "Normalize Test" };

        var shape = ShapeObject.CreateRectangle(new Rectangle(0, 0, 100000, 100000), "Large");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Running);
        shape.StitchParams.MaxStitchLength = 200000;
        shape.StitchParams.MaxJumpDistance = 200000;
        project.Objects.Add(shape);

        for (int i = 0; i < 300; i++)
        {
            project.ThreadPalette.Add(new ThreadColor((byte)i, (byte)(i * 2), (byte)(255 - i), "Test", i.ToString("D3"), $"Color {i}"));
        }

        var normalized = _adapter.Normalize(project);

        var normalizedShape = normalized.Objects.First();
        normalizedShape.StitchParams.MaxStitchLength.Should().BeLessOrEqualTo(12700);
        normalizedShape.StitchParams.MaxJumpDistance.Should().BeLessOrEqualTo(12700);
        normalized.ThreadPalette.Count.Should().BeLessOrEqualTo(250);
    }
}

[Flags]
public enum DstFlags : ushort
{
    None = 0,
    Jump = 0x01,
    ColorChange = 0x02,
    Trim = 0x04,
    Stop = 0x08,
    End = 0x03,
}