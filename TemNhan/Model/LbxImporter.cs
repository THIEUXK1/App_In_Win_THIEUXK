using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace TemNhan.Model;

public sealed record LbxSheet(string Name, LabelDocument Document, int SkippedObjects);

public sealed record LbxFile(IReadOnlyList<LbxSheet> Sheets, string? CurrentSheet, string? DatabasePath, string? DatabaseSheet);

/// <summary>Đọc file mẫu P-touch Editor (.lbx = zip chứa label.xml).</summary>
public static class LbxImporter
{
    static readonly XNamespace Pt = "http://schemas.brother.info/ptouch/2007/lbx/main";
    static readonly XNamespace Style = "http://schemas.brother.info/ptouch/2007/lbx/style";
    static readonly XNamespace Txt = "http://schemas.brother.info/ptouch/2007/lbx/text";
    static readonly XNamespace Draw = "http://schemas.brother.info/ptouch/2007/lbx/draw";
    static readonly XNamespace Bc = "http://schemas.brother.info/ptouch/2007/lbx/barcode";
    static readonly XNamespace Db = "http://schemas.brother.info/ptouch/2007/lbx/database";

    public static LbxFile Load(string path)
    {
        XDocument xml;
        using (var zip = ZipFile.OpenRead(path))
        {
            var entry = zip.GetEntry("label.xml") ?? throw new InvalidDataException("File .lbx không có label.xml.");
            using var s = entry.Open();
            xml = XDocument.Load(s);
        }

        var body = xml.Root?.Element(Pt + "body") ?? throw new InvalidDataException("label.xml không đúng định dạng P-touch.");
        var sheets = body.Elements(Style + "sheet").Select(ReadSheet).ToList();
        if (sheets.Count == 0) throw new InvalidDataException("File .lbx không có trang nhãn nào.");

        var db = xml.Root!.Element(Db + "database");
        string? dbSheet = (string?)db?.Attribute("mergeTable");
        dbSheet = dbSheet?.Trim('\'').TrimEnd('$');

        return new LbxFile(sheets, (string?)body.Attribute("currentSheet"), (string?)db?.Attribute("databasePath"), dbSheet);
    }

    static LbxSheet ReadSheet(XElement sheet)
    {
        var doc = new LabelDocument();
        var paper = sheet.Element(Style + "paper");
        if (paper != null)
        {
            double w = Len(paper, "width"), h = Len(paper, "height");
            // Băng P-touch: "width" là bề rộng băng; landscape nghĩa là nhãn nằm ngang (dài theo chiều chạy băng).
            bool landscape = (string?)paper.Attribute("orientation") == "landscape";
            doc.WidthMm = Math.Round(landscape ? h : w, 1);
            doc.HeightMm = Math.Round(landscape ? w : h, 1);
            doc.PaperColor = (string?)paper.Attribute("paperColor") ?? "#FFFFFF";
        }

        int skipped = 0;
        foreach (var obj in sheet.Element(Pt + "objects")?.Elements() ?? [])
        {
            var el = ReadObject(obj);
            if (el != null) doc.Elements.Add(el);
            else skipped++;
        }
        return new LbxSheet((string?)sheet.Attribute("name") ?? "Sheet", doc, skipped);
    }

    static LabelElement? ReadObject(XElement obj)
    {
        var os = obj.Element(Pt + "objectStyle");
        if (os == null) return null;

        LabelElement? el = null;
        if (obj.Name == Txt + "text") el = ReadText(obj, os);
        else if (obj.Name == Draw + "frame" || obj.Name == Draw + "rect")
            el = new FrameElement
            {
                StrokeMm = PenWidth(os),
                CornerMm = obj.Name == Draw + "rect" ? Len(obj.Element(Draw + "rectStyle"), "roundnessX") : 0,
                Filled = (string?)os.Element(Pt + "brush")?.Attribute("style") is { } b && b != "NULL",
            };
        else if (obj.Name == Draw + "line" || obj.Name == Draw + "poly")
            el = new LineElement
            {
                StrokeMm = Math.Max(0.1, PenWidth(os)),
                Orientation = Len(os, "height") > Len(os, "width") ? LineOrientation.Vertical : LineOrientation.Horizontal,
            };
        else if (obj.Name == Bc + "barcode") el = ReadBarcode(obj);

        if (el == null) return null;
        el.X = Len(os, "x");
        el.Y = Len(os, "y");
        el.W = Len(os, "width");
        el.H = Len(os, "height");
        return el;
    }

    static TextElement ReadText(XElement obj, XElement os)
    {
        var font = obj.Element(Txt + "ptFontInfo");
        var logFont = font?.Element(Txt + "logFont");
        var ext = font?.Element(Txt + "fontExt");
        var align = obj.Element(Txt + "textAlign");
        var control = obj.Element(Txt + "textControl");

        var t = new TextElement
        {
            Text = (string?)obj.Element(Pt + "data") ?? "",
            FontFamily = (string?)logFont?.Attribute("name") ?? "Arial",
            Bold = ((int?)logFont?.Attribute("weight") ?? 400) >= 600,
            Italic = (string?)logFont?.Attribute("italic") == "true",
            FontSizePt = ext != null ? Pt2Pt(ext, "size") : 12,
            ShrinkToFit = (string?)control?.Attribute("shrink") != "false",
            Wrap = (string?)control?.Attribute("autoLF") == "true" && (string?)control?.Attribute("control") != "LONGTEXTFIXED",
            HAlign = (string?)align?.Attribute("horizontalAlignment") switch
            {
                "LEFT" => HAlign.Left,
                "RIGHT" => HAlign.Right,
                _ => HAlign.Center,
            },
            VAlign = (string?)align?.Attribute("verticalAlignment") switch
            {
                "TOP" => VAlign.Top,
                "BOTTOM" => VAlign.Bottom,
                _ => VAlign.Center,
            },
        };

        // Trường trộn dữ liệu Excel → {{Tên cột}}
        var field = (string?)os.Element(Pt + "expanded")?.Attribute("dbMergeFieldStyleName");
        if (!string.IsNullOrWhiteSpace(field)) t.Text = "{{" + field + "}}";
        return t;
    }

    static BarcodeElement ReadBarcode(XElement obj)
    {
        var style = obj.Element(Bc + "barcodeStyle");
        string protocol = ((string?)style?.Attribute("protocol") ?? "").ToUpperInvariant();
        var b = new BarcodeElement
        {
            Data = (string?)obj.Element(Pt + "data") ?? "",
            Kind = protocol switch
            {
                var p when p.Contains("QR") => BarcodeKind.QrCode,
                var p when p.Contains("DATAMATRIX") => BarcodeKind.DataMatrix,
                var p when p.Contains("EAN13") || p.Contains("JAN13") => BarcodeKind.Ean13,
                var p when p.Contains("CODE39") => BarcodeKind.Code39,
                _ => BarcodeKind.Code128,
            },
            ShowText = (string?)style?.Attribute("humanReadable") != "false",
        };
        var field = (string?)obj.Element(Pt + "objectStyle")?.Element(Pt + "expanded")?.Attribute("dbMergeFieldStyleName");
        if (!string.IsNullOrWhiteSpace(field)) b.Data = "{{" + field + "}}";
        return b;
    }

    static double PenWidth(XElement os)
    {
        var pen = os.Element(Pt + "pen");
        return (string?)pen?.Attribute("style") == "NULL" ? 0 : Len(pen, "widthX");
    }

    /// <summary>Đọc độ dài ("13.6pt", "5mm", "1in") → mm.</summary>
    static double Len(XElement? e, string attr) => ParseLength((string?)e?.Attribute(attr), toPt: false);

    /// <summary>Đọc độ dài → pt (cỡ chữ).</summary>
    static double Pt2Pt(XElement e, string attr) => ParseLength((string?)e.Attribute(attr), toPt: true);

    static double ParseLength(string? s, bool toPt)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        s = s.Trim();
        double factorMm = 25.4 / 72; // pt → mm
        string num = s;
        if (s.EndsWith("pt")) num = s[..^2];
        else if (s.EndsWith("mm")) { num = s[..^2]; factorMm = 1; }
        else if (s.EndsWith("in")) { num = s[..^2]; factorMm = 25.4; }
        if (!double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 0;
        double mm = v * factorMm;
        return toPt ? mm * 72 / 25.4 : mm;
    }
}
