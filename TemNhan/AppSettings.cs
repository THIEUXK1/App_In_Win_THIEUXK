using System.IO;
using System.Text.Json;

namespace TemNhan;

public sealed record LabelPreset(string Name, double WidthMm, double HeightMm);

/// <summary>Thiết lập ghi nhớ giữa các lần mở app (%AppData%\TemNhan\settings.json).</summary>
public sealed class AppSettings
{
    public string? Printer { get; set; }
    public bool Rotate { get; set; }
    /// <summary>Tuỳ chọn "Xoay 90°" đã chọn cho từng máy in.</summary>
    public Dictionary<string, bool> RotateByPrinter { get; set; } = new();
    public double OffsetXmm { get; set; }
    public double OffsetYmm { get; set; }
    public int Copies { get; set; } = 1;
    public string? LastFolder { get; set; }
    /// <summary>File mẫu mở / lưu gần đây (mới nhất trước).</summary>
    public List<string> RecentFiles { get; set; } = [];
    // Tuỳ chọn máy in băng / driver (chỉ áp dụng nếu máy hỗ trợ)
    public bool AutoCut { get; set; } = true;
    public bool HalfCut { get; set; }
    public bool ChainPrint { get; set; }
    public bool Mirror { get; set; }
    public bool HighQuality { get; set; }
    /// <summary>Khổ nhãn có sẵn (người dùng thêm/xoá được). Null = chưa từng sửa → dùng danh sách mặc định.</summary>
    public List<LabelPreset>? Presets { get; set; }

    public static List<LabelPreset> DefaultPresets() =>
    [
        new("50 × 30 mm", 50, 30), new("40 × 30 mm", 40, 30), new("35 × 22 mm", 35, 22),
        new("60 × 40 mm", 60, 40), new("70 × 50 mm", 70, 50), new("100 × 50 mm", 100, 50),
        new("100 × 100 mm", 100, 100), new("100 × 150 mm (vận đơn)", 100, 150),
        new("Băng 12 mm × 40 mm", 40, 12), new("Băng 24 mm × 58,7 mm", 58.7, 24),
    ];

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TemNhan", "settings.json");

    public static AppSettings Current { get; } = Load();

    static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch { /* không quan trọng */ }
    }
}
