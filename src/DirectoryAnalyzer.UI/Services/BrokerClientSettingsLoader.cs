using System;
using System.IO;
using System.Runtime.Serialization.Json;

namespace DirectoryAnalyzer.Services
{
    public static class BrokerClientSettingsLoader
    {
        public static BrokerClientSettings Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Settings path cannot be null or empty.", nameof(path));
            }

            var logger = LogService.CreateLogger("BrokerClientSettings");
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                if (!File.Exists(path))
                {
                    var defaultSettings = new BrokerClientSettings();
                    BrokerClientSettingsStore.Save(path, defaultSettings);
                    return defaultSettings;
                }

                using var stream = File.OpenRead(path);
                var serializer = new DataContractJsonSerializer(typeof(BrokerClientSettings));
                var settings = serializer.ReadObject(stream) as BrokerClientSettings;
                if (settings == null)
                {
                    throw new InvalidOperationException("Invalid broker client settings.");
                }

                return settings;
            }
            catch (DirectoryNotFoundException ex)
            {
                logger.Warn($"Settings directory not found. Recreating defaults. Details: {ex.Message}");
            }
            catch (FileNotFoundException ex)
            {
                logger.Warn($"Settings file not found. Recreating defaults. Details: {ex.Message}");
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to load broker client settings. Recreating defaults. Details: {ex}");
            }

            var fallbackSettings = new BrokerClientSettings();
            BrokerClientSettingsStore.Save(path, fallbackSettings);
            return fallbackSettings;
        }
    }
}
