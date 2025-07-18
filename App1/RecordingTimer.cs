using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace App1
{
    /// <summary>
    /// 录制计时器类，用于管理录制过程中的时间计算
    /// </summary>
    public class RecordingTimer
    {
        private DateTime? _startTime;
        private DateTime? _endTime;
        private DateTime? _pauseTime;
        private TimeSpan _pausedDuration = TimeSpan.Zero;
        private DispatcherTimer? _timer;
        private DispatcherQueue _dispatcherQueue;
        private bool _isPaused = false;
        
        /// <summary>
        /// 录制开始时间
        /// </summary>
        public DateTime? StartTime => _startTime;
        
        /// <summary>
        /// 录制结束时间
        /// </summary>
        public DateTime? EndTime => _endTime;
        
        /// <summary>
        /// 当前录制时长
        /// </summary>
        public TimeSpan? Duration
        {
            get
            {
                if (_startTime == null) return null;
                
                var endTime = _endTime ?? DateTime.Now;
                var totalTime = endTime - _startTime.Value;
                
                // 如果当前正在暂停，需要加上当前暂停的时间
                var currentPausedTime = _pausedDuration;
                if (_isPaused && _pauseTime.HasValue)
                {
                    currentPausedTime += DateTime.Now - _pauseTime.Value;
                }
                
                // 总时长减去暂停时长
                return totalTime - currentPausedTime;
            }
        }
        
        /// <summary>
        /// 是否正在录制
        /// </summary>
        public bool IsRecording => _startTime != null && _endTime == null && !_isPaused;
        
        /// <summary>
        /// 是否已暂停
        /// </summary>
        public bool IsPaused => _isPaused;
        
        /// <summary>
        /// 计时器更新事件，每秒触发一次
        /// </summary>
        public event Action<TimeSpan>? TimerTick;
        
        /// <summary>
        /// 录制开始事件
        /// </summary>
        public event Action<DateTime>? RecordingStarted;
        
        /// <summary>
        /// 录制结束事件
        /// </summary>
        public event Action<DateTime, TimeSpan>? RecordingStopped;
        
        /// <summary>
        /// 录制暂停事件
        /// </summary>
        public event Action<DateTime>? RecordingPaused;
        
        /// <summary>
        /// 录制恢复事件
        /// </summary>
        public event Action<DateTime>? RecordingResumed;
        
        public RecordingTimer(DispatcherQueue dispatcherQueue)
        {
            _dispatcherQueue = dispatcherQueue;
        }
        
        /// <summary>
        /// 开始录制计时
        /// </summary>
        public void StartRecording()
        {
            if (IsRecording)
            {
                throw new InvalidOperationException("录制已经在进行中");
            }
            
            _startTime = DateTime.Now;
            _endTime = null;
            
            // 创建并启动计时器，每秒更新一次
            _timer = new DispatcherTimer()
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            
            _timer.Tick += (sender, e) =>
            {
                if (Duration.HasValue)
                {
                    TimerTick?.Invoke(Duration.Value);
                }
            };
            
            _timer.Start();
            
            // 触发录制开始事件
            RecordingStarted?.Invoke(_startTime.Value);
        }
        
        /// <summary>
        /// 暂停录制计时
        /// </summary>
        public void PauseRecording()
        {
            if (!IsRecording || _isPaused)
            {
                throw new InvalidOperationException("当前没有进行录制或已经暂停");
            }
            
            _isPaused = true;
            _pauseTime = DateTime.Now;
            
            // 暂停计时器
            _timer?.Stop();
            
            // 触发录制暂停事件
            RecordingPaused?.Invoke(_pauseTime.Value);
        }
        
        /// <summary>
        /// 恢复录制计时
        /// </summary>
        public void ResumeRecording()
        {
            if (!_isPaused || _startTime == null)
            {
                throw new InvalidOperationException("当前没有暂停的录制");
            }
            
            // 累加暂停时长
            if (_pauseTime.HasValue)
            {
                _pausedDuration += DateTime.Now - _pauseTime.Value;
            }
            
            _isPaused = false;
            _pauseTime = null;
            
            // 恢复计时器
            _timer?.Start();
            
            // 触发录制恢复事件
            RecordingResumed?.Invoke(DateTime.Now);
        }
        
        /// <summary>
        /// 停止录制计时
        /// </summary>
        /// <returns>录制总时长</returns>
        public TimeSpan StopRecording()
        {
            if (_startTime == null || _endTime != null)
            {
                throw new InvalidOperationException("当前没有进行录制");
            }
            
            // 如果正在暂停，先处理暂停时长
            if (_isPaused && _pauseTime.HasValue)
            {
                _pausedDuration += DateTime.Now - _pauseTime.Value;
            }
            
            _endTime = DateTime.Now;
            _isPaused = false;
            _pauseTime = null;
            
            // 停止计时器
            _timer?.Stop();
            _timer = null;
            
            var duration = Duration!.Value;
            
            // 触发录制结束事件
            RecordingStopped?.Invoke(_endTime.Value, duration);
            
            return duration;
        }
        
        /// <summary>
        /// 重置计时器
        /// </summary>
        public void Reset()
        {
            _timer?.Stop();
            _timer = null;
            _startTime = null;
            _endTime = null;
            _pauseTime = null;
            _pausedDuration = TimeSpan.Zero;
            _isPaused = false;
        }
        
        /// <summary>
        /// 获取格式化的时长字符串
        /// </summary>
        /// <returns>格式化的时长字符串 (HH:mm:ss)</returns>
        public string GetFormattedDuration()
        {
            if (Duration == null) return "00:00:00";
            
            var duration = Duration.Value;
            return $"{(int)duration.TotalHours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
        }
        
        /// <summary>
        /// 获取录制状态描述
        /// </summary>
        /// <returns>录制状态描述</returns>
        public string GetStatusDescription()
        {
            if (!_startTime.HasValue)
                return "未开始录制";
            
            if (_isPaused)
                return $"录制已暂停 - {GetFormattedDuration()}";
            
            if (IsRecording)
                return $"正在录制 - {GetFormattedDuration()}";
            
            return $"录制已完成 - 总时长: {GetFormattedDuration()}";
        }
    }
}