using System.Printing;
using System.Runtime.InteropServices;

namespace TemNhan.Printing;

/// <summary>Một khổ giấy / tem khai báo trong driver hoặc Control Panel. HeightMm null = băng cuộn (độ dài tự do).</summary>
public sealed record DriverMedia(string Name, double WidthMm, double? HeightMm, string Source)
{
    public string Display => HeightMm is double h
        ? $"{Name} ({WidthMm:0.#}×{h:0.#})"
        : $"{Name} (băng)";

    public string Detail => HeightMm is double h
        ? $"{Name}: {WidthMm:0.##} × {h:0.##} mm — {(Source == "driver" ? "khai báo trong driver" : "Forms trong Control Panel")}"
        : $"{Name}: băng cuộn rộng {WidthMm:0.##} mm, độ dài tự do — khai báo trong driver";
}

/// <summary>
/// Đọc các khổ tem của một máy in từ 2 nguồn:
/// (1) khả năng của driver (Print Schema psk:PageMediaSize — ví dụ các khổ băng của Brother),
/// (2) danh sách Forms của Windows cho máy in đó (khổ do driver khai báo hoặc người dùng tạo trong Control Panel).
/// </summary>
public static class DriverMediaReader
{
    const string PsfNs = "http://schemas.microsoft.com/windows/2003/08/printing/printschemaframework";

    public static List<DriverMedia> Read(string printer)
    {
        var list = new List<DriverMedia>();
        try { list.AddRange(FromCapabilities(printer)); } catch { }
        try { list.AddRange(FromForms(printer)); } catch { }
        // Bỏ trùng kích thước; khổ của driver trước, rồi khổ cỡ tem (≤ 200 mm), khổ giấy lớn xuống cuối
        return list
            .GroupBy(m => (Math.Round(m.WidthMm, 1), m.HeightMm is double h ? Math.Round(h, 1) : -1))
            .Select(g => g.First())
            .OrderBy(m => m.Source == "driver" ? 0 : Math.Max(m.WidthMm, m.HeightMm ?? 0) <= 200 ? 1 : 2)
            .ToList();
    }

    static IEnumerable<DriverMedia> FromCapabilities(string printer)
    {
        using var server = new LocalPrintServer();
        var queue = server.GetPrintQueues([EnumeratedPrintQueueTypes.Local, EnumeratedPrintQueueTypes.Connections])
            .FirstOrDefault(q => q.FullName == printer);
        if (queue == null) yield break;

        var xml = new System.Xml.XmlDocument();
        using (var ms = queue.GetPrintCapabilitiesAsXml()) xml.Load(ms);
        var ns = new System.Xml.XmlNamespaceManager(xml.NameTable);
        ns.AddNamespace("psf", PsfNs);
        var feature = xml.SelectSingleNode("//psf:Feature[@name='psk:PageMediaSize']", ns);
        if (feature == null) yield break;

        foreach (System.Xml.XmlElement o in feature.SelectNodes("psf:Option", ns)!)
        {
            string name = o.SelectSingleNode("psf:Property[@name='psk:DisplayName']/psf:Value", ns)?.InnerText.Trim() ?? "";
            if (name.Length == 0) name = o.GetAttribute("name").Split(':').Last();
            double? w = Microns(o.SelectSingleNode("psf:ScoredProperty[@name='psk:MediaSizeWidth']/psf:Value", ns)?.InnerText);
            double? h = Microns(o.SelectSingleNode("psf:ScoredProperty[@name='psk:MediaSizeHeight']/psf:Value", ns)?.InnerText);
            if (w is double wm && wm > 0) yield return new DriverMedia(name, wm, h, "driver");
        }
    }

    static double? Microns(string? s) =>
        double.TryParse(s?.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0
            ? Math.Round(v / 1000, 2)
            : null;

    // ---- Forms của Windows (winspool EnumForms) ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FORM_INFO_1
    {
        public int Flags;
        public IntPtr pName;
        public int Width, Height;                       // nghìn mm
        public int Left, Top, Right, Bottom;
    }

    const int FORM_BUILTIN = 1;

    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)]
    static extern bool ClosePrinter(IntPtr handle);
    [DllImport("winspool.drv", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool EnumForms(IntPtr handle, int level, IntPtr buffer, int size, out int needed, out int returned);

    /// <summary>Khổ do driver hoặc người dùng tạo (bỏ các khổ giấy chuẩn có sẵn của Windows như A4, Letter, phong bì…).</summary>
    static List<DriverMedia> FromForms(string printer)
    {
        var result = new List<DriverMedia>();
        if (!OpenPrinter(printer, out var h, IntPtr.Zero)) return result;
        try
        {
            EnumForms(h, 1, IntPtr.Zero, 0, out int needed, out _);
            if (needed <= 0) return result;
            IntPtr buf = Marshal.AllocHGlobal(needed);
            try
            {
                if (!EnumForms(h, 1, buf, needed, out _, out int count)) return result;
                int size = Marshal.SizeOf<FORM_INFO_1>();
                for (int i = 0; i < count; i++)
                {
                    var f = Marshal.PtrToStructure<FORM_INFO_1>(buf + i * size);
                    if ((f.Flags & FORM_BUILTIN) != 0 || f.Width <= 0 || f.Height <= 0) continue;
                    string name = Marshal.PtrToStringUni(f.pName) ?? "";
                    result.Add(new DriverMedia(name, Math.Round(f.Width / 1000.0, 2), Math.Round(f.Height / 1000.0, 2), "forms"));
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        finally { ClosePrinter(h); }
        return result;
    }
}
