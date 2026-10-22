using System.Windows;
using System.Windows.Controls;

namespace TemNhan;

/// <summary>Hộp thoại nhập một dòng chữ (đổi tên trang...).</summary>
public static class TextPrompt
{
    public static string? Ask(Window owner, string title, string prompt, string initial)
    {
        var box = new TextBox { Text = initial, Margin = new Thickness(0, 6, 0, 14), Padding = new Thickness(4, 3, 4, 3) };
        var ok = new Button { Content = "OK", IsDefault = true, Width = 90, Style = (Style)Application.Current.Resources["Primary"] };
        var cancel = new Button { Content = "Huỷ", IsCancel = true, Width = 90, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt });
        panel.Children.Add(box);
        panel.Children.Add(buttons);

        var w = new Window
        {
            Title = title,
            Content = panel,
            Owner = owner,
            Width = 340,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            FontSize = 13,
        };
        ok.Click += (_, _) => w.DialogResult = !string.IsNullOrWhiteSpace(box.Text);
        w.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return w.ShowDialog() == true ? box.Text.Trim() : null;
    }
}
