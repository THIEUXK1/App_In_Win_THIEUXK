using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TemNhan.Model;

public abstract class Notifier : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    protected void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public enum HAlign { Left, Center, Right }
public enum VAlign { Top, Center, Bottom }
public enum LineOrientation { Horizontal, Vertical }
public enum BarcodeKind { Code128, Code39, Ean13, QrCode, DataMatrix }

/// <summary>Một đối tượng trên nhãn. Toạ độ và kích thước tính bằng mm từ góc trên-trái nhãn.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TextElement), "text")]
[JsonDerivedType(typeof(FrameElement), "frame")]
[JsonDerivedType(typeof(LineElement), "line")]
[JsonDerivedType(typeof(BarcodeElement), "barcode")]
[JsonDerivedType(typeof(ImageElement), "image")]
public abstract class LabelElement : Notifier
{
    double _x, _y, _w = 20, _h = 8;

    public double X { get => _x; set => Set(ref _x, Math.Round(value, 2)); }
    public double Y { get => _y; set => Set(ref _y, Math.Round(value, 2)); }
    public double W { get => _w; set => Set(ref _w, Math.Round(Math.Max(0.5, value), 2)); }
    public double H { get => _h; set => Set(ref _h, Math.Round(Math.Max(0.5, value), 2)); }

    int _rotation;
    string? _group;
    bool _locked, _printable = true;

    /// <summary>Góc xoay nội dung (0/90/180/270), quay quanh tâm khung.</summary>
    public int Rotation { get => _rotation; set => Set(ref _rotation, (value % 360 + 360) % 360 / 90 * 90); }
    /// <summary>Các đối tượng cùng nhóm được chọn và di chuyển cùng nhau.</summary>
    public string? GroupId { get => _group; set => Set(ref _group, value); }
    /// <summary>Khoá: không kéo / đổi kích thước bằng chuột.</summary>
    public bool Locked { get => _locked; set => Set(ref _locked, value); }
    /// <summary>Bỏ tích để đối tượng chỉ hiện khi thiết kế, không in ra.</summary>
    public bool Printable { get => _printable; set => Set(ref _printable, value); }

    [JsonIgnore] public abstract string KindName { get; }

    public LabelElement Clone() =>
        JsonSerializer.Deserialize<LabelElement>(JsonSerializer.Serialize(this, LabelJson.Options), LabelJson.Options)!;
}

public sealed class TextElement : LabelElement
{
    string _text = "Chữ";
    string _font = "Arial";
    double _size = 12;
    bool _bold, _italic, _shrink = true, _wrap;
    HAlign _h = HAlign.Center;
    VAlign _v = VAlign.Center;

    public override string KindName => "Chữ";

    /// <summary>Nội dung; <c>{{Tên cột}}</c> được thay bằng dữ liệu Excel khi in hàng loạt.</summary>
    public string Text { get => _text; set => Set(ref _text, value ?? ""); }
    public string FontFamily { get => _font; set => Set(ref _font, string.IsNullOrWhiteSpace(value) ? "Arial" : value); }
    public double FontSizePt { get => _size; set => Set(ref _size, Math.Clamp(Math.Round(value, 1), 2, 400)); }
    public bool Bold { get => _bold; set => Set(ref _bold, value); }
    public bool Italic { get => _italic; set => Set(ref _italic, value); }
    public HAlign HAlign { get => _h; set => Set(ref _h, value); }
    public VAlign VAlign { get => _v; set => Set(ref _v, value); }
    /// <summary>Tự thu nhỏ cỡ chữ để vừa khung.</summary>
    public bool ShrinkToFit { get => _shrink; set => Set(ref _shrink, value); }
    public bool Wrap { get => _wrap; set => Set(ref _wrap, value); }
}

public sealed class FrameElement : LabelElement
{
    double _stroke = 0.3, _corner;
    bool _filled;

    public override string KindName => "Khung";

    public double StrokeMm { get => _stroke; set => Set(ref _stroke, Math.Clamp(Math.Round(value, 2), 0, 20)); }
    public double CornerMm { get => _corner; set => Set(ref _corner, Math.Clamp(Math.Round(value, 2), 0, 100)); }
    public bool Filled { get => _filled; set => Set(ref _filled, value); }
}

public sealed class LineElement : LabelElement
{
    double _stroke = 0.3;
    LineOrientation _orientation;

    public override string KindName => "Đường kẻ";

    public double StrokeMm { get => _stroke; set => Set(ref _stroke, Math.Clamp(Math.Round(value, 2), 0.05, 20)); }
    public LineOrientation Orientation { get => _orientation; set => Set(ref _orientation, value); }
}

public sealed class BarcodeElement : LabelElement
{
    BarcodeKind _kind = BarcodeKind.Code128;
    string _data = "123456789";
    bool _showText = true;
    double _textSize = 7;

    public override string KindName => "Mã vạch";

    public BarcodeKind Kind { get => _kind; set { if (Set(ref _kind, value)) Raise(nameof(Is1D)); } }
    public string Data { get => _data; set => Set(ref _data, value ?? ""); }
    public bool ShowText { get => _showText; set => Set(ref _showText, value); }
    public double TextSizePt { get => _textSize; set => Set(ref _textSize, Math.Clamp(Math.Round(value, 1), 3, 72)); }

    [JsonIgnore] public bool Is2D => Kind is BarcodeKind.QrCode or BarcodeKind.DataMatrix;
    [JsonIgnore] public bool Is1D => !Is2D;
}

/// <summary>Ảnh / logo, nhúng thẳng vào file mẫu (base64) để chép file .tnl sang máy khác vẫn còn ảnh.</summary>
public sealed class ImageElement : LabelElement
{
    string _data = "";
    bool _keepAspect = true, _mono;

    public override string KindName => "Ảnh";

    /// <summary>Nội dung file ảnh (PNG/JPG/BMP...) dạng base64.</summary>
    public string ImageData { get => _data; set => Set(ref _data, value ?? ""); }
    public bool KeepAspect { get => _keepAspect; set => Set(ref _keepAspect, value); }
    /// <summary>Chuyển trắng đen (máy in nhiệt / băng chỉ in được một màu).</summary>
    public bool Monochrome { get => _mono; set => Set(ref _mono, value); }
}

public sealed class LabelDocument : Notifier
{
    double _w = 58.7, _h = 24;
    string? _dataPath, _dataSheet;

    public LabelDocument() => Elements.CollectionChanged += OnElementsChanged;

    public double WidthMm { get => _w; set { if (Set(ref _w, Math.Clamp(Math.Round(value, 2), 5, 1000))) Changed?.Invoke("doc"); } }
    public double HeightMm { get => _h; set { if (Set(ref _h, Math.Clamp(Math.Round(value, 2), 5, 1000))) Changed?.Invoke("doc"); } }

    double _margin;
    bool _autoLength;

    /// <summary>Lề (mm) quanh nhãn: hiện vạch lề khi thiết kế, dùng cho "Vừa vùng in" và căn chỉnh.</summary>
    public double MarginMm { get => _margin; set { if (Set(ref _margin, Math.Clamp(Math.Round(value, 2), 0, 50))) Changed?.Invoke("doc"); } }

    /// <summary>Độ dài tự động (như bỏ "Fix Length" của P-touch): chiều rộng nhãn = mép phải đối tượng xa nhất + lề.</summary>
    public bool AutoLength { get => _autoLength; set { if (Set(ref _autoLength, value)) Changed?.Invoke("doc"); } }

    string _name = "Trang 1";
    /// <summary>Tên trang (tab) trong dự án nhiều trang.</summary>
    public string Name { get => _name; set => Set(ref _name, string.IsNullOrWhiteSpace(value) ? "Trang" : value.Trim()); }

    string _paper = "#FFFFFF";
    /// <summary>Màu giấy/băng — chỉ để xem trên màn hình, khi in nền luôn trắng.</summary>
    public string PaperColor { get => _paper; set { if (Set(ref _paper, string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.ToUpperInvariant())) Changed?.Invoke("doc"); } }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ObservableCollection<LabelElement> Elements { get; } = new();

    /// <summary>File Excel/CSV gắn với mẫu (tuỳ chọn).</summary>
    public string? DataSourcePath { get => _dataPath; set => Set(ref _dataPath, value); }
    public string? DataSheet { get => _dataSheet; set => Set(ref _dataSheet, value); }

    /// <summary>Có thay đổi nội dung. Tham số là khoá gộp cho hoàn tác (cùng đối tượng → gộp).</summary>
    public event Action<string>? Changed;

    int _structureVersion;

    void OnElementsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (LabelElement el in e.OldItems) el.PropertyChanged -= OnElementChanged;
        if (e.NewItems != null) foreach (LabelElement el in e.NewItems) el.PropertyChanged += OnElementChanged;
        Changed?.Invoke("structure#" + ++_structureVersion);
    }

    void OnElementChanged(object? sender, PropertyChangedEventArgs e) =>
        Changed?.Invoke("el#" + RuntimeHelpers.GetHashCode(sender!));

    public string Serialize() => JsonSerializer.Serialize(this, LabelJson.Options);

    public static LabelDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<LabelDocument>(json, LabelJson.Options) ?? new LabelDocument();
}

public static class LabelJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
    };
}
