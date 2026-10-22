# Context memory (đọc đầu mỗi task)

## Đang làm
- Dựng lại UI: `MainWindow` hiện là khung trống mặc định; `PrintWindow.xaml` thiếu `.xaml.cs`.

## Blockers
- Chưa build được: `App.xaml.cs:33` gọi `MainWindow.OpenFile` chưa có; `PrintWindow` thiếu code-behind.

## 5 quyết định gần nhất
1. 2026-10-22 — Dựng khung `.claude/` (core.md + docs + plans); không có DB nên không tạo `docs/database-safety.md`.

## Ghi chú phiên sau
- Thử nhập bằng `Layout.lbx`, `Layout2.lbx` ở thư mục gốc.
