using System;
using System.Collections.ObjectModel;
using PixelAssetGenerator.Models;

namespace PixelAssetGenerator.Services
{
    public interface IConsoleService
    {
        ObservableCollection<ConsoleEntry> Entries { get; }
        bool ShowErrors { get; set; }
        bool ShowWarnings { get; set; }
        bool ShowInfos { get; set; }
        bool ShowDebugs { get; set; }

        void Log(LogLevel level, string message, string? source = null, Exception? ex = null);
        void LogError(string message, string? source = null, Exception? ex = null);
        void LogWarning(string message, string? source = null);
        void LogInfo(string message, string? source = null);
        void LogDebug(string message, string? source = null);
        void LogSuccess(string message, string? source = null);
        void Clear();
    }

    public class ConsoleService : IConsoleService
    {
        private const int MaxEntries = 100;
        private readonly object _lock = new();

        public ObservableCollection<ConsoleEntry> Entries { get; } = new();

        public bool ShowErrors { get; set; } = true;
        public bool ShowWarnings { get; set; } = true;
        public bool ShowInfos { get; set; } = true;
        public bool ShowDebugs { get; set; } = true;

        public void Log(LogLevel level, string message, string? source = null, Exception? ex = null)
        {
            var entry = new ConsoleEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message,
                Source = source ?? "",
                ExceptionDetails = ex?.ToString()
            };

            // Also write to Trace for debugger output
            System.Diagnostics.Trace.WriteLine(entry.FormattedText);

            lock (_lock)
            {
                App.Current?.Dispatcher?.BeginInvoke(() =>
                {
                    Entries.Add(entry);
                    TrimExcess();
                });
            }
        }

        public void LogError(string message, string? source = null, Exception? ex = null)
            => Log(LogLevel.Error, message, source, ex);

        public void LogWarning(string message, string? source = null)
            => Log(LogLevel.Warning, message, source);

        public void LogInfo(string message, string? source = null)
            => Log(LogLevel.Info, message, source);

        public void LogDebug(string message, string? source = null)
            => Log(LogLevel.Debug, message, source);

        public void LogSuccess(string message, string? source = null)
            => Log(LogLevel.Success, message, source);

        public void Clear()
        {
            App.Current?.Dispatcher?.BeginInvoke(() => Entries.Clear());
        }

        private void TrimExcess()
        {
            while (Entries.Count > MaxEntries)
                Entries.RemoveAt(0);
        }
    }
}
