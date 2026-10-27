<p align="center">
  <img src="docs/assets/readme/hero-vi.svg" width="100%" alt="Minh họa AF Media Bar: thanh media ở góc dưới bên trái thanh tác vụ Windows, bảng điều khiển đầy đủ phía trên, biểu tượng và giới thiệu dự án bên phải">
</p>

<p align="center">
  <a href="https://github.com/tienwopa/AF-Media-Bar/releases"><img src="https://img.shields.io/github/v/release/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=phi%C3%AAn%20b%E1%BA%A3n" alt="phiên bản"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/Fervent-Tempo/AF-Media-Bar?style=flat-square&amp;label=gi%E1%BA%A5y%20ph%C3%A9p" alt="giấy phép"></a>
  <a href="https://github.com/tienwopa/AF-Media-Bar/stargazers"><img src="https://img.shields.io/github/stars/tienwopa/AF-Media-Bar?style=flat-square&amp;label=Stars&amp;color=gold" alt="Stars"></a>
  <a href="https://github.com/tienwopa/AF-Media-Bar/forks"><img src="https://img.shields.io/github/forks/tienwopa/AF-Media-Bar?style=flat-square&amp;label=Forks" alt="Forks"></a>
  <br>
  <strong>Tiếng Việt</strong> · <a href="README.en-US.md">English</a> · <a href="README.zh-CN.md">简体中文</a>
  <br>
  <a href="#tải-về-và-khởi-chạy">Tải về</a> · <a href="#xem-trước-trực-tiếp">Xem trước</a> · <a href="https://github.com/tienwopa/AF-Media-Bar/issues">Báo cáo lỗi / Góp ý</a> · <a href="https://github.com/Fervent-Tempo/AF-Media-Bar">Dự án gốc (Upstream)</a>
</p>

---

## 🌟 Giới thiệu về Bản Fork này (`tienwopa/AF-Media-Bar`)

Bản fork này được phát triển và tối ưu hóa dựa trên dự án gốc mã nguồn mở [Fervent-Tempo/AF-Media-Bar](https://github.com/Fervent-Tempo/AF-Media-Bar). Mục tiêu chính là đem lại trải nghiệm nghe nhạc song ngữ hoàn hảo, mở rộng khả năng hiển thị lời bài hát độc lập trên nhiều màn hình, và tự động hóa phiên âm Pinyin mượt mà cho người dùng.

### ✨ Các tính năng nâng cao nổi bật được bổ sung:

1. **Dual Taskbar Overlay (2 thanh hiển thị lời bài hát độc lập):**
   - Hỗ trợ hiển thị đồng thời 2 Taskbar Overlay (Màn hình 1 và Màn hình 2, hoặc hai vị trí riêng biệt).
   - Cho phép tùy chỉnh độc lập từng thanh: Cỡ chữ (Nhỏ, Tiêu chuẩn, Lớn, Cực lớn), Phông chữ (Font family), Màu sắc tùy chọn, và Căn lề hiển thị (Trái, Giữa, Phải).

2. **Hiển thị Lời dịch Tiếng Việt / Song ngữ thời gian thực:**
   - Đồng bộ hiển thị song song lời gốc và lời dịch tiếng Việt (hoặc lời phụ) trên thanh Taskbar một cách nhịp nhàng theo tiến trình phát bài hát.
   - Hỗ trợ tùy chọn đưa lời dịch sang màn hình phụ riêng biệt để không chiếm diện tích màn hình chính.

3. **Tự động tạo Pinyin Offline:**
   - Tích hợp sẵn bộ máy chuyển đổi phiên âm offline siêu nhẹ (`TinyPinyin.Net`) cho toàn bộ bài hát tiếng Trung.
   - Không cần kết nối Internet, không gửi dữ liệu ra bên ngoài, tốc độ xử lý tức thì và tự động tách/ghép dòng Pinyin thông minh.

4. **Tự động thích ứng màu nền Taskbar (Auto Adapt Color):**
   - Tự động nhận biết trạng thái màu nền sáng/tối của thanh Taskbar Windows để chọn màu chữ tương phản tối ưu (chữ tối khi nền sáng và chữ sáng khi nền tối), tránh tình trạng chìm màu chữ.

5. **Tối ưu hóa độ trễ & Native Taskbar Docking:**
   - Tinh chỉnh cơ chế luồng hiển thị (Fast-track latency optimization), loại bỏ độ trễ khi chuyển câu hát và neo (dock) chắc chắn vào Taskbar Windows.

---

## Giới thiệu tổng quan

**AF Media Bar** là bộ điều khiển media hiện đại trên thanh tác vụ Windows 10/11. Ứng dụng đọc nội dung đang phát từ phiên media của hệ thống, giúp thông tin media, lời bài hát, điều khiển phát nhạc và chuyển thiết bị trở nên trực quan, dễ sử dụng hơn ngay trên desktop.

## Xem trước trực tiếp

<p align="center">
  <img src="docs/assets/readme/展示.gif" width="80%" alt="AF Media Bar chạy trên thanh tác vụ Windows với ảnh bìa, điều khiển phát nhạc và thông tin bài hát">
</p>

[Xem video giới thiệu trên Bilibili](https://www.bilibili.com/video/BV17yhh6aEgK)

## Tải về và khởi chạy

Truy cập [GitHub Releases](https://github.com/tienwopa/AF-Media-Bar/releases) (hoặc [Releases gốc](https://github.com/Fervent-Tempo/AF-Media-Bar/releases)) và chọn một gói:

1. **Bộ cài đặt (khuyên dùng):** Tải `AFMediaBar-Setup-vX.Y.Z-win-x64.exe` và làm theo hướng dẫn cài đặt.
2. **Bản portable:** Tải `AFMediaBar-vX.Y.Z-win-x64.zip`, giải nén vào thư mục có quyền ghi và chạy `AFMediaBar.exe`.

**Yêu cầu hệ thống:** Windows 10 1809 trở lên, x64, cùng Microsoft Edge WebView2 Runtime. Nếu thiếu, hãy cài Evergreen Runtime. Các gói phát hành đã tích hợp .NET runtime.

## Cập nhật và gỡ cài đặt

- **Cập nhật:** Ứng dụng thông báo khi có phiên bản mới; bạn có thể kiểm tra trong trang Ứng dụng. Bản cài đặt hỗ trợ tải và cài cập nhật trong ứng dụng; bản portable cần thay thế tệp thủ công.
- **Cài đặt:** Lưu tại `%LOCALAPPDATA%\AFMediaBar\settings.json`; có thể mở thư mục cài đặt từ trang Ứng dụng.
- **Gỡ cài đặt:** Gỡ bản cài đặt qua mục Ứng dụng đã cài đặt của Windows hoặc menu Start. Với bản portable, xóa thư mục chương trình. Để xóa cả cài đặt và bộ nhớ đệm, xóa thêm `%LOCALAPPDATA%\AFMediaBar`.

## Quyền riêng tư và bảo mật

- Không có telemetry, quảng cáo hay tài khoản. Điều khiển media và âm lượng được xử lý trên máy.
- Kiểm tra cập nhật và tìm lời bài hát trực tuyến sử dụng mạng. Tìm lời sử dụng tên bài hát và nghệ sĩ; có thể tắt các nguồn lời trong Cài đặt.
- Ứng dụng chạy với quyền của người dùng hiện tại. Báo cáo vấn đề bảo mật riêng tư theo [SECURITY.md](SECURITY.md).

## Lịch sử Star

<a href="https://www.star-history.com/?repos=Fervent-Tempo%2FAF-Media-Bar&amp;type=date&amp;legend=top-left">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=Fervent-Tempo/AF-Media-Bar&amp;type=date&amp;theme=dark&amp;legend=top-left">
    <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=Fervent-Tempo/AF-Media-Bar&amp;type=date&amp;legend=top-left">
    <img src="https://api.star-history.com/chart?repos=Fervent-Tempo/AF-Media-Bar&amp;type=date&amp;legend=top-left" width="100%" alt="Lịch sử số Star của AF Media Bar trên GitHub">
  </picture>
</a>

### Hoạt động dự án

![Thống kê hoạt động và đóng góp của kho AF Media Bar](https://repobeats.axiom.co/api/embed/5c84aada847e73d1f944fc22151a821d0fb2a9a9.svg "Repobeats analytics image")

## Cấu trúc dự án

Mã nguồn ứng dụng nằm trong `src/`, kiểm thử trong `tests/` và công cụ phát triển trong `tools/`. Xem [Tài liệu kiến trúc](docs/architecture.md#仓库目录) để biết cấu trúc thư mục và trách nhiệm, và [Hướng dẫn đóng góp](CONTRIBUTING.md#development-and-validation) để biết cách build và kiểm thử.

## Đóng góp

Đọc [Hướng dẫn đóng góp](CONTRIBUTING.md) trước khi đóng góp; xem [Tài liệu kiến trúc](docs/architecture.md) để biết ranh giới kiến trúc.

Báo cáo lỗi nên kèm phiên bản Windows, phiên bản ứng dụng, trình phát và các bước tái hiện. Xem [CHANGELOG.en-US.md](CHANGELOG.en-US.md) để biết lịch sử phiên bản.

## Bản địa hóa và dịch thuật

Giao diện hiện hỗ trợ **简体中文, 繁體中文, English và Tiếng Việt**. Chọn ngôn ngữ trong trang Ứng dụng. README có phiên bản tiếng Trung, tiếng Anh và tiếng Việt.

Chúng tôi hoan nghênh đóng góp bản dịch ở mọi ngôn ngữ, bao gồm ngôn ngữ mới và cải thiện bản dịch hiện có. Xem [Hướng dẫn đóng góp bản dịch](docs/localization.md#translation-guide). Dùng [Phản hồi bản dịch](https://github.com/Fervent-Tempo/AF-Media-Bar/issues/new?template=localization.yml) để báo cáo bản dịch sai, thiếu, chữ bị cắt hoặc đóng góp ngôn ngữ mới.

## Lời cảm ơn

### Người đóng góp

Cảm ơn tất cả các nhà phát triển đã đóng góp cho AF Media Bar.

[![Người đóng góp cho AF Media Bar](https://contrib.nn.ci/api?repo=Fervent-Tempo/AF-Media-Bar&cols=12&radius=24&no_bot=true)](https://github.com/Fervent-Tempo/AF-Media-Bar/graphs/contributors)

### Dự án mã nguồn mở

Cảm ơn các dự án mã nguồn mở sau:

- [FluentFlyout](https://github.com/unchihugo/FluentFlyout): Bảng điều khiển âm lượng Windows đơn giản và hiện đại.
- [Lyricify-Lyrics-Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper): Thư viện lời bài hát Lyricify để phân tích, tạo, tìm kiếm, giải mã và xử lý lời.
- [TaskbarLyrics](https://github.com/ANYNC/TaskbarLyrics): Công cụ lời bài hát trên thanh tác vụ Windows.
- [NAudio](https://github.com/naudio/NAudio): Thu và xử lý âm thanh Windows.
- [WPF UI](https://github.com/lepoco/wpfui): Điều khiển WPF theo phong cách Fluent.

## Tài trợ

<div align="center">

Nếu AF Media Bar hữu ích với bạn, hãy tặng dự án một Star ❤️

</div>

Ủng hộ tác giả gốc một tách cà phê:

<div align="center">

| WeChat | Alipay |
| :---: | :---: |
| <img src="src/AFMediaBar/Assets/Sponsor/wechat-pay.png" alt="Mã thanh toán WeChat" width="220"> | <img src="src/AFMediaBar/Assets/Sponsor/alipay-pay.png" alt="Mã thanh toán Alipay" width="220"> |

</div>
