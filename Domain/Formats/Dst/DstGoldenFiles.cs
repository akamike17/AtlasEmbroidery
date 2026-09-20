namespace AtlasEmbroidery.Domain.Formats.Dst;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Domain.Formats;
using System.Text;

/// <summary>
/// DST Golden Test Files - Embedded valid DST files for round-trip testing
/// </summary>
public static class DstGoldenFiles
{
    /// <summary>
    /// Minimal valid DST: single running stitch line
    /// </summary>
    public static byte[] SimpleLine => BuildValidDst(
        name: "Simple Line",
        stitches: new[] { (0, 0, (byte)0), (100, 0, (byte)0), (200, 0, (byte)0) },
        colorCount: 1
    );

    /// <summary>
    /// Square with 4 sides (running stitch)
    /// </summary>
    public static byte[] Square => BuildValidDst(
        name: "Square",
        stitches: new[]
        {
            (0, 0, (byte)0), (1000, 0, (byte)0),
            (1000, 1000, (byte)0), (0, 1000, (byte)0),
            (0, 0, (byte)0)
        },
        colorCount: 1
    );

    /// <summary>
    /// Multi-color design: red square + blue circle
    /// </summary>
    public static byte[] MultiColor => BuildValidDst(
        name: "Multi Color",
        stitches: new[]
        {
            // Color 0: Square
            (0, 0, (byte)0), (1000, 0, (byte)0),
            (1000, 1000, (byte)0), (0, 1000, (byte)0),
            (0, 0, (byte)0),
            // Color change
            (2000, 2000, (byte)DstFlags.ColorChange),
            // Color 1: Circle (approximated)
            (2000, 2000, (byte)0), (2100, 2000, (byte)0),
            (2100, 2100, (byte)0), (2000, 2100, (byte)0),
            (2000, 2000, (byte)0)
        },
        colorCount: 2
    );

    /// <summary>
    /// Design with jumps and trims
    /// </summary>
    public static byte[] WithJumpsAndTrims => BuildValidDst(
        name: "Jumps and Trims",
        stitches: new[]
        {
            // First object
            (0, 0, (byte)0), (500, 0, (byte)0), (500, 500, (byte)0), (0, 500, (byte)0), (0, 0, (byte)0),
            // Jump to second object
            (2000, 2000, (byte)(DstFlags.Jump | DstFlags.Trim)),
            (2000, 2000, (byte)0), (2500, 2000, (byte)0), (2500, 2500, (byte)0), (2000, 2500, (byte)0), (2000, 2000, (byte)0),
            // Trim at end
            (0, 0, (byte)(DstFlags.Trim | DstFlags.Stop))
        },
        colorCount: 1
    );

    /// <summary>
    /// Large design near format limits
    /// </summary>
    public static byte[] LargeDesign => BuildValidDst(
        name: "Large Design",
        stitches: GenerateSpiral(centerX: 50000, centerY: 50000, radius: 40000, turns: 5, pointsPerTurn: 100),
        colorCount: 1
    );

    /// <summary>
    /// Design with many color changes
    /// </summary>
    public static byte[] ManyColors => BuildValidDst(
        name: "Many Colors",
        stitches: GenerateColorBands(),
        colorCount: 10
    );

    /// <summary>
    /// Satin-like column (dense parallel lines)
    /// </summary>
    public static byte[] SatinColumn => BuildValidDst(
        name: "Satin Column",
        stitches: GenerateSatinColumn(x: 10000, y: 10000, width: 200, height: 5000, spacing: 50),
        colorCount: 1
    );

    /// <summary>
    /// Tatami-like fill (dense grid)
    /// </summary>
    public static byte[] TatamiFill => BuildValidDst(
        name: "Tatami Fill",
        stitches: GenerateTatamiFill(x: 0, y: 0, width: 5000, height: 5000, spacing: 200),
        colorCount: 1
    );

    /// <summary>
    /// Design with stops (color changes without trims)
    /// </summary>
    public static byte[] WithStops => BuildValidDst(
        name: "With Stops",
        stitches: new[]
        {
            (0, 0, (byte)0), (1000, 0, (byte)0),
            (1000, 0, (byte)(DstFlags.Stop)),  // Stop
            (1000, 1000, (byte)0), (0, 1000, (byte)0),
            (0, 0, (byte)0)
        },
        colorCount: 1
    );

    /// <summary>
    /// Empty design (header only, no stitches)
    /// </summary>
    public static byte[] EmptyDesign => BuildValidDst(
        name: "Empty",
        stitches: Array.Empty<(int dx, int dy, byte flags)>(),
        colorCount: 0
    );

    private static byte[] BuildValidDst(string name, (int dx, int dy, byte flags)[] stitches, int colorCount)
    {
        var ms = new MemoryStream();
        var writer = new BinaryWriter(ms, Encoding.ASCII, leaveOpen: true);

        // Header (512 bytes)
        var header = new byte[512];
        header[0] = 0x20; // LA: magic
        header[1] = 0x20;
        Encoding.ASCII.GetBytes(name.PadRight(16)).CopyTo(header, 2);
        
        // Calculate bounds
        int x = 0, y = 0;
        int minX = 0, maxX = 0, minY = 0, maxY = 0;
        
        foreach (var (dx, dy, _) in stitches)
        {
            x += dx;
            y += dy;
            minX = Math.Min(minX, x);
            maxX = Math.Max(maxX, x);
            minY = Math.Min(minY, y);
            maxY = Math.Max(maxY, y);
        }
        
        // Dimensions in 0.1mm units
        short width = (short)((maxX - minX) / 10);
        short height = (short)((maxY - minY) / 10);
        if (width < 0) width = 0;
        if (height < 0) height = 0;
        
        BitConverter.GetBytes(width).CopyTo(header, 90);
        BitConverter.GetBytes(height).CopyTo(header, 92);
        BitConverter.GetBytes(stitches.Length).CopyTo(header, 98);
        header[102] = (byte)colorCount;
        
        writer.Write(header);

        // Stitch data
        int lastX = 0, lastY = 0;
        
        foreach (var (dx, dy, flags) in stitches)
        {
            int absX = lastX + dx;
            int absY = lastY + dy;
            
            var (b1, b2, b3) = EncodeStitch(absX - lastX, absY - lastY, flags);
            writer.Write(b1);
            writer.Write(b2);
            writer.Write(b3);
            
            lastX = absX;
            lastY = absY;
        }

        // End marker
        writer.Write((byte)0xF3);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        // Pad to 3-byte boundary
        while (ms.Position % 3 != 0)
        {
            writer.Write((byte)0x00);
        }

        writer.Flush();
        return ms.ToArray();
    }

    private static (byte b1, byte b2, byte b3) EncodeStitch(int dx, int dy, int flags)
    {
        dx = Math.Clamp(dx, -2048, 2047);
        dy = Math.Clamp(dy, -2048, 2047);
        
        int x = dx & 0xFFF;
        int y = (-dy) & 0xFFF;
        
        byte b1 = (byte)(((y >> 4) & 0xFC) | ((x >> 10) & 0x03));
        byte b2 = (byte)(((x >> 4) & 0x3F) | ((y >> 2) & 0xC0));
        byte b3 = (byte)(((y & 0x03) << 4) | ((x & 0x03) << 2) | (flags & 0x0F));
        
        return (b1, b2, b3);
    }

    private static (int dx, int dy, byte flags)[] GenerateSpiral(int centerX, int centerY, int radius, int turns, int pointsPerTurn)
    {
        var stitches = new List<(int, int, byte)>();
        int lastX = 0, lastY = 0;
        
        for (int t = 0; t < turns * pointsPerTurn; t++)
        {
            double angle = (t * 2 * Math.PI) / pointsPerTurn;
            double r = radius * (1.0 - (double)t / (turns * pointsPerTurn));
            
            int x = centerX + (int)(r * Math.Cos(angle));
            int y = centerY + (int)(r * Math.Sin(angle));
            
            int dx = x - lastX;
            int dy = y - lastY;
            
            stitches.Add((dx, dy, 0));
            lastX = x;
            lastY = y;
        }
        
        return stitches.ToArray();
    }

    private static (int dx, int dy, byte flags)[] GenerateColorBands()
    {
        var stitches = new List<(int, int, byte)>();
        int lastX = 0, lastY = 0;
        
        for (int color = 0; color < 10; color++)
        {
            if (color > 0)
            {
                stitches.Add((0, 0, (byte)DstFlags.ColorChange));
            }
            
            for (int i = 0; i < 50; i++)
            {
                int x = color * 1000 + i * 10;
                int y = color * 100;
                int dx = x - lastX;
                int dy = y - lastY;
                stitches.Add((dx, dy, 0));
                lastX = x;
                lastY = y;
            }
        }
        
        return stitches.ToArray();
    }

    private static (int dx, int dy, byte flags)[] GenerateSatinColumn(int x, int y, int width, int height, int spacing)
    {
        var stitches = new List<(int, int, byte)>();
        int lastX = 0, lastY = 0;
        int rows = height / spacing;
        
        for (int row = 0; row <= rows; row++)
        {
            int yPos = y + row * spacing;
            int direction = row % 2 == 0 ? 1 : -1;
            int startX = x + (direction == 1 ? 0 : width);
            int endX = x + (direction == 1 ? width : 0);
            
            // Jump to start of row
            if (row == 0)
            {
                stitches.Add((startX - lastX, yPos - lastY, 0));
            }
            else
            {
                stitches.Add((startX - lastX, yPos - lastY, (byte)DstFlags.Jump));
            }
            
            // Stitch across
            stitches.Add((endX - startX, 0, 0));
            
            lastX = endX;
            lastY = yPos;
        }
        
        return stitches.ToArray();
    }

    private static (int dx, int dy, byte flags)[] GenerateTatamiFill(int x, int y, int width, int height, int spacing)
    {
        var stitches = new List<(int, int, byte)>();
        int lastX = 0, lastY = 0;
        int rows = height / spacing;
        
        for (int row = 0; row <= rows; row++)
        {
            int yPos = y + row * spacing;
            int direction = row % 2 == 0 ? 1 : -1;
            int startX = x + (direction == 1 ? 0 : width);
            int endX = x + (direction == 1 ? width : 0);
            
            if (row == 0)
            {
                stitches.Add((startX - lastX, yPos - lastY, 0));
            }
            else
            {
                stitches.Add((startX - lastX, yPos - lastY, (byte)DstFlags.Jump));
            }
            
            stitches.Add((endX - startX, 0, 0));
            
            lastX = endX;
            lastY = yPos;
        }
        
        return stitches.ToArray();
    }
}