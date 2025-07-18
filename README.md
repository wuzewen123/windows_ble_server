# App1 - BLE 服务示例应用

这是一个基于 WinUI 的应用程序，实现了一个简单的 BLE (Bluetooth Low Energy) 服务，支持读、写和通知特性，并包含了图片发送和接收的功能。

## 项目结构

- `App.xaml` / `App.xaml.cs`: 应用程序的入口点和生命周期管理。
- `MainWindow.xaml` / `MainWindow.xaml.cs`: 主窗口的用户界面和 BLE 服务的主要逻辑。
- `Assets/`: 包含应用程序的资源文件，如图片。

## 功能特性

- **BLE 服务广播**: 应用程序启动后会广播一个 BLE 服务。
- **GATT 特性**: 包含一个支持读、写和通知的特性。
  - **读请求**: 客户端读取特性时，会返回 "Hello BLE Client!" 字符串。
  - **写请求**: 客户端写入特性时，应用程序会根据写入的数据进行不同的处理：
    - 如果写入 "SEND_IMAGE" (10字节)，应用程序会尝试从 `Assets/a89bd3023999cb6ea6209ad6e0e3f82.jpg` 发送图片数据到客户端。
    - 如果写入4字节数据，应用程序会将其解析为图片的总长度，并准备接收后续的图片分包。
    - 接收到所有图片分包后，会将图片保存为 `recv_YYYYMMDD_HHmmss.jpg` 文件。
- **通知**: 支持客户端订阅特性，以便接收通知。

## 如何运行

1.  **打开项目**: 使用 Visual Studio 2019 或更高版本打开 `App1.sln` 解决方案文件。
2.  **安装依赖**: 确保已安装 WinUI 3 (Windows App SDK) 开发环境。
3.  **构建项目**: 在 Visual Studio 中构建解决方案。
4.  **运行应用程序**: 运行 `App1` 项目。应用程序启动后，将自动初始化 BLE 服务并开始广播。

## BLE 服务信息

- **服务 UUID**: `12345678-1234-5678-1234-56789abcdef0`
- **特性 UUID**: `12345678-1234-5678-1234-56789abcdef1`

## 使用说明

1.  **启动应用**: 运行 `App1` 应用程序。
2.  **连接 BLE 设备**: 使用支持 BLE 的客户端设备（如手机上的 BLE 调试工具）扫描并连接到名为 `App1` 的设备。
3.  **发现服务和特性**: 发现上述服务 UUID 和特性 UUID。
4.  **读操作**: 对特性执行读操作，将收到 "Hello BLE Client!"。
5.  **写操作 - 发送图片**: 向特性写入 ASCII 字符串 "SEND_IMAGE" (确保是10字节)。应用程序将通过通知发送 `Assets/a89bd3023999cb6ea6209ad6e0e3f82.jpg` 图片。
6.  **写操作 - 接收图片**: 
    - 首先，客户端需要将图片的总长度（4字节，小端序）写入特性。
    - 接着，客户端将图片数据分包写入特性。应用程序将接收并拼接这些分包，最终保存为 JPG 文件。
7.  **通知订阅**: 订阅特性以接收来自应用程序的通知（例如，在发送图片时）。

## 注意事项

- 确保您的 Windows 设备支持 BLE，并且蓝牙功能已开启。
- 在某些情况下，您可能需要以管理员权限运行应用程序才能正常广播 BLE 服务。
- 图片发送和接收的 MTU (Maximum Transmission Unit) 默认为 180 字节，这可能会影响传输速度，可以根据实际情况调整。

统一服务ID：12345678-1234-5678-1234-56789abcdef0

## 1、查询蓝牙连接状态接口
服务 UUID
特征码 12345678-1234-5678-1234-56789abcdef1

## 2、查询足球相机设备信息
服务 UUID
特征码 6F8D0B2C-4E1A-3C5D-7B9E-0F2A4C6E8B1D

## 3、开始录制
服务 UUID
特征码 8B7F1A2C-3E4D-4B8A-9C1E-2F6D7A8B9COD

## 4、暂停录制
服务 UUID
特征码 1C2D3E4F-5A6B-7C8D-9EOF-1A2B3C4D5E6F

## 5、继续录制
服务 UUID
特征码 9A8B7C6D-5E4F-3A2B-1COD-9E8F7A6B5C4D

## 6、结束录制
服务 UUID
特征码 2F4E6D8C-1B3A-5C7E-9DOF-2A4C6E8B1D3F

## 7、拍摄视频上传成功（公网访问） 相机主动发送过来信息
服务 UUID
特征码 7E6D5C4B-3A2F-1E0D-9C8B-7A6F5E4D3C2B

## 8、查询足球相机内部已录制完成但未成功上传的视频列表
服务 UUID
特征码 0A1B2C3D-4E5F-6A7B-8C9D-0E1F2A3B4C5D

## 9、根据录制任务ID删除足球相机本地缓存的视频
服务 UUID
特征码 3C5E7A9B-1D2F-4B6D-8EOF-3A5C7E9B1D2F

1、点击开始录制后，识别出推流地址，随后将视频流实时推流到服务器，

{"sign":"testStartRecord","time":1752749993583,"event":"start_recording","data":{"title":"友谊赛","myteam":"11","opponentteam":"22","address":"广东省深圳市南山区滨海大道深圳湾体育中心W128","type":null,"status":"on","starttime":"2025-07-17T10:59:51Z","id":267,"push_auth_uri":"rtmp://oneshot.push.jkxuetang.com/live/267?%7B%22appname%22%3A+%22live%22%2C+%22streamname%22%3A+%22267%22%7D&auth_key=1752753592-0-0-b1ada850c16389524be31fbeaae62b2c","live_auth_uri":"{\"artc\":\"artc:\\/\\/oneshot.live.jkxuetang.com\\/live\\/267?auth_key=1752753592-0-0-0ae826cf6efe3146fdd391f36c905c9d\",\"rtmp\":\"rtmp:\\/\\/oneshot.live.jkxuetang.com\\/live\\/267?auth_key=1752753592-0-0-0ae826cf6efe3146fdd391f36c905c9d\",\"flv\":\"https:\\/\\/oneshot.live.jkxuetang.com\\/live\\/267\\/.flv?auth_key=1752753592-0-0-272804e50b19a0db5a4148ae8a9f438a\",\"hls\":\"https:\\/\\/oneshot.live.jkxuetang.com\\/live\\/267\\/.m3u8?auth_key=1752753592-0-0-48a7a8fbb0758e063a778c37c470323e\"}","alltype_live_auth_uri":{"artc":"artc://oneshot.live.jkxuetang.com/live/267?auth_key=1752753592-0-0-0ae826cf6efe3146fdd391f36c905c9d","rtmp":"rtmp://oneshot.live.jkxuetang.com/live/267?auth_key=1752753592-0-0-0ae826cf6efe3146fdd391f36c905c9d","flv":"https://oneshot.live.jkxuetang.com/live/267/.flv?auth_key=1752753592-0-0-272804e50b19a0db5a4148ae8a9f438a","hls":"https://oneshot.live.jkxuetang.com/live/267/.m3u8?auth_key=1752753592-0-0-48a7a8fbb0758e063a778c37c470323e"},"artc_push_auth_uri":"artc://oneshot.push.jkxuetang.com/live/267?%7B%22appname%22%3A+%22live%22%2C+%22streamname%22%3A+%22267%22%7D&auth_key=1752753592-0-0-b1ada850c16389524be31fbeaae62b2c","myteam_name":"11","opponentteam_name":"22"}}

测试
循环一个视频推流
ffmpeg -re -stream_loop -1 -i input.mp4 -c:v libx264 -preset veryfast -c:a aac -b:a 128k -f flv "rtmp://oneshot.push.jkxuetang.com/live/267?%7B%22appname%22%3A%22live%22%2C%22streamname%22%3A%22267%22%7D&auth_key=1752753592-0-0-b1ada850c16389524be31fbeaae62b2c"

拉流
ffmpeg -i "rtmp://oneshot.live.jkxuetang.com/live/267?auth_key=1752753592-0-0-0ae826cf6efe3146fdd391f36c905c9d" -c copy output.flv

