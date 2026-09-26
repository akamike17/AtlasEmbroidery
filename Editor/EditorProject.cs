namespace AtlasEmbroidery.Editor;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AtlasEmbroidery.Editor.ViewModels;

/// <summary>
/// Editor-specific project wrapper with UI state
/// </summary>
public sealed class EditorProject
{
    public AtlasProject Project { get; }
    public ObservableCollection<EditorLayer> Layers { get; } = new();
    public EditorSelection Selection { get; } = new();
    public EditorViewport Viewport { get; } = new();
    public EditorHistory History { get; } = new();
    public EditorTool CurrentTool { get; set; } = EditorTool.Select;
    public Guid ActiveLayerId { get; set; }
    public EditorViewModel? ViewModel { get; set; }

    public EditorProject(AtlasProject? project = null)
    {
        Project = project ?? new AtlasProject();
        // Create default layer
        var defaultLayer = new EditorLayer("Default", true, true);
        Layers.Add(defaultLayer);
        ActiveLayerId = defaultLayer.Id;
    }

    public EditorLayer GetActiveLayer() => Layers.FirstOrDefault(l => l.Id == ActiveLayerId) ?? Layers[0];

    public void AddObject(EmbroideryObject obj)
    {
        var layer = GetActiveLayer();
        layer.Objects.Add(obj.Id);
        Project.Objects.Add(obj);
        Project.Touch();
    }

    public void RemoveObject(Guid objectId)
    {
        var obj = Project.Objects.FirstOrDefault(o => o.Id == objectId);
        if (obj != null)
        {
            foreach (var layer in Layers)
                layer.Objects.Remove(objectId);
            Project.Objects.Remove(obj);
            Selection.Clear();
            Project.Touch();
        }
    }
}

/// <summary>
/// Editor layer for organizing objects
/// </summary>
public sealed class EditorLayer : INotifyPropertyChanged
{
    public Guid Id { get; } = Guid.NewGuid();
    private string _name;
    private bool _visible = true;
    private bool _locked = false;
    private double _opacity = 1.0;
    public HashSet<Guid> Objects { get; } = new();

    public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }
    public bool Visible { get => _visible; set { _visible = value; OnPropertyChanged(); } }
    public bool Locked { get => _locked; set { _locked = value; OnPropertyChanged(); } }
    public double Opacity { get => _opacity; set { _opacity = Math.Clamp(value, 0, 1); OnPropertyChanged(); } }

    public EditorLayer(string name, bool visible = true, bool locked = false)
    {
        _name = name;
        _visible = visible;
        _locked = locked;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Selection management
/// </summary>
public sealed class EditorSelection : INotifyPropertyChanged
{
    public HashSet<Guid> ObjectIds { get; } = new();
    public HashSet<Guid> ControlPointIds { get; } = new(); // For vertex/handle selection

    public bool HasSelection => ObjectIds.Count > 0 || ControlPointIds.Count > 0;
    public int ObjectCount => ObjectIds.Count;
    public int ControlPointCount => ControlPointIds.Count;

    public void Select(Guid objectId, bool additive = false)
    {
        if (!additive) Clear();
        ObjectIds.Add(objectId);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ObjectCount));
    }

    public void SelectControlPoint(Guid objectId, int pointIndex, bool additive = false)
    {
        var key = CreateControlPointKey(objectId, pointIndex);
        if (!additive) ControlPointIds.Clear();
        ControlPointIds.Add(key);
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ControlPointCount));
    }

    public void Clear()
    {
        ObjectIds.Clear();
        ControlPointIds.Clear();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ObjectCount));
        OnPropertyChanged(nameof(ControlPointCount));
    }

    public bool IsObjectSelected(Guid objectId) => ObjectIds.Contains(objectId);
    public bool IsControlPointSelected(Guid objectId, int pointIndex) => ControlPointIds.Contains(CreateControlPointKey(objectId, pointIndex));

    private static Guid CreateControlPointKey(Guid objectId, int index) => Guid.NewGuid(); // Simplified - real impl would use deterministic hash

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Viewport state (pan/zoom)
/// </summary>
public sealed class EditorViewport : INotifyPropertyChanged
{
    private double _zoom = 1.0;
    private Point _pan = Point.Zero;
    private Rectangle _viewBounds = new(0, 0, 800, 600);

    public double Zoom { get => _zoom; set { _zoom = Math.Clamp(value, 0.01, 100); OnPropertyChanged(); OnPropertyChanged(nameof(Transform)); } }
    public Point Pan { get => _pan; set { _pan = value; OnPropertyChanged(); OnPropertyChanged(nameof(Transform)); } }
    public Rectangle ViewBounds { get => _viewBounds; set { _viewBounds = value; OnPropertyChanged(); } }

    public Matrix Transform => Matrix.CreateTranslation(-_pan.X, -_pan.Y) * Matrix.CreateScale(_zoom);
    public Matrix InverseTransform => Transform.Invert();

    public Point ScreenToWorld(Point screen) => InverseTransform.Transform(screen);
    public Point WorldToScreen(Point world) => Transform.Transform(world);

    public void ZoomAt(Point screenCenter, double factor)
    {
        var worldBefore = ScreenToWorld(screenCenter);
        Zoom *= factor;
        var worldAfter = ScreenToWorld(screenCenter);
        Pan = new Point((int)(Pan.X + (worldAfter.X - worldBefore.X) * Zoom), (int)(Pan.Y + (worldAfter.Y - worldBefore.Y) * Zoom));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Undo/redo history
/// </summary>
public sealed class EditorHistory
{
    private readonly List<IEditorCommand> _undoStack = new();
    private readonly List<IEditorCommand> _redoStack = new();
    private const int MaxHistory = 100;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public event Action? CanUndoChanged;
    public event Action? CanRedoChanged;

    public void Execute(IEditorCommand command)
    {
        command.Execute();
        _undoStack.Add(command);
        if (_undoStack.Count > MaxHistory) _undoStack.RemoveAt(0);
        _redoStack.Clear();
        CanUndoChanged?.Invoke();
        CanRedoChanged?.Invoke();
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var cmd = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        cmd.Undo();
        _redoStack.Add(cmd);
        CanUndoChanged?.Invoke();
        CanRedoChanged?.Invoke();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var cmd = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        cmd.Execute();
        _undoStack.Add(cmd);
        CanUndoChanged?.Invoke();
        CanRedoChanged?.Invoke();
    }
}

/// <summary>
/// Editor tool modes
/// </summary>
public enum EditorTool
{
    Select,
    Pan,
    Zoom,
    CreateRectangle,
    CreateEllipse,
    CreatePolygon,
    CreatePath,
    CreateBezier,
    CreateText,
    EditVertices,
    Measure
}

/// <summary>
/// Command interface for undo/redo
/// </summary>
public interface IEditorCommand
{
    void Execute();
    void Undo();
    string Description { get; }
    bool CanExecute { get; }
    event EventHandler? CanExecuteChanged;
    void RaiseCanExecuteChanged();
}