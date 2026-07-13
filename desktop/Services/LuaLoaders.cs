using Microsoft.Win32;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using OpenSteam.Views;

namespace OpenSteam.Services
{
    public class LuaLoaders
    {
        private static readonly HttpClient _staticHttpClient = new HttpClient();
        private readonly HttpClient _instanceHttpClient = new HttpClient();

        public void Load(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                MessageBox.Show("The Steam path was not detected.");
                return;
            }
            string luaPathSteam = Path.Combine(path, "config", Properties.Settings.Default.LuaPath);
            OpenFileDialog luaLoader = new OpenFileDialog
            {
                Filter = "Lua Files|*.lua",
                Title = "Select Lua"
            };

            if (luaLoader.ShowDialog() == true)
            {
                try
                {
                    Directory.CreateDirectory(luaPathSteam);
                    string destinationFile = Path.Combine(luaPathSteam, luaLoader.SafeFileName);
                    File.Copy(luaLoader.FileName, destinationFile, true);
                    SteamUtils.FixManifests(SteamUtils.GetSteamPath());
                    NotificationWindow win = new NotificationWindow("Lua successfully loaded!", 2);
                    win.Show();
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show("Error: You do not have permission to write to the Steam folder. Run as administrator.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Something went wrong: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Generates a Lua file by calling the RedSea server (validates key, 1 key = 1 game).
        /// Saves the .lua file to the Steam plugin directory and then fixes manifests.
        /// </summary>
        public static async Task<string> GenerateFromServer(int appId, string steamPath)
        {
            string key = Properties.Settings.Default.LicenseKey?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(key))
                throw new Exception("No access key set. Enter your key in the Access Key field.");

            string serverUrl = AppConfig.ServerUrl;
            if (serverUrl.Contains("YOUR-REPLIT-URL"))
                throw new Exception("Server URL not configured. Update AppConfig.json next to RedSea.exe with your server URL.");

            string url = $"{serverUrl}/api/generate/{appId}?key={Uri.EscapeDataString(key)}";

            HttpResponseMessage response = await _staticHttpClient.GetAsync(url);
            string json = await response.Content.ReadAsStringAsync();

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                throw new Exception("Access key is invalid or already used. Ask for a new key.");

            if (!response.IsSuccessStatusCode)
            {
                string errMsg = "Server error.";
                try
                {
                    using var errDoc = JsonDocument.Parse(json);
                    if (errDoc.RootElement.TryGetProperty("error", out var e)) errMsg = e.GetString() ?? errMsg;
                }
                catch { }
                throw new Exception(errMsg);
            }

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var errProp))
                throw new Exception(errProp.GetString() ?? "Generation failed.");

            string luaContent = doc.RootElement.GetProperty("lua").GetString()
                ?? throw new Exception("Server returned empty Lua.");

            // Save to Steam plugin directory
            string luaDir = Path.Combine(steamPath, "config", Properties.Settings.Default.LuaPath);
            Directory.CreateDirectory(luaDir);
            string luaFile = Path.Combine(luaDir, $"{appId}.lua");
            await File.WriteAllTextAsync(luaFile, luaContent, new UTF8Encoding(false));

            return luaContent;
        }

        /// <summary>
        /// Legacy direct-API generator (used internally; does NOT enforce key).
        /// </summary>
        public static async Task<string> SteamLuaGenerator(int appId, string path, int cacheDays = 7)
        {
            string apiUrl = $"https://api.steamproof.net/apps/depots?ids={appId}";
            string apiJson = await _staticHttpClient.GetStringAsync(apiUrl);

            string cacheDir = Path.Combine(path, "cache");
            Directory.CreateDirectory(cacheDir);
            string cacheFile = Path.Combine(cacheDir, "depotkeys.json");

            string[] keysUrls = new[]
            {
                "https://gitlab.com/steamautocracks/manifesthub/-/raw/main/depotkeys.json",
                "https://api.993499094.xyz/depotkeys.json"
            };

            var depotKeys = new Dictionary<string, string>();
            bool shouldRefresh = true;

            if (File.Exists(cacheFile))
            {
                DateTime lastWriteUtc = File.GetLastWriteTimeUtc(cacheFile);
                if (lastWriteUtc >= DateTime.UtcNow.AddDays(-cacheDays))
                {
                    try
                    {
                        string cachedJson = await File.ReadAllTextAsync(cacheFile, Encoding.UTF8);
                        depotKeys = JsonSerializer.Deserialize<Dictionary<string, string>>(cachedJson) ?? new();
                        shouldRefresh = false;
                    }
                    catch { }
                }
            }

            if (shouldRefresh)
            {
                foreach (string url in keysUrls)
                {
                    try
                    {
                        string jsonFromServer = await _staticHttpClient.GetStringAsync(url);
                        var downloaded = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonFromServer);
                        if (downloaded != null)
                            foreach (var kvp in downloaded) depotKeys.TryAdd(kvp.Key, kvp.Value);
                    }
                    catch { continue; }
                }
                await File.WriteAllTextAsync(cacheFile, JsonSerializer.Serialize(depotKeys));
            }

            using var doc = JsonDocument.Parse(apiJson);
            var apps = doc.RootElement.GetProperty("apps");
            if (apps.GetArrayLength() == 0)
                throw new Exception("No depot data returned from API for this game.");

            var app = apps[0];
            var depots = app.GetProperty("depots");

            var sb = new StringBuilder();
            sb.AppendLine($"-- RedSea {System.Reflection.Assembly.GetExecutingAssembly().GetName().Version}");
            sb.AppendLine($"addappid({appId})");

            foreach (var depot in depots.EnumerateArray())
            {
                int depotId = depot.GetProperty("depotId").GetInt32();
                string depotIdKey = depotId.ToString();

                if (depotKeys.TryGetValue(depotIdKey, out var depotKey) && !string.IsNullOrWhiteSpace(depotKey))
                    sb.AppendLine($"addappid({depotId},1,\"{depotKey}\")");
                else
                    sb.AppendLine($"addappid({depotId},0,\"\")");

                if (depot.TryGetProperty("manifests", out var manifests) && manifests.ValueKind == JsonValueKind.Object)
                {
                    string? manifestId = null;
                    if (manifests.TryGetProperty("public", out var pub) && pub.TryGetProperty("manifestId", out var mid))
                        manifestId = mid.GetString();
                    else
                        foreach (var branch in manifests.EnumerateObject())
                            if (branch.Value.TryGetProperty("manifestId", out var anyMid) && !string.IsNullOrWhiteSpace(anyMid.GetString()))
                            { manifestId = anyMid.GetString(); break; }

                    if (!string.IsNullOrWhiteSpace(manifestId))
                        sb.AppendLine($"setManifestid({depotId},\"{manifestId}\")");
                }
            }

            Directory.CreateDirectory(path);
            string outputFile = Path.Combine(path, $"{appId}.lua");
            await File.WriteAllTextAsync(outputFile, sb.ToString(), Encoding.UTF8);
            return sb.ToString();
        }

        public async Task OnlineLoad(string appIdStr, string steamPath)
        {
            _instanceHttpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RedSea-Manager/3.0");

            try
            {
                if (!int.TryParse(appIdStr, out int appId))
                    throw new Exception("Invalid AppID — must be a number.");

                // Always use the server (validates key + generates)
                await GenerateFromServer(appId, steamPath);

                // Fix manifests after adding the new game
                try { await SteamUtils.FixManifests(steamPath); } catch { }

                NotificationWindow win = new NotificationWindow("✔ Game added! Key consumed.", 3);
                win.Show();
                await Task.Delay(500);
                SteamUtils.Reset();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
