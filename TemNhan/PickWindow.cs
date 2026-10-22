using System.Windows;
using System.Windows.Controls;

namespace TemNhan;

/// <summary>Hộp thoại chọn một mục trong danh sách (trang nhãn, sheet Excel...).</summary>
public static class PickWindow
{
    public static string? Pick(Window owner, string title, string prompt, IReadOnlyList<string> items, string? preselect = null)
    {
        var list = new ListBox { ItemsSource = items, Height = 180, Margin = new Thickness(0, 6, 0, 10) };
        list.SelectedItem = items.Contains(preselect) ? preselect : items.FirstOrDefault();

        var ok = new Button { Content = "Chọn", IsDefault = true, Width = 90, Style = (Style)Application.Current.Resources["Primary"] };
        var cancel = new Button { Content = "Huỷ", IsCancel = true, Width = 90, Margin = new Thickness(8, 0, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(list);
        panel.Children.Add(buttons);

        var w = new Window
        {
            Title = title,
            Content = panel,
            Owner = owner,
            Width = 360,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
        };
        ok.Click += (_, _) => w.DialogResult = list.SelectedItem != null;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem != null) w.DialogResult = true; };
        return w.ShowDialog() == true ? list.SelectedItem as string : null;
    }
}
