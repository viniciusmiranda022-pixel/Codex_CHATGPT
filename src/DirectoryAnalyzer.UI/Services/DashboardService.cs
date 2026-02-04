using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DirectoryAnalyzer.Services
{
    [DataContract]
    public sealed class DashboardActivityEntry
    {
        [DataMember]
        public DateTime Timestamp { get; set; }

        [DataMember]
        public string Module { get; set; }

        [DataMember]
        public string Status { get; set; }

        [DataMember]
        public double? DurationSeconds { get; set; }

        [DataMember]
        public int? ItemCount { get; set; }

        [DataMember]
        public int? ErrorCount { get; set; }
    }

    public sealed class DashboardService
    {
        private const int MaxEntries = 10;
        private static readonly DashboardService InstanceInternal = new DashboardService();
        private readonly object _lock = new object();
        private readonly List<DashboardActivityEntry> _entries = new List<DashboardActivityEntry>();
        private readonly ConcurrentDictionary<string, DateTime> _startTimes = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly string _filePath;

        private DashboardService()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DirectoryAnalyzer");
            Directory.CreateDirectory(root);
            _filePath = Path.Combine(root, "recent.json");
            LoadFromDisk();
        }

        public static DashboardService Instance => InstanceInternal;

        public event EventHandler<DashboardActivityEntry> ActivityAdded;

        public string RecentFilePath => _filePath;

        public string LogFilePath => LogService.GetLogFilePath("General");

        public DashboardActivityEntry LastCompletedEntry { get; private set; }

        public IReadOnlyList<DashboardActivityEntry> RecentEntries
        {
            get
            {
                lock (_lock)
                {
                    return _entries.ToList();
                }
            }
        }

        public void RecordModuleStart(string moduleName)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                moduleName = "Módulo";
            }

            _startTimes[moduleName] = DateTime.Now;
            AddEntry(new DashboardActivityEntry
            {
                Timestamp = DateTime.Now,
                Module = moduleName,
                Status = "Iniciado"
            });
        }

        public void RecordModuleFinish(string moduleName, bool success, int? itemCount = null, int? errorCount = null, bool wasCanceled = false)
        {
            if (string.IsNullOrWhiteSpace(moduleName))
            {
                moduleName = "Módulo";
            }

            DateTime completedAt = DateTime.Now;
            TimeSpan? duration = null;

            if (_startTimes.TryRemove(moduleName, out DateTime startTime))
            {
                duration = completedAt - startTime;
            }

            string status = wasCanceled ? "Cancelado" : success ? "Concluído" : "Com erros";
            if (!success && !wasCanceled && errorCount == null)
            {
                errorCount = 1;
            }

            var entry = new DashboardActivityEntry
            {
                Timestamp = completedAt,
                Module = moduleName,
                Status = status,
                DurationSeconds = duration?.TotalSeconds,
                ItemCount = itemCount,
                ErrorCount = errorCount
            };

            AddEntry(entry);
            LastCompletedEntry = entry;
        }

        private void AddEntry(DashboardActivityEntry entry)
        {
            lock (_lock)
            {
                _entries.Insert(0, entry);
                if (_entries.Count > MaxEntries)
                {
                    _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
                }

                SaveToDisk();
            }

            ActivityAdded?.Invoke(this, entry);
        }

        private void LoadFromDisk()
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            try
            {
                using (var stream = File.OpenRead(_filePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(List<DashboardActivityEntry>));
                    if (serializer.ReadObject(stream) is List<DashboardActivityEntry> entries)
                    {
                        _entries.Clear();
                        _entries.AddRange(entries.OrderByDescending(entry => entry.Timestamp).Take(MaxEntries));
                        LastCompletedEntry = _entries.FirstOrDefault(entry => entry.Status != "Iniciado");
                    }
                }
            }
            catch
            {
                _entries.Clear();
                LastCompletedEntry = null;
            }
        }

        private void SaveToDisk()
        {
            try
            {
                using (var stream = File.Create(_filePath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(List<DashboardActivityEntry>));
                    serializer.WriteObject(stream, _entries);
                }
            }
            catch
            {
            }
        }
    }
}
