namespace AtlasEmbroidery.Editor.ViewModels;

using AtlasEmbroidery.Domain.Models;
using AtlasEmbroidery.Domain.Geometry;
using AtlasEmbroidery.Domain.Stitching;
using AtlasEmbroidery.Editor;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

/// <summary>
/// Main editor view model - coordinates all editor state
/// </summary>
public sealed class EditorViewModel : INotifyPropertyChanged
{
    public EditorProject Project { get; }
    public EditorTool CurrentTool { get => Project.CurrentTool; set { Project.CurrentTool = value; OnPropertyChanged(); UpdateToolCursors(); } }
    public EditorViewport Viewport => Project.Viewport;
    public EditorSelection Selection => Project.Selection;
    public ObservableCollection<EditorLayer> Layers => Project.Layers;
    public EditorHistory History => Project.History;

    // Stitch preview
    public StitchPlan? CurrentStitchPlan { get; private set; }
    public bool ShowStitchPreview { get; set; } = true;
    public bool ShowGrid { get; set; } = true;
    public int GridSize { get; set; } = 10000; // 10mm in microns
    public bool SnapToGrid { get; set; } = false;

    // Property panel
    public StitchParams? SelectedObjectParams { get; private set; }
    public EmbroideryObject? SelectedObject => GetSingleSelectedObject();

    // Commands
    public IEditorCommand CreateRectangleCommand { get; }
    public IEditorCommand CreateEllipseCommand { get; }
    public IEditorCommand CreatePathCommand { get; }
    public IEditorCommand DeleteCommand { get; }
    public IEditorCommand UndoCommand { get; }
    public IEditorCommand RedoCommand { get; }
    public IEditorCommand CompileStitchesCommand { get; }

    private readonly StitchEngine _stitchEngine = new();

    public EditorViewModel(AtlasProject? project = null)
    {
        Project = new EditorProject(project);
        
        CreateRectangleCommand = new DelegateCommand(CreateRectangle, () => CurrentTool == EditorTool.CreateRectangle);
        CreateEllipseCommand = new DelegateCommand(CreateEllipse, () => CurrentTool == EditorTool.CreateEllipse);
        CreatePathCommand = new DelegateCommand(CreatePath, () => CurrentTool == EditorTool.CreatePath);
        DeleteCommand = new DelegateCommand(DeleteSelected, () => Selection.HasSelection);
        UndoCommand = new DelegateCommand(() => History.Undo(), () => History.CanUndo);
        RedoCommand = new DelegateCommand(() => History.Redo(), () => History.CanRedo);
        CompileStitchesCommand = new DelegateCommand(CompileStitches);

        // Subscribe to selection changes
        Selection.PropertyChanged += (s, e) => 
        {
            if (e.PropertyName == nameof(EditorSelection.HasSelection) || 
                e.PropertyName == nameof(EditorSelection.ObjectCount))
            {
                UpdateSelectedObjectParams();
                DeleteCommand.RaiseCanExecuteChanged();
            }
        };

        History.CanUndoChanged += () => UndoCommand.RaiseCanExecuteChanged();
        History.CanRedoChanged += () => RedoCommand.RaiseCanExecuteChanged();
    }

    private void UpdateSelectedObjectParams()
    {
        var obj = GetSingleSelectedObject();
        SelectedObjectParams = obj?.StitchParams.DeepClone();
        OnPropertyChanged(nameof(SelectedObjectParams));
        OnPropertyChanged(nameof(SelectedObject));
    }

    private EmbroideryObject? GetSingleSelectedObject()
    {
        return Selection.ObjectIds.Count == 1 
            ? Project.Project.Objects.FirstOrDefault(o => o.Id == Selection.ObjectIds.First()) 
            : null;
    }

    public void CreateRectangle()
    {
        // In real implementation, this would be handled by mouse interaction
        // Here we just demonstrate creating a centered rectangle
        var rect = new Rectangle(-50000, -30000, 100000, 60000);
        var shape = ShapeObject.CreateRectangle(rect, "Rectangle");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Tatami);
        shape.StitchParams.Density = 400;
        shape.StitchParams.ColorIndex = (byte)Project.Project.ThreadPalette.Count;
        
        if (Project.Project.ThreadPalette.Count == 0)
        {
            Project.Project.ThreadPalette.Add(new ThreadColor(255, 0, 0, "Default", "001", "Red"));
        }
        
        var cmd = new AddObjectCommand(Project, shape);
        History.Execute(cmd);
    }

    public void CreateEllipse()
    {
        var shape = ShapeObject.CreateEllipse(Point.Zero, 50000, 30000, 32, "Ellipse");
        shape.StitchParams = StitchParams.DefaultFor(StitchType.Satin);
        shape.StitchParams.ColorIndex = (byte)Project.Project.ThreadPalette.Count;
        
        var cmd = new AddObjectCommand(Project, shape);
        History.Execute(cmd);
    }

    public void CreatePath()
    {
        var path = new PathObject
        {
            Name = "Path",
            Segments = new List<PathSegment>
            {
                new LineSegment { Start = new Point(-50000, 0), End = new Point(50000, 0) }
            },
            StitchParams = StitchParams.DefaultFor(StitchType.Running)
        };
        path.StitchParams.RunningSpacing = 2000;
        path.StitchParams.ColorIndex = (byte)Project.Project.ThreadPalette.Count;
        
        var cmd = new AddObjectCommand(Project, path);
        History.Execute(cmd);
    }

    public void DeleteSelected()
    {
        foreach (var id in Selection.ObjectIds.ToList())
        {
            var cmd = new RemoveObjectCommand(Project, id);
            History.Execute(cmd);
        }
        Selection.Clear();
    }

    public void CompileStitches()
    {
        CurrentStitchPlan = _stitchEngine.Compile(Project.Project);
        OnPropertyChanged(nameof(CurrentStitchPlan));
    }

    public void UpdateObjectParams(StitchParams newParams)
    {
        var obj = GetSingleSelectedObject();
        if (obj != null)
        {
            var oldParams = obj.StitchParams.DeepClone();
            var cmd = new ChangeParamsCommand(obj, oldParams, newParams.DeepClone());
            History.Execute(cmd);
            SelectedObjectParams = obj.StitchParams.DeepClone();
            OnPropertyChanged(nameof(SelectedObjectParams));
        }
    }

    public void AddThreadColor(ThreadColor color)
    {
        Project.Project.ThreadPalette.Add(color);
        Project.Project.Touch();
        OnPropertyChanged(nameof(Project.Project.ThreadPalette));
    }

    public void RemoveThreadColor(int index)
    {
        if (index >= 0 && index < Project.Project.ThreadPalette.Count)
        {
            Project.Project.ThreadPalette.RemoveAt(index);
            Project.Project.Touch();
            OnPropertyChanged(nameof(Project.Project.ThreadPalette));
        }
    }

    public void UpdateViewportTransform(Matrix transform)
    {
        Viewport.Pan = new Point((int)transform.OffsetX, (int)transform.OffsetY);
        Viewport.Zoom = transform.M11; // Assuming uniform scale
    }

    private void UpdateToolCursors()
    {
        // Update cursor based on tool
        OnPropertyChanged(nameof(CurrentTool));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Base command implementation
/// </summary>
public abstract class BaseEditorCommand : IEditorCommand, INotifyPropertyChanged
{
    private bool _canExecute = true;
    public virtual bool CanExecute => _canExecute;
    public abstract string Description { get; }
    public abstract void Execute();
    public abstract void Undo();

    public void RaiseCanExecuteChanged() 
    { 
        OnPropertyChanged(nameof(CanExecute)); 
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? CanExecuteChanged;
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>
/// Delegate command for simple actions
/// </summary>
public sealed class DelegateCommand : BaseEditorCommand
{
    private readonly Action _execute;
    private readonly Func<bool> _canExecute;

    public DelegateCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute ?? (() => true);
    }

    public override string Description => "Action";
    public override bool CanExecute => _canExecute();

    public override void Execute() => _execute();
    public override void Undo() { /* Not undoable by default */ }
}

/// <summary>
/// Add object command (undoable)
/// </summary>
public sealed class AddObjectCommand : BaseEditorCommand
{
    private readonly EditorProject _project;
    private readonly EmbroideryObject _object;
    private readonly Guid _layerId;

    public AddObjectCommand(EditorProject project, EmbroideryObject obj)
    {
        _project = project;
        _object = obj;
        _layerId = project.GetActiveLayer().Id;
    }

    public override string Description => $"Add {_object.Name}";
    public override void Execute() => _project.AddObject(_object);
    public override void Undo() => _project.RemoveObject(_object.Id);
}

/// <summary>
/// Remove object command (undoable)
/// </summary>
public sealed class RemoveObjectCommand : BaseEditorCommand
{
    private readonly EditorProject _project;
    private readonly Guid _objectId;
    private EmbroideryObject? _removedObject;
    private Guid _layerId;
    private int _sequenceOrder;

    public RemoveObjectCommand(EditorProject project, Guid objectId)
    {
        _project = project;
        _objectId = objectId;
    }

    public override string Description => "Remove object";
    public override void Execute()
    {
        _removedObject = _project.Project.Objects.FirstOrDefault(o => o.Id == _objectId);
        if (_removedObject != null)
        {
            _layerId = _project.Layers.FirstOrDefault(l => l.Objects.Contains(_objectId))?.Id ?? Guid.Empty;
            _sequenceOrder = _removedObject.SequenceOrder;
            _project.RemoveObject(_objectId);
        }
    }
    public override void Undo()
    {
        if (_removedObject != null)
        {
            _removedObject.SequenceOrder = _sequenceOrder;
            var layer = _project.Layers.FirstOrDefault(l => l.Id == _layerId);
            layer?.Objects.Add(_objectId);
            _project.Project.Objects.Add(_removedObject);
            _project.Project.Touch();
        }
    }
}

/// <summary>
/// Change parameters command (undoable)
/// </summary>
public sealed class ChangeParamsCommand : BaseEditorCommand
{
    private readonly EmbroideryObject _object;
    private readonly StitchParams _oldParams;
    private readonly StitchParams _newParams;

    public ChangeParamsCommand(EmbroideryObject obj, StitchParams oldParams, StitchParams newParams)
    {
        _object = obj;
        _oldParams = oldParams;
        _newParams = newParams;
    }

    public override string Description => $"Change {_object.Name} params";
    public override void Execute() => _object.StitchParams = _newParams;
    public override void Undo() => _object.StitchParams = _oldParams;
}