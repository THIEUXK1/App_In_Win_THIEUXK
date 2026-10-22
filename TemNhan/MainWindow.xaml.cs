using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TemNhan.Controls;
using TemNhan.Data;
using TemNhan.Model;
using TemNhan.Printing;
using TemNhan.Rendering;

namespace TemNhan;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    const string TemplateFilter = "Mẫu nhãn (*.tnl;*.lbx)|*.tnl;*.lbx|Mẫu Tem in(THIEUXK) (*.tnl)|*.tnl|Brother P-touch (*.lbx)|*.lbx";

    readonly UndoManager _undo = new();
    LabelDocument _doc = new();
    string? _filePath;
    string _displayName = "Nhãn mới";
    bool _dirty;

    DataTable? _data;
    string? _dataPath, _dataSheet;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        Canvas.SelectionChanged += (_, _) => Raise(nameof(Selected), nameof(HasSelection), nameof(NoSelection), nameof(PanelTitle), nameof(IsMulti), nameof(IsSingle));
        Canvas.EditStarted += (_, _) => _undo.BreakMerge();
        Canvas.ElementDoubleClicked += (_, _) => FocusContentBox();
        Canvas.ZoomChanged += (_, _) => OnZoomChanged();
        Canvas.FieldDropped += OnFieldDropped;
        Canvas.OriginChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateRulers);

        try { Printers = LabelPrinter.GetPrinters(); }
        catch (Exception ex) { SetStatus("Không đọc được danh sách máy in: " + ex.Message); }
        _printer = AppSettings.Current.Printer is { } p && Printers.Contains(p) ? p : LabelPrinter.GetDefaultPrinter();

        Raise(nameof(Printers), nameof(SelectedPrinter));
        LoadPrinterMedia(_printer);

        LoadProject(new LabelProject { Sheets = [CreateDefaultDocument()] }, 0);
        _undo.Reset(Snapshot());
        Loaded += (_, _) => FitToView();
    }

    public sealed record PaperColorItem(string Name, string Hex);

    public List<PaperColorItem> PaperColors { get; } =
    [
        new("Trắng", "#FFFFFF"), new("Vàng", "#FFED00"), new("Xanh lá", "#9BE07A"), new("Xanh dương", "#9FD2F5"),
        new("Đỏ", "#F47C7C"), new("Cam", "#FFB65C"), new("Hồng", "#F7B6D2"), new("Bạc", "#D9D9D9"),
    ];

    public List<string> Printers { get; } = [];

    string? _printer;
    /// <summary>Máy in đang chọn ở bảng trái — hộp thoại In sẽ chọn sẵn máy này.</summary>
    public string? SelectedPrinter
    {
        get => _printer;
        set
        {
            _printer = value;
            AppSettings.Current.Printer = value;
            AppSettings.Current.Save();
            Raise(nameof(SelectedPrinter));
            LoadPrinterMedia(value);
        }
    }

    bool _snap = true;
    public bool Snap
    {
        get => _snap;
        set { _snap = value; Canvas.Snap = value; Raise(nameof(Snap)); }
    }

    public bool LeftVisible
    {
        get => LeftCol.Width.Value > 0;
        set { LeftCol.Width = new GridLength(value ? 236 : 0); Raise(nameof(LeftVisible), nameof(LeftHidden)); Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateRulers); }
    }
    public bool LeftHidden => !LeftVisible;

    public bool RightVisible
    {
        get => RightCol.Width.Value > 0;
        set { RightCol.Width = new GridLength(value ? 290 : 0); Raise(nameof(RightVisible), nameof(RightHidden)); }
    }
    public bool RightHidden => !RightVisible;

    public string PanelTitle => Canvas.SelectedItems.Count > 1 ? $"{Canvas.SelectedItems.Count} đối tượng" : Selected?.KindName ?? "Chưa chọn";
    public bool IsMulti => Canvas.SelectedItems.Count > 1;
    public bool IsSingle => Canvas.SelectedItems.Count == 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    void Raise(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    // ---- Thuộc tính cho binding ----

    public LabelDocument Doc => _doc;
    public LabelElement? Selected => Canvas.Selected;
    public bool HasSelection => Selected != null;
    public bool NoSelection => Selected == null;
    public bool HasData => _data != null;
    public DataView? DataView => _data?.DefaultView;
    public ObservableCollection<string> Columns { get; } = new();
    public List<string> FontNames { get; } = Fonts.SystemFontFamilies.Select(f => f.Source).Distinct().OrderBy(s => s).ToList();

    // ---- Tài liệu ----

    static LabelDocument CreateDefaultDocument()
    {
        var d = new LabelDocument { WidthMm = 50, HeightMm = 30 };
        d.Elements.Add(new TextElement { Text = "Tem nhãn", X = 2, Y = 2, W = 46, H = 26, FontSizePt = 20, Bold = true });
        return d;
    }

    // ---- Nhiều trang (như Sheet của P-touch) ----

    /// <summary>Các trang của dự án; trang đang mở là <see cref="Doc"/>.</summary>
    public ObservableCollection<LabelDocument> Sheets { get; } = new();

    public LabelDocument? ActiveSheet
    {
        get => _doc;
        set
        {
            if (value == null || ReferenceEquals(value, _doc) || !Sheets.Contains(value)) return;
            SetDocument(value);
            FitToView();
        }
    }

    public bool HasManySheets => Sheets.Count > 1;

    /// <summary>Ảnh chụp toàn bộ dự án cho hoàn tác (không gồm trang đang mở).</summary>
    string Snapshot() => new LabelProject { Sheets = Sheets.ToList() }.Serialize();

    void LoadProject(LabelProject project, int active)
    {
        Sheets.Clear();
        foreach (var s in project.Sheets) Sheets.Add(s);
        SetDocument(Sheets[Math.Clamp(active, 0, Sheets.Count - 1)]);
        Raise(nameof(HasManySheets));
    }

    void SetDocument(LabelDocument doc, int selectIndex = -1)
    {
        _doc.Changed -= OnDocChanged;
        _doc = doc;
        _doc.Changed += OnDocChanged;
        Canvas.Document = doc;
        if (selectIndex >= 0 && selectIndex < doc.Elements.Count) Canvas.Selected = doc.Elements[selectIndex];
        Raise(nameof(Doc), nameof(ActiveSheet), nameof(IsLandscape), nameof(IsPortrait));
        UpdateTitle();
        CheckFields();
        UpdateRulers();
        SyncPreset();
        SyncPrinterMedia();
    }

    int _sheetOps;

    /// <summary>Ghi một bước hoàn tác cho thao tác trên trang (thêm/xoá/đổi tên…).</summary>
    void RecordSheets()
    {
        _undo.BreakMerge();
        _undo.Record(Snapshot(), "sheets#" + ++_sheetOps);
        MarkDirty();
        Raise(nameof(HasManySheets));
    }

    void MarkDirty()
    {
        if (!_dirty) { _dirty = true; UpdateTitle(); }
    }

    void AddSheet_Click(object sender, RoutedEventArgs e)
    {
        var d = new LabelDocument
        {
            WidthMm = _doc.WidthMm,
            HeightMm = _doc.HeightMm,
            PaperColor = _doc.PaperColor,
            Name = LabelProject.NextName(Sheets),
        };
        Sheets.Insert(Sheets.IndexOf(_doc) + 1, d);
        SetDocument(d);
        FitToView();
        RecordSheets();
        SetStatus($"Đã thêm \"{d.Name}\" ({d.WidthMm:0.##} × {d.HeightMm:0.##} mm)");
    }

    void DuplicateSheet_Click(object sender, RoutedEventArgs e)
    {
        var d = LabelDocument.Deserialize(_doc.Serialize());
        d.Name = _doc.Name + " (bản sao)";
        Sheets.Insert(Sheets.IndexOf(_doc) + 1, d);
        SetDocument(d);
        RecordSheets();
    }

    void RenameSheet_Click(object sender, RoutedEventArgs e)
    {
        if (TextPrompt.Ask(this, "Đổi tên trang", "Tên trang:", _doc.Name) is not { } name || name == _doc.Name) return;
        _doc.Name = name;
        RecordSheets();
    }

    void DeleteSheet_Click(object sender, RoutedEventArgs e)
    {
        if (Sheets.Count <= 1) { SetStatus("Dự án phải còn ít nhất một trang."); return; }
        if (MessageBox.Show(this, $"Xoá trang \"{_doc.Name}\" ({_doc.Elements.Count} đối tượng)?\nCó thể Hoàn tác (Ctrl+Z) nếu xoá nhầm.",
                "Xoá trang", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        int i = Sheets.IndexOf(_doc);
        Sheets.RemoveAt(i);
        SetDocument(Sheets[Math.Min(i, Sheets.Count - 1)]);
        FitToView();
        RecordSheets();
    }

    void PrevSheet_Click(object sender, RoutedEventArgs e) => StepSheet(-1);
    void NextSheet_Click(object sender, RoutedEventArgs e) => StepSheet(+1);

    void StepSheet(int delta)
    {
        int i = Sheets.IndexOf(_doc) + delta;
        if (i >= 0 && i < Sheets.Count) ActiveSheet = Sheets[i];
    }

    void SheetTab_DoubleClick(object sender, MouseButtonEventArgs e) => RenameSheet_Click(sender, e);

    void OnDocChanged(string key)
    {
        if (_doc.AutoLength) ApplyAutoLength();
        _undo.Record(Snapshot(), _batchKey ?? key);
        MarkDirty();
        if (key.StartsWith("structure")) CheckFields();
        if (key == "doc") { KeepElementsInside(); UpdateRulers(); SyncPreset(); SyncPrinterMedia(); Raise(nameof(IsLandscape), nameof(IsPortrait)); }
    }

    /// <summary>Độ dài tự động: chiều rộng nhãn = mép phải đối tượng xa nhất + lề.</summary>
    void ApplyAutoLength()
    {
        if (_clamping || _doc.Elements.Count == 0) return;
        _clamping = true;
        double w = Math.Max(5, _doc.Elements.Max(el => el.X + el.W) + _doc.MarginMm);
        if (Math.Abs(w - _doc.WidthMm) > 0.01) _doc.WidthMm = w;
        _clamping = false;
    }

    bool _clamping;

    /// <summary>Sau khi đổi khổ nhãn: thu nhỏ / dời các đối tượng bị tràn để chúng nằm gọn trong nhãn.</summary>
    void KeepElementsInside()
    {
        if (_clamping) return;
        _clamping = true;
        int moved = 0;
        foreach (var el in _doc.Elements)
        {
            var before = (el.X, el.Y, el.W, el.H);
            if (!_doc.AutoLength) el.W = Math.Min(el.W, _doc.WidthMm);
            el.H = Math.Min(el.H, _doc.HeightMm);
            if (!_doc.AutoLength) el.X = Math.Clamp(el.X, 0, _doc.WidthMm - el.W);
            el.Y = Math.Clamp(el.Y, 0, _doc.HeightMm - el.H);
            if (before != (el.X, el.Y, el.W, el.H)) moved++;
        }
        _clamping = false;
        if (moved > 0) SetStatus($"Đã thu {moved} đối tượng vào trong nhãn {_doc.WidthMm:0.##} × {_doc.HeightMm:0.##} mm");
    }

    void UpdateTitle()
    {
        Title = $"{_displayName}{(_dirty ? " *" : "")} — Tem in(THIEUXK)";
    }

    void SetStatus(string text) => StatusText.Text = text;

    bool ConfirmDiscard()
    {
        if (!_dirty) return true;
        var r = MessageBox.Show(this, $"Lưu thay đổi của \"{_displayName}\"?", "Tem in(THIEUXK)",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return r == MessageBoxResult.No || (r == MessageBoxResult.Yes && Save(saveAs: false));
    }

    void New_Click(object sender, RoutedEventArgs e)
    {
        if (!ConfirmDiscard()) return;
        _filePath = null;
        _displayName = "Nhãn mới";
        _dirty = false;
        LoadProject(new LabelProject { Sheets = [CreateDefaultDocument()] }, 0);
        _undo.Reset(Snapshot());
        FitToView();
        SetStatus("Đã tạo nhãn mới");
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = TemplateFilter, InitialDirectory = AppSettings.Current.LastFolder ?? "" };
        if (dlg.ShowDialog(this) == true) OpenFile(dlg.FileName);
    }

    public void OpenFile(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext is ".xlsx" or ".xlsm" or ".csv" or ".txt" or ".xls")
        {
            LoadData(path, null);
            return;
        }
        if (!ConfirmDiscard()) return;
        RememberFolder(path);

        if (ext == ".lbx")
        {
            ImportLbx(path);
            return;
        }

        var project = LabelProject.Deserialize(File.ReadAllText(path));
        _filePath = path;
        _displayName = Path.GetFileName(path);
        _dirty = false;
        LoadProject(project, 0);
        _undo.Reset(Snapshot());
        FitToView();
        SetStatus($"Đã mở {path} ({project.Sheets.Count} trang)");
        AddRecent(path);

        if (project.DataSourcePath is { } dp)
        {
            if (File.Exists(dp)) LoadData(dp, project.DataSheet);
            else SetStatus($"Đã mở mẫu. Không tìm thấy file dữ liệu đã gắn: {dp}");
        }
    }

    void ImportLbx(string path)
    {
        var lbx = LbxImporter.Load(path);
        AddRecent(path);
        foreach (var s in lbx.Sheets) s.Document.Name = s.Name;
        int active = Math.Max(0, lbx.Sheets.ToList().FindIndex(s => s.Name == lbx.CurrentSheet));

        _filePath = null; // .lbx chỉ đọc; khi lưu sẽ hỏi tên file .tnl
        _displayName = Path.GetFileNameWithoutExtension(path) + " [từ .lbx]";
        _dirty = false;
        LoadProject(new LabelProject { Sheets = lbx.Sheets.Select(s => s.Document).ToList() }, active);
        _undo.Reset(Snapshot());
        FitToView();

        var cur = lbx.Sheets[active].Document;
        string msg = $"Đã nhập {Path.GetFileName(path)}: {lbx.Sheets.Count} trang, nhãn {cur.WidthMm:0.#} × {cur.HeightMm:0.#} mm";
        int skipped = lbx.Sheets.Sum(s => s.SkippedObjects);
        if (skipped > 0) msg += $" (bỏ qua {skipped} đối tượng chưa hỗ trợ, ví dụ ảnh)";
        SetStatus(msg);

        if (lbx.DatabasePath is { } db)
        {
            if (File.Exists(db))
            {
                if (MessageBox.Show(this, $"Mẫu này gắn với file dữ liệu:\n{db}\n\nMở luôn file dữ liệu để in hàng loạt?",
                        "Dữ liệu Excel", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    LoadData(db, lbx.DatabaseSheet);
            }
            else
                SetStatus(msg + $". Mẫu gắn với file dữ liệu \"{db}\" nhưng không tìm thấy — bấm \"Dữ liệu Excel…\" để chọn file.");
        }
    }

    void Save_Click(object sender, RoutedEventArgs e) => Save(saveAs: false);
    void SaveAs_Click(object sender, RoutedEventArgs e) => Save(saveAs: true);

    bool Save(bool saveAs)
    {
        string? path = _filePath;
        if (saveAs || path == null)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "Mẫu Tem in(THIEUXK) (*.tnl)|*.tnl",
                FileName = Path.GetFileNameWithoutExtension(_displayName.Replace(" [từ .lbx]", "")),
                InitialDirectory = AppSettings.Current.LastFolder ?? "",
            };
            if (dlg.ShowDialog(this) != true) return false;
            path = dlg.FileName;
        }

        var project = new LabelProject { Sheets = Sheets.ToList(), DataSourcePath = _dataPath, DataSheet = _dataSheet };
        File.WriteAllText(path, project.Serialize());
        _filePath = path;
        _displayName = Path.GetFileName(path);
        _dirty = false;
        RememberFolder(path);
        AddRecent(path);
        UpdateTitle();
        SetStatus($"Đã lưu {path}");
        return true;
    }

    static void RememberFolder(string path)
    {
        AppSettings.Current.LastFolder = Path.GetDirectoryName(path);
        AppSettings.Current.Save();
    }

    void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!ConfirmDiscard()) e.Cancel = true;
    }

    void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) OpenFile(files[0]);
    }

    // ---- Hoàn tác ----

    void Undo_Click(object sender, RoutedEventArgs e) => Restore(_undo.Undo());
    void Redo_Click(object sender, RoutedEventArgs e) => Restore(_undo.Redo());

    void Restore(string? state)
    {
        if (state == null) return;
        int sheet = Sheets.IndexOf(_doc);
        int idx = Selected != null ? _doc.Elements.IndexOf(Selected) : -1;
        var project = LabelProject.Deserialize(state);
        if (project.Sheets.Count < Sheets.Count && sheet > 0) sheet--; // hoàn tác "thêm trang" → về trang trước đó
        Sheets.Clear();
        foreach (var s in project.Sheets) Sheets.Add(s);
        sheet = Math.Clamp(sheet, 0, Sheets.Count - 1);
        SetDocument(Sheets[sheet], idx);
        Raise(nameof(HasManySheets));
        MarkDirty();
    }

    // ---- Đối tượng ----

    void AddElement(LabelElement el)
    {
        el.X = Math.Max(0, (_doc.WidthMm - el.W) / 2);
        el.Y = Math.Max(0, (_doc.HeightMm - el.H) / 2);
        // Chỗ giữa đã có đối tượng cùng vị trí → lệch 2 mm (xếp bậc thang) để không chồng khít lên nhau
        for (int i = 0; i < 20 && _doc.Elements.Any(o => Math.Abs(o.X - el.X) < 0.5 && Math.Abs(o.Y - el.Y) < 0.5
                 || Math.Abs(o.X + o.W / 2 - (el.X + el.W / 2)) < 0.5 && Math.Abs(o.Y + o.H / 2 - (el.Y + el.H / 2)) < 0.5); i++)
        {
            el.X += 2;
            el.Y += 2;
        }
        _undo.BreakMerge();
        _doc.Elements.Add(el);
        Canvas.Selected = el;
    }

    double FitW(double w) => Math.Max(2, Math.Min(_doc.WidthMm - 4, w));
    double FitH(double h) => Math.Max(2, Math.Min(_doc.HeightMm - 4, h));

    void AddText_Click(object sender, RoutedEventArgs e)
    {
        AddElement(new TextElement { W = FitW(30), H = FitH(8) });
        FocusContentBox();
    }

    void AddFrame_Click(object sender, RoutedEventArgs e) =>
        AddElement(new FrameElement { W = FitW(_doc.WidthMm - 2), H = FitH(_doc.HeightMm - 2) });

    void AddLine_Click(object sender, RoutedEventArgs e) =>
        AddElement(new LineElement { W = FitW(_doc.WidthMm - 4), H = 2 });

    void AddBarcode_Click(object sender, RoutedEventArgs e) =>
        AddElement(new BarcodeElement { W = FitW(40), H = FitH(14) });

    void AddQr_Click(object sender, RoutedEventArgs e)
    {
        double s = Math.Min(FitW(20), FitH(20));
        AddElement(new BarcodeElement { Kind = BarcodeKind.QrCode, Data = "https://", W = s, H = s, ShowText = false });
    }

    void Duplicate_Click(object sender, RoutedEventArgs e) => Duplicate();
    void Delete_Click(object sender, RoutedEventArgs e) => DeleteSelected();

    void InsertField_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: string col } combo) return;
        string field = "{{" + col + "}}";
        switch (combo.DataContext)
        {
            case TextElement t: t.Text = t.Text is "Chữ" or "" ? field : t.Text + field; break;
            case BarcodeElement b: b.Data = field; break;
        }
        combo.SelectedItem = null;
        CheckFields();
    }

    void FocusContentBox()
    {
        PropTabs.SelectedIndex = 0;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (FindChild<TextBox>(ElementEditor, tb => (tb.Tag as string) == "content") is { } box)
            {
                box.Focus();
                box.SelectAll();
            }
        });
    }

    static T? FindChild<T>(DependencyObject parent, Func<T, bool> match) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t && match(t)) return t;
            if (FindChild(child, match) is { } found) return found;
        }
        return null;
    }

    void Window_KeyDown(object sender, KeyEventArgs e)
    {
        var mods = Keyboard.Modifiers;
        if (mods == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.N: New_Click(this, e); break;
                case Key.O: Open_Click(this, e); break;
                case Key.S: Save(saveAs: false); break;
                case Key.P: Print_Click(this, e); break;
                case Key.Z: Restore(_undo.Undo()); break;
                case Key.Y: Restore(_undo.Redo()); break;
                case Key.D: Duplicate(); break;
                case Key.G: Group_Click(this, e); break;
                case Key.C when !IsTyping(): CopySelection(); break;
                case Key.X when !IsTyping(): if (CopySelection()) DeleteSelected(); break;
                case Key.V when !IsTyping(): Paste(); break;
                case Key.A when !IsTyping(): Canvas.SetSelection(_doc.Elements); break;
                case Key.PageUp: StepSheet(-1); break;
                case Key.PageDown: StepSheet(+1); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        if (mods == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.G) { Ungroup_Click(this, e); e.Handled = true; return; }

        // Các phím còn lại chỉ áp dụng khi không gõ trong ô nhập
        if (IsTyping() || Selected == null) return;
        double step = mods.HasFlag(ModifierKeys.Shift) ? 2 : 0.5;
        switch (e.Key)
        {
            case Key.Delete: DeleteSelected(); break;
            case Key.Left: Nudge(-step, 0); break;
            case Key.Right: Nudge(step, 0); break;
            case Key.Up: Nudge(0, -step); break;
            case Key.Down: Nudge(0, step); break;
            case Key.Escape: Canvas.SetSelection([]); break;
            default: return;
        }
        e.Handled = true;
    }

    /// <summary>Đang gõ trong ô nhập / bảng → để phím tắt cho ô đó (Ctrl+C trong ô chữ là chép chữ).</summary>
    static bool IsTyping() =>
        Keyboard.FocusedElement is TextBox or ComboBox or ComboBoxItem or DataGridCell or System.Windows.Controls.DataGrid;

    // ---- Khổ nhãn / thu phóng ----

    // ---- Khổ có sẵn (thêm / xoá, lưu trong settings.json) ----

    public ObservableCollection<LabelPreset> Presets { get; } = new(AppSettings.Current.Presets ?? AppSettings.DefaultPresets());

    LabelPreset? _preset;
    bool _syncingPreset;

    /// <summary>Chọn từ danh sách → áp dụng khổ cho nhãn.</summary>
    public LabelPreset? SelectedPreset
    {
        get => _preset;
        set
        {
            _preset = value;
            Raise(nameof(SelectedPreset), nameof(NoPresetSelected), nameof(HasPresetSelected));
            if (value == null || _syncingPreset) return;
            if (Math.Abs(_doc.WidthMm - value.WidthMm) < 0.01 && Math.Abs(_doc.HeightMm - value.HeightMm) < 0.01) return;
            _undo.BreakMerge();
            _doc.WidthMm = value.WidthMm;
            _doc.HeightMm = value.HeightMm;
            FitToView();
        }
    }

    public bool NoPresetSelected => _preset == null;
    public bool HasPresetSelected => _preset != null;

    /// <summary>Hiện khổ trùng với kích thước nhãn hiện tại (không áp dụng lại).</summary>
    void SyncPreset()
    {
        _syncingPreset = true;
        SelectedPreset = Presets.FirstOrDefault(p => Math.Abs(p.WidthMm - _doc.WidthMm) < 0.01 && Math.Abs(p.HeightMm - _doc.HeightMm) < 0.01);
        _syncingPreset = false;
    }

    void SavePresets()
    {
        AppSettings.Current.Presets = Presets.ToList();
        AppSettings.Current.Save();
    }

    void AddPreset_Click(object sender, RoutedEventArgs e)
    {
        var preset = PresetDialog.Ask(this, _doc.WidthMm, _doc.HeightMm, (w, h) =>
            Presets.FirstOrDefault(p => Math.Abs(p.WidthMm - w) < 0.01 && Math.Abs(p.HeightMm - h) < 0.01)?.Name);
        if (preset == null) return;
        Presets.Add(preset);
        SavePresets();
        SelectedPreset = preset; // áp dụng luôn khổ vừa thêm
        SetStatus($"Đã thêm khổ \"{preset.Name}\"");
    }

    void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_preset is not { } p) return;
        if (MessageBox.Show(this, $"Xoá khổ \"{p.Name}\" khỏi danh sách?\n(Nhãn đang mở không bị thay đổi.)", "Xoá khổ nhãn",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Presets.Remove(p);
        SavePresets();
        SyncPreset();
        SetStatus($"Đã xoá khổ \"{p.Name}\"");
    }

    void ResetPresets_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Đưa danh sách khổ có sẵn về mặc định? Các khổ bạn tự thêm sẽ bị xoá.", "Khổ có sẵn",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        Presets.Clear();
        foreach (var p in AppSettings.DefaultPresets()) Presets.Add(p);
        AppSettings.Current.Presets = null;
        AppSettings.Current.Save();
        SyncPreset();
    }

    void ZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(Canvas.Zoom * 1.25);
    void ZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(Canvas.Zoom / 1.25);
    void Zoom100_Click(object sender, RoutedEventArgs e) => SetZoom(1);
    void Fit_Click(object sender, RoutedEventArgs e) => FitToView();

    void SetZoom(double z)
    {
        Canvas.Zoom = z;
        OnZoomChanged();
    }

    void OnZoomChanged()
    {
        ZoomText.Text = $"{Canvas.Zoom * 100:0} %";
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, UpdateRulers);
    }

    /// <summary>Vừa màn hình nhưng không quá 6× — nhãn nhỏ không bị phóng quá to.</summary>
    void FitToView()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            double vw = CanvasScroll.ViewportWidth - 2 * LabelCanvas.Pad - 10;
            double vh = CanvasScroll.ViewportHeight - 2 * LabelCanvas.Pad - 10;
            if (vw <= 0 || vh <= 0) return;
            double z = Math.Min(vw / (_doc.WidthMm * LabelRenderer.DipPerMm), vh / (_doc.HeightMm * LabelRenderer.DipPerMm));
            SetZoom(Math.Min(z, 6));
        });
    }

    void CanvasScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateRulers();

    void UpdateRulers()
    {
        double scale = LabelRenderer.DipPerMm * Canvas.Zoom;
        TopRuler.Update(scale, Canvas.OriginX - CanvasScroll.HorizontalOffset, _doc.WidthMm);
        LeftRuler.Update(scale, Canvas.OriginY - CanvasScroll.VerticalOffset, _doc.HeightMm);
    }

    void HideLeft_Click(object sender, RoutedEventArgs e) => LeftVisible = false;
    void ShowLeft_Click(object sender, RoutedEventArgs e) => LeftVisible = true;
    void HideRight_Click(object sender, RoutedEventArgs e) => RightVisible = false;
    void ShowRight_Click(object sender, RoutedEventArgs e) => RightVisible = true;

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    void Shortcuts_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "Ctrl+N  Nhãn mới\nCtrl+O  Mở\nCtrl+PageUp / PageDown  Trang trước / sau\nCtrl+S  Lưu\nCtrl+P  In\nCtrl+Z / Ctrl+Y  Hoàn tác / Làm lại\nCtrl+D  Nhân bản\nDelete  Xoá\n" +
        "Mũi tên  Dịch 0,5 mm (Shift: 2 mm)\nAlt khi kéo  Tạm tắt bắt lưới\nCtrl + lăn chuột  Thu phóng\nChuột phải  Menu đối tượng",
        "Phím tắt", MessageBoxButton.OK, MessageBoxImage.Information);

    void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "Tem in(THIEUXK) — thiết kế và in nhãn cho mọi máy in (TSC, Zebra, Brother, Xprinter, máy văn phòng...).\n" +
        "Mở được mẫu Brother P-touch (.lbx), in hàng loạt từ Excel/CSV.",
        "Giới thiệu", MessageBoxButton.OK, MessageBoxImage.Information);

    // ---- Dữ liệu Excel ----

    void OpenData_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = DataSource.FileFilter, InitialDirectory = AppSettings.Current.LastFolder ?? "" };
        if (dlg.ShowDialog(this) == true) LoadData(dlg.FileName, null);
    }

    void LoadData(string path, string? sheet, bool askSheet = true)
    {
        try
        {
            if (DataSource.IsExcel(path))
            {
                var sheets = DataSource.GetSheets(path);
                if (sheet == null || !sheets.Contains(sheet))
                {
                    sheet = sheets.Count > 1 && askSheet
                        ? PickWindow.Pick(this, "Chọn sheet", $"{Path.GetFileName(path)} có {sheets.Count} sheet. Chọn sheet chứa dữ liệu:", sheets)
                        : sheets.FirstOrDefault();
                    if (sheet == null) return;
                }
            }
            else sheet = null;

            var table = DataSource.Load(path, sheet);
            _data = table;
            _dataPath = path;
            _dataSheet = sheet;
            Columns.Clear();
            foreach (DataColumn c in table.Columns) Columns.Add(c.ColumnName);
            DataRowDef.Height = new GridLength(230);
            Raise(nameof(HasData), nameof(DataView), nameof(RecordText));
            DataInfo.Text = $"Dữ liệu: {Path.GetFileName(path)}{(sheet != null ? $" — sheet \"{sheet}\"" : "")} — {table.Rows.Count} dòng. Bấm một dòng để xem trước nhãn.";
            if (table.Rows.Count > 0) DataGrid.SelectedIndex = 0;
            else Canvas.Row = null;
            SetStatus($"Đã mở {table.Rows.Count} dòng dữ liệu từ {path}");
            CheckFields();
        }
        catch (IOException ex)
        {
            MessageBox.Show(this, $"Không đọc được file:\n{ex.Message}", "Dữ liệu", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    void ChangeSheet_Click(object sender, RoutedEventArgs e)
    {
        if (_dataPath == null) return;
        if (!DataSource.IsExcel(_dataPath)) { SetStatus("File CSV chỉ có một bảng."); return; }
        LoadData(_dataPath, null);
    }

    void ReloadData_Click(object sender, RoutedEventArgs e)
    {
        if (_dataPath != null) LoadData(_dataPath, _dataSheet, askSheet: false);
    }

    void CloseData_Click(object sender, RoutedEventArgs e)
    {
        _data = null;
        _dataPath = _dataSheet = null;
        Columns.Clear();
        DataRowDef.Height = new GridLength(0);
        Canvas.Row = null;
        Raise(nameof(HasData), nameof(DataView), nameof(RecordText));
        SetStatus("Đã đóng dữ liệu");
    }

    void DataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        // Tên cột có dấu chấm, gạch chéo... làm hỏng đường dẫn binding mặc định → dùng chỉ mục
        if (e.Column is DataGridTextColumn col)
        {
            col.Binding = new Binding("[" + e.PropertyName + "]");
            col.Header = e.PropertyName;
        }
    }

    // ---- Kéo tiêu đề cột thả vào nhãn (như P-touch "Drag and drop field headers") ----

    Point _headerDown;
    System.Windows.Controls.Primitives.DataGridColumnHeader? _headerDrag;

    void ColumnHeader_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Bấm vào vạch đổi độ rộng cột thì không bắt đầu kéo
        for (var d = e.OriginalSource as DependencyObject; d != null && d != sender; d = d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is System.Windows.Controls.Primitives.Thumb) { _headerDrag = null; return; }
        _headerDrag = sender as System.Windows.Controls.Primitives.DataGridColumnHeader;
        _headerDown = e.GetPosition(this);
    }

    void ColumnHeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (_headerDrag == null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(this);
        if (Math.Abs(p.X - _headerDown.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(p.Y - _headerDown.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var header = _headerDrag;
        _headerDrag = null;
        if (header.Column?.Header is not string field) return;
        SetStatus($"Thả \"{field}\" vào chữ / mã vạch trên nhãn, hoặc vào chỗ trống để tạo chữ mới");
        DragDrop.DoDragDrop(header, new DataObject(LabelCanvas.FieldFormat, field), DragDropEffects.Copy);
    }

    void OnFieldDropped(string field, LabelElement? target, Point mm)
    {
        string tag = "{{" + field + "}}";
        _undo.BreakMerge();
        switch (target)
        {
            case TextElement t:
                t.Text = tag;
                Canvas.Selected = t;
                SetStatus($"Đã gán {tag} cho chữ");
                break;
            case BarcodeElement b:
                b.Data = tag;
                Canvas.Selected = b;
                SetStatus($"Đã gán {tag} cho mã vạch");
                break;
            default:
                var el = new TextElement { Text = tag, W = FitW(30), H = FitH(8) };
                el.X = Math.Clamp(mm.X - el.W / 2, 0, Math.Max(0, _doc.WidthMm - el.W));
                el.Y = Math.Clamp(mm.Y - el.H / 2, 0, Math.Max(0, _doc.HeightMm - el.H));
                _doc.Elements.Add(el);
                Canvas.Selected = el;
                SetStatus($"Đã thêm chữ mới với {tag}");
                break;
        }
        CheckFields();
    }

    void DataGrid_LoadingRow(object sender, DataGridRowEventArgs e) => e.Row.Header = (e.Row.GetIndex() + 1).ToString();

    void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataGrid.SelectedItem is DataRowView rv) Canvas.Row = DataSource.ToDictionary(rv.Row);
        Raise(nameof(RecordText));
    }

    IReadOnlyDictionary<string, string>? RowAt(int index) =>
        _data != null && index >= 0 && index < _data.Rows.Count ? DataSource.ToDictionary(_data.Rows[index]) : null;

    /// <summary>Cảnh báo trường {{...}} không có trong dữ liệu đang mở.</summary>
    void CheckFields()
    {
        if (_data == null) return;
        var missing = MissingFields();
        if (missing.Count > 0)
            SetStatus($"⚠ Mẫu dùng trường không có trong dữ liệu: {string.Join(", ", missing)}");
    }

    List<string> MissingFields()
    {
        if (_data == null) return [];
        var used = Sheets.SelectMany(s => s.Elements).SelectMany(el => el switch
        {
            TextElement t => LabelRenderer.FieldsIn(t.Text),
            BarcodeElement b => LabelRenderer.FieldsIn(b.Data),
            _ => [],
        });
        return used.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(f => !_data.Columns.Contains(f) && !LabelRenderer.IsBuiltIn(f)).ToList();
    }

    // ---- In ----

    void Print_Click(object sender, RoutedEventArgs e)
    {
        int rowCount = _data?.Rows.Count ?? 0;
        var selected = DataGrid.SelectedItems.OfType<DataRowView>()
            .Select(rv => _data!.Rows.IndexOf(rv.Row)).Where(i => i >= 0).OrderBy(i => i).ToList();

        var dlg = new PrintWindow(rowCount, selected, DataGrid.SelectedIndex, _doc, Sheets.ToList(), i => i < 0 ? Canvas.Row : RowAt(i)) { Owner = this };
        if (dlg.ShowDialog() != true) return;
        _printer = dlg.Options.PrinterName;
        Raise(nameof(SelectedPrinter));

        List<IReadOnlyDictionary<string, string>?> rows = dlg.Rows.Count == 0
            ? [Canvas.Row]
            : dlg.Rows.Select(RowAt).ToList();

        if (LabelPrinter.GetProblem(dlg.Options.PrinterName) is { } problem && MessageBox.Show(this,
                $"Máy in \"{dlg.Options.PrinterName}\": {problem}.\n\nNếu gửi bây giờ, lệnh in sẽ nằm chờ cho đến khi máy in sẵn sàng. Vẫn gửi?",
                "In nhãn", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        var missing = MissingFields();
        if (missing.Count > 0 && MessageBox.Show(this,
                $"Các trường sau không có trong dữ liệu và sẽ được in nguyên văn:\n{string.Join(", ", missing.Select(f => "{{" + f + "}}"))}\n\nVẫn in?",
                "In nhãn", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        // Trang cần in: trang đang mở, hoặc tất cả các trang
        var docs = dlg.AllSheets ? Sheets.ToList() : [_doc];

        var errors = rows.Select((r, i) => (i, err: docs.SelectMany(d => LabelRenderer.Validate(d, r)).FirstOrDefault()))
            .Where(x => x.err != null).ToList();
        if (errors.Count > 0)
        {
            var (i, err) = errors[0];
            string where = dlg.Rows.Count > 0 ? $"dòng {dlg.Rows[i] + 1}" : "nhãn";
            if (MessageBox.Show(this,
                    $"{errors.Count} nhãn có mã vạch không hợp lệ (bỏ trống mã vạch khi in).\nVí dụ {where}: {err}\n\nVẫn in?",
                    "In nhãn", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
        }

        Mouse.OverrideCursor = Cursors.Wait;
        string? warning;
        try
        {
            var warnings = new List<string>();
            foreach (var d in docs)
            {
                // Trang khác chiều (ngang/dọc) với trang đang mở thì dùng chế độ xoay mặc định của máy in cho chiều đó
                bool sameOrientation = (d.WidthMm > d.HeightMm) == (_doc.WidthMm > _doc.HeightMm);
                var o = new PrintOptions
                {
                    PrinterName = dlg.Options.PrinterName,
                    Copies = dlg.Options.Copies,
                    OffsetXmm = dlg.Options.OffsetXmm,
                    OffsetYmm = dlg.Options.OffsetYmm,
                    Rotate = sameOrientation ? dlg.Options.Rotate : LabelPrinter.DefaultRotate(dlg.Options.PrinterName, d.WidthMm, d.HeightMm),
                };
                if (LabelPrinter.Print(d, rows, o) is { } w) warnings.Add(docs.Count > 1 ? $"{d.Name}: {w}" : w);
            }
            warning = warnings.Count > 0 ? string.Join("\n\n", warnings) : null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"In không thành công:\n{ex.Message}", "In nhãn", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }

        int total = rows.Count * dlg.Options.Copies * docs.Count;
        SetStatus($"Đã gửi {total} nhãn tới máy in {dlg.Options.PrinterName}. Nếu không thấy in ra, mở hàng đợi in của máy này trong Windows (Printers & scanners) để xem lỗi.");
        if (warning != null) MessageBox.Show(this, warning, "Khổ giấy", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
