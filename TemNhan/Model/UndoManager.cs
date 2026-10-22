namespace TemNhan.Model;

/// <summary>Hoàn tác theo ảnh chụp JSON. Các thay đổi liên tiếp cùng khoá trong 2 giây được gộp làm một.</summary>
public sealed class UndoManager
{
    readonly Stack<string> _undo = new(), _redo = new();
    string _current = "";
    string? _lastKey;
    DateTime _lastTime;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Reset(string state)
    {
        _undo.Clear();
        _redo.Clear();
        _current = state;
        _lastKey = null;
    }

    public void Record(string state, string key)
    {
        if (state == _current) return;
        var now = DateTime.UtcNow;
        bool merge = key == _lastKey && (now - _lastTime).TotalSeconds < 2;
        if (!merge) _undo.Push(_current);
        _redo.Clear();
        _current = state;
        _lastKey = key;
        _lastTime = now;
    }

    /// <summary>Thay đổi kế tiếp luôn tạo bước hoàn tác mới.</summary>
    public void BreakMerge() => _lastKey = null;

    public string? Undo() => Step(_undo, _redo);
    public string? Redo() => Step(_redo, _undo);

    string? Step(Stack<string> from, Stack<string> to)
    {
        if (from.Count == 0) return null;
        to.Push(_current);
        _current = from.Pop();
        _lastKey = null;
        return _current;
    }
}
