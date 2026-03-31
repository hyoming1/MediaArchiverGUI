using System;
using System.Windows.Media;

namespace MediaArchiver.ViewModels
{
    /// <summary>로그 패널 한 행의 표시 모델</summary>
    public class LogEntryViewModel
    {
        public string Time       { get; init; } = "";
        public string Message    { get; init; } = "";
        public Brush  LevelBrush { get; init; } = Brushes.White;

        // 색상 캐시 (Brush 는 스레드 안전하도록 Freeze)
        private static readonly Brush BrInfo;
        private static readonly Brush BrWarn;
        private static readonly Brush BrSuccess;
        private static readonly Brush BrError;

        static LogEntryViewModel()
        {
            BrInfo    = Freeze(new SolidColorBrush(Color.FromRgb(0xE8, 0xEA, 0xF2)));
            BrWarn    = Freeze(new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)));
            BrSuccess = Freeze(new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)));
            BrError   = Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)));
        }

        private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }

        public static LogEntryViewModel Create(LogLevel level, string message)
        {
            var brush = level switch
            {
                LogLevel.Warn    => BrWarn,
                LogLevel.Success => BrSuccess,
                LogLevel.Error   => BrError,
                _                => BrInfo,
            };

            return new LogEntryViewModel
            {
                Time       = DateTime.Now.ToString("HH:mm:ss"),
                Message    = message,
                LevelBrush = brush,
            };
        }
    }
}
