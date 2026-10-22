using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using TemNhan.Controls;
using TemNhan.Model;
using TemNhan.Printing;
using TemNhan.Rendering;

namespace TemNhan;

public partial class PrintWindow : Window
{
    readonly int _rowCount;
    readonly IReadOnlyList<int> _selectedRows;
    readonly int _currentRow;
    readonly double _widthMm, _heightMm;
    readonly int _sheetCount;
    readonly LabelDocument _current;
    readonly IReadOnlyList<LabelDocument> _sheets;
    readonly Func<int, IReadOnlyDictionary<string, string>?> _rowAt;

    /// <summary>Chỉ số dòng dữ liệu cần in (rỗng nếu không có dữ liệu → in 1 nhãn như đang thiết kế).</summary>
    public List<int> Rows { get; private set; } = [];
    public PrintOptions Options { get; } = new();
    /// <summary>In tất cả các trang của dự án (không chỉ trang đang mở).</summary>
    public bool AllSheets => AllSheetsBox.IsChecked == true;

    /// <param name="rowAt">Dữ liệu của dòng (chỉ số 0-based); -1 = nhãn đang xem trên màn hình.</param>
    public PrintWindow(int rowCount, IReadOnlyList<int> selectedRows, int currentRow,
        LabelDocument current, IReadOnlyList<LabelDocument> sheets, Func<int, IReadOnlyDictionary<string, string>?> rowAt)
    {
        InitializeComponent();
        _rowCount = rowCount;
        _selectedRows = selectedRows;
        _currentRow = currentRow;
        _current = current;
        _sheets = sheets;
        _rowAt = rowAt;
        _widthMm = current.WidthMm;
        _heightMm = current.HeightMm;
        int sheetCount = sheets.Count;

        var s = AppSettings.Current;
        var printers = LabelPrinter.GetPrinters();
        PrinterBox.ItemsSource = printers;
        PrinterBox.SelectedItem = s.Printer != null && printers.Contains(s.Printer) ? s.Printer : LabelPrinter.GetDefaultPrinter() ?? printers.FirstOrDefault();
        CopiesBox.Text = Math.Max(1, s.Copies).ToString();
        OffsetXBox.Text = s.OffsetXmm.ToString(CultureInfo.InvariantCulture);
        OffsetYBox.Text = s.OffsetYmm.ToString(CultureInfo.InvariantCulture);
        AutoCutBox.IsChecked = s.AutoCut;
        HalfCutBox.IsChecked = s.HalfCut;
        ChainBox.IsChecked = s.ChainPrint;
        MirrorBox.IsChecked = s.Mirror;
        QualityBox.IsChecked = s.HighQuality;
        // Xoay theo máy in: máy băng Brother tự xoay nhãn ngang; lựa chọn của người dùng được nhớ theo từng máy
        PrinterBox.SelectionChanged += (_, _) => UpdateRotate();
        UpdateRotate();

        if (sheetCount > 1)
        {
            AllSheetsBox.Content = new TextBlock { Text = $"In tất cả {sheetCount} trang (mỗi dòng dữ liệu in đủ {sheetCount} trang)", TextWrapping = TextWrapping.Wrap };
            AllSheetsBox.Visibility = Visibility.Visible;
            AllSheetsBox.Checked += (_, _) => UpdateSummary();
            AllSheetsBox.Unchecked += (_, _) => UpdateSummary();
        }
        _sheetCount = sheetCount;

        bool hasData = rowCount > 0;
        RangeAll.Content = $"Tất cả {rowCount} dòng dữ liệu";
        RangeSelected.Content = $"Các dòng đang chọn trong bảng ({selectedRows.Count})";
        RangeAll.IsEnabled = RangeFromTo.IsEnabled = FromBox.IsEnabled = ToBox.IsEnabled = hasData;
        RangeSelected.IsEnabled = hasData && selectedRows.Count > 1;
        ToBox.Text = rowCount.ToString();
        if (hasData) RangeAll.IsChecked = true;

        foreach (var rb in new[] { RangeCurrent, RangeAll, RangeSelected, RangeFromTo })
            rb.Checked += (_, _) => UpdateSummary();
        foreach (var tb in new[] { FromBox, ToBox, CopiesBox })
            tb.TextChanged += (_, _) => UpdateSummary();
        FromBox.GotFocus += (_, _) => RangeFromTo.IsChecked = true;
        ToBox.GotFocus += (_, _) => RangeFromTo.IsChecked = true;
        UpdateSummary();
    }

    PrinterFeatures _features = new(false, false, false, false, false);

    /// <summary>Hiện các tuỳ chọn mà driver của máy in đang chọn hỗ trợ.</summary>
    void UpdateDriverOptions()
    {
        if (PrinterBox.SelectedItem is not string p) return;
        _features = LabelPrinter.GetFeatures(p);
        static Visibility V(bool b) => b ? Visibility.Visible : Visibility.Collapsed;
        AutoCutBox.Visibility = V(_features.AutoCut);
        HalfCutBox.Visibility = V(_features.HalfCut);
        ChainBox.Visibility = V(_features.ChainPrint);
        MirrorBox.Visibility = V(_features.Mirror);
        QualityBox.Visibility = V(_features.Quality);
        DriverOptions.Visibility = V(_features.Any);
    }

    void UpdateRotate()
    {
        UpdateDriverOptions();
        if (PrinterBox.SelectedItem is not string p) return;
        RotateBox.IsChecked = LabelPrinter.DefaultRotate(p, _widthMm, _heightMm);
        PrinterHint.Text = LabelPrinter.IsTapePrinter(p)
            ? "Máy in băng (P-touch): nhãn nằm ngang sẽ được xoay 90° để chạy dọc theo băng. Nếu chữ in ra bị ngược, bỏ chọn \"Xoay nhãn 90°\"."
            : "Máy in tem (TSC, Zebra, Xprinter...): khổ giấy trong Printing Preferences của driver nên khớp kích thước nhãn.";
    }

    List<int> ComputeRows()
    {
        if (_rowCount == 0) return [];
        if (RangeAll.IsChecked == true) return Enumerable.Range(0, _rowCount).ToList();
        if (RangeSelected.IsChecked == true) return _selectedRows.ToList();
        if (RangeFromTo.IsChecked == true)
        {
            int from = Math.Clamp(ParseInt(FromBox.Text, 1), 1, _rowCount);
            int to = Math.Clamp(ParseInt(ToBox.Text, _rowCount), from, _rowCount);
            return Enumerable.Range(from - 1, to - from + 1).ToList();
        }
        return [Math.Max(0, _currentRow)];
    }

    /// <summary>Cập nhật danh sách xem trước và tổng số nhãn.</summary>
    void UpdateSummary()
    {
        var rows = ComputeRows();
        var docs = AllSheets ? _sheets : [_current];
        int copies = Math.Clamp(ParseInt(CopiesBox.Text, 1), 1, 999);
        bool many = docs.Count > 1;

        // Mỗi nhãn một dòng xem trước; nếu nhãn có số tự tăng thì các bản sao khác nhau → tách từng bản
        var items = new List<PreviewItem>();
        foreach (var d in docs)
        {
            bool serial = LabelRenderer.UsesSerial(d);
            int n = 0;
            foreach (int r in rows.Count == 0 ? [-1] : rows)
            {
                string caption = (r < 0 ? "" : $"Dòng {r + 1}") + (many ? (r < 0 ? "" : " · ") + d.Name : "");
                if (serial && copies > 1)
                    for (int c = 0; c < copies; c++) items.Add(new PreviewItem(d, r, 1, caption, _rowAt, n++));
                else
                {
                    items.Add(new PreviewItem(d, r, copies, caption, _rowAt, n));
                    n += copies;
                }
            }
        }
        PreviewList.ItemsSource = items;

        Summary.Text = items.Sum(i => i.Copies).ToString("N0", CultureInfo.GetCultureInfo("vi-VN"));
        PreviewNote.Text = items.Count == 1 ? "Xem trước nhãn sẽ in" : $"Xem trước {items.Count:N0} nhãn khác nhau";
    }

    static int ParseInt(string s, int fallback) => int.TryParse(s.Trim(), out var v) ? v : fallback;

    static bool TryParseMm(TextBox box, out double v) =>
        double.TryParse(box.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    void Print_Click(object sender, RoutedEventArgs e)
    {
        if (PrinterBox.SelectedItem is not string printer)
        {
            MessageBox.Show(this, "Hãy chọn máy in.", Title);
            return;
        }
        if (!TryParseMm(OffsetXBox, out var ox) || !TryParseMm(OffsetYBox, out var oy))
        {
            MessageBox.Show(this, "Độ lệch phải là số (mm), ví dụ 0.5 hoặc -1.", Title);
            return;
        }

        Rows = ComputeRows();
        Options.PrinterName = printer;
        Options.Copies = Math.Clamp(ParseInt(CopiesBox.Text, 1), 1, 999);
        Options.Rotate = RotateBox.IsChecked == true;
        Options.OffsetXmm = ox;
        Options.OffsetYmm = oy;
        bool? Opt(bool supported, CheckBox box) => supported ? box.IsChecked == true : null;
        Options.AutoCut = Opt(_features.AutoCut, AutoCutBox);
        Options.HalfCut = Opt(_features.HalfCut, HalfCutBox);
        Options.ChainPrint = Opt(_features.ChainPrint, ChainBox);
        Options.Mirror = Opt(_features.Mirror, MirrorBox);
        Options.HighQuality = Opt(_features.Quality, QualityBox);

        var s = AppSettings.Current;
        s.Printer = printer;
        s.Copies = Options.Copies;
        s.Rotate = Options.Rotate;
        s.RotateByPrinter[LabelPrinter.RotateKey(printer, _widthMm, _heightMm)] = Options.Rotate;
        s.OffsetXmm = ox;
        s.OffsetYmm = oy;
        if (_features.AutoCut) s.AutoCut = AutoCutBox.IsChecked == true;
        if (_features.HalfCut) s.HalfCut = HalfCutBox.IsChecked == true;
        if (_features.ChainPrint) s.ChainPrint = ChainBox.IsChecked == true;
        if (_features.Mirror) s.Mirror = MirrorBox.IsChecked == true;
        if (_features.Quality) s.HighQuality = QualityBox.IsChecked == true;
        s.Save();
        DialogResult = true;
    }
}
