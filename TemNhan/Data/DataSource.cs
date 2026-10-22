using System.Data;
using System.IO;
using System.Text;
using ClosedXML.Excel;

namespace TemNhan.Data;

/// <summary>Đọc dữ liệu in hàng loạt từ Excel (.xlsx/.xlsm) hoặc CSV. Dòng đầu là tên cột.</summary>
public static class DataSource
{
    public const string FileFilter = "Dữ liệu (*.xlsx;*.xlsm;*.csv;*.txt)|*.xlsx;*.xlsm;*.csv;*.txt|Tất cả|*.*";

    public static bool IsExcel(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xlsm";

    public static IReadOnlyList<string> GetSheets(string path)
    {
        if (!IsExcel(path)) return [];
        using var fs = OpenShared(path);
        using var wb = new XLWorkbook(fs);
        return wb.Worksheets.Select(w => w.Name).ToList();
    }

    public static DataTable Load(string path, string? sheet)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".xls")
            throw new NotSupportedException("File .xls (Excel 97-2003) chưa hỗ trợ. Hãy mở bằng Excel và lưu lại thành .xlsx.");
        return IsExcel(path) ? LoadExcel(path, sheet) : LoadCsv(path);
    }

    /// <summary>Mở được cả khi file đang mở trong Excel.</summary>
    static FileStream OpenShared(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    static DataTable LoadExcel(string path, string? sheet)
    {
        using var fs = OpenShared(path);
        using var wb = new XLWorkbook(fs);
        var ws = sheet != null && wb.TryGetWorksheet(sheet, out var found) ? found : wb.Worksheet(1);
        var table = new DataTable(ws.Name);
        var range = ws.RangeUsed();
        if (range == null) return table;

        int firstCol = range.FirstColumn().ColumnNumber(), lastCol = range.LastColumn().ColumnNumber();
        int firstRow = range.FirstRow().RowNumber(), lastRow = range.LastRow().RowNumber();

        AddColumns(table, Enumerable.Range(firstCol, lastCol - firstCol + 1)
            .Select(c => ws.Cell(firstRow, c).GetFormattedString()));

        for (int r = firstRow + 1; r <= lastRow; r++)
        {
            var values = Enumerable.Range(firstCol, lastCol - firstCol + 1)
                .Select(c => ws.Cell(r, c).GetFormattedString().Trim()).ToArray();
            if (values.All(string.IsNullOrEmpty)) continue;
            table.Rows.Add(values);
        }
        return table;
    }

    static DataTable LoadCsv(string path)
    {
        string text;
        using (var fs = OpenShared(path))
        using (var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            text = reader.ReadToEnd();

        string firstLine = text.Split('\n')[0];
        char sep = new[] { ',', ';', '\t' }.OrderByDescending(c => firstLine.Count(ch => ch == c)).First();

        var rows = ParseCsv(text, sep).ToList();
        var table = new DataTable(Path.GetFileNameWithoutExtension(path));
        if (rows.Count == 0) return table;

        int cols = rows.Max(r => r.Count);
        AddColumns(table, Enumerable.Range(0, cols).Select(i => i < rows[0].Count ? rows[0][i] : ""));
        foreach (var r in rows.Skip(1))
        {
            if (r.All(string.IsNullOrWhiteSpace)) continue;
            table.Rows.Add(Enumerable.Range(0, cols).Select(i => i < r.Count ? r[i].Trim() : "").ToArray<object>());
        }
        return table;
    }

    static void AddColumns(DataTable table, IEnumerable<string> headers)
    {
        int i = 0;
        foreach (var raw in headers)
        {
            i++;
            string name = string.IsNullOrWhiteSpace(raw) ? "F" + i : raw.Trim();
            string unique = name;
            for (int n = 2; table.Columns.Contains(unique); n++) unique = $"{name} ({n})";
            table.Columns.Add(unique, typeof(string));
        }
    }

    static IEnumerable<List<string>> ParseCsv(string text, char sep)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == sep) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                yield return row;
                row = new List<string>();
            }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString().TrimEnd('\r'));
            yield return row;
        }
    }

    public static Dictionary<string, string> ToDictionary(DataRow row)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DataColumn c in row.Table.Columns) d[c.ColumnName] = row[c]?.ToString() ?? "";
        return d;
    }
}
