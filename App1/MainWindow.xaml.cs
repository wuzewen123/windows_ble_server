using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Dispatching;
using System;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text;
using System.Linq;


namespace App1
{
    public sealed partial class MainWindow : Window
    {
        private GattServiceProvider? serviceProvider;
        // private GattLocalCharacteristic? localCharacteristic; // 未使用，注释掉
        private List<GattSubscribedClient> notifyClients = new List<GattSubscribedClient>();
        private Dictionary<string, SortedDictionary<byte, byte[]>> blePacketCache = new Dictionary<string, SortedDictionary<byte, byte[]>>();
        private Dictionary<string, int> blePacketTotal = new Dictionary<string, int>();
        private Dictionary<string, int> blePacketDataLen = new Dictionary<string, int>();
        private object blePacketLock = new object();
        private List<GattLocalCharacteristic> localCharacteristics = new List<GattLocalCharacteristic>();
        private int maxPackageSize = 512; // 默认最大包大小
        private int currentId = 0; // 默认最大包大小
        private GattLocalCharacteristic? packageSizeCharacteristic; // 专门用于包大小协商的特征码
        
        // 文件传输状态管理
        private bool isFileTransferInProgress = false;
        private readonly object fileTransferLock = new object();
        private string currentTransferFile = string.Empty;
        
        // 录制计时器
        private RecordingTimer? recordingTimer;
        
        // 设备信息查询事件类型
        private int lastDeviceQueryEvent = 0; // 默认为获取设备信息
        
        // 全局设备状态
        private string device_status = "idle"; // idle, recording, paused
        
        // 获取当前传输状态
        public bool IsFileTransferInProgress
        {
            get
            {
                lock (fileTransferLock)
                {
                    return isFileTransferInProgress;
                }
            }
        }
        
        // 获取当前传输的文件名
        public string CurrentTransferFile
        {
            get
            {
                lock (fileTransferLock)
                {
                    return currentTransferFile;
                }
            }
        }

                    // List<GattLocalCharacteristic> localCharacteristics = new List<GattLocalCharacteristic>();
        // 发送按钮点击事件
        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var characteristic1 in localCharacteristics)
            {
                UpdateLogMessage($"特征码: {characteristic1.Uuid} 订阅客户端数: {characteristic1.SubscribedClients.Count}");
            }       

            string message = MessageTextBox.Text.Trim();
            if (string.IsNullOrEmpty(message))
            {
                UpdateLogMessage("无法发送消息：消息内容为空");
                return;
            }
            // 获取当前选中的特征码UUID
            string selectedUuid = (CharacteristicComboBox.SelectedItem as string) ?? "";

            if (string.IsNullOrEmpty(selectedUuid))
            {
                UpdateLogMessage("请选择特征码");
                return;
            }
            var characteristic = localCharacteristics.FirstOrDefault(c => c.Uuid.ToString() == selectedUuid);
            if (characteristic == null)
            {
                UpdateLogMessage($"未找到选中特征码: {selectedUuid}");
                return;
            }
            if (notifyClients.Count == 0)
            {
                UpdateLogMessage("无法发送消息：没有连接的客户端");
                return;
            }
            
            try
            {
                byte[] msgBytes = Encoding.UTF8.GetBytes(message);
                int dataTotalLen = msgBytes.Length;
                int partDataLen = 15;
                int total = (dataTotalLen + partDataLen - 1) / partDataLen;
                List<byte[]> packets = new List<byte[]>();
                for (int i = 0; i < total; i++)
                {
                    byte[] packet = new byte[20];
                    packet[0] = 0xAA;
                    packet[1] = (byte)total;
                    packet[2] = (byte)(i+1);
                    packet[3] = (byte)((dataTotalLen >> 8) & 0xFF);
                    packet[4] = (byte)(dataTotalLen & 0xFF);
                    int copyLen = Math.Min(partDataLen, dataTotalLen - i * partDataLen);
                    Array.Clear(packet, 5, 15);
                    Array.Copy(msgBytes, i * partDataLen, packet, 5, copyLen);
                    packets.Add(packet);
                }
                int successCount = 0;
                foreach (var client in notifyClients)
                {
                    try
                    {
                        for (int i = 0; i < packets.Count; i++)
                        {
                            var packet = packets[i];
                            var writer = new DataWriter();
                            writer.WriteBytes(packet);
                            var buffer = writer.DetachBuffer();
                            await characteristic.NotifyValueAsync(buffer, client);
                            UpdateLogMessage($"已发送分包 {i+1}/{packets.Count}，内容(HEX): {BitConverter.ToString(packet)}");
                            await Task.Delay(10);
                        }
                        successCount++;
                    }
                    catch (Exception clientEx)
                    {
                        Debug.WriteLine($"向客户端发送消息失败: {clientEx.Message}");
                    }
                }
                UpdateLogMessage($"发送消息: {message} (分包{total}包, 成功发送给 {successCount}/{notifyClients.Count} 个客户端)");
                MessageTextBox.Text = string.Empty;
            }
            catch (Exception ex)
            {
                UpdateLogMessage($"发送消息失败: {ex.Message}");
            }
        }
        
        // 更新日志消息
        private void UpdateLogMessage(string message)
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            
            // 确保UI更新在UI线程上执行
            if (DispatcherQueue.HasThreadAccess)
            {
                // 已在UI线程上，直接更新
                LogTextBlock.Text += $"[{timestamp}] {message}\n";
            }
            else
            {
                // 不在UI线程上，调度到UI线程
                DispatcherQueue.TryEnqueue(() => {
                    try
                    {
                        LogTextBlock.Text += $"[{timestamp}] {message}\n";
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[UI更新错误] UpdateLogMessage失败: {ex.Message}");
                    }
                });
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            InitBleServer();
            
            // 初始化录制计时器
            InitializeRecordingTimer();
            
            // 初始化UI状态
            UpdateLogMessage("应用程序已启动");
        }
        
        private void InitializeRecordingTimer()
        {
            recordingTimer = new RecordingTimer(DispatcherQueue);
            
            // 订阅计时器事件
            recordingTimer.TimerTick += (duration) => {
                UpdateLogMessage($"[录制计时] 当前录制时长: {recordingTimer.GetFormattedDuration()}");
            };
            
            recordingTimer.RecordingStarted += (startTime) => {
                device_status = "recording";
                UpdateLogMessage($"[录制计时] 录制开始于: {startTime:yyyy-MM-dd HH:mm:ss}");
                UpdateLogMessage($"[设备状态] 状态更新为: {device_status}");
            };
            
            recordingTimer.RecordingStopped += (endTime, totalDuration) => {
                device_status = "idle";
                UpdateLogMessage($"[录制计时] 录制结束于: {endTime:yyyy-MM-dd HH:mm:ss}");
                UpdateLogMessage($"[录制计时] 总录制时长: {recordingTimer.GetFormattedDuration()}");
                UpdateLogMessage($"[设备状态] 状态更新为: {device_status}");
            };
            
            recordingTimer.RecordingPaused += (pauseTime) => {
                device_status = "paused";
                UpdateLogMessage($"[录制计时] 录制暂停于: {pauseTime:yyyy-MM-dd HH:mm:ss}");
                UpdateLogMessage($"[设备状态] 状态更新为: {device_status}");
            };
            
            recordingTimer.RecordingResumed += (resumeTime) => {
                device_status = "recording";
                UpdateLogMessage($"[录制计时] 录制恢复于: {resumeTime:yyyy-MM-dd HH:mm:ss}");
                UpdateLogMessage($"[设备状态] 状态更新为: {device_status}");
            };
        }

        private async void InitBleServer()
        {
            Guid serviceUuid = Guid.Parse("12345678-1234-5678-1234-56789abcdef0");
            var result = await GattServiceProvider.CreateAsync(serviceUuid);
            if (result.Error != BluetoothError.Success)
                return;
            serviceProvider = result.ServiceProvider;

            // 1. 定义11个特征码UUID（包含新的包大小协商特征码）
            Guid[] characteristicUuids = new Guid[] {
                Guid.Parse("12345678-1234-5678-1234-56789abcdef1"), // 查询蓝牙连接状态接口
                Guid.Parse("6F8D0B2C-4E1A-3C5D-7B9E-0F2A4C6E8B1D"), // 查看足球相机设备信息\录制总时长\设备状态
                Guid.Parse("8B7F1A2C-3E4D-4B8A-9C1E-2F6D7A8B9C0D"), // 开始录制
                Guid.Parse("1C2D3E4F-5A6B-7C8D-9E0F-1A2B3C4D5E6F"), // 暂停录制
                Guid.Parse("9A8B7C6D-5E4F-3A2B-1C0D-9E8F7A6B5C4D"), // 继续录制
                Guid.Parse("2F4E6D8C-1B3A-5C7E-9D0F-2A4C6E8B1D3F"), // 结束录制
                Guid.Parse("7E6D5C4B-3A2F-1E0D-9C8B-7A6F5E4D3C2B"), // 拍摄视频上传成功(公网访问)相机主动发送过来信息
                Guid.Parse("0A1B2C3D-4E5F-6A7B-8C9D-0E1F2A3B4C5D"), // 查询足球相机内部已录制完成但未成功上传的视频列表
                Guid.Parse("3C5E7A9B-1D2F-4B6D-8E0F-3A5C7E9B1D2F"), // 根据录制任务id删除足球相机本地缓存的视频
                Guid.Parse("4B5D6E7F-8A9B-1C2D-3E4F-5A6B7C8D9E0F"), // send file
                Guid.Parse("5D6F7A8B-9C0D-1E2F-3A4B-5C6D7E8F9A0B") // 新增的包大小协商特征码
            };

            // 假设 characteristicUuids 是你的特征码UUID数组
            CharacteristicComboBox.ItemsSource = characteristicUuids.Select(u => u.ToString()).ToList();
            CharacteristicComboBox.SelectedIndex = 0;    
            // 2. 在 InitBleServer 方法中循环注册
            foreach(var uuid in characteristicUuids)
            {
                var parameters = new GattLocalCharacteristicParameters
                {
                    CharacteristicProperties = GattCharacteristicProperties.Read | GattCharacteristicProperties.Write | GattCharacteristicProperties.Notify,
                    WriteProtectionLevel = GattProtectionLevel.Plain,
                    ReadProtectionLevel = GattProtectionLevel.Plain
                };
                var characteristicResult = await serviceProvider.Service.CreateCharacteristicAsync(uuid, parameters);
                if (characteristicResult.Error != BluetoothError.Success)
                {
                    // 错误处理
                    continue;
                }
                var characteristic = characteristicResult.Characteristic;
                characteristic.ReadRequested += LocalCharacteristic_ReadRequested;
                characteristic.WriteRequested += LocalCharacteristic_WriteRequested;
                characteristic.SubscribedClientsChanged += LocalCharacteristic_SubscribedClientsChanged;
                
                // 如果是包大小协商特征码，保存引用并设置特殊的订阅处理
                if (uuid.ToString().ToLower() == "5d6f7a8b-9c0d-1e2f-3a4b-5c6d7e8f9a0b")
                {
                    packageSizeCharacteristic = characteristic;
                    characteristic.SubscribedClientsChanged += PackageSizeCharacteristic_SubscribedClientsChanged;
                }
                
                localCharacteristics.Add(characteristic);
            }
           
            serviceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters
            {
                IsDiscoverable = true,
                IsConnectable = true
            });
            Debug.WriteLine("BLE服务已启动，等待连接...");
            
            // 更新UI状态
            DispatcherQueue.TryEnqueue(() => {
                BleStatusTextBlock.Text = "BLE 服务已启动并正在广播...";
                UpdateLogMessage("BLE服务已启动，等待连接...");
            });
        }

        private void LocalCharacteristic_SubscribedClientsChanged(GattLocalCharacteristic sender, object args)
        {
            notifyClients.Clear();
            foreach (var client in sender.SubscribedClients)
            {
                notifyClients.Add(client);
            }
            Debug.WriteLine($"Notify订阅客户端数: {notifyClients.Count}");
            
            // 更新UI状态
            DispatcherQueue.TryEnqueue(() => {
                BleStatusTextBlock.Text = $"BLE 服务已启动，当前订阅客户端数: {notifyClients.Count}";
                UpdateLogMessage($"订阅客户端数量变化: {notifyClients.Count}");
            });
        }
        
        // 包大小协商特征码的订阅处理
        private async void PackageSizeCharacteristic_SubscribedClientsChanged(GattLocalCharacteristic sender, object args)
        {
            Debug.WriteLine($"包大小协商特征码订阅客户端数: {sender.SubscribedClients.Count}");
            
            // 当有客户端订阅时，主动发送512字节的0xAA数据
            if (sender.SubscribedClients.Count > 0)
            {
                try
                {
                    byte[] initPacket = new byte[512];
                    for (int i = 0; i < 512; i++)
                    {
                        initPacket[i] = 0xAA;
                    }
                    
                    var writer = new DataWriter();
                    writer.WriteBytes(initPacket);
                    var buffer = writer.DetachBuffer();
                    
                    foreach (var client in sender.SubscribedClients)
                    {
                        await sender.NotifyValueAsync(buffer, client);
                    }
                    
                    Debug.WriteLine("已向客户端发送512字节初始化数据包");
                    DispatcherQueue.TryEnqueue(() => {
                        UpdateLogMessage("包大小协商特征码：已发送512字节初始化数据包(全0xAA)");
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"发送初始化数据包失败: {ex.Message}");
                    DispatcherQueue.TryEnqueue(() => {
                        UpdateLogMessage($"发送初始化数据包失败: {ex.Message}");
                    });
                }
            }
        }
        // private List<byte[]> packets = null; // 未使用，注释掉
        private List<byte[]> BuildPackets(string data)
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            int totalLen = bytes.Length;
            int payloadLen = 15;
            int totalPackets = (totalLen + payloadLen - 1) / payloadLen;
            var result = new List<byte[]>();
            // Debug.WriteLine($"package_size:{totalPackets}");
            for (int i = 0; i < totalPackets; i++)
            {
                int chunkSize = Math.Min(payloadLen, totalLen - i * payloadLen);
                var packet = new byte[20];
                packet[0] = 0xAA;
                packet[1] = (byte)totalPackets;
                packet[2] = (byte)(i + 1);
                packet[3] = (byte)((totalLen >> 8) & 0xFF);
                packet[4] = (byte)(totalLen & 0xFF);
                Array.Copy(bytes, i * payloadLen, packet, 5, chunkSize);
                result.Add(packet);
            }
            return result;
        }
        private Dictionary<string, (List<byte[]> packets, int index)> packetStates = new Dictionary<string, (List<byte[]>, int)>();

        private async void LocalCharacteristic_ReadRequested(GattLocalCharacteristic sender, GattReadRequestedEventArgs args)
        {
            Debug.WriteLine($"收到请求，特征码: {sender.Uuid}");
            var deferral = args.GetDeferral();
            try
            {
                var request = await args.GetRequestAsync();
            if (request == null) return;
                if (request != null)
                {
                    string uuid = sender.Uuid.ToString().ToLower();
                    string json = "";
                    switch (uuid)
                    {
                        case "12345678-1234-5678-1234-56789abcdef1":
                            json = $"{{\"errorcode\":0,\"msg\":\"success\",\"data\":{{\"device_status\":\"wait\",\"server_status\":\"connect\",\"characteristic_id\":\"{uuid}\"}}}}";
                            break;
                        case "6f8d0b2c-4e1a-3c5d-7b9e-0f2a4c6e8b1d":
                            if(lastDeviceQueryEvent == 0)
                            {
                                // 默认返回蓝牙设备信息
                                string deviceId = await GetBluetoothDeviceId();
                                string deviceName = await GetBluetoothDeviceName();
                                UpdateLogMessage($"获取蓝牙设备信息: device_id={deviceId}, device_name={deviceName}");
                                json = $"{{\"errorcode\":0,\"msg\":\"success\",\"data\":{{\"device_id\":\"{deviceId}\",\"device_name\":\"{deviceName}\"}}}}"; 
                            }
                            else if (lastDeviceQueryEvent == 1)
                            {
                                // 返回录制时长信息
                                bool isRecording = recordingTimer?.IsRecording ?? false;
                                string formattedDuration = isRecording ? (recordingTimer?.GetFormattedDuration() ?? "00:00:00") : "00:00:00";
                                UpdateLogMessage($"获取录制时长信息: 时长={formattedDuration}");
                                json = $"{{\"errorcode\":0,\"msg\":\"success\",\"data\":\"{formattedDuration}\"}}";
                            }
                            else if(lastDeviceQueryEvent == 2)
                            {
                                UpdateLogMessage($"获取设备状态: {device_status}");
                                json = $"{{\"errorcode\":0,\"msg\":\"success\",\"data\":{{\"characteristic_id\":\"{uuid}\",\"device_status\":\"{device_status}\"}}"; 

                            }
                            break;
                        case "8b7f1a2c-3e4d-4b8a-9c1e-2f6d7a8b9c0d":
                        case "1c2d3e4f-5a6b-7c8d-9e0f-1a2b3c4d5e6f":
                        case "9a8b7c6d-5e4f-3a2b-1c0d-9e8f7a6b5c4d":
                        case "2f4e6d8c-1b3a-5c7e-9d0f-2a4c6e8b1d3f":
                        case "7e6d5c4b-3a2f-1e0d-9c8b-7a6f5e4d3c2b":
                        case "3c5e7a9b-1d2f-4b6d-8e0f-3a5c7e9b1d2f":
                            json = $"{{\"errorcode\":0,\"msg\":\"success\",\"data\":{{\"characteristic_id\":\"{uuid}\",\"id\":\"{currentId}\"}}}}";
                            break;
                        case "0a1b2c3d-4e5f-6a7b-8c9d-0e1f2a3b4c5d":
                            json = $"{{\"errorcode\":0,\"msg\":\"success\",\"data\":{{\"uploadfailed_videoes\":[{{\"id\":\"1\",\"push_auth_uri\":\"xxxx\",\"title\":\"录制标题\",\"myteam\":\"我的队伍名称\",\"opponentteam\":\"队伍名称\",\"address\":\"比赛场所\",\"type\":\"11V11\",\"created_at\":\"2025-05-26 13:56:06\",\"thumb_image\":\"xxx\",\"second\":\"120\",\"finished_time\":\"2025-05-26 13:56:06\"}}],\"characteristic_id\":\"{uuid}\"}}}}";
                            break;
                        case "5d6f7a8b-9c0d-1e2f-3a4b-5c6d7e8f9a0b":
                            json = $"{{\"errorcode\":0,\"msg\":\"success\",\"data\":{{\"max_package_size\":{maxPackageSize},\"characteristic_id\":\"{uuid}\"}}}}";
                            break;
                    }
                    if (string.IsNullOrEmpty(json))
                    {
                        json = "{\"errorcode\":1,\"msg\":\"uuid not supported\"}";
                    }
                    // 分包状态管理
                    if (!packetStates.ContainsKey(uuid) || packetStates[uuid].packets == null || packetStates[uuid].index >= packetStates[uuid].packets.Count)
                    {
                        var packets = BuildPackets(json);
                        packetStates[uuid] = (packets, 0);
                    }
                    var state = packetStates[uuid];
                    // 索引越界保护
                    if (state.index < 0 || state.packets == null || state.index >= state.packets.Count)
                    {
                        UpdateLogMessage($"[错误] 分包索引越界：uuid={uuid}, index={state.index}, packets.Count={(state.packets == null ? 0 : state.packets.Count)}");
                        request.RespondWithProtocolError(GattProtocolError.UnlikelyError);
                        packetStates.Remove(uuid);
                        return;
                    }
                    // 再次保护：防止 state.packets[state.index] 越界
                    byte[] currentPacket = null;
                    if (state.packets != null && state.index >= 0 && state.index < state.packets.Count)
                    {
                        currentPacket = state.packets[state.index];
                    }
                    else
                    {
                        UpdateLogMessage($"[严重错误] 读取分包时索引越界，uuid={uuid}, index={state.index}, packets.Count={(state.packets == null ? 0 : state.packets.Count)}");
                        request.RespondWithProtocolError(GattProtocolError.UnlikelyError);
                        packetStates.Remove(uuid);
                        return;
                    }
                    var writer = new DataWriter();
                    writer.WriteBytes(currentPacket);
                    DispatcherQueue.TryEnqueue(() => {
                        ReceivedValueTextBlock.Text = BitConverter.ToString(currentPacket);
                        UpdateLogMessage($"客户端读取请求，返回json分包[{state.index+1}/{state.packets.Count}]: {BitConverter.ToString(currentPacket)}");
                    });
                    request.RespondWithValue(writer.DetachBuffer());
                    // state = (state.packets, state.index + 1);
                    int nextIndex = state.index + 1;

                    if (nextIndex >= state.packets.Count)
                    {
                        packetStates.Remove(uuid);
                    }
                    else
                    {
                        packetStates[uuid] = (state.packets, nextIndex);
                    }
                }
            }
            finally
            {
                deferral.Complete();
            }
        }

        // 3. 在 WriteRequested/ReadRequested 事件中通过 sender.Uuid 区分（如需特殊处理）
        private async void LocalCharacteristic_WriteRequested(GattLocalCharacteristic sender, GattWriteRequestedEventArgs args)
        {
            Debug.WriteLine($"收到写入，特征码: {sender.Uuid}");
            
            // 特殊处理包大小协商特征码
            if (sender.Uuid.ToString().ToLower() == "5d6f7a8b-9c0d-1e2f-3a4b-5c6d7e8f9a0b")
            {
                await HandlePackageSizeNegotiation(args);
                return;
            }
            
            // 下面逻辑可直接复用你现有的分包、组包、日志等功能
            var deferral = args.GetDeferral();
            bool isPreviewCommand = false;
            int preview_id = 0;
            try
            {
                var request = await args.GetRequestAsync();
                if (request != null)
                {
                    var reader = DataReader.FromBuffer(request.Value);
                    uint dataLen = reader.UnconsumedBufferLength;
                    Debug.WriteLine($"[WriteRequested] 收到写入, dataLen={dataLen}");
                    if (dataLen == 20)
                    {
                        byte[] packet = new byte[20];
                        reader.ReadBytes(packet);
                        Debug.WriteLine($"[BLE包原始数据] {BitConverter.ToString(packet)}");
                        if (packet[0] == 0xAA)
                        {
                            byte total = packet[1];
                            byte index = packet[2];
                            int dataTotalLen = (packet[3] << 8) | packet[4];
                            byte[] dataPart = new byte[15];
                            Array.Copy(packet, 5, dataPart, 0, 15);
                            DispatcherQueue.TryEnqueue(() => {
                                UpdateLogMessage($"特征id:{sender.Uuid},收到分包原始UTF-8字节 序号: {index}, 内容(HEX): {BitConverter.ToString(dataPart)}");
                            });
                            string msgKey = $"{total}-{dataTotalLen}";
                            lock (blePacketLock)
                            {
                                if (!blePacketCache.ContainsKey(msgKey))
                                {
                                    blePacketCache[msgKey] = new SortedDictionary<byte, byte[]>();
                                    blePacketTotal[msgKey] = total;
                                    blePacketDataLen[msgKey] = dataTotalLen;
                                }
                                var partDict = blePacketCache[msgKey] as SortedDictionary<byte, byte[]>;
                                partDict[index] = dataPart;
                                // 简化：直接拼接所有已收到分包
                                var allData = partDict.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value).ToList();
                                // 若已收到全部分包，按总长度截断
                                if (partDict.Count == total)
                                    allData = allData.Take(dataTotalLen).ToList();
                                string dataStr;
                                try
                                {
                                    dataStr = Encoding.UTF8.GetString(allData.ToArray());
                                    Debug.WriteLine($"[实时组包] 当前已拼接字符串: {dataStr}");
                                    
                                    // 检查是否为预览指令（分包组装完成后）
                                    if (partDict.Count == total) // 只有在分包完成时才检查预览指令
                                    {
                                        // 尝试解析录制事件
                                        if (RecordingEventData.TryParse(dataStr, out var recordingEvent) && recordingEvent != null)
                                        {
                                            // 检查此特征码是获取信息或获取时间或者设备状态
                                            if (sender.Uuid.ToString().ToLower() == "6f8d0b2c-4e1a-3c5d-7b9e-0f2a4c6e8b1d")
                                            {
                                                if(recordingEvent.Event == "get_device")
                                                {
                                                    lastDeviceQueryEvent = 0;
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        UpdateLogMessage($"[设备查询] 收到获取设备信息请求，已设置查询类型为设备信息");
                                                    });
                                                }
                                                else if(recordingEvent.Event == "get_record_time")
                                                {
                                                    lastDeviceQueryEvent = 1;
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        UpdateLogMessage($"[时间查询] 收到获取录制时间请求，已设置查询类型为录制时间");
                                                    });
                                                }else if(recordingEvent.Event == "check_device_status")
                                                {
                                                    lastDeviceQueryEvent = 2;
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        UpdateLogMessage($"[时间查询] 收到获取录制时间请求，已设置查询类型为录制时间");
                                                    });
                                                }
                                            }
                                            switch (recordingEvent.Event)
                                            {
                                                case "start_recording":
                                                    if (recordingEvent.Data != null)
                                                    {
                                                        currentId = recordingEvent.Data.Id;
                                                        DispatcherQueue.TryEnqueue(() => {
                                                            UpdateLogMessage($"[开始录制] 解析成功 - 标题: {recordingEvent.Data.Title}, 我方队伍: {recordingEvent.Data.MyTeamName}, 对方队伍: {recordingEvent.Data.OpponentTeamName}");
                                                            UpdateLogMessage($"[开始录制] 推流地址: {recordingEvent.Data.PushAuthUri}");
                                                            UpdateLogMessage($"[开始录制] 拉流地址 rtmp: {recordingEvent.Data.AllTypeLiveAuthUri?.Rtmp}");
                                                            UpdateLogMessage($"[开始录制] 拉流地址 Artc: {recordingEvent.Data.AllTypeLiveAuthUri?.Artc}");
                                                            UpdateLogMessage($"[开始录制] 拉流地址 Flv: {recordingEvent.Data.AllTypeLiveAuthUri?.Flv}");
                                                            UpdateLogMessage($"[开始录制] 拉流地址 Hls: {recordingEvent.Data.AllTypeLiveAuthUri?.Hls}");
                                                            UpdateLogMessage($"[开始录制] 录制ID: {recordingEvent.Data.Id}, 开始时间: {recordingEvent.Data.StartTime}");
                                                            
                                                            // 启动录制计时器
                                                            try
                                                            {
                                                                recordingTimer?.StartRecording();
                                                                UpdateLogMessage($"[录制计时] 计时器已启动");
                                                            }
                                                            catch (Exception ex)
                                                            {
                                                                UpdateLogMessage($"[录制计时] 启动计时器失败: {ex.Message}");
                                                            }
                                                        });
                                                    }
                                                    break;
                                                    
                                                case "stop_recording":
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        UpdateLogMessage($"[停止录制] 收到停止录制事件");
                                                        
                                                        // 停止录制计时器
                                                        try
                                                        {
                                                            if (recordingTimer?.IsRecording == true)
                                                            {
                                                                var totalDuration = recordingTimer.StopRecording();
                                                                UpdateLogMessage($"[录制计时] 计时器已停止，总录制时长: {recordingTimer.GetFormattedDuration()}");
                                                            }
                                                            else
                                                            {
                                                                UpdateLogMessage($"[录制计时] 当前没有进行录制，无需停止计时器");
                                                            }
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            UpdateLogMessage($"[录制计时] 停止计时器失败: {ex.Message}");
                                                        }
                                                    });
                                                    break;
                                                    
                                                case "pause_recording":
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        UpdateLogMessage($"[暂停录制] 收到暂停录制事件");
                                                        
                                                        // 暂停录制计时器
                                                        try
                                                        {
                                                            if (recordingTimer?.IsRecording == true)
                                                            {
                                                                recordingTimer.PauseRecording();
                                                                UpdateLogMessage($"[录制计时] 计时器已暂停，当前录制时长: {recordingTimer.GetFormattedDuration()}");
                                                            }
                                                            else
                                                            {
                                                                UpdateLogMessage($"[录制计时] 当前没有进行录制，无法暂停计时器");
                                                            }
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            UpdateLogMessage($"[录制计时] 暂停计时器失败: {ex.Message}");
                                                        }
                                                    });
                                                    break;
                                                    
                                                case "continue_recording":
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        UpdateLogMessage($"[恢复录制] 收到恢复录制事件");
                                                        
                                                        // 恢复录制计时器
                                                        try
                                                        {
                                                            if (recordingTimer?.IsPaused == true)
                                                            {
                                                                recordingTimer.ResumeRecording();
                                                                UpdateLogMessage($"[录制计时] 计时器已恢复，当前录制时长: {recordingTimer.GetFormattedDuration()}");
                                                            }
                                                            else
                                                            {
                                                                UpdateLogMessage($"[录制计时] 当前没有暂停的录制，无法恢复计时器");
                                                            }
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            UpdateLogMessage($"[录制计时] 恢复计时器失败: {ex.Message}");
                                                        }
                                                    });
                                                    break;
                                                case "preview_recording":
                                                    isPreviewCommand = true;
                                                    preview_id = recordingEvent.Data?.Id ?? 0;
                                                    UpdateLogMessage($"[预览] id: {recordingEvent.Data?.Id ?? 0}");
                                                    break;
                                                default:
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        UpdateLogMessage($"[未知事件] 收到未知录制事件: {recordingEvent.Event}");
                                                    });
                                                    break;
                                            }
                                        }
                                        else
                                        {
                                            DispatcherQueue.TryEnqueue(() => {
                                                UpdateLogMessage($"JSON解析失败: {dataStr}");
                                            });
                                        }
        
                                        // ----- send video to client -----
                                        if (isPreviewCommand)
                                        {
                                            // 检查是否已有文件传输在进行，防止重复发送
                                            bool shouldProcessPreview = false;
                                            lock (fileTransferLock)
                                            {
                                                if (isFileTransferInProgress)
                                                {
                                                    Debug.WriteLine($"[预览指令] 文件传输被拒绝：已有传输在进行中，当前传输文件: {currentTransferFile}");
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        UpdateLogMessage($"预览指令被忽略：已有文件传输在进行中，当前传输文件: {System.IO.Path.GetFileName(currentTransferFile)}");
                                                    });
                                                    shouldProcessPreview = false;
                                                }
                                                else
                                                {
                                                    shouldProcessPreview = true;
                                                }
                                            }
                                            
                                            if (shouldProcessPreview)
                                            {
                                                Debug.WriteLine($"[预览指令] 收到预览请求: {dataStr}");
                                                DispatcherQueue.TryEnqueue(() => {
                                                    UpdateLogMessage($"收到预览指令: {dataStr}" + (!string.IsNullOrEmpty(preview_id.ToString()) ? $",预览ID: {preview_id}" : "") + "，开始发送文件");
                                                });
                                                
                                                // 异步发送文件，避免阻塞
                                                _ = Task.Run(async () => {
                                                try
                                                {
                                                    string filePath = "";
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        filePath = FilePathTextBox.Text;
                                                    });
                                                    
                                                    // 等待UI更新完成
                                                    await Task.Delay(100);
                                                    
                                                    if (!string.IsNullOrEmpty(filePath))
                                                    {
                                                        await SendFileToClient(filePath);
                                                    }
                                                    else
                                                    {
                                                        DispatcherQueue.TryEnqueue(() => {
                                                            UpdateLogMessage("[预览错误] 文件路径为空，无法发送文件");
                                                        });
                                                    }
                                                }
                                                catch (System.Runtime.InteropServices.COMException comEx)
                                                {
                                                    Debug.WriteLine($"[预览错误] 蓝牙COMException: {comEx.Message}, HResult: 0x{comEx.HResult:X8}");
                                                    Debug.WriteLine($"[预览错误] 异常详情: {comEx}");
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        UpdateLogMessage($"[预览错误] 蓝牙发送失败: {comEx.Message} (错误代码: 0x{comEx.HResult:X8})");
                                                    });
                                                }
                                                catch (Exception ex)
                                                {
                                                    Debug.WriteLine($"[预览错误] 发送文件失败: {ex.Message}");
                                                    Debug.WriteLine($"[预览错误] 异常类型: {ex.GetType().Name}");
                                                    Debug.WriteLine($"[预览错误] 堆栈跟踪: {ex.StackTrace}");
                                                    DispatcherQueue.TryEnqueue(() => {
                                                        UpdateLogMessage($"[预览错误] 发送文件失败: {ex.Message}");
                                                    });
                                                }
                                            });
                                            }
                                        }
                                    }
                                    
                                    DispatcherQueue.TryEnqueue(() => {
                                        ClientValueTextBlock.Text = dataStr;
                                        UpdateLogMessage($"实时组包内容: {dataStr}");
                                    });

                                }
                                catch
                                {
                                    dataStr = BitConverter.ToString(allData.ToArray());
                                    Debug.WriteLine($"[实时组包] 当前已拼接二进制: {dataStr}");
                                    DispatcherQueue.TryEnqueue(() => {
                                        ClientValueTextBlock.Text = $"实时组包{allData.Count}字节 (HEX: {dataStr})";
                                        UpdateLogMessage($"实时组包二进制: {dataStr}");
                                    });
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[警告] 收到非协议包头数据，已忽略");
                        }
                    }
                    else if (dataLen > 0)
                    {
                        byte[] receivedData = new byte[dataLen];
                        reader.ReadBytes(receivedData);
                        Debug.WriteLine($"[数据接收] 收到非20字节包: {BitConverter.ToString(receivedData)}");
                        DispatcherQueue.TryEnqueue(() => {
                            ClientValueTextBlock.Text = $"收到非协议包({dataLen}字节): {BitConverter.ToString(receivedData)}";
                            UpdateLogMessage($"收到非协议包({dataLen}字节): {BitConverter.ToString(receivedData)}");
                        });
                        string dataStr;
                        try
                        {
                            dataStr = Encoding.UTF8.GetString(receivedData);
                            Debug.WriteLine($"[数据接收] 收到文本数据: {dataStr}");
                            
                            DispatcherQueue.TryEnqueue(() => {
                                ClientValueTextBlock.Text = dataStr;
                                UpdateLogMessage($"收到文本数据: {dataStr}");
                            });
                        }
                        catch
                        {
                            dataStr = BitConverter.ToString(receivedData);
                            Debug.WriteLine($"[数据接收] 收到二进制数据: {dataStr}");
                            DispatcherQueue.TryEnqueue(() => {
                                ClientValueTextBlock.Text = $"收到{dataLen}字节数据 (HEX: {dataStr})";
                                UpdateLogMessage($"收到二进制数据: {dataStr}");
                            });
                        }
                    }
                    if (request.Option == GattWriteOption.WriteWithResponse)
                    {
                        request.Respond();
                    }
                }
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                Debug.WriteLine($"[WriteRequested错误] 蓝牙COMException: {comEx.Message}, HResult: 0x{comEx.HResult:X8}");
                DispatcherQueue.TryEnqueue(() => {
                    UpdateLogMessage($"[WriteRequested错误] 蓝牙通信异常: {comEx.Message} (错误代码: 0x{comEx.HResult:X8})");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WriteRequested错误] 处理写入请求失败: {ex.Message}");
                Debug.WriteLine($"[WriteRequested错误] 异常类型: {ex.GetType().Name}");
                Debug.WriteLine($"[WriteRequested错误] 堆栈跟踪: {ex.StackTrace}");
                DispatcherQueue.TryEnqueue(() => {
                    UpdateLogMessage($"[WriteRequested错误] 处理写入请求失败: {ex.Message}");
                });
            }
            finally
            {
                deferral.Complete();
            }
         }
         
        // 处理包大小协商
        private async Task HandlePackageSizeNegotiation(GattWriteRequestedEventArgs args)
        {
            var deferral = args.GetDeferral();
            try
            {
                var request = await args.GetRequestAsync();
                if (request != null)
                {
                    var reader = DataReader.FromBuffer(request.Value);
                    uint dataLen = reader.UnconsumedBufferLength;
                    Debug.WriteLine($"[包大小协商] 收到写入, dataLen={dataLen}");
                    
                    if (dataLen == 20)
                    {
                        byte[] packet = new byte[20];
                        reader.ReadBytes(packet);
                        Debug.WriteLine($"[包大小协商包原始数据] {BitConverter.ToString(packet)}");
                        
                        if (packet[0] == 0xAA)
                        {
                            byte total = packet[1];
                            byte index = packet[2];
                            int dataTotalLen = (packet[3] << 8) | packet[4];
                            byte[] dataPart = new byte[15];
                            Array.Copy(packet, 5, dataPart, 0, 15);
                            
                            DispatcherQueue.TryEnqueue(() => {
                                UpdateLogMessage($"包大小协商：收到分包 序号: {index}, 内容(HEX): {BitConverter.ToString(dataPart)}");
                            });
                            
                            string msgKey = $"packagesize-{total}-{dataTotalLen}";
                            lock (blePacketLock)
                            {
                                if (!blePacketCache.ContainsKey(msgKey))
                                {
                                    blePacketCache[msgKey] = new SortedDictionary<byte, byte[]>();
                                    blePacketTotal[msgKey] = total;
                                    blePacketDataLen[msgKey] = dataTotalLen;
                                }
                                var partDict = blePacketCache[msgKey] as SortedDictionary<byte, byte[]>;
                                partDict[index] = dataPart;
                                
                                // 检查是否收到全部分包
                                if (partDict.Count == total)
                                {
                                    // 组装完整数据
                                    var allData = partDict.OrderBy(kv => kv.Key).SelectMany(kv => kv.Value).Take(dataTotalLen).ToArray();
                                    
                                    // 尝试解析maxpackagesize
                                    try
                                    {
                                        string dataStr = Encoding.UTF8.GetString(allData);
                                        Debug.WriteLine($"[包大小协商] 完整数据: {dataStr}");
                                        
                                        // 首先尝试解析JSON格式
                                        int parsedSize = 0;
                                        bool parseSuccess = false;
                                        
                                        if (dataStr.Trim().StartsWith("{") && dataStr.Trim().EndsWith("}"))
                                        {
                                            try
                                            {
                                                // 简单的JSON解析，提取data字段
                                                var jsonStr = dataStr.Trim();
                                                var dataIndex = jsonStr.IndexOf("\"data\":");
                                                if (dataIndex != -1)
                                                {
                                                    var dataStart = jsonStr.IndexOf(':', dataIndex) + 1;
                                                    var dataEnd = jsonStr.IndexOf('}', dataStart);
                                                    if (dataEnd == -1) dataEnd = jsonStr.Length;
                                                    
                                                    var dataValue = jsonStr.Substring(dataStart, dataEnd - dataStart).Trim().TrimEnd('}');
                                                    if (int.TryParse(dataValue, out parsedSize))
                                                    {
                                                        parseSuccess = true;
                                                        Debug.WriteLine($"[包大小协商] 从JSON提取data字段: {parsedSize}");
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                Debug.WriteLine($"[包大小协商] JSON解析失败: {ex.Message}");
                                            }
                                        }
                                        
                                        // 如果JSON解析失败，尝试直接解析为数字
                                        if (!parseSuccess && int.TryParse(dataStr.Trim(), out parsedSize))
                                        {
                                            parseSuccess = true;
                                            Debug.WriteLine($"[包大小协商] 直接解析为数字: {parsedSize}");
                                        }
                                        
                                        if (parseSuccess)
                                        {
                                            if (parsedSize > 0 && parsedSize <= 4096)
                                            {
                                                maxPackageSize = parsedSize;
                                                Debug.WriteLine($"更新最大包大小为: {maxPackageSize}");
                                                DispatcherQueue.TryEnqueue(() => {
                                                    UpdateLogMessage($"包大小协商：客户端设置最大包大小为 {maxPackageSize} 字节");
                                                });
                                            }
                                            else
                                            {
                                                Debug.WriteLine($"无效的包大小: {parsedSize}");
                                                DispatcherQueue.TryEnqueue(() => {
                                                    UpdateLogMessage($"包大小协商：收到无效的包大小 {parsedSize}，已忽略");
                                                });
                                            }
                                        }
                                        else
                                        {
                                            Debug.WriteLine($"无法解析包大小数据: {dataStr}");
                                            DispatcherQueue.TryEnqueue(() => {
                                                UpdateLogMessage($"包大小协商：无法解析数据 {dataStr}");
                                            });
                                        }
                                    }
                                    catch
                                    {
                                        // 尝试解析为二进制整数
                                        if (allData.Length >= 4)
                                        {
                                            int newMaxPackageSize = BitConverter.ToInt32(allData, 0);
                                            if (newMaxPackageSize > 0 && newMaxPackageSize <= 4096)
                                            {
                                                maxPackageSize = newMaxPackageSize;
                                                Debug.WriteLine($"更新最大包大小为: {maxPackageSize}");
                                                DispatcherQueue.TryEnqueue(() => {
                                                    UpdateLogMessage($"包大小协商：客户端设置最大包大小为 {maxPackageSize} 字节");
                                                });
                                            }
                                        }
                                        else
                                        {
                                            Debug.WriteLine("无法解析包大小数据");
                                        }
                                    }
                                    
                                    // 清理缓存
                                    blePacketCache.Remove(msgKey);
                                    blePacketTotal.Remove(msgKey);
                                    blePacketDataLen.Remove(msgKey);
                                }
                            }
                        }
                        else
                        {
                            Debug.WriteLine($"[包大小协商警告] 收到非协议包头数据，已忽略");
                        }
                    }
                    else if (dataLen > 0)
                    {
                        // 处理非20字节的直接数据
                        byte[] receivedData = new byte[dataLen];
                        reader.ReadBytes(receivedData);
                        Debug.WriteLine($"[包大小协商] 收到非20字节包: {BitConverter.ToString(receivedData)}");
                        
                        try
                        {
                            string dataStr = Encoding.UTF8.GetString(receivedData);
                            Debug.WriteLine($"[包大小协商] 非20字节数据: {dataStr}");
                            
                            // 首先尝试解析JSON格式
                            int parsedSize = 0;
                            bool parseSuccess = false;
                            
                            if (dataStr.Trim().StartsWith("{") && dataStr.Trim().EndsWith("}"))
                            {
                                try
                                {
                                    // 简单的JSON解析，提取data字段
                                    var jsonStr = dataStr.Trim();
                                    var dataIndex = jsonStr.IndexOf("\"data\":");
                                    if (dataIndex != -1)
                                    {
                                        var dataStart = jsonStr.IndexOf(':', dataIndex) + 1;
                                        var dataEnd = jsonStr.IndexOf('}', dataStart);
                                        if (dataEnd == -1) dataEnd = jsonStr.Length;
                                        
                                        var dataValue = jsonStr.Substring(dataStart, dataEnd - dataStart).Trim().TrimEnd('}');
                                        if (int.TryParse(dataValue, out parsedSize))
                                        {
                                            parseSuccess = true;
                                            Debug.WriteLine($"[包大小协商] 从JSON提取data字段: {parsedSize}");
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[包大小协商] JSON解析失败: {ex.Message}");
                                }
                            }
                            
                            // 如果JSON解析失败，尝试直接解析为数字
                            if (!parseSuccess && int.TryParse(dataStr.Trim(), out parsedSize))
                            {
                                parseSuccess = true;
                                Debug.WriteLine($"[包大小协商] 直接解析为数字: {parsedSize}");
                            }
                            
                            if (parseSuccess && parsedSize > 0 && parsedSize <= 4096)
                            {
                                maxPackageSize = parsedSize;
                                Debug.WriteLine($"更新最大包大小为: {maxPackageSize}");
                                DispatcherQueue.TryEnqueue(() => {
                                    UpdateLogMessage($"包大小协商：客户端设置最大包大小为 {maxPackageSize} 字节");
                                });
                            }
                        }
                        catch
                        {
                            // 尝试解析为二进制整数
                            if (receivedData.Length >= 4)
                            {
                                int newMaxPackageSize = BitConverter.ToInt32(receivedData, 0);
                                if (newMaxPackageSize > 0 && newMaxPackageSize <= 4096)
                                {
                                    maxPackageSize = newMaxPackageSize;
                                    Debug.WriteLine($"更新最大包大小为: {maxPackageSize}");
                                    DispatcherQueue.TryEnqueue(() => {
                                        UpdateLogMessage($"包大小协商：客户端设置最大包大小为 {maxPackageSize} 字节");
                                    });
                                }
                            }
                        }
                    }
                    
                    if (request.Option == GattWriteOption.WriteWithResponse)
                    {
                        request.Respond();
                    }
                }
            }
            finally
            {
                deferral.Complete();
            }
        }
        
        // 移除了SendFileButton_Click方法，现在只通过预览指令触发文件发送  
        
        private async Task SendFileToClient(string filePath)
        {
            Debug.WriteLine("发送文件的实现");

            // 检查是否已有文件传输在进行
            lock (fileTransferLock)
            {
                if (isFileTransferInProgress)
                {
                    Debug.WriteLine($"文件传输被拒绝：已有传输在进行中，当前传输文件: {currentTransferFile}");
                    UpdateLogMessage($"文件传输被拒绝：已有传输在进行中，当前传输文件: {System.IO.Path.GetFileName(currentTransferFile)}");
                    return;
                }
                isFileTransferInProgress = true;
                currentTransferFile = filePath;
            }

            try
            {
                // 使用固定的特征码4B5D6E7F-8A9B-1C2D-3E4F-5A6B7C8D9E0F发送文件
                string targetUuid = "4B5D6E7F-8A9B-1C2D-3E4F-5A6B7C8D9E0F";
                Debug.WriteLine($"使用固定特征码发送文件: {targetUuid}");
                
                var characteristic = localCharacteristics.FirstOrDefault(c => c.Uuid.ToString().ToUpper() == targetUuid.ToUpper());
                if (characteristic == null)
                {
                    Debug.WriteLine($"退出：未找到特征码 {targetUuid}");
                    UpdateLogMessage($"未找到目标特征码: {targetUuid}");
                    return;
                }
                
                // 修改2：检查该特征码的订阅客户端
                if (characteristic.SubscribedClients.Count == 0)
                {
                    Debug.WriteLine("退出：没有订阅客户端");
                    UpdateLogMessage("没有客户端订阅该特征码的通知服务");
                    return;
                }
                
                if (!System.IO.File.Exists(filePath))
                {
                    Debug.WriteLine($"文件不存在: {filePath}");
                    UpdateLogMessage($"文件不存在: {filePath}");
                    return;
                }
            
            int use_packet_size = maxPackageSize; // 使用动态协商的包大小
            int data_length = use_packet_size - 9;
            byte[] fileBytes = System.IO.File.ReadAllBytes(filePath);
            int dataLen = fileBytes.Length;
            int packetDataLen = data_length;
            int totalPackets = (dataLen + packetDataLen - 1) / packetDataLen;
            int batchSize = 5;
            var sendTasks = new List<Task>();
            Debug.WriteLine($"file_size: {dataLen}");
            UpdateLogMessage($"开始发送文件，大小: {dataLen} 字节，总包数: {totalPackets}，使用包大小: {use_packet_size}");
            
            var sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < totalPackets; i++)
            {
                byte[] packet;
                packet = new byte[use_packet_size];
                packet[0] = 0xAB;
                packet[1] = (byte)((totalPackets >> 8) & 0xFF);
                packet[2] = (byte)(totalPackets & 0xFF);
                packet[3] = (byte)(((i+1) >> 8) & 0xFF);
                packet[4] = (byte)((i+1) & 0xFF);
                packet[5] = (byte)((dataLen >> 24) & 0xFF);
                packet[6] = (byte)((dataLen >> 16) & 0xFF);
                packet[7] = (byte)((dataLen >> 8) & 0xFF);
                packet[8] = (byte)(dataLen & 0xFF);
                int copyLen = Math.Min(data_length, dataLen - i * data_length);
                Array.Copy(fileBytes, i * data_length, packet, 9, copyLen);
                
                var writer = new DataWriter();
                writer.WriteBytes(packet);
                var buffer = writer.DetachBuffer();
                
                // 修改3：向该特征码的所有订阅客户端发送
                foreach (var client in characteristic.SubscribedClients)
                {
                    Debug.WriteLine($"发送包[{i+1}/{totalPackets}]: " + BitConverter.ToString(packet));
                    try
                    {
                        sendTasks.Add(characteristic.NotifyValueAsync(buffer, client).AsTask());
                    }
                    catch (System.Runtime.InteropServices.COMException comEx)
                    {
                        Debug.WriteLine($"[蓝牙发送错误] COMException: {comEx.Message}, HResult: 0x{comEx.HResult:X8}");
                        UpdateLogMessage($"蓝牙发送失败: {comEx.Message}");
                        throw; // 重新抛出异常以便上层处理
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[蓝牙发送错误] 其他异常: {ex.Message}");
                        UpdateLogMessage($"发送异常: {ex.Message}");
                        throw;
                    }
                }
                
                if (sendTasks.Count >= batchSize * characteristic.SubscribedClients.Count || i == totalPackets - 1)
                {
                    await Task.WhenAll(sendTasks);
                    sendTasks.Clear();
                    int percent = (int)(((i + 1) * 100.0) / totalPackets);
                    Debug.WriteLine($"文件发送进度: {percent}%");
                    UpdateLogMessage($"文件发送进度: {percent}%");
                    if (i < totalPackets - 1)
                    {
                        await Task.Delay(5);
                    }
                }
            }
                sw.Stop();
                double seconds = sw.Elapsed.TotalSeconds;
                double speed = dataLen / 1024.0 / seconds;
                Debug.WriteLine($"文件发送完成，总字节数: {fileBytes.Length}, 耗时: {seconds:F2} 秒，速率: {speed:F2} KB/s");
                UpdateLogMessage($"文件发送完成，总字节数: {fileBytes.Length}, 耗时: {seconds:F2} 秒，速率: {speed:F2} KB/s");
            }
            finally
            {
                // 无论成功还是失败，都要重置传输状态
                lock (fileTransferLock)
                {
                    isFileTransferInProgress = false;
                    currentTransferFile = string.Empty;
                }
                Debug.WriteLine("文件传输状态已重置");
            }
        }
        
        // 获取蓝牙设备ID（MAC地址格式）
        private async Task<string> GetBluetoothDeviceId()
        {
            try
            {
                Debug.WriteLine("开始获取蓝牙适配器...");
                
                // 检查蓝牙是否可用
                var bluetoothSupport = BluetoothAdapter.GetDeviceSelector();
                Debug.WriteLine($"蓝牙设备选择器: {bluetoothSupport}");
                
                // 获取默认蓝牙适配器
                var adapter = await BluetoothAdapter.GetDefaultAsync();
                if (adapter == null)
                {
                    Debug.WriteLine("警告: BluetoothAdapter.GetDefaultAsync() 返回 null");
                    Debug.WriteLine("可能原因: 1) 蓝牙未启用 2) 权限不足 3) 系统不支持蓝牙");
                    
                    // 生成一个基于时间的伪MAC地址作为备用方案
                    var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
                    var pseudoMac = $"02:{(timestamp >> 32) & 0xFF:X2}:{(timestamp >> 24) & 0xFF:X2}:{(timestamp >> 16) & 0xFF:X2}:{(timestamp >> 8) & 0xFF:X2}:{timestamp & 0xFF:X2}";
                    Debug.WriteLine($"使用备用伪MAC地址: {pseudoMac}");
                    return pseudoMac;
                }
                
                Debug.WriteLine($"成功获取蓝牙适配器: {adapter.DeviceId}");
                
                // 尝试从BluetoothAddress获取MAC地址
                ulong bluetoothAddress = adapter.BluetoothAddress;
                Debug.WriteLine($"蓝牙地址 (ulong): {bluetoothAddress}");
                
                if (bluetoothAddress != 0)
                {
                    // 将蓝牙地址转换为MAC地址格式 (XX:XX:XX:XX:XX:XX)
                    byte[] addressBytes = new byte[6];
                    for (int i = 0; i < 6; i++)
                    {
                        addressBytes[5 - i] = (byte)((bluetoothAddress >> (i * 8)) & 0xFF);
                    }
                    var macAddress = string.Join(":", addressBytes.Select(b => b.ToString("X2")));
                    Debug.WriteLine($"转换后的MAC地址: {macAddress}");
                    return macAddress;
                }
                else
                {
                    Debug.WriteLine("警告: BluetoothAddress 为 0");
                    return "00:00:00:00:00:00";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取蓝牙设备ID失败: {ex.Message}");
                Debug.WriteLine($"异常类型: {ex.GetType().Name}");
                Debug.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                return "00:00:00:00:00:00";
            }
        }
        
        // 获取蓝牙设备名称
        private async Task<string> GetBluetoothDeviceName()
        {
            try
            {
                Debug.WriteLine("开始获取蓝牙设备名称...");
                
                // 方案1: 尝试获取系统计算机名称
                try
                {
                    string computerName = Environment.MachineName;
                    if (!string.IsNullOrEmpty(computerName))
                    {
                        // 使用计算机名称生成设备名称，限制长度避免过长
                        string deviceName = computerName;
                        if (deviceName.Length > 20) // BLE设备名称通常有长度限制
                        {
                            deviceName = computerName.Substring(0, Math.Min(computerName.Length, 20));
                        }
                        Debug.WriteLine($"基于计算机名称生成设备名称: {deviceName}");
                        return deviceName;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"获取计算机名称失败: {ex.Message}");
                }
                
                // 方案2: 尝试获取用户名
                try
                {
                    string userName = Environment.UserName;
                    if (!string.IsNullOrEmpty(userName))
                    {
                        string deviceName = userName;
                        if (deviceName.Length > 20)
                        {
                            deviceName = userName.Substring(0, Math.Min(userName.Length, 20));
                        }
                        Debug.WriteLine($"基于用户名生成设备名称: {deviceName}");
                        return deviceName;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"获取用户名失败: {ex.Message}");
                }
                
                // 方案3: 尝试获取蓝牙适配器（如果可用）
                try
                {
                    var adapter = await BluetoothAdapter.GetDefaultAsync();
                    if (adapter != null)
                    {
                        Debug.WriteLine($"成功获取蓝牙适配器，设备ID: {adapter.DeviceId}");
                        
                        // 尝试获取MAC地址来生成设备名称
                        ulong bluetoothAddress = adapter.BluetoothAddress;
                        if (bluetoothAddress != 0)
                        {
                            var deviceName = $"MAC_{bluetoothAddress & 0xFFFFFF:X6}";
                            Debug.WriteLine($"基于MAC地址生成设备名称: {deviceName}");
                            return deviceName;
                        }
                    }
                    else
                    {
                        Debug.WriteLine("警告: BluetoothAdapter.GetDefaultAsync() 返回 null");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"获取蓝牙适配器失败: {ex.Message}");
                }
                
                // 方案4: 生成基于时间和随机数的设备名称
                var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
                var random = new Random();
                var deviceName_fallback = $"Device_{(timestamp & 0xFFFF):X4}{random.Next(0x1000, 0xFFFF):X4}";
                Debug.WriteLine($"使用最终备用设备名称: {deviceName_fallback}");
                return deviceName_fallback;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取蓝牙设备名称完全失败: {ex.Message}");
                Debug.WriteLine($"异常类型: {ex.GetType().Name}");
                return "UNKNOWN";
            }
        }
    }
}
