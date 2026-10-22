using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using TemNhan.Model;

namespace TemNhan;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ev) =>
        {
            MessageBox.Show(ev.Exception.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            ev.Handled = true;
        };

        // Enter trong ô nhập một dòng → cập nhật giá trị ngay (các ô số dùng UpdateSourceTrigger=LostFocus)
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.KeyDownEvent, new KeyEventHandler((s, ev) =>
        {
            if (ev.Key == Key.Enter && s is TextBox { AcceptsReturn: false } tb)
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }));
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.GotKeyboardFocusEvent,
            new KeyboardFocusChangedEventHandler((s, _) => { if (s is TextBox { AcceptsReturn: false } tb) tb.SelectAll(); }));

        var window = new MainWindow();
        window.Show();
        if (e.Args.Length > 0) window.OpenFile(e.Args[0]);
    }
}

/// <summary>Số thập phân hiển thị gọn ("12.5"); khi nhập chấp nhận cả dấu phẩy lẫn dấu chấm.</summary>
public sealed class NumConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d ? d.ToString("0.##", CultureInfo.InvariantCulture) : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        double.TryParse((value as string)?.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
            ? d
            : DependencyProperty.UnsetValue;
}

public static class EnumLists
{
    public static HAlign[] HAligns { get; } = Enum.GetValues<HAlign>();
    public static VAlign[] VAligns { get; } = Enum.GetValues<VAlign>();
    public static LineOrientation[] Orientations { get; } = Enum.GetValues<LineOrientation>();
    public static BarcodeKind[] BarcodeKinds { get; } = Enum.GetValues<BarcodeKind>();
}

/// <summary>Tên tiếng Việt cho các giá trị enum hiển thị trong ComboBox.</summary>
public sealed class EnumNameConverter : IValueConverter
{
    static readonly Dictionary<object, string> Names = new()
    {
        [HAlign.Left] = "Trái",
        [HAlign.Center] = "Giữa",
        [HAlign.Right] = "Phải",
        [VAlign.Top] = "Trên",
        [VAlign.Center] = "Giữa",
        [VAlign.Bottom] = "Dưới",
        [LineOrientation.Horizontal] = "Ngang",
        [LineOrientation.Vertical] = "Dọc",
        [BarcodeKind.Code128] = "Code 128",
        [BarcodeKind.Code39] = "Code 39",
        [BarcodeKind.Ean13] = "EAN-13",
        [BarcodeKind.QrCode] = "QR Code",
        [BarcodeKind.DataMatrix] = "Data Matrix",
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value != null && Names.TryGetValue(value, out var n) ? n : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
