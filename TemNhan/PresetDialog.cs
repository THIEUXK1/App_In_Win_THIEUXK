using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TemNhan;

/// <summary>Hộp thoại thêm khổ nhãn: tên + rộng + cao (điền sẵn khổ hiện tại).</summary>
public static class PresetDialog
{
    /// <param name="exists">Trả về tên khổ đã có cùng kích thước, hoặc null.</param>
    public static LabelPreset? Ask(Window owner, double width, double height, Func<double, double, string?> exists)
    {
        static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static bool TryNum(TextBox b, out double v) =>
            double.TryParse(b.Text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= 5 && v <= 1000;

        var name = new TextBox { Padding = new Thickness(4, 3, 4, 3) };
        var w = new TextBox { Text = Fmt(width), Width = 80, Padding = new Thickness(4, 3, 4, 3) };
        var h = new TextBox { Text = Fmt(height), Width = 80, Padding = new Thickness(4, 3, 4, 3) };
        var error = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

        bool nameEdited = false;
        void Suggest()
        {
            if (!nameEdited) name.Text = $"{w.Text.Trim()} × {h.Text.Trim()} mm";
            error.Text = TryNum(w, out var a) && TryNum(h, out var b) && exists(a, b) is { } same
                ? $"Khổ này đã có trong danh sách: \"{same}\"."
                : "";
        }
        name.TextChanged += (_, _) => nameEdited = name.IsKeyboardFocusWithin;
        w.TextChanged += (_, _) => Suggest();
        h.TextChanged += (_, _) => Suggest();
        Suggest();

        var size = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        size.Children.Add(new TextBlock { Text = "Rộng", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        size.Children.Add(w);
        size.Children.Add(new TextBlock { Text = "×  Cao", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 6, 0) });
        size.Children.Add(h);
        size.Children.Add(new TextBlock { Text = "mm", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });

        var ok = new Button { Content = "Thêm", IsDefault = true, Width = 90, Style = (Style)Application.Current.Resources["Primary"] };
        var cancel = new Button { Content = "Huỷ", IsCancel = true, Width = 90, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = "Kích thước" });
        panel.Children.Add(size);
        panel.Children.Add(new TextBlock { Text = "Tên hiển thị", Margin = new Thickness(0, 12, 0, 0) });
        panel.Children.Add(name);
        panel.Children.Add(error);
        panel.Children.Add(buttons);

        var win = new Window
        {
            Title = "Thêm khổ nhãn",
            Content = panel,
            Owner = owner,
            Width = 400,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FontSize = 13,
        };

        LabelPreset? result = null;
        ok.Click += (_, _) =>
        {
            if (!TryNum(w, out var a) || !TryNum(h, out var b)) { error.Text = "Rộng và Cao phải là số từ 5 đến 1000 (mm)."; return; }
            if (exists(a, b) is { } same) { error.Text = $"Khổ này đã có trong danh sách: \"{same}\". Hãy nhập kích thước khác."; return; }
            if (string.IsNullOrWhiteSpace(name.Text)) { error.Text = "Hãy đặt tên cho khổ."; return; }
            result = new LabelPreset(name.Text.Trim(), Math.Round(a, 2), Math.Round(b, 2));
            win.DialogResult = true;
        };
        win.Loaded += (_, _) => { w.Focus(); w.SelectAll(); };
        return win.ShowDialog() == true ? result : null;
    }
}
