namespace AtlasEmbroidery.Editor.Rendering;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Editor;
using SkiaSharp;
using System.Collections.Generic;

/// <summary>
/// SkiaSharp-based renderer for editor viewport
/// </summary>
public sealed class EditorRenderer
{
    private readonly EditorProject _project;
    private SKCanvas? _canvas;
    private readonly SKPaint _gridPaint = new SKPaint
    {
        Color = new SKColor(0xCC, 0xCC, 0xCC, 0x80),
        StrokeWidth = 1,
        IsStroke = true,
        IsAntialias = true
    };

    private readonly SKPaint _selectionPaint = new SKPaint
    {
        Color = SKColors.DodgerBlue,
        StrokeWidth = 2,
        IsStroke = true,
        IsAntialias = true,
        PathEffect = SKPathEffect.CreateDash(new float[] { 5, 5 }, 0)
    };

    private readonly SKPaint _controlPointPaint = new SKPaint
    {
        Color = SKColors.DodgerBlue,
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };

    private readonly SKPaint _handlePaint = new SKPaint
    {
        Color = SKColors.White,
        StrokeWidth = 2,
        IsStroke = true,
        IsAntialias = true
    };

    private readonly SKPaint _marqueePaint = new SKPaint
    {
        Color = new SKColor(0x1E, 0x90, 0xFF, 0x40),
        StrokeWidth = 1,
        IsStroke = true,
        IsAntialias = true,
        PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0)
    };

    private readonly SKPaint _stitchPaint = new SKPaint
    {
        Color = SKColors.Black,
        StrokeWidth = 1,
        IsStroke = true,
        IsAntialias = true
    };

    private readonly SKPaint _jumpPaint = new SKPaint
    {
        Color = SKColors.Gray,
        StrokeWidth = 1,
        IsStroke = true,
        IsAntialias = true,
        PathEffect = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0)
    };

    private readonly SKPaint _trimPaint = new SKPaint
    {
        Color = SKColors.Red,
        StrokeWidth = 1,
        IsStroke = true,
        IsAntialias = true,
        PathEffect = SKPathEffect.CreateDash(new float[] { 2, 2 }, 0)
    };

    private readonly SKPaint _underlayPaint = new SKPaint
    {
        Color = new SKColor(0x80, 0x80, 0x80, 0x80),
        StrokeWidth = 1,
        IsStroke = true,
        IsAntialias = true
    };

    private readonly SKFont _textFont = new SKFont(SKTypeface.FromFamilyName("Arial"), 14);

    private readonly SKPaint _objectFillPaint = new SKPaint
    {
        Color = new SKColor(0xE0, 0xE0, 0xE0, 0x80),
        Style = SKPaintStyle.Fill,
        IsAntialias = true
    };

    private readonly SKPaint _objectStrokePaint = new SKPaint
    {
        Color = SKColors.DimGray,
        StrokeWidth = 1,
        IsStroke = true,
        IsAntialias = true
    };

    public EditorRenderer(EditorProject project)
    {
        _project = project;
    }

    public void Render(SKCanvas canvas, EditorViewport viewport)
    {
        _canvas = canvas;
        canvas.Save();
        canvas.Concat(viewport.Transform.ToSKMatrix());

        // Render grid
        if (_project.Viewport.Zoom > 0.1) // Don't render grid when too zoomed out
        {
            RenderGrid(viewport);
        }

        // Render objects
        RenderObjects();

        // Render stitch preview
        if (_project.ViewModel?.ShowStitchPreview == true && _project.ViewModel?.CurrentStitchPlan != null)
        {
            RenderStitchPreview(_project.ViewModel.CurrentStitchPlan);
        }

        // Render selection
        RenderSelection();

        // Render marquee
        // (marquee rendered by SelectTool directly)

        canvas.Restore();
    }

    private void RenderGrid(EditorViewport viewport)
    {
        var viewRect = viewport.InverseTransform.TransformRect(new Rectangle(0, 0, viewport.ViewBounds.Width, viewport.ViewBounds.Height));
        int gridSize = _project.ViewModel?.GridSize ?? 10000;

        int startX = (int)Math.Floor(viewRect.X / (double)gridSize) * gridSize;
        int startY = (int)Math.Floor(viewRect.Y / (double)gridSize) * gridSize;
        int endX = (int)Math.Ceiling(viewRect.Right / (double)gridSize) * gridSize;
        int endY = (int)Math.Ceiling(viewRect.Bottom / (double)gridSize) * gridSize;

        // Vertical lines
        for (int x = startX; x <= endX; x += gridSize)
        {
            _canvas!.DrawLine(x, startY, x, endY, _gridPaint);
        }

        // Horizontal lines
        for (int y = startY; y <= endY; y += gridSize)
        {
            _canvas!.DrawLine(startX, y, endX, y, _gridPaint);
        }

        // Origin axes
        var axisPaint = new SKPaint { Color = SKColors.Red, StrokeWidth = 2, IsStroke = true, IsAntialias = true };
        _canvas!.DrawLine(viewRect.X, 0, viewRect.Right, 0, axisPaint); // X axis
        axisPaint.Color = SKColors.Green;
        _canvas!.DrawLine(0, viewRect.Y, 0, viewRect.Bottom, axisPaint); // Y axis
    }

    private void RenderObjects()
    {
        foreach (var layer in _project.Layers)
        {
            if (!layer.Visible) continue;

            foreach (var objId in layer.Objects)
            {
                var obj = _project.Project.Objects.FirstOrDefault(o => o.Id == objId);
                if (obj == null || !obj.Visible) continue;

                RenderObject(obj, layer.Locked);
            }
        }
    }

    private void RenderObject(EmbroideryObject obj, bool locked)
    {
        SKPaint fillPaint;
        SKPaint strokePaint;
        
        if (locked)
        {
            fillPaint = new SKPaint
            {
                Color = new SKColor(0xA0, 0xA0, 0xA0, 0x80),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };
            strokePaint = new SKPaint
            {
                Color = SKColors.DarkGray,
                StrokeWidth = 1,
                IsStroke = true,
                IsAntialias = true
            };
        }
        else
        {
            fillPaint = _objectFillPaint;
            strokePaint = _objectStrokePaint;
        }

        if (obj is ShapeObject shape)
        {
            var path = CreatePathFromShape(shape);
            _canvas!.DrawPath(path, fillPaint);
            _canvas!.DrawPath(path, strokePaint);
        }
        else if (obj is PathObject pathObj)
        {
            var path = CreatePathFromPathObject(pathObj);
            _canvas!.DrawPath(path, strokePaint);
        }
        // Text and Image objects - placeholder rendering
    }

    private SKPath CreatePathFromShape(ShapeObject shape)
    {
        var path = new SKPath();
        if (shape.Vertices.Count == 0) return path;

        var pts = shape.Vertices;
        path.MoveTo(pts[0].X, pts[0].Y);
        for (int i = 1; i < pts.Count; i++)
        {
            path.LineTo(pts[i].X, pts[i].Y);
        }
        if (shape.IsClosed && pts.Count > 2)
        {
            path.Close();
        }
        return path;
    }

    private SKPath CreatePathFromPathObject(PathObject pathObj)
    {
        var path = new SKPath();
        if (pathObj.Segments.Count == 0) return path;

        var firstSeg = pathObj.Segments[0];
        path.MoveTo(firstSeg.Start.X, firstSeg.Start.Y);

        foreach (var seg in pathObj.Segments)
        {
            if (seg is LineSegment line)
            {
                path.LineTo(line.End.X, line.End.Y);
            }
            else if (seg is QuadraticBezierSegment quad)
            {
                path.QuadTo(quad.Control.X, quad.Control.Y, quad.End.X, quad.End.Y);
            }
            else if (seg is CubicBezierSegment cubic)
            {
                path.CubicTo(cubic.Control1.X, cubic.Control1.Y, cubic.Control2.X, cubic.Control2.Y, cubic.End.X, cubic.End.Y);
            }
        }

        if (pathObj.IsClosed)
        {
            path.Close();
        }

        return path;
    }

    private void RenderStitchPreview(StitchPlan plan)
    {
        foreach (var kvp in plan.ObjectStitches)
        {
            var stitches = kvp.Value;
            if (stitches.Count < 2) continue;

            var threadColor = plan.ThreadPalette.Count > kvp.Value[0].ColorIndex 
                ? plan.ThreadPalette[kvp.Value[0].ColorIndex] 
                : new ThreadColor(0, 0, 0, "Unknown", "000", "Black");

            var stitchColor = new SKColor(threadColor.R, threadColor.G, threadColor.B);

            for (int i = 0; i < stitches.Count - 1; i++)
            {
                var s1 = stitches[i];
                var s2 = stitches[i + 1];

                var paint = s1.IsJump ? _jumpPaint : (s1.IsTrim ? _trimPaint : (s1.IsUnderlay ? _underlayPaint : _stitchPaint));
                paint.Color = s1.IsUnderlay ? _underlayPaint.Color : stitchColor;

                _canvas!.DrawLine(s1.X, s1.Y, s2.X, s2.Y, paint);
            }

            // Draw color change markers
            for (int i = 0; i < stitches.Count; i++)
            {
                if (stitches[i].IsColorChange)
                {
                    var markerPaint = new SKPaint { Color = SKColors.Magenta, StrokeWidth = 3, IsStroke = true, IsAntialias = true };
                    _canvas!.DrawCircle(stitches[i].X, stitches[i].Y, 3000, markerPaint); // 3mm radius
                }
            }
        }
    }

    private void RenderSelection()
    {
        foreach (var objId in _project.Selection.ObjectIds)
        {
            var obj = _project.Project.Objects.FirstOrDefault(o => o.Id == objId);
            if (obj == null) continue;

            // Render bounding box
            var boundsPaint = new SKPaint { Color = SKColors.DodgerBlue, StrokeWidth = 2, IsStroke = true, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new float[] { 8, 4 }, 0) };
            _canvas!.DrawRect(obj.Bounds.X, obj.Bounds.Y, obj.Bounds.Width, obj.Bounds.Height, boundsPaint);

            // Render control points
            var points = obj.GetControlPoints().ToList();
            for (int i = 0; i < points.Count; i++)
            {
                bool isSelected = _project.Selection.IsControlPointSelected(obj.Id, i);
                
                SKPaint cpPaint;
                SKPaint handlePaint;
                
                if (isSelected)
                {
                    cpPaint = _controlPointPaint;
                    handlePaint = _handlePaint;
                }
                else
                {
                    cpPaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
                    handlePaint = new SKPaint { Color = SKColors.DodgerBlue, StrokeWidth = 2, IsStroke = true, IsAntialias = true };
                }

                _canvas!.DrawCircle(points[i].X, points[i].Y, 4000, cpPaint); // 4mm radius
                _canvas!.DrawCircle(points[i].X, points[i].Y, 5000, handlePaint);
            }
        }
    }

    public void Dispose()
    {
        _gridPaint?.Dispose();
        _selectionPaint?.Dispose();
        _controlPointPaint?.Dispose();
        _handlePaint?.Dispose();
        _marqueePaint?.Dispose();
        _stitchPaint?.Dispose();
        _jumpPaint?.Dispose();
        _trimPaint?.Dispose();
        _underlayPaint?.Dispose();
        _objectFillPaint?.Dispose();
        _objectStrokePaint?.Dispose();
    }
}

/// <summary>
/// Extensions for SkiaSharp integration
/// </summary>
internal static class SkiaExtensions
{
    public static SKMatrix ToSKMatrix(this Matrix m)
    {
        return new SKMatrix
        {
            ScaleX = (float)m.M11,
            ScaleY = (float)m.M22,
            SkewX = (float)m.M12,
            SkewY = (float)m.M21,
            TransX = (float)m.OffsetX,
            TransY = (float)m.OffsetY,
            Persp0 = 0,
            Persp1 = 0,
            Persp2 = 1
        };
    }

    public static SKRect ToSKRect(this Rectangle r)
    {
        return new SKRect(r.X, r.Y, r.Right, r.Bottom);
    }
}