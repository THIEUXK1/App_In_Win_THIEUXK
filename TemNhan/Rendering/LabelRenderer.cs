using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using TemNhan.Model;
using ZXing;
using ZXing.Common;
using ZXing.QrCode.Internal;

namespace TemNhan.Rendering;

public sealed class RenderContext
{
    /// <summary>DIP trên mỗi mm (1 DIP = 1/96 inch).</summary>
    public double Scale = LabelRenderer.DipPerMm;
    public IReadOnlyDictionary<string, string>? Row;
    public double PixelsPerDip = 1;
    /// <summary>Vẽ ô báo lỗi đỏ khi mã vạch không hợp lệ (chỉ trên màn hình).</summary>
    public bool ShowErrors;
    public List<string>? Errors;
    /// <summary>Nền nhãn; khi in luôn là trắng.</summary>
    public Brush Paper = Brushes.White;
    /// <summary>Thứ tự nhãn trong lệnh in (0, 1, 2...) cho trường <c>{{SỐ:001}}</c>.</summary>
    public int SerialIndex;
    /// <summary>Thời điểm in cho <c>{{NGÀY}}</c> / <c>{{GIỜ}}</c>.</summary>
    public DateTime Now = DateTime.Now;
    /// <summary>Đang in / xem trước khi in: bỏ qua các đối tượng không in.</summary>
    public bool Printing;

    public double Zoom => Scale / LabelRenderer.DipPerMm;
}

/// <summary>Vẽ nhãn vào DrawingContext — dùng chung cho khung thiết kế và bản in.</summary>
public static partial class LabelRenderer
{
    public const double DipPerMm = 96 / 25.4;
    public const double DipPerPt = 96 / 72.0;

    [GeneratedRegex(@"\{\{(.+?)\}\}")]
    private static partial Regex FieldRegex();

    /// <summary>
    /// Thay <c>{{Tên cột}}</c> bằng giá trị của dòng dữ liệu; cột không có thì giữ nguyên.
    /// Trường có sẵn (khi không trùng tên cột): <c>{{NGÀY}}</c>, <c>{{NGÀY:dd/MM/yyyy}}</c>, <c>{{GIỜ}}</c>, <c>{{GIỜ:HH:mm}}</c>,
    /// <c>{{SỐ:001}}</c> (đánh số tự tăng từ 001, mỗi nhãn in ra +1).
    /// </summary>
    public static string Merge(string text, RenderContext ctx) => Merge(text, ctx.Row, ctx.SerialIndex, ctx.Now);

    public static string Merge(string text, IReadOnlyDictionary<string, string>? row, int serial = 0, DateTime? now = null) =>
        FieldRegex().Replace(text, m =>
        {
            string name = m.Groups[1].Value.Trim();
            if (row != null && row.TryGetValue(name, out var v)) return v;
            return BuiltIn(name, serial, now ?? DateTime.Now) ?? m.Value;
        });

    static string? BuiltIn(string name, int serial, DateTime now)
    {
        int colon = name.IndexOf(':');
        string key = Fold(colon < 0 ? name : name[..colon]);
        string? arg = colon < 0 ? null : name[(colon + 1)..];
        try
        {
            return key switch
            {
                "NGAY" => now.ToString(arg ?? "dd/MM/yyyy", CultureInfo.GetCultureInfo("vi-VN")),
                "GIO" => now.ToString(arg ?? "HH:mm", CultureInfo.GetCultureInfo("vi-VN")),
                "SO" => long.TryParse(arg ?? "1", out var start)
                    ? (start + serial).ToString(new string('0', Math.Max(1, (arg ?? "1").Length)))
                    : null,
                _ => null,
            };
        }
        catch (System.FormatException) { return null; }
    }

    /// <summary>Bỏ dấu, viết hoa: "Ngày" → "NGAY".</summary>
    static string Fold(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder();
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToUpperInvariant().Trim();
    }

    /// <summary>Trường có sẵn (ngày, giờ, số tự tăng) — không cần cột Excel.</summary>
    public static bool IsBuiltIn(string field)
    {
        int colon = field.IndexOf(':');
        return Fold(colon < 0 ? field : field[..colon]) is "NGAY" or "GIO" or "SO";
    }

    public static IEnumerable<string> FieldsIn(string text) =>
        FieldRegex().Matches(text).Select(m => m.Groups[1].Value.Trim());

    /// <summary>Có dùng trường đánh số tự tăng không (khi đó mỗi bản in là một nhãn khác nhau).</summary>
    public static bool UsesSerial(LabelDocument doc) =>
        doc.Elements.Any(el => (el switch { TextElement t => t.Text, BarcodeElement b => b.Data, _ => "" })
            is var s && FieldsIn(s).Any(f => { int c = f.IndexOf(':'); return Fold(c < 0 ? f : f[..c]) == "SO"; }));

    public static void Render(DrawingContext dc, LabelDocument doc, RenderContext ctx)
    {
        var bounds = new Rect(0, 0, doc.WidthMm * ctx.Scale, doc.HeightMm * ctx.Scale);
        dc.DrawRectangle(ctx.Paper, null, bounds);
        dc.PushClip(new RectangleGeometry(bounds));
        foreach (var el in doc.Elements)
        {
            if (ctx.Printing && !el.Printable) continue;
            var r = new Rect(el.X * ctx.Scale, el.Y * ctx.Scale, el.W * ctx.Scale, el.H * ctx.Scale);
            // Xoay quanh tâm khung: nội dung vẽ trong khung đã đổi chiều (90°/270° thì rộng ↔ cao)
            if (el.Rotation != 0)
            {
                var c = new Point(r.X + r.Width / 2, r.Y + r.Height / 2);
                dc.PushTransform(new RotateTransform(el.Rotation, c.X, c.Y));
                if (el.Rotation is 90 or 270) r = new Rect(c.X - r.Height / 2, c.Y - r.Width / 2, r.Height, r.Width);
            }
            switch (el)
            {
                case TextElement t: DrawText(dc, t, r, ctx); break;
                case FrameElement f: DrawFrame(dc, f, r, ctx); break;
                case LineElement l: DrawLine(dc, l, r, ctx); break;
                case BarcodeElement b: DrawBarcode(dc, b, r, ctx); break;
                case ImageElement i: DrawImage(dc, i, r); break;
            }
            if (el.Rotation != 0) dc.Pop();
        }
        dc.Pop();
    }

    static void DrawText(DrawingContext dc, TextElement t, Rect r, RenderContext ctx)
    {
        string text = Merge(t.Text, ctx);
        if (text.Length == 0) return;

        var typeface = new Typeface(new FontFamily(t.FontFamily),
            t.Italic ? FontStyles.Italic : FontStyles.Normal,
            t.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);

        double size = t.FontSizePt * DipPerPt * ctx.Zoom;
        FormattedText ft;
        for (int i = 0; ; i++)
        {
            ft = Format(text, typeface, size, ctx, t.Wrap ? r.Width : null);
            bool fits = ft.Height <= r.Height + 0.5 && (t.Wrap || ft.WidthIncludingTrailingWhitespace <= r.Width + 0.5);
            if (!t.ShrinkToFit || fits || i > 60 || size < 2 * DipPerPt * ctx.Zoom) break;
            size *= 0.95;
        }

        // Không xuống dòng: đặt độ rộng dòng ≥ khung để căn lề từng dòng vẫn đúng.
        double lineW = t.Wrap ? r.Width : Math.Max(r.Width, ft.WidthIncludingTrailingWhitespace + 0.5);
        ft.MaxTextWidth = Math.Max(1, lineW);
        ft.TextAlignment = t.HAlign switch { HAlign.Left => TextAlignment.Left, HAlign.Right => TextAlignment.Right, _ => TextAlignment.Center };

        double x = r.X + t.HAlign switch
        {
            HAlign.Left => 0,
            HAlign.Right => r.Width - lineW,
            _ => (r.Width - lineW) / 2,
        };
        double y = r.Y + t.VAlign switch
        {
            VAlign.Top => 0,
            VAlign.Bottom => r.Height - ft.Height,
            _ => (r.Height - ft.Height) / 2,
        };
        dc.DrawText(ft, new Point(x, y));
    }

    static FormattedText Format(string text, Typeface typeface, double size, RenderContext ctx, double? maxWidth)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            typeface, size, Brushes.Black, ctx.PixelsPerDip);
        if (maxWidth is double w) ft.MaxTextWidth = Math.Max(1, w);
        return ft;
    }

    static readonly Dictionary<(string, bool), System.Windows.Media.Imaging.BitmapSource?> ImageCache = new();

    /// <summary>Giải mã ảnh base64 (có đệm). Trả null nếu dữ liệu ảnh hỏng.</summary>
    public static System.Windows.Media.Imaging.BitmapSource? DecodeImage(string base64, bool gray)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        if (ImageCache.TryGetValue((base64, gray), out var cached)) return cached;
        System.Windows.Media.Imaging.BitmapSource? bmp = null;
        try
        {
            var img = new System.Windows.Media.Imaging.BitmapImage();
            img.BeginInit();
            img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            img.StreamSource = new System.IO.MemoryStream(Convert.FromBase64String(base64));
            img.EndInit();
            bmp = gray ? new System.Windows.Media.Imaging.FormatConvertedBitmap(img, PixelFormats.Gray8, null, 0) : img;
            bmp.Freeze();
        }
        catch (Exception) { /* ảnh hỏng → vẽ ô báo */ }
        if (ImageCache.Count > 64) ImageCache.Clear();
        ImageCache[(base64, gray)] = bmp;
        return bmp;
    }

    static void DrawImage(DrawingContext dc, ImageElement img, Rect r)
    {
        var bmp = DecodeImage(img.ImageData, img.Monochrome);
        if (bmp == null)
        {
            dc.DrawRectangle(null, new Pen(Brushes.Gray, 1) { DashStyle = DashStyles.Dash }, r);
            return;
        }
        var target = r;
        if (img.KeepAspect && bmp.PixelWidth > 0 && bmp.PixelHeight > 0)
        {
            double k = Math.Min(r.Width / bmp.PixelWidth, r.Height / bmp.PixelHeight);
            double w = bmp.PixelWidth * k, h = bmp.PixelHeight * k;
            target = new Rect(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h);
        }
        dc.DrawImage(bmp, target);
    }

    static void DrawFrame(DrawingContext dc, FrameElement f, Rect r, RenderContext ctx)
    {
        double t = f.StrokeMm * ctx.Scale;
        var pen = t > 0 ? new Pen(Brushes.Black, t) : null;
        // Viền nằm trong khung (giống INSIDEFRAME của P-touch)
        var inner = r;
        if (t > 0 && r.Width > t && r.Height > t) inner.Inflate(-t / 2, -t / 2);
        double radius = f.CornerMm * ctx.Scale;
        dc.DrawRoundedRectangle(f.Filled ? Brushes.Black : null, pen, inner, radius, radius);
    }

    static void DrawLine(DrawingContext dc, LineElement l, Rect r, RenderContext ctx)
    {
        var pen = new Pen(Brushes.Black, l.StrokeMm * ctx.Scale);
        if (l.Orientation == LineOrientation.Horizontal)
            dc.DrawLine(pen, new Point(r.Left, r.Top + r.Height / 2), new Point(r.Right, r.Top + r.Height / 2));
        else
            dc.DrawLine(pen, new Point(r.Left + r.Width / 2, r.Top), new Point(r.Left + r.Width / 2, r.Bottom));
    }

    static void DrawBarcode(DrawingContext dc, BarcodeElement b, Rect r, RenderContext ctx)
    {
        string data = Merge(b.Data, ctx);
        BitMatrix m;
        try
        {
            m = Encode(b.Kind, data);
        }
        catch (Exception ex)
        {
            ctx.Errors?.Add($"Mã vạch \"{data}\": {ex.Message}");
            if (ctx.ShowErrors)
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 220, 0, 0)), new Pen(Brushes.Red, 1), r);
                var ft = Format("Lỗi mã vạch", new Typeface("Segoe UI"), Math.Max(8, Math.Min(14, r.Height / 3)), ctx, r.Width);
                ft.TextAlignment = TextAlignment.Center;
                ft.SetForegroundBrush(Brushes.Red);
                dc.DrawText(ft, new Point(r.X, r.Y + (r.Height - ft.Height) / 2));
            }
            return;
        }

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            if (b.Is2D)
            {
                double mod = Math.Min(r.Width / m.Width, r.Height / m.Height);
                double ox = r.X + (r.Width - mod * m.Width) / 2, oy = r.Y + (r.Height - mod * m.Height) / 2;
                for (int y = 0; y < m.Height; y++)
                    foreach (var (x0, x1) in Runs(m, y))
                        // chồng nhẹ theo chiều dọc để không lộ khe trắng giữa các hàng khi khử răng cưa
                        AddRect(g, new Rect(ox + x0 * mod, oy + y * mod, (x1 - x0) * mod, mod * 1.02));
            }
            else
            {
                double textH = b.ShowText ? b.TextSizePt * DipPerPt * ctx.Zoom * 1.2 : 0;
                double barsH = Math.Max(r.Height * 0.3, r.Height - textH);
                double mod = r.Width / m.Width;
                foreach (var (x0, x1) in Runs(m, 0))
                    AddRect(g, new Rect(r.X + x0 * mod, r.Y, (x1 - x0) * mod, barsH));

                if (b.ShowText)
                {
                    var ft = Format(data, new Typeface("Arial"), b.TextSizePt * DipPerPt * ctx.Zoom, ctx, r.Width);
                    ft.TextAlignment = TextAlignment.Center;
                    dc.DrawText(ft, new Point(r.X, r.Y + barsH));
                }
            }
        }
        geo.Freeze();
        dc.DrawGeometry(Brushes.Black, null, geo);
    }

    /// <summary>Lỗi mã vạch của một nhãn (dữ liệu không hợp lệ với loại mã).</summary>
    public static IEnumerable<string> Validate(LabelDocument doc, IReadOnlyDictionary<string, string>? row)
    {
        foreach (var b in doc.Elements.OfType<BarcodeElement>())
        {
            string data = Merge(b.Data, row);
            string? error = null;
            try { Encode(b.Kind, data); }
            catch (Exception ex) { error = $"mã vạch \"{data}\": {ex.Message}"; }
            if (error != null) yield return error;
        }
    }

    static IEnumerable<(int, int)> Runs(BitMatrix m, int y)
    {
        int x = 0;
        while (x < m.Width)
        {
            if (!m[x, y]) { x++; continue; }
            int start = x;
            while (x < m.Width && m[x, y]) x++;
            yield return (start, x);
        }
    }

    static void AddRect(StreamGeometryContext g, Rect rc)
    {
        g.BeginFigure(rc.TopLeft, true, true);
        g.PolyLineTo([rc.TopRight, rc.BottomRight, rc.BottomLeft], false, false);
    }

    static readonly Dictionary<(BarcodeKind, string), BitMatrix> Cache = new();

    static BitMatrix Encode(BarcodeKind kind, string data)
    {
        if (string.IsNullOrEmpty(data)) throw new ArgumentException("chưa có dữ liệu");
        if (Cache.TryGetValue((kind, data), out var cached)) return cached;

        var hints = new Dictionary<EncodeHintType, object> { [EncodeHintType.MARGIN] = 0 };
        var format = kind switch
        {
            BarcodeKind.Code39 => BarcodeFormat.CODE_39,
            BarcodeKind.Ean13 => BarcodeFormat.EAN_13,
            BarcodeKind.QrCode => BarcodeFormat.QR_CODE,
            BarcodeKind.DataMatrix => BarcodeFormat.DATA_MATRIX,
            _ => BarcodeFormat.CODE_128,
        };
        if (kind == BarcodeKind.QrCode)
        {
            hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
            hints[EncodeHintType.ERROR_CORRECTION] = ErrorCorrectionLevel.M;
        }

        var m = new MultiFormatWriter().encode(data, format, 0, 0, hints);
        if (Cache.Count > 512) Cache.Clear();
        Cache[(kind, data)] = m;
        return m;
    }
}
