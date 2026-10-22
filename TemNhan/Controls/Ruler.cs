using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TemNhan.Controls;

/// <summary>Thước kẻ mm cạnh vùng thiết kế (giống P-touch Editor): nền xanh nhạt, đoạn ứng với nhãn để trắng.</summary>
public sealed class Ruler : FrameworkElement
{
    static readonly Brush Bg = Freeze(new SolidColorBrush(Color.FromRgb(0xDC, 0xE4, 0xF2)));
    static readonly Pen TickPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80)), 1));
    static readonly Pen EdgePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xC5, 0xCD, 0xDA)), 1));
    static readonly Typeface Font = new("Segoe UI");

    double _dipPerMm = 1, _origin, _labelMm;

    public Orientation Orientation { get; set; } = Orientation.Horizontal;

    public Ruler() => ClipToBounds = true;

    /// <param name="dipPerMm">Tỉ lệ hiện tại.</param>
    /// <param name="origin">Vị trí (DIP, trong thước) của mép nhãn = 0 mm.</param>
    /// <param name="labelMm">Chiều dài nhãn theo trục của thước.</param>
    public void Update(double dipPerMm, double origin, double labelMm)
    {
        _dipPerMm = dipPerMm;
        _origin = origin;
        _labelMm = labelMm;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        bool h = Orientation == Orientation.Horizontal;
        double len = h ? ActualWidth : ActualHeight, thick = h ? ActualHeight : ActualWidth;
        if (len <= 0 || thick <= 0) return;

        dc.DrawRectangle(Bg, null, new Rect(RenderSize));
        double a = _origin, b = _origin + _labelMm * _dipPerMm;
        dc.DrawRectangle(Brushes.White, null, h ? new Rect(a, 0, Math.Max(0, b - a), thick) : new Rect(0, a, thick, Math.Max(0, b - a)));

        // Bước vạch theo mức thu phóng để vạch không dính nhau
        double minor = _dipPerMm >= 4 ? 1 : _dipPerMm >= 1.2 ? 5 : 10;
        double labelStep = 10 * _dipPerMm >= 28 ? 10 : 50 * _dipPerMm >= 28 ? 50 : 100;
        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        int first = (int)Math.Floor(-_origin / _dipPerMm / minor);
        int last = (int)Math.Ceiling((len - _origin) / _dipPerMm / minor);
        for (int i = first; i <= last; i++)
        {
            double mm = i * minor;
            double p = Math.Round(_origin + mm * _dipPerMm) + 0.5;
            double tick = mm % 10 == 0 ? thick * 0.5 : mm % 5 == 0 ? thick * 0.32 : thick * 0.18;
            if (h) dc.DrawLine(TickPen, new Point(p, thick - tick), new Point(p, thick));
            else dc.DrawLine(TickPen, new Point(thick - tick, p), new Point(thick, p));

            if (mm % labelStep != 0) continue;
            var ft = new FormattedText(mm.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Font, 9.5, TickPen.Brush, pixelsPerDip);
            if (h) dc.DrawText(ft, new Point(p + 2, 1));
            else
            {
                dc.PushTransform(new RotateTransform(-90, 1, p - 2));
                dc.DrawText(ft, new Point(1, p - 2));
                dc.Pop();
            }
        }

        if (h) dc.DrawLine(EdgePen, new Point(0, thick - 0.5), new Point(len, thick - 0.5));
        else dc.DrawLine(EdgePen, new Point(thick - 0.5, 0), new Point(thick - 0.5, len));
    }

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
