using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TemNhan.Model;

namespace TemNhan;

/// <summary>Các lệnh chỉnh sửa / bố cục trên nhiều đối tượng (như menu Edit và Layout của P-touch).</summary>
public partial class MainWindow
{
    // ---- Gộp nhiều thay đổi thành một bước hoàn tác ----

    string? _batchKey;
    int _batchCount;

    sealed class BatchScope(MainWindow w) : IDisposable
    {
        public void Dispose() => w._batchKey = null;
    }

    IDisposable Batch(string? key = null)
    {
        if (key == null) _undo.BreakMerge();
        _batchKey = key ?? "batch#" + ++_batchCount;
        return new BatchScope(this);
    }

    List<LabelElement> Sel => Canvas.SelectedItems.ToList();
    List<LabelElement> Movable => Canvas.SelectedItems.Where(e => !e.Locked).ToList();

    /// <summary>Vùng in = nhãn trừ lề.</summary>
    Rect PrintArea => new(_doc.MarginMm, _doc.MarginMm,
        Math.Max(1, _doc.WidthMm - 2 * _doc.MarginMm), Math.Max(1, _doc.HeightMm - 2 * _doc.MarginMm));

    static Rect Bounds(IEnumerable<LabelElement> items)
    {
        var r = Rect.Empty;
        foreach (var e in items) r.Union(new Rect(e.X, e.Y, e.W, e.H));
        return r;
    }

    // ---- Cắt / Sao chép / Dán ----

    const string ClipFormat = "TemNhan.Elements";
    static string? _clip;
    LabelDocument? _clipSheet;
    int _pasteCount;

    void Cut_Click(object sender, RoutedEventArgs e) { if (CopySelection()) DeleteSelected(); }
    void Copy_Click(object sender, RoutedEventArgs e) => CopySelection();
    void Paste_Click(object sender, RoutedEventArgs e) => Paste();
    void SelectAll_Click(object sender, RoutedEventArgs e) => Canvas.SetSelection(_doc.Elements);

    bool CopySelection()
    {
        if (Sel.Count == 0) return false;
        // Giữ đúng thứ tự trên dưới như trong trang
        var items = _doc.Elements.Where(Sel.Contains).ToList();
        _clip = JsonSerializer.Serialize(items, LabelJson.Options);
        _clipSheet = _doc;
        _pasteCount = 0;
        try { Clipboard.SetData(ClipFormat, _clip); } catch { /* clipboard bận: vẫn dùng bộ nhớ trong */ }
        SetStatus($"Đã sao chép {items.Count} đối tượng");
        return true;
    }

    void Paste()
    {
        string? json = null;
        try { if (Clipboard.ContainsData(ClipFormat)) json = Clipboard.GetData(ClipFormat) as string; } catch { }
        json ??= _clip;
        if (json == null) return;

        var items = JsonSerializer.Deserialize<List<LabelElement>>(json, LabelJson.Options) ?? [];
        // Dán vào cùng trang thì lệch 2 mm mỗi lần cho khỏi chồng lên bản gốc
        double offset = ReferenceEquals(_clipSheet, _doc) ? 2 * ++_pasteCount : 0;
        AddItems(items, offset);
        SetStatus($"Đã dán {items.Count} đối tượng");
    }

    /// <summary>Thêm đối tượng (bản sao), đổi mã nhóm để không dính nhóm với bản gốc.</summary>
    void AddItems(List<LabelElement> items, double offset)
    {
        var groupMap = new Dictionary<string, string>();
        using (Batch())
        {
            foreach (var el in items)
            {
                if (el.GroupId is { } g)
                {
                    if (!groupMap.TryGetValue(g, out var ng)) groupMap[g] = ng = NewGroupId();
                    el.GroupId = ng;
                }
                el.X += offset;
                el.Y += offset;
                _doc.Elements.Add(el);
            }
        }
        Canvas.SetSelection(items);
    }

    static string NewGroupId() => Guid.NewGuid().ToString("N")[..8];

    void Duplicate()
    {
        if (Sel.Count == 0) return;
        var items = _doc.Elements.Where(Sel.Contains).Select(e => e.Clone()).ToList();
        AddItems(items, 2);
    }

    void DeleteSelected()
    {
        var items = Sel;
        if (items.Count == 0) return;
        using (Batch())
            foreach (var el in items) _doc.Elements.Remove(el);
        Canvas.SetSelection([]);
    }

    void Nudge(double dx, double dy)
    {
        // Các lần nhấn mũi tên liên tiếp gộp thành một bước hoàn tác
        using (Batch("nudge"))
            foreach (var el in Movable)
            {
                el.X += dx;
                el.Y += dy;
            }
    }

    // ---- Thứ tự ----

    void Front_Click(object sender, RoutedEventArgs e) => Reorder(toEnd: true, oneStep: false);
    void Back_Click(object sender, RoutedEventArgs e) => Reorder(toEnd: false, oneStep: false);
    void Forward_Click(object sender, RoutedEventArgs e) => Reorder(toEnd: true, oneStep: true);
    void Backward_Click(object sender, RoutedEventArgs e) => Reorder(toEnd: false, oneStep: true);

    void Reorder(bool toEnd, bool oneStep)
    {
        var sel = Sel;
        if (sel.Count == 0) return;
        var els = _doc.Elements;
        // Lên: duyệt từ trên xuống; xuống: duyệt từ dưới lên — giữ nguyên thứ tự tương đối giữa các đối tượng được chọn
        var ordered = sel.OrderBy(els.IndexOf).ToList();
        if (oneStep == toEnd) ordered.Reverse();
        using (Batch())
            foreach (var el in ordered)
            {
                int i = els.IndexOf(el);
                int target = oneStep
                    ? (toEnd ? (i + 1 < els.Count && !sel.Contains(els[i + 1]) ? i + 1 : i)
                             : (i > 0 && !sel.Contains(els[i - 1]) ? i - 1 : i))
                    : (toEnd ? els.Count - 1 : 0);
                if (target != i) els.Move(i, target);
            }
        Canvas.SetSelection(sel);
    }

    // ---- Căn chỉnh ----

    void AlignLeft_Click(object sender, RoutedEventArgs e) => AlignTo((el, a) => el.X = a.Left);
    void CenterH_Click(object sender, RoutedEventArgs e) => AlignTo((el, a) => el.X = a.Left + (a.Width - el.W) / 2);
    void AlignRight_Click(object sender, RoutedEventArgs e) => AlignTo((el, a) => el.X = a.Right - el.W);
    void AlignTop_Click(object sender, RoutedEventArgs e) => AlignTo((el, a) => el.Y = a.Top);
    void CenterV_Click(object sender, RoutedEventArgs e) => AlignTo((el, a) => el.Y = a.Top + (a.Height - el.H) / 2);
    void AlignBottom_Click(object sender, RoutedEventArgs e) => AlignTo((el, a) => el.Y = a.Bottom - el.H);

    /// <summary>Một đối tượng: căn theo vùng in của nhãn. Nhiều đối tượng: căn theo nhau (khung bao vùng chọn).</summary>
    void AlignTo(Action<LabelElement, Rect> apply)
    {
        var items = Movable;
        if (items.Count == 0) return;
        var area = items.Count == 1 ? PrintArea : Bounds(items);
        using (Batch())
            foreach (var el in items) apply(el, area);
    }

    void DistributeH_Click(object sender, RoutedEventArgs e) => Distribute(horizontal: true);
    void DistributeV_Click(object sender, RoutedEventArgs e) => Distribute(horizontal: false);

    /// <summary>Phân bố đều: giữ hai đối tượng ngoài cùng, chia đều khoảng cách cho các đối tượng giữa.</summary>
    void Distribute(bool horizontal)
    {
        var items = Movable;
        if (items.Count < 3) { SetStatus("Phân bố đều cần chọn ít nhất 3 đối tượng."); return; }
        items = horizontal ? items.OrderBy(e => e.X).ToList() : items.OrderBy(e => e.Y).ToList();
        double start = horizontal ? items[0].X : items[0].Y;
        double end = horizontal ? items[^1].X + items[^1].W : items[^1].Y + items[^1].H;
        double gap = (end - start - items.Sum(e => horizontal ? e.W : e.H)) / (items.Count - 1);
        using (Batch())
        {
            double pos = start;
            foreach (var el in items)
            {
                if (horizontal) el.X = pos; else el.Y = pos;
                pos += (horizontal ? el.W : el.H) + gap;
            }
        }
    }

    // ---- Xoay / đổi cỡ ----

    void RotateLeft_Click(object sender, RoutedEventArgs e) => RotateSelection(-90);
    void RotateRight_Click(object sender, RoutedEventArgs e) => RotateSelection(90);

    /// <summary>Xoay 90° quanh tâm từng đối tượng (khung đổi chiều rộng ↔ cao).</summary>
    void RotateSelection(int delta)
    {
        var items = Movable;
        if (items.Count == 0) return;
        using (Batch())
            foreach (var el in items)
            {
                double cx = el.X + el.W / 2, cy = el.Y + el.H / 2, w = el.W, h = el.H;
                el.Rotation += delta;
                el.W = h;
                el.H = w;
                el.X = cx - h / 2;
                el.Y = cy - w / 2;
            }
    }

    void Larger_Click(object sender, RoutedEventArgs e) => ScaleSelection(1.1);
    void Smaller_Click(object sender, RoutedEventArgs e) => ScaleSelection(1 / 1.1);

    void Maximum_Click(object sender, RoutedEventArgs e) => FitSelection(keepRatio: false);
    void MaximumRatio_Click(object sender, RoutedEventArgs e) => FitSelection(keepRatio: true);

    /// <summary>Phóng / thu cả vùng chọn quanh tâm của nó (cỡ chữ đổi theo).</summary>
    void ScaleSelection(double k)
    {
        var items = Movable;
        if (items.Count == 0) return;
        var b = Bounds(items);
        ScaleInto(items, b, new Rect(b.X + b.Width / 2 - b.Width * k / 2, b.Y + b.Height / 2 - b.Height * k / 2, b.Width * k, b.Height * k));
    }

    /// <summary>"Vừa vùng in": phóng vùng chọn cho vừa nhãn trừ lề (giữ tỉ lệ thì canh giữa).</summary>
    void FitSelection(bool keepRatio)
    {
        var items = Movable;
        if (items.Count == 0) return;
        var b = Bounds(items);
        var a = PrintArea;
        if (keepRatio)
        {
            double k = Math.Min(a.Width / b.Width, a.Height / b.Height);
            a = new Rect(a.X + (a.Width - b.Width * k) / 2, a.Y + (a.Height - b.Height * k) / 2, b.Width * k, b.Height * k);
        }
        ScaleInto(items, b, a);
    }

    void ScaleInto(List<LabelElement> items, Rect from, Rect to)
    {
        double kx = to.Width / from.Width, ky = to.Height / from.Height;
        using (Batch())
            foreach (var el in items)
            {
                el.X = to.X + (el.X - from.X) * kx;
                el.Y = to.Y + (el.Y - from.Y) * ky;
                el.W *= kx;
                el.H *= ky;
                if (el is TextElement t && Math.Abs(kx - ky) < 0.001) t.FontSizePt *= kx; // phóng đều thì chữ to theo
            }
    }

    // ---- Nhóm ----

    void Group_Click(object sender, RoutedEventArgs e)
    {
        var items = Sel;
        if (items.Count < 2) { SetStatus("Chọn ít nhất 2 đối tượng để nhóm (Ctrl + bấm hoặc kéo khung chọn)."); return; }
        string id = NewGroupId();
        using (Batch())
            foreach (var el in items) el.GroupId = id;
        SetStatus($"Đã nhóm {items.Count} đối tượng");
    }

    void Ungroup_Click(object sender, RoutedEventArgs e)
    {
        var items = Sel.Where(x => x.GroupId != null).ToList();
        if (items.Count == 0) return;
        using (Batch())
            foreach (var el in items) el.GroupId = null;
        SetStatus("Đã bỏ nhóm");
    }

    // ---- Chèn ảnh / ngày giờ / số tự tăng ----

    /// <summary>Chọn file ảnh, thu về tối đa 1200 px, mã hoá PNG base64. Null nếu huỷ / lỗi.</summary>
    (string Data, int Width, int Height, string Name)? PickImage()
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Ảnh (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif",
            InitialDirectory = AppSettings.Current.LastFolder ?? "",
        };
        if (dlg.ShowDialog(this) != true) return null;
        try
        {
            // Ảnh lớn thu về tối đa 1200 px để file mẫu không quá nặng (đủ nét cho nhãn ở 300 dpi)
            var probe = BitmapFrame.Create(new Uri(dlg.FileName), BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(dlg.FileName);
            img.CacheOption = BitmapCacheOption.OnLoad;
            if (probe.PixelWidth > 1200) img.DecodePixelWidth = 1200;
            img.EndInit();

            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(img));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return (Convert.ToBase64String(ms.ToArray()), img.PixelWidth, img.PixelHeight, Path.GetFileName(dlg.FileName));
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Không mở được ảnh:\n{ex.Message}", "Ảnh", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
    }

    void AddImage_Click(object sender, RoutedEventArgs e)
    {
        if (PickImage() is not { } img) return;
        double ratio = (double)img.Width / Math.Max(1, img.Height);
        double h = FitH(Math.Min(_doc.HeightMm - 4, 20)), w = h * ratio;
        if (w > _doc.WidthMm - 4) { w = FitW(_doc.WidthMm - 4); h = w / ratio; }
        AddElement(new ImageElement { ImageData = img.Data, W = w, H = h });
        SetStatus($"Đã chèn ảnh {img.Name} ({img.Width} × {img.Height} px, nhúng trong file mẫu)");
    }

    void ReplaceImage_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not ImageElement el || PickImage() is not { } img) return;
        el.ImageData = img.Data;
        SetStatus($"Đã thay ảnh bằng {img.Name}");
    }
    void AddDate_Click(object sender, RoutedEventArgs e)
    {
        AddElement(new TextElement { Text = "{{NGÀY:dd/MM/yyyy}}", W = FitW(30), H = FitH(7), FontSizePt = 10 });
        SetStatus("Ngày được lấy lúc in. Đổi định dạng trong nội dung: {{NGÀY:dd/MM/yyyy}}, {{GIỜ:HH:mm}}…");
    }

    void AddNumber_Click(object sender, RoutedEventArgs e)
    {
        AddElement(new TextElement { Text = "{{SỐ:001}}", W = FitW(20), H = FitH(8), FontSizePt = 12, Bold = true });
        SetStatus("Số tự tăng: mỗi nhãn in ra +1. {{SỐ:001}} bắt đầu từ 001; {{SỐ:50}} bắt đầu từ 50.");
    }

    /// <summary>Trường đặc biệt chèn được vào chữ / mã vạch.</summary>
    public sealed record SpecialField(string Label, string Token);

    public List<SpecialField> SpecialFields { get; } =
    [
        new("Ngày in (22/10/2026)", "{{NGÀY:dd/MM/yyyy}}"),
        new("Ngày in (22/10)", "{{NGÀY:dd/MM}}"),
        new("Giờ in (14:30)", "{{GIỜ:HH:mm}}"),
        new("Số tự tăng 001, 002…", "{{SỐ:001}}"),
        new("Số tự tăng 1, 2…", "{{SỐ:1}}"),
    ];

    void InsertSpecial_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: SpecialField f } combo) return;
        switch (combo.DataContext)
        {
            case TextElement t: t.Text = t.Text is "Chữ" or "" ? f.Token : t.Text + f.Token; break;
            case BarcodeElement b: b.Data = f.Token; break;
        }
        combo.SelectedItem = null;
    }
}
