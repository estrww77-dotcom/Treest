using OpenSteam.Properties;
using OpenSteam.Services;
using OpenSteam.Models;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace OpenSteam.Views
{
    public partial class OnlineLua : UserControl
    {
        private static readonly HttpClient _http = new HttpClient();
        private List<Game> CachedList = new List<Game>();

        public OnlineLua()
        {
            InitializeComponent();
            LoadData();
        }

        private async void LoadData()
        {
            ButtonSearch.IsEnabled = false;
            ButtonSearch.Opacity = 0.6;
            ButtonText.Visibility = Visibility.Collapsed;
            ButtonProgress.Visibility = Visibility.Visible;

            try
            {
                CachedList = await SteamUtils.DownloadGameListAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load game data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ButtonSearch.IsEnabled = true;
                ButtonSearch.Opacity = 1.0;
                ButtonText.Visibility = Visibility.Visible;
                ButtonProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async Task<bool> ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return false;
            try
            {
                var body = new StringContent(JsonSerializer.Serialize(new { key }), Encoding.UTF8, "application/json");
                var res = await _http.PostAsync($"{AppConfig.ServerUrl}/api/validate", body);
                var json = await res.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("valid", out var v) && v.GetBoolean();
            }
            catch
            {
                return false;
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            string userKey = KeyBox.Text.Trim();
            string userInput = SearchBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(userInput))
            {
                MessageBox.Show("Please enter an AppID or Name first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ButtonSearch.IsEnabled = false;
            ButtonSearch.Opacity = 0.6;
            ButtonText.Visibility = Visibility.Collapsed;
            ButtonProgress.Visibility = Visibility.Visible;

            try
            {
                bool keyValid = await ValidateKey(userKey);
                if (!keyValid)
                {
                    MessageBox.Show("Access denied. Please enter a valid access key.", "Access Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Settings.Default.LicenseKey = userKey;
                Settings.Default.Save();

                LuaLoaders luaLoaders = new LuaLoaders();
                string steamPath = SteamUtils.GetSteamPath();

                if (Properties.Settings.Default.FilterManager)
                {
                    var results = await Task.Run(() => SteamUtils.GetFilteredGames(userInput, CachedList));

                    if (results == null || !results.Any())
                    {
                        MessageBox.Show("No games found with that ID or Name. You can try disabling the filter in the settings (Only works with appid)", "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    Game selectedGame = results.First();

                    if (selectedGame.nsfw && !Properties.Settings.Default.DisableNFSWAlert)
                    {
                        var res = MessageBox.Show("This game is marked as NSFW. Continue?", "NSFW Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (res == MessageBoxResult.No) return;
                    }

                    if (selectedGame.drm)
                    {
                        var res = MessageBox.Show("This game has DRM. It may not work. Continue?", "DRM Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (res == MessageBoxResult.No) return;
                    }

                    await luaLoaders.OnlineLoad(selectedGame.appid, steamPath);
                }
                else
                {
                    await luaLoaders.OnlineLoad(userInput, steamPath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ButtonSearch.IsEnabled = true;
                ButtonSearch.Opacity = 1.0;
                ButtonText.Visibility = Visibility.Visible;
                ButtonProgress.Visibility = Visibility.Collapsed;
            }
        }
    }
}
