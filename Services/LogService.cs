using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace DirectoryAnalyzer.Services
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warn,
        Error
    }

    public sealed class LogEntry
    {
        public LogEntry(DateTime timestamp, LogLevel level, string className, string methodName, string message, string correlationId)
        {
            Timestamp = timestamp;
            Level = level;
            ClassName = className;
            MethodName = methodName;
            Message = message;
            CorrelationId = correlationId;
        }

        public DateTime Timestamp { get; }
        public LogLevel Level { get; }
        public string ClassName { get; }
        public string MethodName { get; }
        public string Message { get; }
        public string CorrelationId { get; }
    }

    public interface ILogService
    {
        string ModuleName { get; }
        event EventHandler<LogEntry> EntryAdded;
        IReadOnlyCollection<LogEntry> Buffer { get; }
        void Debug(string message, string correlationId = null, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null);
        void Info(string message, string correlationId = null, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null);
        void Warn(string message, string correlationId = null, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null);
        void Error(string message, string correlationId = null, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null);
        void Write(LogLevel level, string message, string correlationId = null, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null);
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

            string logRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DirectoryAnalyzer", "Logs", moduleName);
            Directory.CreateDirectory(logRoot);
            _filePath = Path.Combine(logRoot, $"{moduleName}_{RunId}.log");
        }

        public string ModuleName { get; }

        public string FilePath => _filePath;

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

        public static string GetLogFilePath(string moduleName = "General")
        {
            var logger = CreateLogger(moduleName) as LogService;
            return logger?._filePath;
        }

        public static void Write(string moduleName, string message, LogLevel level = LogLevel.Info)
        {
            var logger = CreateLogger(moduleName);
            logger.Write(level, message);
        }

        public void Debug(string message, string correlationId = null, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null)
            => Write(LogLevel.Debug, message, correlationId, memberName, sourceFilePath);

        public void Info(string message, string correlationId = null, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null)
            => Write(LogLevel.Info, message, correlationId, memberName, sourceFilePath);

        public void Warn(string message, string correlationId = null, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null)
            => Write(LogLevel.Warn, message, correlationId, memberName, sourceFilePath);

        public void Error(string message, string correlationId = null, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null)
            => Write(LogLevel.Error, message, correlationId, memberName, sourceFilePath);

        public void Write(LogLevel level, string message, string correlationId = null, [CallerMemberName] string memberName = null, [CallerFilePath] string sourceFilePath = null)
        {
            string safeMessage = message ?? string.Empty;
            string className = ResolveClassName(sourceFilePath);
            string methodName = string.IsNullOrWhiteSpace(memberName) ? "Unknown" : memberName;
            string safeCorrelationId = string.IsNullOrWhiteSpace(correlationId) ? "N/A" : correlationId;
            var entry = new LogEntry(DateTime.Now, level, className, methodName, safeMessage, safeCorrelationId);

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
            string line = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} | {entry.Level} | {entry.ClassName} | {entry.MethodName} | {entry.CorrelationId} | {entry.Message}";
            lock (FileLock)
            {
                File.AppendAllText(_filePath, line + Environment.NewLine, new UTF8Encoding(true));
            }
        }

        public static string CreateCorrelationId()
        {
            return Guid.NewGuid().ToString("N");
        }

        private static string ResolveClassName(string sourceFilePath)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
            {
                return "Unknown";
            }

            return Path.GetFileNameWithoutExtension(sourceFilePath) ?? "Unknown";
        }
    }
}
