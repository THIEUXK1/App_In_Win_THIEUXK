using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using TemNhan.Model;
using TemNhan.Printing;
using TemNhan.Rendering;

namespace TemNhan;

/// <summary>Máy Brother (dò băng, hướng nhãn), file gần đây, xuất PNG/PDF, chuyển bản ghi.</summary>
public partial class MainWindow
{
    // ---- Kiểm tra khổ giấy / dò băng thật (như "Check Media" của P-touch) ----

    void CheckMedia_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPrinter is not { } printer) { SetStatus("Hãy chọn máy in."); return; }

        if (LabelPrinter.IsTapePrinter(printer))
        {
            Mouse.OverrideCursor = Cursors.Wait;
            BrotherMediaStatus? st;
            try { st = BrotherStatus.Query(); }
            finally { Mouse.OverrideCursor = null; }

            if (st != null)
            {
                string tape = st.TapeWidthMm > 0 ? $"Băng đang lắp: {st.TapeWidthMm} mm {st.MediaType}".TrimEnd() : "Máy không thấy băng.";
                string errors = st.Errors.Count > 0 ? "\n\n⚠ Lỗi: " + string.Join(", ", st.Errors) + "." : "\n\n✔ Máy sẵn sàng in.";
                double shortSide = Math.Min(_doc.WidthMm, _doc.HeightMm);
                if (st.TapeWidthMm > 0 && Math.Abs(shortSide - st.TapeWidthMm) > 0.5)
                {
                    if (MessageBox.Show(this,
                            $"{tape}{errors}\n\nNhãn đang thiết kế cho bề ngang {shortSide:0.#} mm. Đổi theo băng {st.TapeWidthMm} mm?",
                            "Kiểm tra băng", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _undo.BreakMerge();
                        if (_doc.WidthMm >= _doc.HeightMm) _doc.HeightMm = st.TapeWidthMm; else _doc.WidthMm = st.TapeWidthMm;
                        FitToView();
                    }
                }
                else
                    MessageBox.Show(this, tape + errors, "Kiểm tra băng", MessageBoxButton.OK,
                        st.Errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
                return;
            }
            SetStatus("Không đọc được máy Brother qua USB (máy chưa cắm USB hoặc đang kết nối Wi-Fi) — kiểm tra qua driver.");
        }

        var size = LabelPrinter.ProbeMediaSize(printer, _doc.WidthMm, _doc.HeightMm);
        string want = $"{_doc.WidthMm:0.#} × {_doc.HeightMm:0.#} mm";
        string msg = size is not { } sz
            ? "Driver không trả về khổ giấy."
            : Math.Abs(sz.W - _doc.WidthMm) < 1.5 && Math.Abs(sz.H - _doc.HeightMm) < 1.5
                ? $"✔ Máy in \"{printer}\" nhận đúng khổ nhãn {want}."
                : $"Máy in \"{printer}\" không nhận khổ {want}, sẽ dùng {sz.W:0.#} × {sz.H:0.#} mm.\n\n" +
                  "Hãy tạo khổ giấy đúng kích thước nhãn trong Printing Preferences của driver, hoặc thử tuỳ chọn \"Xoay nhãn 90°\" khi in.";
        MessageBox.Show(this, msg, "Kiểm tra khổ giấy", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---- Khổ tem khai báo trong driver / Control Panel của máy in đang chọn ----

    public System.Collections.ObjectModel.ObservableCollection<DriverMedia> PrinterMedia { get; } = new();

    DriverMedia? _media;
    bool _mediaLoading, _syncingMedia;

    public DriverMedia? SelectedPrinterMedia
    {
        get => _media;
        set
        {
            _media = value;
            Raise(nameof(SelectedPrinterMedia), nameof(NoPrinterMediaSelected));
            if (value != null && !_syncingMedia) ApplyMedia(value);
        }
    }

    public bool NoPrinterMediaSelected => _media == null;

    public string PrinterMediaHint =>
        _mediaLoading ? "Đang đọc khổ của máy in…"
        : PrinterMedia.Count == 0 ? "(driver không khai báo khổ nào)"
        : $"Chọn khổ… ({PrinterMedia.Count} khổ)";

    /// <summary>Đọc ngầm danh sách khổ của máy in (driver mạng có thể mất vài trăm ms).</summary>
    async void LoadPrinterMedia(string? printer)
    {
        _mediaLoading = true;
        PrinterMedia.Clear();
        _media = null;
        Raise(nameof(SelectedPrinterMedia), nameof(NoPrinterMediaSelected), nameof(PrinterMediaHint));
        if (printer == null) { _mediaLoading = false; Raise(nameof(PrinterMediaHint)); return; }

        var list = await Task.Run(() => DriverMediaReader.Read(printer));
        if (printer != _printer) return; // người dùng đã chọn máy khác trong lúc đọc
        foreach (var m in list) PrinterMedia.Add(m);
        _mediaLoading = false;
        Raise(nameof(PrinterMediaHint));
        SyncPrinterMedia();
    }

    /// <summary>Hiện khổ trùng với kích thước nhãn hiện tại (không áp dụng lại).</summary>
    void SyncPrinterMedia()
    {
        double shortSide = Math.Min(_doc.WidthMm, _doc.HeightMm);
        var match = PrinterMedia.FirstOrDefault(m => m.HeightMm is double h
            ? (Math.Abs(m.WidthMm - _doc.WidthMm) < 0.6 && Math.Abs(h - _doc.HeightMm) < 0.6)
              || (Math.Abs(m.WidthMm - _doc.HeightMm) < 0.6 && Math.Abs(h - _doc.WidthMm) < 0.6)
            : Math.Abs(m.WidthMm - shortSide) < 0.6);
        _syncingMedia = true;
        SelectedPrinterMedia = match;
        _syncingMedia = false;
    }

    void ApplyMedia(DriverMedia m)
    {
        _undo.BreakMerge();
        if (m.HeightMm is double h)
        {
            _doc.WidthMm = m.WidthMm;
            _doc.HeightMm = h;
        }
        // Băng cuộn: chỉ đổi bề ngang băng, giữ độ dài nhãn
        else if (_doc.WidthMm >= _doc.HeightMm) _doc.HeightMm = m.WidthMm;
        else _doc.WidthMm = m.WidthMm;
        FitToView();
        SetStatus($"Đã đặt khổ nhãn theo máy in: {m.Display}");
    }

    // ---- Hướng nhãn: Ngang / Dọc (xoay cả bố cục 90°) ----

    public bool IsLandscape
    {
        get => _doc.WidthMm >= _doc.HeightMm;
        set { if (value != IsLandscape) RotateLayout(); }
    }

    public bool IsPortrait
    {
        get => !IsLandscape;
        set => IsLandscape = !value;
    }

    /// <summary>Đổi hướng nhãn: hoán đổi rộng ↔ cao và xoay mọi đối tượng 90° theo chiều kim đồng hồ.</summary>
    void RotateLayout()
    {
        double oldH = _doc.HeightMm, oldW = _doc.WidthMm;
        bool auto = _doc.AutoLength;
        _clamping = true; // không tự thu đối tượng vào nhãn giữa chừng
        using (Batch())
        {
            _doc.AutoLength = false;
            _doc.WidthMm = oldH;
            _doc.HeightMm = oldW;
            foreach (var el in _doc.Elements)
            {
                double x = el.X, y = el.Y, w = el.W, h = el.H;
                el.X = oldH - (y + h);
                el.Y = x;
                el.W = h;
                el.H = w;
                el.Rotation += 90;
            }
            _doc.AutoLength = auto;
        }
        _clamping = false;
        Raise(nameof(IsLandscape), nameof(IsPortrait));
        FitToView();
    }

    // ---- File gần đây ----

    void AddRecent(string path)
    {
        var list = AppSettings.Current.RecentFiles;
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        if (list.Count > 10) list.RemoveRange(10, list.Count - 10);
        AppSettings.Current.Save();
    }

    void RecentMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menu || !ReferenceEquals(e.OriginalSource, menu)) return;
        menu.Items.Clear();
        var files = AppSettings.Current.RecentFiles;
        if (files.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(chưa có)", IsEnabled = false });
            return;
        }
        int n = 0;
        foreach (var path in files)
        {
            var item = new MenuItem
            {
                Header = $"_{++n % 10}  {Path.GetFileName(path)}",
                ToolTip = path,
                IsEnabled = File.Exists(path),
            };
            item.Click += (_, _) => OpenFile(path);
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Xoá danh sách" };
        clear.Click += (_, _) => { AppSettings.Current.RecentFiles.Clear(); AppSettings.Current.Save(); };
        menu.Items.Add(clear);
    }

    // ---- Xuất ảnh PNG / PDF ----

    const double ExportDpi = 300;

    /// <summary>Vẽ một nhãn ra ảnh (như bản in: nền trắng, bỏ đối tượng không in).</summary>
    static BitmapSource RenderLabel(LabelDocument doc, IReadOnlyDictionary<string, string>? row, int serial)
    {
        int pw = (int)Math.Ceiling(doc.WidthMm / 25.4 * ExportDpi), ph = (int)Math.Ceiling(doc.HeightMm / 25.4 * ExportDpi);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
            LabelRenderer.Render(dc, doc, new RenderContext { Row = row, SerialIndex = serial, Printing = true });
        var bmp = new RenderTargetBitmap(pw, ph, ExportDpi, ExportDpi, PixelFormats.Pbgra32);
        bmp.Render(dv);
        bmp.Freeze();
        return bmp;
    }

    void ExportPng_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "Ảnh PNG (*.png)|*.png",
            FileName = Path.GetFileNameWithoutExtension(_displayName.Replace(" [từ .lbx]", "")) + " - " + _doc.Name,
            InitialDirectory = AppSettings.Current.LastFolder ?? "",
        };
        if (dlg.ShowDialog(this) != true) return;
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(RenderLabel(_doc, Canvas.Row, 0)));
        using (var fs = File.Create(dlg.FileName)) enc.Save(fs);
        SetStatus($"Đã xuất {dlg.FileName} ({ExportDpi:0} dpi)");
    }

    void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        // Có dữ liệu Excel thì hỏi xuất tất cả các dòng hay chỉ nhãn đang xem
        List<IReadOnlyDictionary<string, string>?> rows = [Canvas.Row];
        if (_data is { Rows.Count: > 1 })
        {
            var r = MessageBox.Show(this, $"Xuất tất cả {_data.Rows.Count} dòng dữ liệu?\n\nYes: tất cả (mỗi dòng một trang PDF)\nNo: chỉ nhãn đang xem",
                "Xuất PDF", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) return;
            if (r == MessageBoxResult.Yes) rows = Enumerable.Range(0, _data.Rows.Count).Select(RowAt).ToList();
        }

        var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            FileName = Path.GetFileNameWithoutExtension(_displayName.Replace(" [từ .lbx]", "")),
            InitialDirectory = AppSettings.Current.LastFolder ?? "",
        };
        if (dlg.ShowDialog(this) != true) return;

        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            using var pdf = new PdfSharp.Pdf.PdfDocument();
            pdf.Info.Title = _displayName;
            for (int i = 0; i < rows.Count; i++)
            {
                var page = pdf.AddPage();
                page.Width = PdfSharp.Drawing.XUnit.FromMillimeter(_doc.WidthMm);
                page.Height = PdfSharp.Drawing.XUnit.FromMillimeter(_doc.HeightMm);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(RenderLabel(_doc, rows[i], i)));
                var ms = new MemoryStream();
                enc.Save(ms);
                ms.Position = 0;
                using var gfx = PdfSharp.Drawing.XGraphics.FromPdfPage(page);
                using var img = PdfSharp.Drawing.XImage.FromStream(ms);
                gfx.DrawImage(img, 0, 0, page.Width.Point, page.Height.Point);
            }
            pdf.Save(dlg.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Xuất PDF không thành công:\n{ex.Message}", "Xuất PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        finally { Mouse.OverrideCursor = null; }
        SetStatus($"Đã xuất {rows.Count} nhãn ra {dlg.FileName}");
    }

    // ---- Chuyển bản ghi: ◀ 1/104 ▶ ----

    public string RecordText => _data == null ? "" : $"{Math.Max(0, DataGrid.SelectedIndex) + 1}/{_data.Rows.Count}";

    void PrevRecord_Click(object sender, RoutedEventArgs e) => StepRecord(-1);
    void NextRecord_Click(object sender, RoutedEventArgs e) => StepRecord(+1);

    void StepRecord(int delta)
    {
        if (_data == null || _data.Rows.Count == 0) return;
        int i = Math.Clamp(DataGrid.SelectedIndex + delta, 0, _data.Rows.Count - 1);
        DataGrid.SelectedIndex = i;
        DataGrid.ScrollIntoView(DataGrid.SelectedItem);
    }
}
