using System.Printing;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Xps;
using TemNhan.Model;
using TemNhan.Rendering;

namespace TemNhan.Printing;

public sealed class PrintOptions
{
    public string PrinterName = "";
    public int Copies = 1;
    /// <summary>Xoay nhãn 90° (khi máy in kéo giấy theo chiều ngang của nhãn).</summary>
    public bool Rotate;
    public double OffsetXmm, OffsetYmm;

    // Tuỳ chọn của driver — chỉ áp dụng nếu máy in hỗ trợ (null = để mặc định của driver)
    /// <summary>Tự cắt sau mỗi nhãn (máy băng Brother).</summary>
    public bool? AutoCut;
    /// <summary>Cắt nửa: cắt lớp băng nhưng giữ lớp đế để nhãn còn dính liền.</summary>
    public bool? HalfCut;
    /// <summary>In nối tiếp: không cắt/đẩy băng sau nhãn cuối (tiết kiệm băng khi in nhiều lần).</summary>
    public bool? ChainPrint;
    public bool? Mirror;
    public bool? HighQuality;
}

/// <summary>Các tuỳ chọn mà driver máy in có hỗ trợ.</summary>
public sealed record PrinterFeatures(bool AutoCut, bool HalfCut, bool ChainPrint, bool Mirror, bool Quality)
{
    public bool Any => AutoCut || HalfCut || ChainPrint || Mirror || Quality;
}

/// <summary>In qua driver Windows nên dùng được cho mọi máy in (TSC, Zebra, Brother, Xprinter, máy văn phòng...).</summary>
public static class LabelPrinter
{
    static readonly EnumeratedPrintQueueTypes[] QueueTypes =
        [EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections];

    public static List<string> GetPrinters()
    {
        using var server = new LocalPrintServer();
        return server.GetPrintQueues(QueueTypes).Select(q => q.FullName).OrderBy(n => n).ToList();
    }

    /// <summary>Máy in băng (Brother P-touch / QL): bề rộng trang = bề ngang băng, chiều dài chạy theo băng.</summary>
    public static bool IsTapePrinter(string printer) =>
        System.Text.RegularExpressions.Regex.IsMatch(printer, @"P-?touch|\bPT-|\bQL-|\bTD-", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Có cần xoay 90° không: theo lựa chọn đã nhớ cho máy in; nếu chưa có thì máy băng tự xoay khi nhãn nằm ngang
    /// (để chiều ngắn của nhãn nằm theo bề ngang băng).
    /// </summary>
    public static bool DefaultRotate(string printer, double widthMm, double heightMm) =>
        AppSettings.Current.RotateByPrinter.TryGetValue(RotateKey(printer, widthMm, heightMm), out var r) ? r : IsTapePrinter(printer) && widthMm > heightMm;

    /// <summary>Khoá ghi nhớ: máy in + chiều nhãn (ngang/dọc).</summary>
    public static string RotateKey(string printer, double widthMm, double heightMm) =>
        printer + (widthMm > heightMm ? "|ngang" : "|doc");

    public static string? GetDefaultPrinter()
    {
        try { return LocalPrintServer.GetDefaultPrintQueue().FullName; }
        catch { return null; }
    }

    /// <summary>In các nhãn; trả về cảnh báo (nếu có) về khổ giấy mà driver chấp nhận.</summary>
    public static string? Print(LabelDocument doc, IReadOnlyList<IReadOnlyDictionary<string, string>?> rows, PrintOptions o)
    {
        using var server = new LocalPrintServer();
        var queue = server.GetPrintQueues(QueueTypes).FirstOrDefault(q => q.FullName == o.PrinterName)
            ?? throw new InvalidOperationException($"Không tìm thấy máy in \"{o.PrinterName}\".");

        double w = doc.WidthMm * LabelRenderer.DipPerMm, h = doc.HeightMm * LabelRenderer.DipPerMm;
        double pw = o.Rotate ? h : w, ph = o.Rotate ? w : h;

        var ticket = (queue.UserPrintTicket ?? queue.DefaultPrintTicket).Clone();
        ticket.PageMediaSize = new PageMediaSize(pw, ph);
        ticket.PageOrientation = PageOrientation.Portrait;
        ticket.CopyCount = 1;
        var validated = queue.MergeAndValidatePrintTicket(queue.UserPrintTicket ?? queue.DefaultPrintTicket, ticket).ValidatedPrintTicket;
        if (OptionsTicket(queue, o) is { } delta)
            validated = queue.MergeAndValidatePrintTicket(validated, delta).ValidatedPrintTicket;

        string? warning = null;
        if (validated.PageMediaSize is { Width: double vw, Height: double vh } && !SameSize(vw, vh, pw, ph))
            warning = $"Driver máy in đã đổi khổ giấy thành {vw / LabelRenderer.DipPerMm:0.#} × {vh / LabelRenderer.DipPerMm:0.#} mm " +
                      $"(khổ nhãn {pw / LabelRenderer.DipPerMm:0.#} × {ph / LabelRenderer.DipPerMm:0.#} mm). " +
                      "Nếu in lệch, hãy tạo khổ giấy đúng kích thước trong Printing Preferences của máy in.";

        queue.CurrentJobSettings.Description = "Tem in(THIEUXK)";
        var writer = PrintQueue.CreateXpsDocumentWriter(queue);
        writer.Write(BuildDocument(doc, rows, o), validated);
        return warning;
    }

    /// <summary>Mỗi nhãn (× số bản) là một trang đúng khổ nhãn.</summary>
    public static FixedDocument BuildDocument(LabelDocument doc, IReadOnlyList<IReadOnlyDictionary<string, string>?> rows, PrintOptions o)
    {
        double w = doc.WidthMm * LabelRenderer.DipPerMm, h = doc.HeightMm * LabelRenderer.DipPerMm;
        double pw = o.Rotate ? h : w, ph = o.Rotate ? w : h;
        var fd = new FixedDocument();
        fd.DocumentPaginator.PageSize = new Size(pw, ph);
        int serial = 0; // {{SỐ:...}} tăng theo từng nhãn in ra (kể cả các bản sao)
        foreach (var row in rows)
            for (int c = 0; c < Math.Max(1, o.Copies); c++)
                fd.Pages.Add(MakePage(doc, row, o, pw, ph, serial++));
        return fd;
    }

    /// <summary>Mô tả sự cố của máy in (lỗi, ngoại tuyến, lệnh kẹt…), hoặc null nếu bình thường.</summary>
    public static string? GetProblem(string printer)
    {
        using var server = new LocalPrintServer();
        var q = server.GetPrintQueues(QueueTypes).FirstOrDefault(x => x.FullName == printer);
        if (q == null) return "không tìm thấy máy in";
        q.Refresh();
        var issues = new List<string>();
        if (q.IsOffline || q.IsNotAvailable) issues.Add("máy in đang ngoại tuyến / chưa kết nối");
        if (q.IsInError || q.IsOutOfPaper || q.IsPaperJammed || q.IsDoorOpened) issues.Add("máy in đang báo lỗi (hết giấy/kẹt giấy/mở nắp…)");
        if (q.IsPaused) issues.Add("hàng đợi in đang tạm dừng");
        int stuck = q.GetPrintJobInfoCollection().Count(j => j.IsInError || j.IsPaused || j.IsBlocked);
        if (stuck > 0) issues.Add($"còn {stuck} lệnh in bị kẹt trong hàng đợi");
        return issues.Count == 0 ? null : string.Join("; ", issues);
    }

    /// <summary>Khổ giấy (mm) mà driver chấp nhận khi yêu cầu khổ nhãn — để chẩn đoán.</summary>
    public static (double W, double H)? ProbeMediaSize(string printer, double widthMm, double heightMm)
    {
        using var server = new LocalPrintServer();
        var queue = server.GetPrintQueues(QueueTypes).FirstOrDefault(q => q.FullName == printer);
        if (queue == null) return null;
        var t = queue.DefaultPrintTicket.Clone();
        t.PageMediaSize = new PageMediaSize(widthMm * LabelRenderer.DipPerMm, heightMm * LabelRenderer.DipPerMm);
        var v = queue.MergeAndValidatePrintTicket(queue.DefaultPrintTicket, t).ValidatedPrintTicket.PageMediaSize;
        return v is { Width: double vw, Height: double vh } ? (vw / LabelRenderer.DipPerMm, vh / LabelRenderer.DipPerMm) : null;
    }

    static bool SameSize(double aw, double ah, double bw, double bh)
    {
        const double tol = 1.5 * LabelRenderer.DipPerMm;
        return (Math.Abs(aw - bw) < tol && Math.Abs(ah - bh) < tol) || (Math.Abs(aw - bh) < tol && Math.Abs(ah - bw) < tol);
    }

    static PageContent MakePage(LabelDocument doc, IReadOnlyDictionary<string, string>? row, PrintOptions o, double pw, double ph, int serial)
    {
        var page = new FixedPage { Width = pw, Height = ph };
        var visual = new LabelPageVisual(doc, row, o, serial) { Width = pw, Height = ph };
        page.Children.Add(visual);
        page.Measure(new Size(pw, ph));
        page.Arrange(new Rect(0, 0, pw, ph));
        var content = new PageContent();
        ((IAddChild)content).AddChild(page);
        return content;
    }

    sealed class LabelPageVisual(LabelDocument doc, IReadOnlyDictionary<string, string>? row, PrintOptions o, int serial) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            double h = doc.HeightMm * LabelRenderer.DipPerMm;
            if (o.Rotate) dc.PushTransform(new MatrixTransform(0, 1, -1, 0, h, 0)); // xoay 90° theo chiều kim đồng hồ
            dc.PushTransform(new TranslateTransform(o.OffsetXmm * LabelRenderer.DipPerMm, o.OffsetYmm * LabelRenderer.DipPerMm));
            LabelRenderer.Render(dc, doc, new RenderContext { Row = row, SerialIndex = serial, Printing = true });
            dc.Pop();
            if (o.Rotate) dc.Pop();
        }
    }
    // ---- Tuỳ chọn riêng của driver (Print Schema) ----

    const string PsfNs = "http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework";
    const string PskNs = "http://schemas.microsoft.com/windows/2003/08/printing/printschemakeywords";

    /// <summary>Đọc khả năng của driver: tên tính năng → namespace của tiền tố (để ghép PrintTicket).</summary>
    static (HashSet<string> Features, Dictionary<string, string> Ns) ReadCapabilities(PrintQueue queue)
    {
        var features = new HashSet<string>();
        var ns = new Dictionary<string, string> { ["psk"] = PskNs };
        try
        {
            using var ms = queue.GetPrintCapabilitiesAsXml();
            var xml = new System.Xml.XmlDocument();
            xml.Load(ms);
            var mgr = new System.Xml.XmlNamespaceManager(xml.NameTable);
            mgr.AddNamespace("psf", PsfNs);
            foreach (System.Xml.XmlElement f in xml.SelectNodes("//psf:Feature", mgr)!)
            {
                string name = f.GetAttribute("name");
                features.Add(name);
                int colon = name.IndexOf(':');
                if (colon > 0 && f.GetNamespaceOfPrefix(name[..colon]) is { Length: > 0 } uri) ns[name[..colon]] = uri;
            }
        }
        catch { /* driver không trả khả năng → không áp tuỳ chọn */ }
        return (features, ns);
    }

    public static PrinterFeatures GetFeatures(string printer)
    {
        using var server = new LocalPrintServer();
        var queue = server.GetPrintQueues(QueueTypes).FirstOrDefault(q => q.FullName == printer);
        if (queue == null) return new(false, false, false, false, false);
        var (f, _) = ReadCapabilities(queue);
        return new(f.Contains("brpsk:JobRollCutSheet"), f.Contains("brpsk:JobRollHalfCutSheet"),
            f.Contains("psk:JobRollCutAtEndOfJob"), f.Contains("psk:PageMirrorImage"), f.Contains("psk:PageOutputQuality"));
    }

    /// <summary>PrintTicket chỉ chứa các tuỳ chọn người dùng chọn mà driver hỗ trợ.</summary>
    static PrintTicket? OptionsTicket(PrintQueue queue, PrintOptions o)
    {
        var (f, ns) = ReadCapabilities(queue);
        var parts = new List<(string Feature, string Option)>();
        void Add(bool? on, string feature, string yes, string no)
        {
            if (on is bool v && f.Contains(feature)) parts.Add((feature, v ? yes : no));
        }
        Add(o.AutoCut, "brpsk:JobRollCutSheet", "brpsk:CutSheet", "psk:None");
        Add(o.HalfCut, "brpsk:JobRollHalfCutSheet", "brpsk:HalfCutSheet", "psk:None");
        Add(o.ChainPrint, "psk:JobRollCutAtEndOfJob", "psk:None", "psk:CutSheetAtStandardMediaSize");
        Add(o.Mirror, "psk:PageMirrorImage", "psk:MirrorImageWidth", "psk:None");
        Add(o.HighQuality, "psk:PageOutputQuality", "psk:High", "psk:Normal");
        if (parts.Count == 0) return null;

        var sb = new System.Text.StringBuilder();
        sb.Append($"<psf:PrintTicket xmlns:psf=\"{PsfNs}\" version=\"1\"");
        foreach (var (prefix, uri) in ns) sb.Append($" xmlns:{prefix}=\"{uri}\"");
        sb.Append('>');
        foreach (var (feature, option) in parts)
            sb.Append($"<psf:Feature name=\"{feature}\"><psf:Option name=\"{option}\"/></psf:Feature>");
        sb.Append("</psf:PrintTicket>");
        return new PrintTicket(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(sb.ToString())));
    }
}