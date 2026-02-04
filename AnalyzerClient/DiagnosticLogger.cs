using System;
using System.IO;
using System.Text;

namespace DirectoryAnalyzer.AnalyzerClient
{
    public sealed class DiagnosticLogger
    {
        private readonly string _logPath;
        private readonly object _lock = new object();

        public DiagnosticLogger(string logPath)
        {
            _logPath = logPath;
        }

        public void Write(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var line = $"{DateTime.UtcNow:O} | {message}";
            try
            {
                var directory = Path.GetDirectoryName(_logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                lock (_lock)
                {
                    File.AppendAllText(_logPath, line + Environment.NewLine, new UTF8Encoding(true));
                }
            }
            catch
            {
            }
        }
    }
}
