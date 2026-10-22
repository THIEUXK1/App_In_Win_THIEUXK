# Kiến trúc & quy trình

## Git flow
- Hiện chưa có git. Khi `git init`: nhánh `main` ổn định, nhánh `feat/<tên>` cho từng việc; commit nhỏ, thông điệp tiếng Việt.
- `.gitignore` khi init: `bin/ obj/ .vs/ *.user .claude/settings.local.json .claude/logs/ *.zip *.exe`.

## Tạo file ở đâu (đầy đủ)
| Thứ cần tạo | Vị trí | Ghi chú |
|---|---|---|
| Cửa sổ mới | `TemNhan/<Tên>Window.xaml` + `.xaml.cs` | style dùng chung đặt ở `App.xaml` (`Primary`, `Caption`, `Muted`) |
| Hộp thoại đơn giản dựng bằng code | `TemNhan/<Tên>Window.cs` (static class) | mẫu: `PickWindow` |
| Converter XAML | `App.xaml.cs` nếu nhỏ; nhiều thì `Converters/` | |
| Loại đối tượng nhãn mới | `Model/LabelModel.cs` (lớp con `LabelElement`) | + vẽ ở `Rendering/LabelRenderer.cs` + đọc ở `LbxImporter` nếu P-touch có |
| Đọc định dạng mẫu khác | `Model/<Định dạng>Importer.cs` | trả về `LabelDocument` |
| Nguồn dữ liệu mới | `Data/` | trả về `DataTable` / `Dictionary<string,string>` |
| Tuỳ chọn in | `Printing/PrintOptions` | lưu bền thì thêm vào `AppSettings` |
| Thiết lập nhớ giữa phiên | `AppSettings.cs` | |

## Quy trình mỗi thay đổi
1. Đọc `plans/00-context-memory.md`.
2. Grep vị trí → đọc đúng đoạn.
3. Sửa đúng tầng (bảng trên); model đổi → kiểm cả render, importer, undo (JSON snapshot).
4. `dotnet build -v q -clp:ErrorsOnly`.
5. Thử tay: mở `Layout.lbx`/`Layout2.lbx`, xem trước, in thử (máy in thật hoặc Microsoft Print to PDF).
6. Cập nhật `plans/00-context-memory.md`; quyết định quan trọng → `plans/decision-log.md`.

## Khi gọi subagent
- `system-architect`: thêm thư mục/tầng, thêm gói NuGet, đổi pipeline in (XPS) hoặc render.
- `code-reviewer`: sau thay đổi ở Printing/Rendering/LbxImporter/DataSource, hoặc trước bản phát hành.
