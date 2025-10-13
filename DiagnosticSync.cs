using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PrintagoFolderWatch
{
    /// <summary>
    /// Diagnostic tool to log the complete sync process
    /// </summary>
    public class DiagnosticSync
    {
        private readonly FileWatcherServiceV2 service;
        private readonly string logPath;

        public DiagnosticSync(FileWatcherServiceV2 service)
        {
            this.service = service;
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrintagoFolderWatch"
            );
            Directory.CreateDirectory(appData);
            logPath = Path.Combine(appData, $"diagnostic-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        }

        public void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            var line = $"[{timestamp}] {message}";
            Console.WriteLine(line);
            File.AppendAllText(logPath, line + Environment.NewLine);
        }

        public string GetLogPath() => logPath;
    }
}
