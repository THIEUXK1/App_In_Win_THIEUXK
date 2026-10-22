# core.md — TemNhan (quy tắc riêng dự án; quy tắc chung ở ~/.claude/CLAUDE.md)

## Dự án
- App desktop WPF thiết kế + in tem nhãn, thay P-touch Editor; nhập mẫu `.lbx`, in hàng loạt từ Excel/CSV.
- Stack: .NET 10 `net10.0-windows`, WPF, C# Nullable+ImplicitUsings · ClosedXML (Excel) · ZXing.Net (mã vạch).
- Mã nguồn: `TemNhan/` (một csproj, không sln). Không DB, không server, không máy từ xa. Chưa có git.
- Thiết lập người dùng: `%AppData%\TemNhan\settings.json` (`AppSettings`).

## Phân bổ file
| Loại | Thư mục |
|---|---|
| Cửa sổ/hộp thoại (XAML + code-behind mỏng) | gốc `TemNhan/` (`*Window.xaml`) |
| Control tự vẽ, tương tác chuột | `Controls/` |
| Model nhãn, nhập `.lbx`, undo | `Model/` |
| Vẽ nhãn (dùng chung màn hình + in) | `Rendering/` |
| In qua driver Windows | `Printing/` |
| Đọc Excel/CSV | `Data/` |
- Code-behind chỉ nối UI ↔ Model/Printing/Data; không vẽ, không parse file, không gọi printer trực tiếp.
- Vẽ nhãn chỉ qua `LabelRenderer.Render` — màn hình và bản in phải ra cùng một hình.

## Cấm tuyệt đối
- Không thêm gói NuGet / đổi TargetFramework khi chưa hỏi.
- Không ghi đè/xoá `Layout*.lbx`, `*.exe`, `*.zip` ở gốc (mẫu thật + bộ cài Brother).
- Không chặn UI thread khi in/đọc file lớn quá ~1 s → `Task.Run` + `Dispatcher`.
- Không `catch {}` nuốt lỗi ngoài chỗ "không quan trọng" đã ghi chú (vd. lưu settings).

## Code style tóm tắt
- File-scoped namespace `TemNhan.<Thư mục>`; `static class` cho tiện ích không trạng thái; `sealed` mặc định.
- Đơn vị nội bộ = mm; đổi sang DIP chỉ bằng `LabelRenderer.DipPerMm`.
- Property model qua `Notifier.Set(ref …)` (INotifyPropertyChanged), làm tròn/clamp trong setter.
- Chuỗi UI + `<summary>` tiếng Việt. Chi tiết: `docs/coding-standards.md`.

## Build / chạy / hot reload (chạy ở `TemNhan/`)
- Build: `dotnet build -v q -clp:ErrorsOnly`
- Watch nền: `dotnet watch run --non-interactive > ../.claude/logs/watch.log 2>&1` → đọc `tail -n 30 ../.claude/logs/watch.log`.
- XAML hot reload chỉ có trong Visual Studio; `dotnet watch` chỉ restart app khi đổi `.cs`/`.xaml`.
- Build lỗi "file is locked" → app đang mở; tắt `TemNhan.exe` rồi build lại.

## Task → đọc doc nào
| Task | Mở |
|---|---|
| Bắt đầu task | `plans/00-context-memory.md` |
| Thêm file / quy trình thay đổi / khi gọi subagent | `docs/architecture-workflow.md` |
| Viết code mới, đặt tên, xử lý lỗi | `docs/coding-standards.md` |
| `.lbx`, in ấn, render, mã vạch, Excel, cạm bẫy | `docs/wpf-architecture.md` |
| Tính năng có nằm trong phạm vi? | `docs/project-scope.md` |
| Quyết định cũ / lộ trình | `plans/decision-log.md` · `plans/00-master-plan.md` |

## Subagent
- `system-architect`: đổi cấu trúc thư mục, thêm gói, đổi cách in/render.
- `code-reviewer`: trước khi chốt thay đổi lớn ở Printing/Rendering/LbxImporter.
- `database-auditor`: không dùng (không có DB).
