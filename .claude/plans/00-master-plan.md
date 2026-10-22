# Master plan

## Phase 1 — Lõi (đã có code)
- [x] Model nhãn + undo
- [x] Nhập `.lbx`
- [x] Render + mã vạch + trộn `{{cột}}`
- [x] In qua driver
- [x] Đọc Excel/CSV

## Phase 2 — UI (đang làm)
- [ ] MainWindow: canvas, danh sách đối tượng, bảng thuộc tính, mở file, chọn trang
- [ ] PrintWindow code-behind: máy in, phạm vi, số bản, xoay, lệch, lưu `AppSettings`
- [ ] Build sạch

## Phase 3 — Phát hành
- [ ] `dotnet publish` single-file, thử trên máy sạch

## Checklist kiểm thử tay
- [ ] Mở `Layout.lbx`, `Layout2.lbx` — đủ đối tượng, báo số object bỏ qua
- [ ] Kéo/resize/undo trên canvas
- [ ] Gắn Excel đang mở trong Excel — vẫn đọc được
- [ ] In thử Microsoft Print to PDF: đúng khổ, đúng vị trí
- [ ] In máy tem: bật/tắt xoay, bù lệch X/Y, nhiều bản
- [ ] Mã vạch dữ liệu sai → ô đỏ trên màn hình, không ra bản in
