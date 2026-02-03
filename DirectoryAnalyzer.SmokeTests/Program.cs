using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DirectoryAnalyzer.Services;

namespace DirectoryAnalyzer.SmokeTests
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            var logger = LogService.CreateLogger("SmokeTests");
            logger.Info("Iniciando smoke tests do DirectoryAnalyzer.");

            try
            {
                StatusService.Instance.SetStatus("Executando smoke tests...");
                InitializeThemeResources(logger);
                RunPowerShellSmokeTest(logger).GetAwaiter().GetResult();

                StatusService.Instance.SetStatus("Concluído");
                logger.Info("Smoke tests concluídos com sucesso.");
                return 0;
            }
            catch (Exception ex)
            {
                StatusService.Instance.SetStatus("Erro - ver log");
                logger.Error("Falha nos smoke tests: " + ex);
                return 1;
            }
        }

        private static void InitializeThemeResources(ILogService logger)
        {
            logger.Info("Validando carregamento dos ResourceDictionaries de tema.");

            var app = new Application();
            var dictionaries = new List<Uri>
            {
                new Uri("pack://application:,,,/Themes/Colors.xaml", UriKind.Absolute),
                new Uri("pack://application:,,,/Themes/Typography.xaml", UriKind.Absolute),
                new Uri("pack://application:,,,/Themes/Styles.xaml", UriKind.Absolute),
                new Uri("pack://application:,,,/Themes/Cards.xaml", UriKind.Absolute)
            };

            foreach (var uri in dictionaries)
            {
                var dictionary = new ResourceDictionary { Source = uri };
                app.Resources.MergedDictionaries.Add(dictionary);
                logger.Info($"ResourceDictionary carregado: {uri}");
            }
        }

        private static async Task RunPowerShellSmokeTest(ILogService logger)
        {
            logger.Info("Executando PowerShell smoke test (Get-Date).");
            var powerShellService = new PowerShellService();
            var result = await powerShellService.ExecuteScriptWithResultAsync("Get-Date");

            logger.Info($"PowerShell output count: {result.Output.Count}.");
            logger.Info($"PowerShell HadErrors: {result.HadErrors}.");

            if (result.Errors.Count > 0)
            {
                logger.Warn("PowerShell Streams.Error: " + string.Join(" | ", result.Errors));
            }

            if (result.HadErrors || result.Errors.Any())
            {
                throw new InvalidOperationException("PowerShell smoke test retornou erros.");
            }
        }
    }
}
