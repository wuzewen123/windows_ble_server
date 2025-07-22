using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;

namespace App1
{
    /// <summary>
    /// 提供与后端API通信的服务
    /// </summary>
    public class ApiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        
        /// <summary>
        /// 初始化API服务
        /// </summary>
        /// <param name="baseUrl">API基础URL</param>
        public ApiService(string baseUrl = "https://oneshot.makebaba.com/")
        {
            _baseUrl = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
            _httpClient = new HttpClient();
            _httpClient.BaseAddress = new Uri(_baseUrl);
        }
        
        /// <summary>
        /// 暂停录制
        /// </summary>
        /// <param name="recordingId">录制ID</param>
        /// <returns>API响应结果</returns>
        public async Task<ApiResponse> PauseRecordingAsync(int recordingId)
        {
            try
            {
                string requestUrl = $"{_baseUrl}api/sso/pauseRecording";
                string requestBody = JsonSerializer.Serialize(new { id = recordingId });
                Debug.WriteLine($"[API] 发送暂停录制请求，完整URL: {requestUrl}，请求体: {requestBody}");
                
                var content = new StringContent(
                    requestBody,
                    Encoding.UTF8,
                    "application/json");
                    
                var response = await _httpClient.PostAsync("api/sso/pauseRecording", content);
                
                return await ProcessResponseAsync(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API错误] 暂停录制请求失败: {ex.Message}");
                return new ApiResponse
                {
                    Success = false,
                    Message = $"请求失败: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// 继续录制
        /// </summary>
        /// <param name="recordingId">录制ID</param>
        /// <returns>API响应结果</returns>
        public async Task<ApiResponse> RestartRecordingAsync(int recordingId)
        {
            try
            {
                string requestUrl = $"{_baseUrl}api/sso/restartRecording";
                string requestBody = JsonSerializer.Serialize(new { id = recordingId });
                Debug.WriteLine($"[API] 发送继续录制请求，完整URL: {requestUrl}，请求体: {requestBody}");
                
                var content = new StringContent(
                    requestBody,
                    Encoding.UTF8,
                    "application/json");
                    
                var response = await _httpClient.PostAsync("api/sso/restartRecording", content);
                
                return await ProcessResponseAsync(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API错误] 继续录制请求失败: {ex.Message}");
                return new ApiResponse
                {
                    Success = false,
                    Message = $"请求失败: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// 结束录制
        /// </summary>
        /// <param name="recordingId">录制ID</param>
        /// <returns>API响应结果</returns>
        public async Task<ApiResponse> EndRecordingAsync(int recordingId)
        {
            try
            {
                string requestUrl = $"{_baseUrl}api/sso/endRecording";
                string requestBody = JsonSerializer.Serialize(new { id = recordingId });
                Debug.WriteLine($"[API] 发送结束录制请求，完整URL: {requestUrl}，请求体: {requestBody}");
                
                var content = new StringContent(
                    requestBody,
                    Encoding.UTF8,
                    "application/json");
                    
                var response = await _httpClient.PostAsync("api/sso/endRecording", content);
                
                return await ProcessResponseAsync(response);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[API错误] 结束录制请求失败: {ex.Message}");
                return new ApiResponse
                {
                    Success = false,
                    Message = $"请求失败: {ex.Message}"
                };
            }
        }
        
        /// <summary>
        /// 处理API响应
        /// </summary>
        /// <param name="response">HTTP响应</param>
        /// <returns>API响应结果</returns>
        private async Task<ApiResponse> ProcessResponseAsync(HttpResponseMessage response)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            Debug.WriteLine($"[API] 收到响应: {responseContent}");
            
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    // 尝试将响应解析为后端API响应模型
                    var backendResponse = JsonSerializer.Deserialize<ApiResponseModel>(responseContent);
                    
                    if (backendResponse != null)
                    {
                        // 将后端响应转换为客户端使用的ApiResponse格式
                        return new ApiResponse
                        {
                            // code为0表示成功
                            Success = backendResponse.code == 0,
                            Message = backendResponse.msg,
                            Data = backendResponse.data
                        };
                    }
                    else
                    {
                        return new ApiResponse { Success = false, Message = "请求成功，但响应为空" };
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[API错误] 解析响应失败: {ex.Message}");
                    return new ApiResponse
                    {
                        Success = false,
                        Message = $"请求成功，但解析响应失败: {responseContent}"
                    };
                }
            }
            else
            {
                return new ApiResponse
                {
                    Success = false,
                    Message = $"请求失败，状态码: {(int)response.StatusCode}, 响应: {responseContent}"
                };
            }
        }
    }
    
    /// <summary>
    /// API响应模型 (客户端使用)
    /// </summary>
    public class ApiResponse
    {
        /// <summary>
        /// 请求是否成功
        /// </summary>
        public bool Success { get; set; }
        
        /// <summary>
        /// 响应消息
        /// </summary>
        public string Message { get; set; } = string.Empty;
        
        /// <summary>
        /// 响应数据
        /// </summary>
        public JsonElement? Data { get; set; }
    }
    
    /// <summary>
    /// 后端API响应模型
    /// </summary>
    public class ApiResponseModel
    {
        /// <summary>
        /// 状态码，0表示成功
        /// </summary>
        public int code { get; set; }
        
        /// <summary>
        /// 响应消息
        /// </summary>
        public string msg { get; set; } = string.Empty;
        
        /// <summary>
        /// 响应数据
        /// </summary>
        public JsonElement? data { get; set; }
    }
}