namespace AtlasEmbroidery.Editor.Tools;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Editor;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// Base class for editor tools
/// </summary>
public abstract class EditorToolBase
{
    protected EditorProject Project { get; }
    protected EditorViewport Viewport => Project.Viewport;
    protected EditorSelection Selection => Project.Selection;

    protected EditorToolBase(EditorProject project)
    {
        Project = project;
    }

    public virtual void OnMouseDown(Point worldPos, MouseButton button, ModifierKeys modifiers) { }
    public virtual void OnMouseMove(Point worldPos, ModifierKeys modifiers) { }
    public virtual void OnMouseUp(Point worldPos, MouseButton button, ModifierKeys modifiers) { }
    public virtual void OnKeyDown(Key key, ModifierKeys modifiers) { }
    public virtual void OnKeyUp(Key key, ModifierKeys modifiers) { }
    public virtual void OnMouseWheel(Point worldPos, int delta, ModifierKeys modifiers) { }
    public virtual void Activate() { }
    public virtual void Deactivate() { }
}

/// <summary>
/// Select tool - handles object and control point selection
/// </summary>
public sealed class SelectTool : EditorToolBase
{
    private Point _dragStart;
    private bool _isDragging;
    private bool _isMarquee;
    private Rectangle _marqueeRect;

    public SelectTool(EditorProject project) : base(project) { }

    public override void OnMouseDown(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Left)
        {
            _dragStart = worldPos;
            _isDragging = true;
            
            // Check if clicking on a control point
            var (obj, pointIndex) = HitTestControlPoint(worldPos);
            if (obj != null && pointIndex >= 0)
            {
                if (modifiers.HasFlag(ModifierKeys.Control))
                    Selection.SelectControlPoint(obj.Id, pointIndex, true);
                else
                    Selection.SelectControlPoint(obj.Id, pointIndex, false);
                return;
            }

            // Check if clicking on an object
            var hitObj = HitTestObject(worldPos);
            if (hitObj != null)
            {
                if (modifiers.HasFlag(ModifierKeys.Control))
                    Selection.Select(hitObj.Id, true);
                else
                    Selection.Select(hitObj.Id, false);
            }
            else
            {
                // Start marquee selection
                if (!modifiers.HasFlag(ModifierKeys.Control))
                    Selection.Clear();
                _isMarquee = true;
                _marqueeRect = new Rectangle(worldPos.X, worldPos.Y, 0, 0);
            }
        }
        else if (button == MouseButton.Middle)
        {
            // Pan with middle mouse
            _dragStart = worldPos;
        }
    }

    public override void OnMouseMove(Point worldPos, ModifierKeys modifiers)
    {
        if (_isDragging && _isMarquee)
        {
            // Update marquee rectangle
            int x = Math.Min(_dragStart.X, worldPos.X);
            int y = Math.Min(_dragStart.Y, worldPos.Y);
            int w = Math.Abs(worldPos.X - _dragStart.X);
            int h = Math.Abs(worldPos.Y - _dragStart.Y);
            _marqueeRect = new Rectangle(x, y, w, h);

            // Select objects within marquee
            foreach (var obj in Project.Project.Objects)
            {
                if (obj.Visible && obj.Bounds.Intersects(_marqueeRect))
                {
                    Selection.Select(obj.Id, true);
                }
            }
        }
        else if (_isDragging && Selection.ControlPointIds.Count > 0)
        {
            // Drag selected control points
            DragControlPoints(worldPos);
        }
    }

    public override void OnMouseUp(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Left)
        {
            _isDragging = false;
            _isMarquee = false;
        }
    }

    private (EmbroideryObject? obj, int pointIndex) HitTestControlPoint(Point worldPos)
    {
        const int hitRadius = 5000; // 5mm in microns
        
        foreach (var obj in Project.Project.Objects)
        {
            if (!obj.Visible) continue;
            
            var points = obj.GetControlPoints().ToList();
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].DistanceTo(worldPos) <= hitRadius)
                    return (obj, i);
            }
        }
        return (null, -1);
    }

    private EmbroideryObject? HitTestObject(Point worldPos)
    {
        // Check in reverse order (topmost first)
        for (int i = Project.Project.Objects.Count - 1; i >= 0; i--)
        {
            var obj = Project.Project.Objects[i];
            if (obj.Visible && obj.Bounds.Contains(worldPos))
            {
                // More precise hit test for shapes
                if (obj is ShapeObject shape && shape.IsClosed)
                {
                    if (GeometryUtils.PointInPolygon(worldPos, shape.Vertices))
                        return obj;
                }
                else
                {
                    return obj;
                }
            }
        }
        return null;
    }

    private void DragControlPoints(Point worldPos)
    {
        var delta = new Point(worldPos.X - _dragStart.X, worldPos.Y - _dragStart.Y);
        
        foreach (var key in Selection.ControlPointIds.ToList())
        {
            // In real implementation, map key back to (objectId, pointIndex)
            // For now, simplified
        }
        
        _dragStart = worldPos;
    }
}

/// <summary>
/// Pan tool
/// </summary>
public sealed class PanTool : EditorToolBase
{
    private Point _lastPos;
    private bool _isPanning;

    public PanTool(EditorProject project) : base(project) { }

    public override void OnMouseDown(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Middle || (button == MouseButton.Left && modifiers.HasFlag(ModifierKeys.Space)))
        {
            _lastPos = worldPos;
            _isPanning = true;
        }
    }

    public override void OnMouseMove(Point worldPos, ModifierKeys modifiers)
    {
        if (_isPanning)
        {
            var delta = new Point(worldPos.X - _lastPos.X, worldPos.Y - _lastPos.Y);
            Viewport.Pan = new Point(Viewport.Pan.X - delta.X, Viewport.Pan.Y - delta.Y);
            _lastPos = worldPos;
        }
    }

    public override void OnMouseUp(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Middle || button == MouseButton.Left)
        {
            _isPanning = false;
        }
    }
}

/// <summary>
/// Zoom tool
/// </summary>
public sealed class ZoomTool : EditorToolBase
{
    private Point _zoomCenter;

    public ZoomTool(EditorProject project) : base(project) { }

    public override void OnMouseDown(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Left)
        {
            _zoomCenter = worldPos;
            double factor = modifiers.HasFlag(ModifierKeys.Control) ? 0.8 : 1.25;
            Viewport.ZoomAt(Viewport.WorldToScreen(worldPos), factor);
        }
    }

    public override void OnMouseWheel(Point worldPos, int delta, ModifierKeys modifiers)
    {
        double factor = delta > 0 ? 1.15 : 0.87;
        Viewport.ZoomAt(Viewport.WorldToScreen(worldPos), factor);
    }
}

/// <summary>
/// Rectangle creation tool
/// </summary>
public sealed class RectangleTool : EditorToolBase
{
    private Point _startPos;
    private ShapeObject? _previewShape;
    private bool _isDrawing;

    public RectangleTool(EditorProject project) : base(project) { }

    public override void OnMouseDown(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Left && !_isDrawing)
        {
            _startPos = worldPos;
            _isDrawing = true;
            
            _previewShape = ShapeObject.CreateRectangle(new Rectangle(worldPos.X, worldPos.Y, 0, 0), "Rectangle");
            _previewShape.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
            _previewShape.StitchParams.ColorIndex = (byte)Project.Project.ThreadPalette.Count;
            
            Project.Project.Objects.Add(_previewShape);
        }
    }

    public override void OnMouseMove(Point worldPos, ModifierKeys modifiers)
    {
        if (_isDrawing && _previewShape != null)
        {
            int x = Math.Min(_startPos.X, worldPos.X);
            int y = Math.Min(_startPos.Y, worldPos.Y);
            int w = Math.Abs(worldPos.X - _startPos.X);
            int h = Math.Abs(worldPos.Y - _startPos.Y);
            
            _previewShape.Vertices = new List<Point>
            {
                new Point(x, y),
                new Point(x + w, y),
                new Point(x + w, y + h),
                new Point(x, y + h)
            };
            _previewShape.RecalculateBounds();
        }
    }

    public override void OnMouseUp(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Left && _isDrawing)
        {
            _isDrawing = false;
            
            if (_previewShape != null)
            {
                var w = Math.Abs(worldPos.X - _startPos.X);
                var h = Math.Abs(worldPos.Y - _startPos.Y);
                
                if (w < 1000 || h < 1000) // Too small - remove
                {
                    Project.Project.Objects.Remove(_previewShape);
                }
                else
                {
                    var layer = Project.GetActiveLayer();
                    layer.Objects.Add(_previewShape.Id);
                }
            }
            _previewShape = null;
        }
    }

    public override void Deactivate()
    {
        if (_isDrawing && _previewShape != null)
        {
            Project.Project.Objects.Remove(_previewShape);
            _previewShape = null;
            _isDrawing = false;
        }
    }
}

/// <summary>
/// Ellipse creation tool
/// </summary>
public sealed class EllipseTool : EditorToolBase
{
    private Point _center;
    private ShapeObject? _previewShape;
    private bool _isDrawing;

    public EllipseTool(EditorProject project) : base(project) { }

    public override void OnMouseDown(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Left && !_isDrawing)
        {
            _center = worldPos;
            _isDrawing = true;
            
            _previewShape = ShapeObject.CreateEllipse(worldPos, 0, 0, 32, "Ellipse");
            _previewShape.StitchParams = StitchParams.DefaultFor(StitchType.Satin);
            _previewShape.StitchParams.ColorIndex = (byte)Project.Project.ThreadPalette.Count;
            
            Project.Project.Objects.Add(_previewShape);
        }
    }

    public override void OnMouseMove(Point worldPos, ModifierKeys modifiers)
    {
        if (_isDrawing && _previewShape != null)
        {
            int radiusX = Math.Abs(worldPos.X - _center.X);
            int radiusY = Math.Abs(worldPos.Y - _center.Y);
            
            _previewShape = ShapeObject.CreateEllipse(_center, radiusX, radiusY, 32, "Ellipse");
            _previewShape.StitchParams = StitchParams.DefaultFor(StitchType.Satin);
            _previewShape.StitchParams.ColorIndex = (byte)Project.Project.ThreadPalette.Count;
            
            // Replace preview
            Project.Project.Objects.RemoveAt(Project.Project.Objects.Count - 1);
            Project.Project.Objects.Add(_previewShape);
        }
    }

    public override void OnMouseUp(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Left && _isDrawing)
        {
            _isDrawing = false;
            
            if (_previewShape != null)
            {
                int radiusX = Math.Abs(worldPos.X - _center.X);
                int radiusY = Math.Abs(worldPos.Y - _center.Y);
                
                if (radiusX < 1000 || radiusY < 1000)
                {
                    Project.Project.Objects.Remove(_previewShape);
                }
                else
                {
                    var layer = Project.GetActiveLayer();
                    layer.Objects.Add(_previewShape.Id);
                }
            }
            _previewShape = null;
        }
    }

    public override void Deactivate()
    {
        if (_isDrawing && _previewShape != null)
        {
            Project.Project.Objects.Remove(_previewShape);
            _previewShape = null;
            _isDrawing = false;
        }
    }
}

/// <summary>
/// Path/Bezier creation tool
/// </summary>
public sealed class PathTool : EditorToolBase
{
    private readonly List<Point> _points = new();
    private PathObject? _previewPath;
    private bool _isDrawing;

    public PathTool(EditorProject project) : base(project) { }

    public override void OnMouseDown(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Left)
        {
            if (!_isDrawing)
            {
                _isDrawing = true;
                _points.Clear();
                _points.Add(worldPos);
                
                _previewPath = new PathObject
                {
                    Name = "Path",
                    Segments = new List<PathSegment>(),
                    StitchParams = StitchParams.DefaultFor(StitchType.Running)
                };
                _previewPath.StitchParams.RunningSpacing = 2000;
                _previewPath.StitchParams.ColorIndex = (byte)Project.Project.ThreadPalette.Count;
                
                Project.Project.Objects.Add(_previewPath);
            }
            else
            {
                _points.Add(worldPos);
                
                if (_points.Count >= 2)
                {
                    var segment = new LineSegment { Start = _points[_points.Count - 2], End = _points[_points.Count - 1] };
                    _previewPath!.Segments.Add(segment);
                }
            }
        }
        else if (button == MouseButton.Right && _isDrawing)
        {
            // Finish path
            FinishPath();
        }
    }

    public override void OnMouseMove(Point worldPos, ModifierKeys modifiers)
    {
        if (_isDrawing && _points.Count > 0 && _previewPath != null)
        {
            // Update last segment preview
            if (_previewPath.Segments.Count > 0)
            {
                var lastSegment = _previewPath.Segments[^1];
                if (lastSegment is LineSegment line)
                {
                    line.End = worldPos;
                }
            }
        }
    }

    public override void OnKeyDown(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Escape && _isDrawing)
        {
            CancelPath();
        }
        else if (key == Key.Enter && _isDrawing && _points.Count >= 2)
        {
            FinishPath();
        }
    }

    private void FinishPath()
    {
        _isDrawing = false;
        
        if (_previewPath != null && _previewPath.Segments.Count > 0)
        {
            var layer = Project.GetActiveLayer();
            layer.Objects.Add(_previewPath.Id);
        }
        _previewPath = null;
        _points.Clear();
    }

    private void CancelPath()
    {
        if (_isDrawing && _previewPath != null)
        {
            Project.Project.Objects.Remove(_previewPath);
        }
        _isDrawing = false;
        _previewPath = null;
        _points.Clear();
    }

    public override void Deactivate()
    {
        CancelPath();
    }
}

/// <summary>
/// Vertex editing tool
/// </summary>
public sealed class VertexEditTool : EditorToolBase
{
    private Point _dragStart;
    private bool _isDragging;

    public VertexEditTool(EditorProject project) : base(project) { }

    public override void OnMouseDown(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Left && Selection.ControlPointIds.Count > 0)
        {
            _dragStart = worldPos;
            _isDragging = true;
        }
    }

    public override void OnMouseMove(Point worldPos, ModifierKeys modifiers)
    {
        if (_isDragging)
        {
            var delta = new Point(worldPos.X - _dragStart.X, worldPos.Y - _dragStart.Y);
            
            foreach (var obj in Project.Project.Objects)
            {
                if (Selection.IsObjectSelected(obj.Id))
                {
                    if (obj is ShapeObject shape)
                    {
                        for (int i = 0; i < shape.Vertices.Count; i++)
                        {
                            if (Selection.IsControlPointSelected(obj.Id, i))
                            {
                                shape.Vertices[i] = new Point(
                                    shape.Vertices[i].X + delta.X,
                                    shape.Vertices[i].Y + delta.Y
                                );
                            }
                        }
                        shape.RecalculateBounds();
                    }
                    else if (obj is PathObject path)
                    {
                        // Handle path segment control points
                        int pointIndex = 0;
                        foreach (var segment in path.Segments)
                        {
                            if (Selection.IsControlPointSelected(obj.Id, pointIndex))
                            {
                                segment.Start = new Point(segment.Start.X + delta.X, segment.Start.Y + delta.Y);
                            }
                            pointIndex++;
                            if (Selection.IsControlPointSelected(obj.Id, pointIndex))
                            {
                                if (segment is QuadraticBezierSegment quad)
                                {
                                    quad.Control = new Point(quad.Control.X + delta.X, quad.Control.Y + delta.Y);
                                }
                                else if (segment is CubicBezierSegment cubic)
                                {
                                    cubic.Control1 = new Point(cubic.Control1.X + delta.X, cubic.Control1.Y + delta.Y);
                                }
                            }
                            pointIndex++;
                            if (Selection.IsControlPointSelected(obj.Id, pointIndex))
                            {
                                if (segment is CubicBezierSegment cubic)
                                {
                                    cubic.Control2 = new Point(cubic.Control2.X + delta.X, cubic.Control2.Y + delta.Y);
                                }
                            }
                            pointIndex++;
                            if (Selection.IsControlPointSelected(obj.Id, pointIndex))
                            {
                                segment.End = new Point(segment.End.X + delta.X, segment.End.Y + delta.Y);
                            }
                            pointIndex++;
                        }
                        path.RecalculateBounds();
                    }
                }
            }
            
            _dragStart = worldPos;
        }
    }

    public override void OnMouseUp(Point worldPos, MouseButton button, ModifierKeys modifiers)
    {
        if (button == MouseButton.Left)
        {
            _isDragging = false;
        }
    }
}

/// <summary>
/// Tool factory
/// </summary>
public static class EditorToolFactory
{
    public static EditorToolBase CreateTool(EditorTool tool, EditorProject project)
    {
        return tool switch
        {
            EditorTool.Select => new SelectTool(project),
            EditorTool.Pan => new PanTool(project),
            EditorTool.Zoom => new ZoomTool(project),
            EditorTool.CreateRectangle => new RectangleTool(project),
            EditorTool.CreateEllipse => new EllipseTool(project),
            EditorTool.CreatePath => new PathTool(project),
            EditorTool.EditVertices => new VertexEditTool(project),
            _ => new SelectTool(project)
        };
    }
}

public enum MouseButton { Left, Middle, Right, XButton1, XButton2 }
public enum Key { Escape, Enter, Space, Control, Shift, Alt, Delete, A, C, V, X, Y, Z }
[Flags]
public enum ModifierKeys { None = 0, Control = 1, Shift = 2, Alt = 4, Space = 8 }