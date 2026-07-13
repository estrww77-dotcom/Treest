using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace OpenSteam
{
    public static class AppConfig
    {
        private static string? _serverUrl;

        public static string ServerUrl
        {
            get
            {
                if (_serverUrl != null) return _serverUrl;
                try
                {
                    // Read from AppConfig.json placed next to RedSea.exe
                    string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
                    if (exeDir != null)
                    {
                        string configPath = Path.Combine(exeDir, "AppConfig.json");
                        if (File.Exists(configPath))
                        {
                            string json = File.ReadAllText(configPath);
                            using var doc = JsonDocument.Parse(json);
                            if (doc.RootElement.TryGetProperty("serverUrl", out var urlProp))
                            {
                                string? val = urlProp.GetString();
                                if (!string.IsNullOrWhiteSpace(val) && val != "https://YOUR-REPLIT-URL.replit.app")
                                {
                                    _serverUrl = val.TrimEnd('/');
                                    return _serverUrl;
                                }
                            }
                        }
                    }
                }
                catch { }
                _serverUrl = "https://YOUR-REPLIT-URL.replit.app";
                return _serverUrl;
            }
        }
    }
}
