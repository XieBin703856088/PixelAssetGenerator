using System;
using System.Windows.Media;

namespace PixelAssetGenerator.Models
{
    public enum LogLevel
    {
        Debug,
        Info,
        Success,
        Warning,
        Error
    }

    public class ConsoleEntry
    {
        public DateTime Timestamp { get; init; }
        public LogLevel Level { get; init; }
        public string Source { get; init; } = "";
        public string Message { get; init; } = "";
        public string? ExceptionDetails { get; init; }

        public string FormattedText => $"[{Timestamp:HH:mm:ss.fff}] [{Level}] {Message}";

        public Brush TextBrush => Level switch
        {
            LogLevel.Error   => new SolidColorBrush(Color.FromRgb(255, 100, 100)),
            LogLevel.Warning => new SolidColorBrush(Color.FromRgb(255, 200, 80)),
            LogLevel.Success => new SolidColorBrush(Color.FromRgb(100, 220, 100)),
            LogLevel.Debug   => new SolidColorBrush(Color.FromRgb(140, 140, 140)),
            _                => new SolidColorBrush(Color.FromRgb(220, 220, 220)),
        };

        public string LevelTag => Level switch
        {
            LogLevel.Error   => "ERR",
            LogLevel.Warning => "WRN",
            LogLevel.Success => "OK ",
            LogLevel.Debug   => "DBG",
            _                => "INF",
        };

        public LogLevelGroup Group => Level switch
        {
            LogLevel.Error   => LogLevelGroup.Error,
            LogLevel.Warning => LogLevelGroup.Warning,
            LogLevel.Info or LogLevel.Success => LogLevelGroup.Info,
            LogLevel.Debug => LogLevelGroup.Debug,
            _ => LogLevelGroup.Info,
        };
    }

    /// <summary>
    /// Coarser grouping for filter toggles (maps 5 levels → 4 filter groups).
    /// </summary>
    public enum LogLevelGroup
    {
        Error,
        Warning,
        Info,
        Debug
    }
}
