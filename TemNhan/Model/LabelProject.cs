using System.Text.Json;
using System.Text.Json.Nodes;

namespace TemNhan.Model;

/// <summary>File .tnl: nhiều trang nhãn (như Sheet của P-touch) + file dữ liệu gắn kèm.</summary>
public sealed class LabelProject
{
    public List<LabelDocument> Sheets { get; set; } = [];
    public string? DataSourcePath { get; set; }
    public string? DataSheet { get; set; }

    public string Serialize() => JsonSerializer.Serialize(this, LabelJson.Options);

    /// <summary>Đọc file .tnl; file cũ chỉ có một trang (có "Elements" ở gốc) vẫn mở được.</summary>
    public static LabelProject Deserialize(string json)
    {
        if (JsonNode.Parse(json) is JsonObject root && !root.ContainsKey("Sheets"))
        {
            var doc = LabelDocument.Deserialize(json);
            return new LabelProject { Sheets = [doc], DataSourcePath = doc.DataSourcePath, DataSheet = doc.DataSheet };
        }
        var p = JsonSerializer.Deserialize<LabelProject>(json, LabelJson.Options) ?? new LabelProject();
        if (p.Sheets.Count == 0) p.Sheets.Add(new LabelDocument());
        return p;
    }

    /// <summary>Tên "Trang N" chưa dùng.</summary>
    public static string NextName(IEnumerable<LabelDocument> sheets)
    {
        var used = sheets.Select(s => s.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; ; i++)
            if (!used.Contains("Trang " + i)) return "Trang " + i;
    }
}
