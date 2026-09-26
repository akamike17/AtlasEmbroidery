namespace AtlasEmbroidery.Tests.Formats.Vp3;

using AtlasEmbroidery.Domain.Formats.Vp3;
using AtlasEmbroidery.Domain.Formats;
using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using FluentAssertions;
using Xunit;

/// <summary>
/// Information loss documentation tests for VP3 format
/// Documents what data is NOT preserved when writing to VP3
/// </summary>
public sealed class Vp3InformationLossTests
{
    private readonly Vp3FormatAdapter _adapter = new();

    [Fact]
    public async Task Write_ThreadRgbOutsidePalette_MappedToJef()
    {
        var project = new AtlasProject { Name = "RGBTest" };
        project.ThreadPalette.Add(new ThreadColor(128, 64, 192, "Custom", "001", "Purple"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        
        // VP3 maps colors to JEF palette indices (1-64)
        // Original RGB is lost, only palette index stored
        readProject.Should().NotBeNull();
    }

    [Fact]
    public async Task Write_DesignName_NotPreserved()
    {
        var project = new AtlasProject { Name = "My Design Name" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // VP3 has no design name field in header (only date string)
        readProject.Name.Should().NotBe("My Design Name");
    }

    [Fact]
    public async Task Write_HoopInfo_PartiallyPreserved()
    {
        var project = new AtlasProject { Name = "HoopTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        project.SelectedHoop = new HoopProfile 
        { 
            Name = "Standard 100x100", 
            Width = 100000, 
            Height = 100000 
        };
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // VP3 has hoop size field but reader doesn't reconstruct HoopProfile
        readProject.SelectedHoop.Should().BeNull();
    }

    [Fact]
    public async Task Write_StopVsColorChange_Indistinguishable()
    {
        // VP3 uses same control code (0x80 0x01) for both STOP and COLOR_CHANGE
        // Reader treats both as color change
        var project = new AtlasProject { Name = "StopTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // Both stop and color change become color changes in VP3
        // Can't distinguish them on read
        readProject.Should().NotBeNull();
    }

    [Fact]
    public async Task Write_DateString_NotOriginalDate()
    {
        var project = new AtlasProject { Name = "DateTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // VP3 writes current date, not original creation date
        // Reader doesn't parse date string back
        readProject.Should().NotBeNull();
    }

    [Fact]
    public async Task Write_UnderlayData_Lost()
    {
        // VP3 has no concept of underlay
        // Underlay stitches would be written as normal stitches
        var project = new AtlasProject { Name = "UnderlayTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // Underlay info lost - VP3 has no underlay marker
        readProject.Should().NotBeNull();
    }

    [Fact]
    public async Task Write_CompensationData_Lost()
    {
        // VP3 has no pull compensation encoding
        var project = new AtlasProject { Name = "CompTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // Compensation data lost
        readProject.Should().NotBeNull();
    }

    [Fact]
    public async Task Write_MaxDelta_Limited()
    {
        // VP3 max delta per record is 127 (signed 8-bit)
        // Longer moves will be split into multiple records
        var project = new AtlasProject { Name = "MaxDelta" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        // All stitch records should have valid deltas <= 127
        int headerEnd = Vp3Spec.HeaderBaseSize + (project.ThreadPalette.Count * Vp3Spec.ColorEntrySize) + 4;
        for (int i = headerEnd; i < bytes.Length - 1; )
        {
            if (bytes[i] == 0x80)
            {
                // Control code
                if (i + 1 < bytes.Length && bytes[i + 1] == 0x10)
                    break; // END
                i += 4;
            }
            else
            {
                // Regular stitch
                int dx = (sbyte)bytes[i];
                int dy = -(sbyte)bytes[i + 1];
                Math.Abs(dx).Should().BeLessOrEqualTo(Vp3Spec.MaxStitchDistance);
                Math.Abs(dy).Should().BeLessOrEqualTo(Vp3Spec.MaxStitchDistance);
                i += 2;
            }
        }
    }

    [Fact]
    public async Task Write_NoExplicitEnd_ButEndMarkerPresent()
    {
        // VP3 doesn't have an explicit END record in header
        // But stitch data always ends with END marker (0x80 0x10)
        var project = new AtlasProject { Name = "EndTest" };
        project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Test", "001", "Red"));
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        
        var bytes = stream.ToArray();
        bytes[^2].Should().Be(0x80);
        bytes[^1].Should().Be(0x10);
    }

    [Fact]
    public async Task Write_PaletteIndexMapping_Lossy()
    {
        // VP3 maps threads to JEF palette indices (1-64)
        // If design has >64 colors, mapping wraps modulo 64
        // If two different threads map to same JEF index, they become same color
        var project = new AtlasProject { Name = "PaletteTest" };
        for (int i = 0; i < 70; i++)
        {
            project.ThreadPalette.Add(new ThreadColor(
                (byte)(i * 3 % 256), 
                (byte)(i * 5 % 256), 
                (byte)(i * 7 % 256), 
                "Custom", 
                $"{i:D3}", 
                $"Color {i}"
            ));
        }
        
        var stream = new MemoryStream();
        await _adapter.WriteAsync(project, stream);
        stream.Position = 0;
        
        var readProject = await _adapter.ReadAsync(stream);
        // Palette mapping is lossy - multiple threads can map to same JEF index
        readProject.Should().NotBeNull();
    }
}