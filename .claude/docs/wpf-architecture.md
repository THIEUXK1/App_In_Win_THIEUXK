# Kiến trúc WPF — TemNhan

## Cây thư mục
```
1.phanmemintemBrotherP-touth/
├─ Layout.lbx, Layout2.lbx      mẫu P-touch thật để thử nhập
├─ pdp75w730cuk.exe, pew6_12_1.exe   bộ cài driver / P-touch Editor Brother (không đụng)
├─ phanmemintemBrotherP-touth.zip    bản lưu (không đụng)
└─ TemNhan/
   ├─ App.xaml(.cs)        style chung, xử lý lỗi toàn cục, EnumNameConverter, mở file từ args
   ├─ AppSettings.cs       %AppData%\TemNhan\settings.json
   ├─ MainWindow.xaml(.cs) cửa sổ chính
   ├─ PrintWindow.xaml     hộp thoại in (máy in, phạm vi, số bản, xoay, lệch)
   ├─ PickWindow.cs        hộp thoại chọn 1 mục (sheet/trang)
   ├─ Controls/LabelCanvas.cs   khung thiết kế: chọn/kéo/resize 8 handle
   ├─ Model/LabelModel.cs       LabelDocument + Text/Frame/Line/Barcode element
   ├─ Model/LbxImporter.cs      .lbx → LabelDocument
   ├─ Model/UndoManager.cs      undo bằng snapshot JSON, gộp thay đổi trong 2 s
   ├─ Rendering/LabelRenderer.cs  vẽ + trộn {{cột}} + mã vạch
   ├─ Printing/LabelPrinter.cs    in XPS qua driver Windows
   └─ Data/DataSource.cs          Excel/CSV → DataTable
```

## Phụ thuộc
- `ClosedXML` — đọc `.xlsx/.xlsm` không cần cài Excel.
- `ZXing.Net` — sinh Code128/Code39/EAN-13/QR/DataMatrix.
- Còn lại dùng BCL/WPF: `System.Printing`, `System.Windows.Xps`, `System.IO.Compression`, `System.Xml.Linq`, `System.Text.Json`.

## Quy ước
- Toạ độ/kích thước element: mm từ góc trên-trái nhãn; cỡ chữ: pt.
- 1 DIP = 1/96 inch; `LabelRenderer.DipPerMm` là hệ số duy nhất.
- `.lbx` = zip chứa `label.xml`, namespace `http://schemas.brother.info/ptouch/2007/lbx/*` (main, style, text, draw, barcode, database).
- Độ dài trong lbx có đơn vị (`13.6pt`, `5mm`, `1in`) → đọc qua helper trong `LbxImporter`.
- `database` trong lbx: `databasePath` + `mergeTable` dạng `'Sheet1$'` → bỏ `'` và `$`.

## Cạm bẫy đã cắn
- Driver máy in tem có thể tự đổi khổ giấy → `LabelPrinter.Print` trả cảnh báo; cách sửa là tạo khổ giấy đúng trong Printing Preferences.
- Máy kéo giấy theo chiều ngang nhãn → bật `Rotate` (xoay 90° cùng chiều kim đồng hồ).
- File Excel đang mở trong Excel → phải mở bằng FileShare.ReadWrite (`DataSource.OpenShared`).
- Object lbx chưa hỗ trợ bị bỏ qua và đếm ở `LbxSheet.SkippedObjects` — báo cho người dùng, đừng im lặng.
- Build lỗi khoá file khi `TemNhan.exe` đang chạy.
- (2026-10-22) `MainWindow` đang là khung trống, `App` gọi `window.OpenFile` chưa tồn tại; `PrintWindow.xaml` chưa có code-behind → dự án chưa build được.

## Lệnh hay dùng (ở `TemNhan/`)
- `dotnet build -v q -clp:ErrorsOnly`
- `dotnet run` · `dotnet run -- "..\Layout.lbx"` (mở file qua args)
- `dotnet watch run --non-interactive > ../.claude/logs/watch.log 2>&1`
- Phát hành: `dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -v q`

## Deploy
- Không có server/SSH. Phát hành = copy thư mục publish sang máy dùng; máy đích cần .NET 10 Desktop Runtime (hoặc publish `--self-contained true`).
