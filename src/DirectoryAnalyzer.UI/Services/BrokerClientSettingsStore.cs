using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace DirectoryAnalyzer.Services
{
    public static class BrokerClientSettingsStore
    {
        public static string ResolvePath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "PrototypeConfigs", "brokerclientsettings.json");
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
