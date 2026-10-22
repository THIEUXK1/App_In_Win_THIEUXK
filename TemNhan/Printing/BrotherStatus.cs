using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace TemNhan.Printing;

/// <summary>Trạng thái máy P-touch / QL đọc trực tiếp qua USB (lệnh ESC i S của Brother).</summary>
public sealed record BrotherMediaStatus(int TapeWidthMm, string MediaType, IReadOnlyList<string> Errors);

public static class BrotherStatus
{
    static readonly Guid UsbPrint = new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");

    [StructLayout(LayoutKind.Sequential)]
    struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr h, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, int i, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFile(string n, uint acc, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    /// <summary>Đọc trạng thái máy Brother đang cắm USB; null nếu không có máy Brother USB nào trả lời.</summary>
    public static BrotherMediaStatus? Query()
    {
        foreach (var path in BrotherDevices())
            if (QueryDevice(path) is { } s) return s;
        return null;
    }

    static List<string> BrotherDevices()
    {
        var result = new List<string>();
        var g = UsbPrint;
        IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        try
        {
            for (int i = 0; ; i++)
            {
                var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) break;
                SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out int size, IntPtr.Zero);
                IntPtr buf = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, size, out _, IntPtr.Zero))
                    {
                        string path = Marshal.PtrToStringUni(buf + 4)!;
                        if (path.Contains("vid_04f9", StringComparison.OrdinalIgnoreCase)) result.Add(path); // 04F9 = Brother
                    }
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return result;
    }

    static BrotherMediaStatus? QueryDevice(string path)
    {
        try
        {
            using var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) return null;
            using var fs = new FileStream(h, FileAccess.ReadWrite, 1, false);
            var init = new byte[102];
            init[100] = 0x1B; init[101] = 0x40;              // xoá lệnh dở + ESC @
            fs.Write(init);
            fs.Write([0x1B, 0x69, 0x53]);                    // ESC i S: hỏi trạng thái
            fs.Flush();

            var st = new byte[32];
            int got = 0;
            var until = DateTime.Now.AddSeconds(3);
            while (got < 32 && DateTime.Now < until)
            {
                int n = fs.Read(st, got, 32 - got);
                if (n <= 0) Thread.Sleep(100); else got += n;
            }
            if (got < 32 || st[0] != 0x80) return null;

            byte e1 = st[8], e2 = st[9];
            var errors = new List<string>();
            if ((e1 & 0x01) != 0) errors.Add("không có băng / chưa lắp cassette");
            if ((e1 & 0x02) != 0) errors.Add("hết băng");
            if ((e1 & 0x04) != 0) errors.Add("kẹt dao cắt");
            if ((e1 & 0x08) != 0) errors.Add("pin yếu");
            if ((e1 & 0x10) != 0) errors.Add("máy đang bận");
            if ((e1 & 0x40) != 0) errors.Add("sai adapter nguồn");
            if ((e2 & 0x01) != 0) errors.Add("băng không đúng loại");
            if ((e2 & 0x08) != 0) errors.Add("lỗi giao tiếp");
            if ((e2 & 0x10) != 0) errors.Add("nắp đang mở");
            if ((e2 & 0x20) != 0) errors.Add("máy quá nóng");

            string type = st[11] switch
            {
                0x01 => "băng ép lớp (TZe)",
                0x03 => "băng không ép lớp",
                0x11 => "băng co nhiệt",
                0x17 => "băng co nhiệt",
                0x0A or 0x0B => "giấy nhãn",
                0x00 => "",
                _ => $"loại 0x{st[11]:X2}",
            };
            return new BrotherMediaStatus(st[10], type, errors);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
