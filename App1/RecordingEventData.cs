using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics;

namespace App1
{
    /// <summary>
    /// 录制事件数据模型，用于解析服务端开始录制接口接收到的JSON字符串
    /// </summary>
    public class RecordingEventData
    {
        [JsonPropertyName("sign")]
        public string? Sign { get; set; }
        
        [JsonPropertyName("time")]
        public long Timestamp { get; set; }
        
        [JsonPropertyName("event")]
        public string? Event { get; set; }
        
        [JsonPropertyName("data")]
        public RecordingData? Data { get; set; }
        
        /// <summary>
        /// 尝试解析JSON字符串为RecordingEventData对象
        /// </summary>
        /// <param name="jsonString">要解析的JSON字符串</param>
        /// <param name="recordingEventData">解析结果</param>
        /// <returns>是否解析成功</returns>
        public static bool TryParse(string jsonString, out RecordingEventData recordingEventData)
        {
            recordingEventData = new RecordingEventData(); // 初始化为空对象而不是null
            try
            {
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                
                var result = JsonSerializer.Deserialize<RecordingEventData>(jsonString, options);
                if (result != null)
                {
                    recordingEventData = result;
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[错误] 解析录制事件JSON失败: {ex.Message}");
                return false;
            }
        }
    }
    
    /// <summary>
    /// 录制数据模型，包含录制相关的详细信息
    /// </summary>
    public class RecordingData
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        
        [JsonPropertyName("myteam")]
        public string? MyTeam { get; set; }
        
        [JsonPropertyName("opponentteam")]
        public string? OpponentTeam { get; set; }
        
        [JsonPropertyName("address")]
        public string? Address { get; set; }
        
        [JsonPropertyName("type")]
        public string? Type { get; set; }
        
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        
        [JsonPropertyName("starttime")]
        public string? StartTime { get; set; }
        
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("push_auth_uri")]
        public string? PushAuthUri { get; set; }
        
        [JsonPropertyName("live_auth_uri")]
        public string? LiveAuthUri { get; set; }
        
        [JsonPropertyName("alltype_live_auth_uri")]
        public LiveAuthUriTypes? AllTypeLiveAuthUri { get; set; }
        
        [JsonPropertyName("artc_push_auth_uri")]
        public string? ArtcPushAuthUri { get; set; }
        
        [JsonPropertyName("myteam_name")]
        public string? MyTeamName { get; set; }
        
        [JsonPropertyName("opponentteam_name")]
        public string? OpponentTeamName { get; set; }
    }
    
    /// <summary>
    /// 不同类型的直播地址
    /// </summary>
    public class LiveAuthUriTypes
    {
        [JsonPropertyName("artc")]
        public string? Artc { get; set; }
        
        [JsonPropertyName("rtmp")]
        public string? Rtmp { get; set; }
        
        [JsonPropertyName("flv")]
        public string? Flv { get; set; }
        
        [JsonPropertyName("hls")]
        public string? Hls { get; set; }
    }
}