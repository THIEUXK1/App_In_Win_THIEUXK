using System.Windows;
using System.Windows.Media;
using TemNhan.Model;
using TemNhan.Rendering;

namespace TemNhan.Controls;

/// <summary>Một nhãn trong danh sách xem trước khi in.</summary>
public sealed record PreviewItem(LabelDocument Doc, int RowIndex, int Copies, string Caption,
    Func<int, IReadOnlyDictionary<string, string>?> RowAt, int Serial = 0)
{
    public string CopiesText => "× " + Copies;
}

/// <summary>Vẽ nhãn của <see cref="PreviewItem"/> (DataContext) đúng như khi in, thu nhỏ theo chiều cao.</summary>
public sealed class LabelPreview : FrameworkElement
{
    const double MaxHeight_ = 92, MaxWidth_ = 460;
    static readonly Pen Border = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xD0, 0xD4, 0xDB)), 1));

    public LabelPreview() => DataContextChanged += (_, _) => { InvalidateMeasure(); InvalidateVisual(); };

    double ScaleFor(LabelDocument d) =>
        Math.Min(MaxHeight_ / d.HeightMm, MaxWidth_ / d.WidthMm);

    protected override Size MeasureOverride(Size availableSize) =>
        DataContext is PreviewItem p ? new Size(p.Doc.WidthMm * ScaleFor(p.Doc), p.Doc.HeightMm * ScaleFor(p.Doc)) : new Size(0, 0);

    protected override void OnRender(DrawingContext dc)
    {
        if (DataContext is not PreviewItem p) return;
        var ctx = new RenderContext
        {
            Scale = ScaleFor(p.Doc),
            Row = p.RowIndex >= 0 ? p.RowAt(p.RowIndex) : p.RowAt(-1),
            PixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip,
            ShowErrors = true,
            SerialIndex = p.Serial,
            Printing = true,
        };
        LabelRenderer.Render(dc, p.Doc, ctx);
        dc.DrawRectangle(null, Border, new Rect(0.5, 0.5, RenderSize.Width - 1, RenderSize.Height - 1));
    }

    static T Freeze<T>(T f) where T : Freezable { f.Freeze(); return f; }
}
