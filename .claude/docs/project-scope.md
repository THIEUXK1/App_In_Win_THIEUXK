# Phạm vi dự án

## Định nghĩa
Phần mềm Windows nhỏ gọn để thiết kế, nhập mẫu P-touch `.lbx` và in tem nhãn (đơn lẻ hoặc hàng loạt từ Excel/CSV) qua bất kỳ driver máy in nào.

## Trong phạm vi
- Đối tượng: chữ, khung, đường kẻ, mã vạch (Code128, Code39, EAN-13, QR, DataMatrix).
- Nhập `.lbx` (nhiều trang, liên kết database), trộn `{{cột}}` từ Excel/CSV.
- Thiết kế bằng chuột, undo, xem trước, in nhiều bản, xoay, bù lệch.
- Nhớ thiết lập in giữa các phiên.

## Ngoài phạm vi
- Xuất ngược ra `.lbx`, giao tiếp trực tiếp lệnh máy in (ESC/P, ZPL, TSPL).
- Server, DB, đăng nhập, đồng bộ mạng.
- Ảnh/logo, đối tượng P-touch hiếm (lịch, đồng hồ, ký hiệu).

## Do / Don't
- Do: giữ in qua driver Windows để dùng mọi máy in.
- Do: báo rõ object lbx bị bỏ qua.
- Don't: phụ thuộc P-touch Editor hay b-PAC SDK.
- Don't: thêm gói nặng cho tính năng nhỏ.

## Vùng xám — phải hỏi
- Hỗ trợ ảnh/logo trong nhãn.
- In thẳng bằng lệnh ZPL/TSPL cho máy in tem cụ thể.
- Lưu mẫu riêng (định dạng file của TemNhan) thay vì chỉ nhập `.lbx`.
- Đóng gói installer.
