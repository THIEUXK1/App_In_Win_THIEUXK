# Coding standards

## Style
- C# mới nhất của .NET 10: file-scoped namespace, primary constructor, collection expression, pattern matching (`is { … }`).
- `sealed` cho lớp không kế thừa; `static class` cho tiện ích (`LabelPrinter`, `DataSource`, `LbxImporter`).
- Nullable bật: không dùng `!` trừ khi đã kiểm ngay trước đó.
- Một lớp chính mỗi file; record nhỏ đi kèm được đặt cùng file (vd. `LbxSheet`, `LbxFile`).

## Naming
- PascalCase cho kiểu/member; field riêng `_camel`; hằng `PascalCase`.
- Đơn vị ghi trong tên: `…Mm`, `…Pt`, `…Dip` (`OffsetXmm`, `FontSizePt`, `StrokeMm`).
- Control XAML: `<Ý nghĩa><Loại>` (`PrinterBox`, `RangeAll`).

## Pattern trong repo
- Model: kế thừa `Notifier`, setter qua `Set(ref _f, value)`; làm tròn/clamp ngay trong setter.
- Undo: `UndoManager` chụp JSON toàn bộ `LabelDocument`; thuộc tính không lưu → `[JsonIgnore]`.
- Trường trộn: `{{Tên cột}}` trong text/data mã vạch, thay bằng `LabelRenderer.Merge`.
- Render dùng chung: `LabelRenderer.Render(dc, doc, RenderContext)` cho canvas và bản in.

## Lỗi & dữ liệu rỗng
- Lỗi người dùng cần thấy → ném `InvalidDataException`/`InvalidOperationException` với thông điệp tiếng Việt; `App` hiện MessageBox.
- Cảnh báo không chặn (vd. driver đổi khổ giấy) → trả `string?` và hiển thị, không ném.
- Ô Excel rỗng = `""`; cột không có trong dữ liệu → giữ nguyên `{{…}}` để người dùng thấy.
- Mã vạch không hợp lệ: màn hình vẽ ô đỏ, bản in không in ô đỏ.

## Frontend (WPF)
- Style dùng chung ở `App.xaml`; không hardcode màu trong từng cửa sổ.
- Ô số dùng `UpdateSourceTrigger=LostFocus`; Enter đã được bắt toàn cục để cập nhật.
- Hộp thoại: `Owner` = cửa sổ gọi, `WindowStartupLocation=CenterOwner`, `ShowInTaskbar=False`.

## Comment
- `<summary>` tiếng Việt cho API public và chỗ có quy ước (đơn vị, thứ tự handle).
- Comment cuối dòng chỉ khi giải thích "tại sao" (vd. hướng xoay 90°).
