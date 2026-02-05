using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace DirectoryAnalyzer.Services
{
    public static class BrokerClientSettingsStore
    {
        public static string ResolvePath()
        {
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "DirectoryAnalyzer");
            return Path.Combine(baseDir, "brokerclientsettings.json");
        }

        public static void Save(string path, BrokerClientSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppDomain.CurrentDomain.BaseDirectory);
            using var stream = File.Create(path);
            var serializer = new DataContractJsonSerializer(typeof(BrokerClientSettings));
            serializer.WriteObject(stream, settings);
        }
    }
}
