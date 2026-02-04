using System;
using System.IO;
using System.ServiceProcess;
using DirectoryAnalyzer.Agent.Contracts.Services;

namespace DirectoryAnalyzer.Agent
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (HasDoctorFlag(args))
            {
                return RunDoctor();
            }

            var policy = new PathPolicy();
            var resolver = new ConfigurationResolver(policy);
            var resolution = resolver.ResolveAgentConfig();
            var loadResult = AgentConfigLoader.Load(resolution.SelectedPath);
            var config = loadResult.Config;
            System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

            var logger = new AgentLogger(config.LogPath);
            LogResolution(logger, resolution, loadResult);

            if (Environment.UserInteractive)
            {
                var cts = new System.Threading.CancellationTokenSource();
                var host = new AgentHost(config);
                Console.WriteLine("Starting DirectoryAnalyzer agent in console mode...");
                Console.WriteLine("Press ENTER to stop.");
                _ = System.Threading.Tasks.Task.Run(() => host.StartAsync(cts.Token));
                Console.ReadLine();
                cts.Cancel();
                host.StopAsync().GetAwaiter().GetResult();
            }
            else
            {
                ServiceBase.Run(new InventoryAgentService(config));
            }

            return 0;
        }

        private static int RunDoctor()
        {
            var policy = new PathPolicy();
            var resolver = new ConfigurationResolver(policy);
            var resolution = resolver.ResolveAgentConfig();
            var defaultLogPath = policy.DefaultAgentLogPath;
            var logger = new AgentLogger(defaultLogPath);
            var failures = 0;

            WriteDiagnostic(logger, $"Modo doctor iniciado. Config path resolvido: {resolution.SelectedPath}");
            WriteDiagnostic(logger, $"Log path padrão: {defaultLogPath}");
            WriteDiagnostic(logger, $"Precedência: {string.Join(", ", resolution.PrecedenceOrder)}. Fonte: {resolution.Source}.");
            LogMigration(logger, resolution);

            if (!AgentConfigLoader.TryLoad(resolution.SelectedPath, out var loadResult, out var error))
            {
                failures++;
                WriteDiagnostic(logger, $"Falha ao ler JSON de config em {resolution.SelectedPath}. Erro: {error}");
                return 1;
            }

            var config = loadResult.Config;
            if (!string.IsNullOrWhiteSpace(config.LogPath) && !string.Equals(config.LogPath, defaultLogPath, StringComparison.OrdinalIgnoreCase))
            {
                logger = new AgentLogger(config.LogPath);
                WriteDiagnostic(logger, $"Log path efetivo detectado: {config.LogPath}");
            }

            if (!ValidateLogPath(config.LogPath, logger))
            {
                failures++;
            }

            var validationErrors = AgentConfigValidator.Validate(config);
            if (validationErrors.Count > 0)
            {
                failures++;
                WriteDiagnostic(logger, $"Campos obrigatórios ausentes ou inválidos: {string.Join("; ", validationErrors)}");
            }

            WriteDiagnostic(logger, $"Log path efetivo: {config.LogPath}. Origem: {loadResult.LogPathSource}.");
            if (loadResult.RegistryOverrides.Count > 0)
            {
                WriteDiagnostic(logger, $"Overrides de registry aplicados: {string.Join(", ", loadResult.RegistryOverrides)}");
            }
            else
            {
                WriteDiagnostic(logger, "Sem overrides de registry aplicados.");
            }

            WriteDiagnostic(logger, failures == 0 ? "Doctor concluído com sucesso." : "Doctor concluiu com falhas.");
            return failures == 0 ? 0 : 1;
        }

        private static bool HasDoctorFlag(string[] args)
        {
            if (args == null)
            {
                return false;
            }

            foreach (var arg in args)
            {
                if (string.Equals(arg, "--doctor", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static void LogResolution(AgentLogger logger, ConfigurationResolutionResult resolution, AgentConfigLoadResult loadResult)
        {
            if (logger == null)
            {
                return;
            }

            TryWriteLog(logger, AgentLogger.Create("config", "Resolution", string.Empty, string.Empty, 0, "Info", null,
                $"Config path: {resolution.SelectedPath}. Fonte: {resolution.Source}."));
            if (!string.IsNullOrWhiteSpace(resolution.MigrationDetails))
            {
                TryWriteLog(logger, AgentLogger.Create("config", "Migration", string.Empty, string.Empty, 0, "Info", null,
                    resolution.MigrationDetails));
            }

            if (loadResult != null && loadResult.RegistryOverrides.Count > 0)
            {
                TryWriteLog(logger, AgentLogger.Create("config", "RegistryOverrides", string.Empty, string.Empty, 0, "Info", null,
                    $"Overrides: {string.Join(", ", loadResult.RegistryOverrides)}"));
            }
        }

        private static void LogMigration(AgentLogger logger, ConfigurationResolutionResult resolution)
        {
            if (!string.IsNullOrWhiteSpace(resolution.MigrationDetails))
            {
                WriteDiagnostic(logger, resolution.MigrationDetails);
            }
        }

        private static void WriteDiagnostic(AgentLogger logger, string message)
        {
            Console.WriteLine(message);
            if (logger == null)
            {
                return;
            }

            TryWriteLog(logger, AgentLogger.Create("doctor", "Diagnostic", string.Empty, string.Empty, 0, "Info", null, message));
        }

        private static void TryWriteLog(AgentLogger logger, AgentLogEntry entry)
        {
            try
            {
                logger.Write(entry);
            }
            catch
            {
            }
        }

        private static bool ValidateLogPath(string logPath, AgentLogger logger)
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var testFile = System.IO.Path.Combine(directory ?? AppDomain.CurrentDomain.BaseDirectory, $"doctor_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(testFile, "ok");
                File.Delete(testFile);
                WriteDiagnostic(logger, "Validação de escrita no diretório de log concluída.");
                return true;
            }
            catch (Exception ex)
            {
                WriteDiagnostic(logger, $"Falha ao validar escrita no diretório de log. Erro: {ex.Message}");
                return false;
            }
        }
    }
}
