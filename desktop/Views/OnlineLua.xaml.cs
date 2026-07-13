using OpenSteam.Properties;
using OpenSteam.Services;
using OpenSteam.Models;
using System.Windows;
using System.Windows.Controls;

namespace OpenSteam.Views
{
    public partial class OnlineLua : UserControl
    {
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
                MessageBox.Show($"Failed to load game list: {ex.Message}", "Network Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                ButtonSearch.IsEnabled = true;
                ButtonSearch.Opacity = 1.0;
                ButtonText.Visibility = Visibility.Visible;
                ButtonProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async void Search_Click(object sender, RoutedEventArgs e)
        {
            string userKey = KeyBox.Text.Trim();
            string userInput = SearchBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(userKey))
            {
                MessageBox.Show("Enter your access key first.", "Access Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(userInput))
            {
                MessageBox.Show("Enter a game name or AppID.", "Input Required",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ButtonSearch.IsEnabled = false;
            ButtonSearch.Opacity = 0.6;
            ButtonText.Visibility = Visibility.Collapsed;
            ButtonProgress.Visibility = Visibility.Visible;

            try
            {
                // Save key to settings
                Settings.Default.LicenseKey = userKey;
                Settings.Default.Save();

                string steamPath = SteamUtils.GetSteamPath();
                if (string.IsNullOrEmpty(steamPath))
                    throw new Exception("Steam path not found. Make sure Steam is installed.");

                string appIdStr;

                if (Settings.Default.FilterManager)
                {
                    var results = await Task.Run(() => SteamUtils.GetFilteredGames(userInput, CachedList));

                    if (results == null || !results.Any())
                    {
                        MessageBox.Show(
                            "Game not found. Try the exact AppID from the Steam store page.\n" +
                            "Tip: Disable 'Filter Manager' in Settings to use AppID directly.",
                            "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // Prefer exact name match, fall back to first result
                    Game selectedGame = results.FirstOrDefault(g =>
                        g.name != null &&
                        g.name.Equals(userInput, StringComparison.OrdinalIgnoreCase))
                        ?? results.First();

                    if (selectedGame.nsfw && !Settings.Default.DisableNFSWAlert)
                    {
                        var r = MessageBox.Show("This game is marked as NSFW. Continue?",
                            "NSFW Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (r == MessageBoxResult.No) return;
                    }

                    if (selectedGame.drm)
                    {
                        var r = MessageBox.Show(
                            "This game has DRM protection — it may not work correctly. Continue anyway?",
                            "DRM Warning", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                        if (r == MessageBoxResult.No) return;
                    }

                    appIdStr = selectedGame.appid;
                }
                else
                {
                    // Direct AppID mode — must be numeric
                    if (!int.TryParse(userInput, out _))
                        throw new Exception("Filter Manager is disabled — enter a numeric AppID only.");
                    appIdStr = userInput;
                }

                // Generate via server (validates key, consumes key on success)
                LuaLoaders luaLoaders = new LuaLoaders();
                await luaLoaders.OnlineLoad(appIdStr, steamPath);

                // Clear key after use (it's been consumed — user needs a new one next time)
                KeyBox.Clear();
                Settings.Default.LicenseKey = string.Empty;
                Settings.Default.Save();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
