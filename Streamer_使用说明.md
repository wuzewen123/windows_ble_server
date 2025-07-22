# Streamer 文件推流类使用说明

## 1. 环境要求
- Windows、Linux 或 Android 系统。
- 已安装并配置好 FFmpeg，且 `ffmpeg` 命令可在命令行直接调用。
- .NET 6/7/8 及以上（建议使用 .NET 8）。

## 2. 类简介
`Streamer` 类用于循环推流本地视频文件到指定推流地址（如 RTMP），直到手动停止。

## 3. 主要方法
- `void Start(string videoPath, string pushUrl)`
  - 启动循环推流。
  - `videoPath`：本地视频文件路径。
  - `pushUrl`：推流地址（如阿里云/腾讯云/自建RTMP服务器）。
- `void Stop()`
  - 停止推流。

## 4. 基本用法示例
```csharp
// using windows_ble_server;

// class Program
// {
//     static void Main(string[] args)
//     {
//         // 实例化推流器
//         var streamer = new Streamer();
//         // 启动推流（请替换为你的视频文件路径和推流地址）
//         streamer.Start(@"C:\\your_video.mp4", "rtmp://your.push.url/app/streamkey");
        
//         Console.WriteLine("推流已启动，按任意键停止...");
//         Console.ReadKey();
//         // 停止推流
//         streamer.Stop();
//         Console.WriteLine("推流已停止。");
//     }
// }
```

## 5. 注意事项
- 请确保 FFmpeg 已正确安装并加入系统环境变量。
- 路径分隔符需根据操作系统调整（Windows为`\\`，Linux/Android为`/`）。
- 推流地址需为有效的 RTMP/RTSP 等流媒体服务器地址。
- 若需在 Linux/Android 下运行，需保证有权限执行 FFmpeg 命令。

## 6. 常见问题
- **推流失败/无输出**：请检查 FFmpeg 是否可用、推流地址是否正确、视频文件路径是否存在。
- **进程未结束**：调用 `Stop()` 方法即可终止推流。

如有更多定制需求或遇到问题，欢迎随时咨询！