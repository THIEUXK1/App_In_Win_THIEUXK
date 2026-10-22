using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TemNhan.Model;
using TemNhan.Rendering;

namespace TemNhan.Controls;

/// <summary>
/// Khung thiết kế: vẽ nhãn, chọn (một / nhiều / kéo khung chọn), kéo, đổi kích thước đối tượng bằng chuột.
/// </summary>
public sealed class LabelCanvas : FrameworkElement
{
    public const double Pad = 28;
    const double HandleSize = 8;
    const double SnapMm = 0.5;

    static readonly Pen SelectionPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 215)), 1.5));
    static readonly Pen LockedPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(200, 120, 0)), 1.5) { DashStyle = DashStyles.Dash });
    static readonly Pen GuidePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(90, 0, 120, 215)), 1) { DashStyle = DashStyles.Dash });
    static readonly Pen MarginPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(150, 230, 80, 80)), 1) { DashStyle = DashStyles.Dot });
    static readonly Pen MarqueePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0, 120, 215)), 1) { DashStyle = DashStyles.Dash });
    static readonly Brush MarqueeBrush = Freeze(new SolidColorBrush(Color.FromArgb(30, 0, 120, 215)));
    static readonly Brush ShadowBrush = Freeze(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)));
    static readonly Brush NonPrintBrush = Freeze(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)));
    static readonly Pen BorderPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(160, 160, 160)), 1));
    static readonly Brush DimBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)));

    LabelDocument? _doc;
    readonly List<LabelElement> _selection = [];
    IReadOnlyDictionary<string, string>? _row;
    double _zoom = 3;

    enum DragMode { None, Move, Resize, Marquee }
    DragMode _mode;
    int _handle;
    Point _startMm, _marqueeStart, _marqueeEnd;
    Rect _startRect;
    readonly Dictionary<LabelElement, Point> _moveStart = [];

    public LabelCanvas()
    {
        Focusable = true;
        FocusVisualStyle = null;
        AllowDrop = true;
    }

    public event EventHandler? SelectionChanged;
    /// <summary>Bắt đầu một thao tác chuột (để tách bước hoàn tác).</summary>
    public event EventHandler? EditStarted;
    public event EventHandler? ElementDoubleClicked;

    public bool Snap { get; set; } = true;

    public LabelDocument? Document
    {
        get => _doc;
        set
        {
            if (_doc != null) _doc.Changed -= OnDocChanged;
            _doc = value;
            if (_doc != null) _doc.Changed += OnDocChanged;
            SetSelection([]);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>Đối tượng chính (bấm sau cùng) — bảng thuộc tính hiển thị đối tượng này.</summary>
    public LabelElement? Selected
    {
        get => _selection.Count > 0 ? _selection[^1] : null;
        set => SetSelection(value == null ? [] : WithGroup([value]));
    }

    public IReadOnlyList<LabelElement> SelectedItems => _selection;

    public void SetSelection(IEnumerable<LabelElement> items)
    {
        var list = items.Distinct().ToList();
        if (list.SequenceEqual(_selection)) return;
        _selection.Clear();
        _selection.AddRange(list);
        InvalidateVisual();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Thêm các đối tượng cùng nhóm.</summary>
    IEnumerable<LabelElement> WithGroup(IEnumerable<LabelElement> items)
    {
        if (_doc == null) return items;
        var groups = items.Select(i => i.GroupId).Where(g => g != null).ToHashSet();
        return items.Concat(_doc.Elements.Where(e => e.GroupId != null && groups.Contains(e.GroupId)));
    }

    public IReadOnlyDictionary<string, string>? Row
    {
        get => _row;
        set { _row = value; InvalidateVisual(); }
    }

    public double Zoom
    {
        get => _zoom;
        set { _zoom = Math.Clamp(value, 0.5, 20); InvalidateMeasure(); InvalidateVisual(); }
    }

    double Scale => LabelRenderer.DipPerMm * _zoom;

    void OnDocChanged(string key)
    {
        // Đối tượng đã bị xoá khỏi trang thì bỏ khỏi vùng chọn
        if (_doc != null && key.StartsWith("structure") && _selection.Any(s => !_doc.Elements.Contains(s)))
            SetSelection(_selection.Where(_doc.Elements.Contains).ToList());
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>Vị trí (DIP) của góc trên-trái nhãn trong control.</summary>
    double _ox = Pad, _oy = Pad;
    public double OriginX => _ox;
    public double OriginY => _oy;

    /// <summary>Gốc toạ độ thay đổi (để thước kẻ cập nhật theo).</summary>
    public event EventHandler? OriginChanged;

    /// <summary>
    /// Vùng control bao cả nhãn lẫn các đối tượng tràn ra ngoài nhãn — để vẫn bấm/kéo được
    /// các nút đổi kích thước nằm ngoài nhãn. Trong lúc kéo giữ nguyên gốc toạ độ để đối tượng không bị nhảy.
    /// </summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (_doc == null) return new Size(0, 0);
        double minX = 0, minY = 0, maxX = _doc.WidthMm, maxY = _doc.HeightMm;
        foreach (var el in _doc.Elements)
        {
            minX = Math.Min(minX, el.X);
            minY = Math.Min(minY, el.Y);
            maxX = Math.Max(maxX, el.X + el.W);
            maxY = Math.Max(maxY, el.Y + el.H);
        }

        if (_mode == DragMode.None)
        {
            double ox = Pad - minX * Scale, oy = Pad - minY * Scale;
            if (Math.Abs(ox - _ox) > 0.01 || Math.Abs(oy - _oy) > 0.01)
            {
                _ox = ox;
                _oy = oy;
                OriginChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        return new Size(Math.Max(_ox + maxX * Scale, 0) + Pad, Math.Max(_oy + maxY * Scale, 0) + Pad);
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Nền trong suốt để nhận chuột trên toàn vùng
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_doc == null) return;

        var label = new Rect(_ox, _oy, _doc.WidthMm * Scale, _doc.HeightMm * Scale);
        dc.DrawRectangle(ShadowBrush, null, new Rect(label.X + 3, label.Y + 3, label.Width, label.Height));

        dc.PushTransform(new TranslateTransform(_ox, _oy));
        LabelRenderer.Render(dc, _doc, new RenderContext
        {
            Scale = Scale,
            Row = _row,
            PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip,
            ShowErrors = true,
            Paper = PaperBrush(_doc.PaperColor),
        });
        dc.Pop();
        dc.DrawRectangle(null, BorderPen, label);
        DrawDimension(dc, label);

        // Vạch lề
        if (_doc.MarginMm > 0)
        {
            var m = label;
            m.Inflate(-_doc.MarginMm * Scale, -_doc.MarginMm * Scale);
            if (m.Width > 0 && m.Height > 0) dc.DrawRectangle(null, MarginPen, m);
        }

        foreach (var el in _doc.Elements)
        {
            var r = ScreenRect(el);
            if (!el.Printable) dc.DrawRectangle(NonPrintBrush, null, r); // làm mờ đối tượng không in
            if (!_selection.Contains(el)) dc.DrawRectangle(null, GuidePen, r);
        }

        if (_dropTarget != null)
            dc.DrawRectangle(DropBrush, DropPen, ScreenRect(_dropTarget));

        foreach (var el in _selection)
            dc.DrawRectangle(null, el.Locked ? LockedPen : SelectionPen, ScreenRect(el));

        // Nút đổi kích thước chỉ khi chọn đúng một đối tượng chưa khoá
        if (_selection.Count == 1 && !_selection[0].Locked)
            foreach (var h in Handles(ScreenRect(_selection[0])))
                dc.DrawRectangle(Brushes.White, SelectionPen, h);

        if (_mode == DragMode.Marquee)
            dc.DrawRectangle(MarqueeBrush, MarqueePen, new Rect(_marqueeStart, _marqueeEnd));
    }

    /// <summary>Ghi kích thước cạnh nhãn (như "24 mm" của P-touch): chiều cao bên trái, chiều rộng phía trên.</summary>
    void DrawDimension(DrawingContext dc, Rect label)
    {
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var tf = new Typeface("Segoe UI");
        string H(double v) => v.ToString("0.#", CultureInfo.InvariantCulture) + " mm";

        var h = new FormattedText(H(_doc!.HeightMm), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, DimBrush, ppd);
        if (label.X >= 18 && label.Height > h.Width)
        {
            var at = new Point(label.X - 16, label.Y + label.Height / 2 + h.Width / 2);
            dc.PushTransform(new RotateTransform(-90, at.X, at.Y));
            dc.DrawText(h, at);
            dc.Pop();
        }
        var w = new FormattedText(H(_doc.WidthMm), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, DimBrush, ppd);
        if (label.Y >= 18 && label.Width > w.Width)
            dc.DrawText(w, new Point(label.X + (label.Width - w.Width) / 2, label.Y - 17));
    }

    static Brush PaperBrush(string hex)
    {
        try { return Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex))); }
        catch (FormatException) { return Brushes.White; }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        // Chuột phải chọn đối tượng dưới con trỏ (giữ vùng chọn nếu bấm trúng đối tượng đã chọn)
        Focus();
        var hit = HitElement(e.GetPosition(this));
        if (hit == null || !_selection.Contains(hit)) Selected = hit;
    }

    Rect ScreenRect(LabelElement el) => new(_ox + el.X * Scale, _oy + el.Y * Scale, el.W * Scale, el.H * Scale);

    Point ToMm(Point screen) => new((screen.X - _ox) / Scale, (screen.Y - _oy) / Scale);

    /// <summary>Thứ tự: 0 TL, 1 T, 2 TR, 3 R, 4 BR, 5 B, 6 BL, 7 L.</summary>
    static Rect[] Handles(Rect r)
    {
        double s = HandleSize, cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
        Point[] pts =
        [
            r.TopLeft, new(cx, r.Top), r.TopRight, new(r.Right, cy),
            r.BottomRight, new(cx, r.Bottom), r.BottomLeft, new(r.Left, cy),
        ];
        return pts.Select(p => new Rect(p.X - s / 2, p.Y - s / 2, s, s)).ToArray();
    }

    int HitHandle(Point screen)
    {
        if (_selection.Count != 1 || _selection[0].Locked) return -1;
        var hs = Handles(ScreenRect(_selection[0]));
        for (int i = 0; i < hs.Length; i++)
        {
            var h = hs[i];
            h.Inflate(3, 3);
            if (h.Contains(screen)) return i;
        }
        return -1;
    }

    LabelElement? HitElement(Point screen)
    {
        if (_doc == null) return null;
        // Ưu tiên đối tượng trên cùng; khung/đường kẻ chỉ bắt khi trỏ gần viền để còn chọn được đối tượng bên trong.
        for (int i = _doc.Elements.Count - 1; i >= 0; i--)
        {
            var el = _doc.Elements[i];
            var r = ScreenRect(el);
            var outer = r;
            outer.Inflate(4, 4);
            if (!outer.Contains(screen)) continue;
            if (el is FrameElement { Filled: false })
            {
                var inner = r;
                inner.Inflate(-6, -6);
                if (inner.Width > 0 && inner.Height > 0 && inner.Contains(screen)) continue;
            }
            return el;
        }
        // Không trúng viền nào → cho phép chọn khung bằng cách bấm vào bên trong
        for (int i = _doc.Elements.Count - 1; i >= 0; i--)
            if (ScreenRect(_doc.Elements[i]).Contains(screen)) return _doc.Elements[i];
        return null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        bool additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        int h = HitHandle(p);
        _mode = DragMode.None;

        if (h >= 0)
        {
            _mode = DragMode.Resize;
            _handle = h;
            var s = _selection[0];
            _startRect = new Rect(s.X, s.Y, s.W, s.H);
        }
        else if (HitElement(p) is { } hit)
        {
            var group = WithGroup([hit]).ToList();
            if (additive)
            {
                // Ctrl/Shift + bấm: thêm / bỏ khỏi vùng chọn
                SetSelection(_selection.Contains(hit) ? _selection.Except(group) : _selection.Concat(group));
            }
            else if (!_selection.Contains(hit))
                SetSelection(group);
            else if (!ReferenceEquals(Selected, hit))
                SetSelection(_selection.Where(x => !ReferenceEquals(x, hit)).Append(hit)); // đưa lên làm đối tượng chính

            if (_selection.Contains(hit) && _selection.Any(x => !x.Locked))
            {
                _mode = DragMode.Move;
                _moveStart.Clear();
                foreach (var x in _selection.Where(x => !x.Locked)) _moveStart[x] = new Point(x.X, x.Y);
            }
        }
        else
        {
            // Bấm vào chỗ trống: kéo khung chọn
            if (!additive) SetSelection([]);
            _mode = DragMode.Marquee;
            _marqueeStart = _marqueeEnd = p;
        }

        if (_mode != DragMode.None)
        {
            _startMm = ToMm(p);
            CaptureMouse();
            if (_mode != DragMode.Marquee) EditStarted?.Invoke(this, EventArgs.Empty);
        }
        if (e.ClickCount == 2 && Selected != null) ElementDoubleClicked?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_mode == DragMode.None)
        {
            int h = HitHandle(p);
            var hit = HitElement(p);
            Cursor = h >= 0 ? HandleCursor(h) : hit is { Locked: false } ? Cursors.SizeAll : Cursors.Arrow;
            return;
        }

        if (_mode == DragMode.Marquee)
        {
            _marqueeEnd = p;
            InvalidateVisual();
            return;
        }

        var mm = ToMm(p);
        double dx = mm.X - _startMm.X, dy = mm.Y - _startMm.Y;
        bool snap = Snap && !Keyboard.IsKeyDown(Key.LeftAlt) && !Keyboard.IsKeyDown(Key.RightAlt);
        double S(double v) => snap ? Math.Round(v / SnapMm) * SnapMm : v;

        if (_mode == DragMode.Move)
        {
            // Bắt lưới theo đối tượng chính, các đối tượng khác dịch cùng một khoảng
            var lead = _moveStart.ContainsKey(Selected!) ? Selected! : _moveStart.Keys.First();
            var ls = _moveStart[lead];
            double sdx = S(ls.X + dx) - ls.X, sdy = S(ls.Y + dy) - ls.Y;
            foreach (var (el, start) in _moveStart)
            {
                el.X = start.X + sdx;
                el.Y = start.Y + sdy;
            }
            return;
        }

        var sel = _selection[0];
        double l = _startRect.Left, t = _startRect.Top, r = _startRect.Right, b = _startRect.Bottom;
        if (_handle is 0 or 6 or 7) l = S(l + dx);
        if (_handle is 2 or 3 or 4) r = S(r + dx);
        if (_handle is 0 or 1 or 2) t = S(t + dy);
        if (_handle is 4 or 5 or 6) b = S(b + dy);
        const double min = 1;
        if (r - l < min) { if (_handle is 0 or 6 or 7) l = r - min; else r = l + min; }
        if (b - t < min) { if (_handle is 0 or 1 or 2) t = b - min; else b = t + min; }
        sel.X = l;
        sel.Y = t;
        sel.W = r - l;
        sel.H = b - t;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_mode == DragMode.Marquee && _doc != null)
        {
            var box = new Rect(_marqueeStart, _marqueeEnd);
            if (box.Width > 3 || box.Height > 3)
            {
                var inside = _doc.Elements.Where(el => box.IntersectsWith(ScreenRect(el)));
                bool additive = (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
                SetSelection(WithGroup(additive ? _selection.Concat(inside) : inside).ToList());
            }
        }
        _mode = DragMode.None;
        ReleaseMouseCapture();
        InvalidateMeasure(); // tính lại gốc toạ độ sau khi kéo xong
        InvalidateVisual();
    }

    // ---- Kéo tiêu đề cột từ bảng dữ liệu thả vào nhãn ----

    /// <summary>Định dạng dữ liệu kéo-thả: tên cột Excel.</summary>
    public const string FieldFormat = "TemNhan.Field";

    static readonly Pen DropPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x1A, 0x56, 0xC4)), 2.5));
    static readonly Brush DropBrush = Freeze(new SolidColorBrush(Color.FromArgb(40, 0x1A, 0x56, 0xC4)));
    LabelElement? _dropTarget;

    /// <summary>Thả một trường dữ liệu: (tên cột, đối tượng chữ/mã vạch dưới con trỏ hoặc null, vị trí mm).</summary>
    public event Action<string, LabelElement?, Point>? FieldDropped;

    LabelElement? DropTargetAt(Point screen) => HitElement(screen) is TextElement or BarcodeElement ? HitElement(screen) : null;

    protected override void OnDragOver(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(FieldFormat)) return;
        e.Effects = DragDropEffects.Copy;
        var t = DropTargetAt(e.GetPosition(this));
        if (!ReferenceEquals(t, _dropTarget)) { _dropTarget = t; InvalidateVisual(); }
        e.Handled = true;
    }

    protected override void OnDragLeave(DragEventArgs e)
    {
        _dropTarget = null;
        InvalidateVisual();
    }

    protected override void OnDrop(DragEventArgs e)
    {
        if (e.Data.GetData(FieldFormat) is not string field) return;
        var p = e.GetPosition(this);
        var target = DropTargetAt(p);
        _dropTarget = null;
        InvalidateVisual();
        FieldDropped?.Invoke(field, target, ToMm(p));
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        Zoom *= e.Delta > 0 ? 1.15 : 1 / 1.15;
        ZoomChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    public event EventHandler? ZoomChanged;

    static Cursor HandleCursor(int h) => h switch
    {
        0 or 4 => Cursors.SizeNWSE,
        2 or 6 => Cursors.SizeNESW,
        1 or 5 => Cursors.SizeNS,
        _ => Cursors.SizeWE,
    };

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
