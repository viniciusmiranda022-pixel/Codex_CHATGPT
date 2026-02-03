using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace DirectoryAnalyzer.Services
{
    public enum LogLevel
    {
        Info,
        Warn,
        Error
    }

    public sealed class LogEntry
    {
        public LogEntry(DateTime timestamp, string moduleName, LogLevel level, string message)
        {
            Timestamp = timestamp;
            ModuleName = moduleName;
            Level = level;
            Message = message;
        }

        public DateTime Timestamp { get; }
        public string ModuleName { get; }
        public LogLevel Level { get; }
        public string Message { get; }
    }

    public interface ILogService
    {
        string ModuleName { get; }
        event EventHandler<LogEntry> EntryAdded;
        IReadOnlyCollection<LogEntry> Buffer { get; }
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    public sealed class LogService : ILogService
    {
        private const int DefaultBufferCapacity = 200;
        private static readonly ConcurrentDictionary<string, LogService> Loggers = new ConcurrentDictionary<string, LogService>(StringComparer.OrdinalIgnoreCase);
        private static readonly string RunId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        private static readonly object FileLock = new object();

        private readonly ConcurrentQueue<LogEntry> _buffer;
        private readonly int _bufferCapacity;
        private readonly bool _bufferEnabled;
        private readonly string _filePath;

        private LogService(string moduleName, bool enableBuffer, int bufferCapacity)
        {
            ModuleName = moduleName;
            _bufferEnabled = enableBuffer;
            _bufferCapacity = Math.Max(1, bufferCapacity);
            _buffer = new ConcurrentQueue<LogEntry>();

            string logRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", moduleName);
            Directory.CreateDirectory(logRoot);
            _filePath = Path.Combine(logRoot, $"{moduleName}_{RunId}.log");
        }

        public string ModuleName { get; }

        public event EventHandler<LogEntry> EntryAdded;

        public IReadOnlyCollection<LogEntry> Buffer => _buffer.ToArray();

        public static ILogService CreateLogger(string moduleName, bool enableBuffer = true, int bufferCapacity = DefaultBufferCapacity)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                moduleName = "General";
            }

            return Loggers.GetOrAdd(moduleName, _ => new LogService(moduleName, enableBuffer, bufferCapacity));
        }

        public static void Write(string moduleName, string message, LogLevel level = LogLevel.Info)
        {
            var logger = CreateLogger(moduleName);
            logger.Write(level, message);
        }

        public void Info(string message) => Write(LogLevel.Info, message);

        public void Warn(string message) => Write(LogLevel.Warn, message);

        public void Error(string message) => Write(LogLevel.Error, message);

        private void Write(LogLevel level, string message)
        {
            string safeMessage = message ?? string.Empty;
            var entry = new LogEntry(DateTime.Now, ModuleName, level, safeMessage);

            WriteToFile(entry);
            if (_bufferEnabled)
            {
                _buffer.Enqueue(entry);
                while (_buffer.Count > _bufferCapacity && _buffer.TryDequeue(out _))
                {
                }
            }

            EntryAdded?.Invoke(this, entry);
        }

        private void WriteToFile(LogEntry entry)
        {
            string line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{entry.Level}] {entry.Message}";
            lock (FileLock)
            {
                File.AppendAllText(_filePath, line + Environment.NewLine, new UTF8Encoding(true));
            }
        }
    }
}
