namespace FastShot;

/// <summary>Where a video canvas is on its timeline.</summary>
public sealed class TimeContext
{
    public double Current { get; set; }
    public double Duration { get; set; }
    /// <summary>How long a newly drawn mark stays on screen; null keeps it to the end.</summary>
    public double? DefaultLength { get; set; }
    /// <summary>While recording, marks stay open until cleared and only open ones are shown.</summary>
    public bool Live { get; set; }
}

/// <summary>Owns a document's annotations and turns mouse gestures into edits: drawing, selecting,
/// moving, reshaping, restyling, deleting, undo and redo. Canvases only translate events into
/// canvas points and paint what this reports.</summary>
public sealed class AnnotationController
{
    public List<Annotation> Annotations { get; private set; } = [];
    public Guid? SelectedId { get; private set; }
    public ToolState Tools { get; }
    public TimeContext? Time { get; set; }
    /// <summary>Precise "now" for live recordings, read the moment a mark is started.</summary>
    public Func<double>? Clock { get; set; }
    public IReadOnlyList<EditorTool> AllowedTools { get; set; } = EditorTools.All;
    /// <summary>Hidden from painting while its text field is open.</summary>
    public Guid? EditingId { get; set; }

    public Action? OnChange { get; set; }
    public Action? OnDisplay { get; set; }
    public Action? OnSelectionChange { get; set; }
    public Action<Guid>? OnBeginTextEditing { get; set; }

    readonly List<List<Annotation>> undoStack = [];
    readonly List<List<Annotation>> redoStack = [];
    /// <summary>A text mark created by a click; removing it on empty commit also drops its undo step.</summary>
    Guid? freshTextId;

    enum DragKind { None, Creating, Moving, Resizing }
    DragKind drag;
    Guid dragId;
    Point dragPoint;
    bool dragRegistered;
    AnnotationHandle dragHandle;
    Annotation? dragOriginal;

    public AnnotationController(ToolState tools)
    {
        Tools = tools;
        tools.OnStyleChange = ApplyStyle;
    }

    // MARK: Reading

    public Annotation? Selected => SelectedId is { } id ? Find(id) : null;

    public Annotation? Find(Guid id) => Annotations.FirstOrDefault(a => a.Id == id);

    public bool IsDragging => drag != DragKind.None;

    /// <summary>Marks to paint now: on a video canvas only those alive at the playhead.</summary>
    public List<Annotation> Visible
    {
        get
        {
            if (Time is not { } time) return Annotations;
            return Annotations.Where(a =>
                a.Id == EditingId || (drag == DragKind.Creating && a.Id == dragId) ||
                (time.Live ? a.End is null : a.IsVisible(time.Current))).ToList();
        }
    }

    public int NextStepNumber => (Annotations.Where(a => a.Kind == AnnotationKind.step).Select(a => a.Number).DefaultIfEmpty(0).Max()) + 1;

    // MARK: Editing

    public void Load(IEnumerable<Annotation> items)
    {
        Annotations = items.Select(a => a.Clone()).ToList();
        undoStack.Clear();
        redoStack.Clear();
        Select(null);
        UpdateUndoState();
        OnDisplay?.Invoke();
    }

    public void Select(Guid? id)
    {
        var previous = SelectedId;
        SelectedId = id;
        if (Selected is { } selected) Tools.Sync(selected);
        Tools.HasSelection = id is not null;
        if (previous != id) OnSelectionChange?.Invoke();
        OnDisplay?.Invoke();
    }

    List<Annotation> Snapshot() => Annotations.Select(a => a.Clone()).ToList();

    public void RegisterUndo()
    {
        undoStack.Add(Snapshot());
        if (undoStack.Count > 200) undoStack.RemoveAt(0);
        redoStack.Clear();
        UpdateUndoState();
    }

    public void Undo()
    {
        if (undoStack.Count == 0) return;
        redoStack.Add(Snapshot());
        Annotations = undoStack[^1];
        undoStack.RemoveAt(undoStack.Count - 1);
        freshTextId = null;
        if (Selected is null) Select(null);
        Changed();
    }

    public void Redo()
    {
        if (redoStack.Count == 0) return;
        undoStack.Add(Snapshot());
        Annotations = redoStack[^1];
        redoStack.RemoveAt(redoStack.Count - 1);
        if (Selected is null) Select(null);
        Changed();
    }

    public void Update(Guid id, Action<Annotation> body, bool notify = true)
    {
        var a = Find(id);
        if (a is null) return;
        body(a);
        if (notify) Changed(); else OnDisplay?.Invoke();
    }

    void Replace(Guid id, Annotation replacement)
    {
        var index = Annotations.FindIndex(a => a.Id == id);
        if (index >= 0) Annotations[index] = replacement;
        OnDisplay?.Invoke();
    }

    public void Add(Annotation annotation)
    {
        RegisterUndo();
        Annotations.Add(annotation);
        Select(annotation.Id);
        Changed();
    }

    public void DeleteSelected()
    {
        if (SelectedId is not { } id) return;
        RegisterUndo();
        Annotations.RemoveAll(a => a.Id == id);
        Select(null);
        Changed();
    }

    public void ClearAll()
    {
        if (Annotations.Count == 0) return;
        RegisterUndo();
        Annotations.Clear();
        Select(null);
        Changed();
    }

    public void NudgeSelected(double dx, double dy)
    {
        if (SelectedId is not { } id) return;
        RegisterUndo();
        Update(id, a => a.Translate(dx, dy));
    }

    /// <summary>Shifts every mark, history included, so they stay put while the canvas origin moves.</summary>
    public void TranslateAll(double dx, double dy)
    {
        foreach (var a in Annotations) a.Translate(dx, dy);
        foreach (var state in undoStack.Concat(redoStack))
            foreach (var a in state) a.Translate(dx, dy);
        OnDisplay?.Invoke();
    }

    /// <summary>Ends every mark still open on a live recording, as the "clear" button does.</summary>
    public void CloseOpenAnnotations(double time)
    {
        if (!Annotations.Any(a => a.End is null)) return;
        RegisterUndo();
        foreach (var a in Annotations.Where(a => a.End is null)) a.End = Math.Max(time, a.Start ?? 0);
        Select(null);
        Changed();
    }

    public void FinishTextEditing(Guid id, string text)
    {
        EditingId = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            Annotations.RemoveAll(a => a.Id == id);
            if (freshTextId == id && undoStack.Count > 0) undoStack.RemoveAt(undoStack.Count - 1);
            Select(null);
        }
        else
        {
            Update(id, a => a.Text = text, notify: false);
        }
        freshTextId = null;
        Changed();
    }

    void ApplyStyle(StyleChange change)
    {
        if (SelectedId is not { } id) return;
        RegisterUndo();
        Update(id, a =>
        {
            switch (change)
            {
                case StyleChange.Color: a.Color = Tools.Color; break;
                case StyleChange.Size: a.LineWidth = Tools.LineWidth; a.FontSize = Tools.FontSize; break;
                case StyleChange.Filled: a.Filled = Tools.Filled; break;
            }
        });
    }

    void Changed()
    {
        UpdateUndoState();
        OnChange?.Invoke();
        OnDisplay?.Invoke();
    }

    void UpdateUndoState()
    {
        Tools.CanUndo = undoStack.Count > 0;
        Tools.CanRedo = redoStack.Count > 0;
    }

    // MARK: Hit testing

    public Guid? HitTest(Point p, double tolerance) => Visible.LastOrDefault(a => a.Contains(p, tolerance))?.Id;

    public AnnotationHandle? HandleAt(Point p, double tolerance)
    {
        if (Selected is not { } selected || !Visible.Any(a => a.Id == selected.Id)) return null;
        foreach (var (handle, point) in selected.Handles)
            if (Geo.Distance(point, p) <= tolerance) return handle;
        return null;
    }

    // MARK: Mouse

    public enum MouseResult { Handled, Background }

    public MouseResult MouseDown(Point p, int clickCount, bool shift, double tolerance)
    {
        var hit = HitTest(p, tolerance);
        if (clickCount >= 2 && hit is { } doubleHit && Find(doubleHit)?.Kind == AnnotationKind.text)
        {
            Select(doubleHit);
            BeginTextEditing(doubleHit);
            return MouseResult.Handled;
        }
        if (Selected is { } selected && HandleAt(p, tolerance * 1.5) is { } handle)
        {
            RegisterUndo();
            drag = DragKind.Resizing;
            dragId = selected.Id;
            dragHandle = handle;
            dragOriginal = selected.Clone();
            return MouseResult.Handled;
        }
        if (Tools.Tool == EditorTool.Select)
        {
            if (hit is not { } selectHit)
            {
                Select(null);
                return MouseResult.Background;
            }
            Select(selectHit);
            BeginMove(selectHit, p, registered: false);
            return MouseResult.Handled;
        }
        if (Tools.Tool.Kind() is not { } kind || !AllowedTools.Contains(Tools.Tool)) return MouseResult.Background;
        if (kind == AnnotationKind.text && hit is { } textHit && Find(textHit)?.Kind == AnnotationKind.text)
        {
            Select(textHit);
            BeginTextEditing(textHit);
            return MouseResult.Handled;
        }
        if (hit is { } own && own == SelectedId)
        {
            BeginMove(own, p, registered: false);
            return MouseResult.Handled;
        }
        RegisterUndo();
        var annotation = MakeAnnotation(kind, p);
        Annotations.Add(annotation);
        Select(annotation.Id);
        switch (kind)
        {
            case AnnotationKind.text:
                freshTextId = annotation.Id;
                BeginTextEditing(annotation.Id);
                break;
            case AnnotationKind.step:
                BeginMove(annotation.Id, p, registered: true);
                break;
            default:
                drag = DragKind.Creating;
                dragId = annotation.Id;
                dragPoint = p;
                break;
        }
        Changed();
        return MouseResult.Handled;
    }

    void BeginMove(Guid id, Point p, bool registered)
    {
        drag = DragKind.Moving;
        dragId = id;
        dragPoint = p;
        dragRegistered = registered;
    }

    public void MouseDragged(Point p, bool shift)
    {
        switch (drag)
        {
            case DragKind.Creating:
            {
                var origin = dragPoint;
                Update(dragId, a =>
                {
                    switch (a.Kind)
                    {
                        case AnnotationKind.pen or AnnotationKind.marker:
                            if (a.Points.Count > 0 && Geo.Distance(a.Points[^1], p) >= 0.75) a.Points.Add(p);
                            break;
                        case AnnotationKind.line or AnnotationKind.arrow:
                            a.Points = [origin, shift ? Geo.SnapAngle(origin, p) : p];
                            break;
                        case AnnotationKind.rectangle or AnnotationKind.ellipse or AnnotationKind.pixelate:
                            a.Points = [origin, shift ? Geo.SquareCorner(origin, p) : p];
                            break;
                    }
                }, notify: false);
                break;
            }
            case DragKind.Moving:
            {
                if (!dragRegistered) { RegisterUndo(); dragRegistered = true; }
                var last = dragPoint;
                Update(dragId, a => a.Translate(p.X - last.X, p.Y - last.Y), notify: false);
                dragPoint = p;
                break;
            }
            case DragKind.Resizing:
                if (dragOriginal is not null) Replace(dragId, Annotation.Resized(dragOriginal, dragHandle, p, shift));
                break;
        }
    }

    public void MouseUp()
    {
        if (drag == DragKind.Creating && Find(dragId) is { } a)
        {
            if (a.IsDegenerate && a.Kind is not (AnnotationKind.pen or AnnotationKind.marker))
            {
                Annotations.RemoveAll(x => x.Id == a.Id);
                if (undoStack.Count > 0) undoStack.RemoveAt(undoStack.Count - 1);
                Select(null);
            }
            else if (a.UsesBox)
            {
                var box = a.Box;
                a.Points = [box.TopLeft, box.BottomRight];
            }
        }
        var wasDragging = IsDragging;
        drag = DragKind.None;
        dragOriginal = null;
        if (wasDragging) Changed();
    }

    Annotation MakeAnnotation(AnnotationKind kind, Point p)
    {
        var a = new Annotation(kind, [p, p], Tools.Color, Tools.LineWidth, Tools.FontSize, Tools.Filled);
        if (kind is AnnotationKind.pen or AnnotationKind.marker or AnnotationKind.text or AnnotationKind.step) a.Points = [p];
        if (kind == AnnotationKind.step) a.Number = NextStepNumber;
        if (Time is { } time)
        {
            var now = Clock?.Invoke() ?? time.Current;
            a.Start = now;
            a.End = time.Live ? null : time.DefaultLength is { } length ? Math.Min(time.Duration, now + length) : null;
        }
        return a;
    }

    void BeginTextEditing(Guid id)
    {
        EditingId = id;
        drag = DragKind.None;
        OnBeginTextEditing?.Invoke(id);
        OnDisplay?.Invoke();
    }

    // MARK: Cursor

    public Cursor? CursorAt(Point p, double tolerance)
    {
        if (HandleAt(p, tolerance * 1.5) is { } handle) return CursorFor(handle);
        if (HitTest(p, tolerance) is { } hit)
        {
            if (Tools.Tool == EditorTool.Text && Find(hit)?.Kind == AnnotationKind.text) return Cursors.IBeam;
            if (Tools.Tool == EditorTool.Select || hit == SelectedId) return Cursors.SizeAll;
        }
        return Tools.Tool switch
        {
            EditorTool.Select => null,
            EditorTool.Text => Cursors.IBeam,
            _ => Cursors.Cross,
        };
    }

    public static Cursor CursorFor(AnnotationHandle handle) => handle switch
    {
        AnnotationHandle.TopLeft or AnnotationHandle.BottomRight => Cursors.SizeNWSE,
        AnnotationHandle.TopRight or AnnotationHandle.BottomLeft => Cursors.SizeNESW,
        AnnotationHandle.Top or AnnotationHandle.Bottom => Cursors.SizeNS,
        AnnotationHandle.Left or AnnotationHandle.Right => Cursors.SizeWE,
        _ => Cursors.Cross,
    };
}

/// <summary>Keys every editing surface understands.</summary>
public static class EditorShortcuts
{
    public static Key KeyOf(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    public static bool Handle(KeyEventArgs e, AnnotationController controller)
    {
        var modifiers = Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt);
        var key = KeyOf(e);
        if (modifiers == ModifierKeys.Control && key == Key.Z) { controller.Undo(); return true; }
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Z) { controller.Redo(); return true; }
        if (modifiers == ModifierKeys.Control && key == Key.Y) { controller.Redo(); return true; }
        if (modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            var step = modifiers == ModifierKeys.Shift ? 10 : 1;
            var selected = controller.Selected is not null;
            switch (key)
            {
                case Key.Delete or Key.Back:
                    if (!selected) return false;
                    controller.DeleteSelected();
                    return true;
                case Key.Left when selected: controller.NudgeSelected(-step, 0); return true;
                case Key.Right when selected: controller.NudgeSelected(step, 0); return true;
                case Key.Down when selected: controller.NudgeSelected(0, step); return true;
                case Key.Up when selected: controller.NudgeSelected(0, -step); return true;
            }
        }
        if (modifiers == ModifierKeys.None)
        {
            foreach (var tool in controller.AllowedTools)
            {
                if (tool.Shortcut() != key) continue;
                controller.Tools.Tool = tool;
                return true;
            }
        }
        return false;
    }
}
