# Decision log
Dòng: **ngày — quyết định — lý do — hệ quả**. Mới nhất ở trên.

- **2026-10-22 — In qua driver Windows (XPS `PrintQueue`) thay vì lệnh máy in riêng** — dùng được mọi máy in (Brother, TSC, Zebra, Xprinter, văn phòng) — phụ thuộc khổ giấy của driver, cần cảnh báo khi driver đổi khổ. *(suy ra từ code hiện có)*
- **2026-10-22 — Đọc Excel bằng ClosedXML, mã vạch bằng ZXing.Net** — không cần cài Excel / SDK Brother — thêm 2 gói NuGet. *(suy ra từ code hiện có)*
