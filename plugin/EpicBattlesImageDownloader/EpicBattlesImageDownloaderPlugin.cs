using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using Octgn.Core.DataExtensionMethods;
using Octgn.Core.DataManagers;
using Octgn.Core.Plugin;
using Octgn.DataNew;
using Octgn.DataNew.Entities;

namespace EpicBattlesImageDownloader
{
    public sealed class EpicBattlesImageDownloaderPlugin : IDeckBuilderPlugin
    {
        public static readonly Guid GameId = Guid.Parse("336cc7ef-c808-5f75-a22e-0171564da1e3");
        public IEnumerable<IPluginMenuItem> MenuItems => new[] { new DownloaderMenuItem() };
        public void OnLoad(GameManager games) { }
        public Guid Id => Guid.Parse("76d85e91-b9e0-4b7e-95b1-213204571c4a");
        public string Name => "Epic Battles Online Image Downloader";
        public Version Version => Version.Parse("0.1.1.0");
        public Version RequiredByOctgnVersion => Version.Parse("3.1.240.0");
    }

    public sealed class DownloaderMenuItem : IPluginMenuItem
    {
        public string Name => "Epic Battles Image Downloader";
        public async void OnClick(IDeckBuilderPluginController controller)
        {
            try
            {
                var game = controller.GetLoadedGame();
                if (game == null || game.Id != EpicBattlesImageDownloaderPlugin.GameId)
                    game = DbContext.Get().GameById(EpicBattlesImageDownloaderPlugin.GameId);
                if (game == null) throw new InvalidOperationException("Epic Battles Online is not installed.");
                new DownloaderWindow(await Downloader.LoadAsync(game)).ShowDialog();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Epic Battles Image Downloader", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }

    internal sealed class DownloaderWindow : Window
    {
        private readonly Catalog _catalog;
        private readonly ListBox _sets = new ListBox { DisplayMemberPath = "DisplayName" };
        private readonly Button _selected = new Button { Content = "Update Selected Set", MinWidth = 145, Padding = new Thickness(8, 5, 8, 5) };
        private readonly Button _all = new Button { Content = "Update All Sets", MinWidth = 125, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(8, 0, 0, 0) };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };

        public DownloaderWindow(Catalog catalog)
        {
            _catalog = catalog; Title = "Epic Battles Image Downloader"; Width = 560; Height = 420;
            MinWidth = 480; MinHeight = 320; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            var root = new DockPanel { Margin = new Thickness(12) }; Content = root;
            var heading = new TextBlock { Text = "Epic Battles Online", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            _status.Text = "Select Tekken Promos, then download its six card images.";
            DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            buttons.Children.Add(_selected); buttons.Children.Add(_all);
            _selected.Click += async (s, e) => { var set = _sets.SelectedItem as ImageSet; if (set != null) await UpdateAsync(new[] { set }); };
            _all.Click += async (s, e) => await UpdateAsync(_catalog.Sets);
            _sets.SelectionChanged += (s, e) => _selected.IsEnabled = _sets.SelectedItem != null;
            foreach (var set in catalog.Sets) _sets.Items.Add(set);
            if (_sets.Items.Count > 0) _sets.SelectedIndex = 0;
            root.Children.Add(_sets);
        }

        private async Task UpdateAsync(IEnumerable<ImageSet> sets)
        {
            SetBusy(true);
            try
            {
                _status.Text = "Downloading images...";
                var result = await Downloader.UpdateAsync(sets);
                _sets.Items.Refresh();
                _status.Text = String.Format("Downloaded: {0}. Already current: {1}. Failed: {2}.", result.Downloaded, result.Current, result.Failed);
                if (result.Failed > 0) MessageBox.Show(_status.Text, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex) { _status.Text = "The image update did not complete."; MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { SetBusy(false); }
        }

        private void SetBusy(bool busy) { _sets.IsEnabled = !busy; _selected.IsEnabled = !busy && _sets.SelectedItem != null; _all.IsEnabled = !busy; }
    }

    internal static class Downloader
    {
        private const string BaseUrl = "https://raw.githubusercontent.com/JoePerry/Epic-Battles-Online/main/image-host/";
        private static readonly HttpClient Client = CreateClient();
        private static HttpClient CreateClient() { var c = new HttpClient { Timeout = TimeSpan.FromSeconds(45) }; c.DefaultRequestHeaders.UserAgent.ParseAdd("OCTGN-Epic-Battles-Image-Downloader/0.1.1"); return c; }

        public static async Task<Catalog> LoadAsync(Game game)
        {
            var json = await Client.GetStringAsync(BaseUrl + "manifest.json");
            var manifest = new JavaScriptSerializer().Deserialize<Manifest>(json);
            Guid gameId;
            if (manifest == null || !Guid.TryParse(manifest.gameGuid, out gameId) || gameId != EpicBattlesImageDownloaderPlugin.GameId)
                throw new InvalidDataException("The online image catalog does not match Epic Battles Online.");
            var installed = game.Sets().ToDictionary(s => s.Id, s => s);
            var sets = new List<ImageSet>();
            foreach (var group in (manifest.images ?? new List<ManifestImage>()).GroupBy(i => i.setGuid, StringComparer.OrdinalIgnoreCase))
            {
                Guid setId; Set set;
                if (!Guid.TryParse(group.Key, out setId) || !installed.TryGetValue(setId, out set)) continue;
                sets.Add(new ImageSet(set, group.Select(i => RemoteImage.Create(setId, i)).ToList()));
            }
            return new Catalog(sets.OrderBy(s => s.Name).ToList());
        }

        public static async Task<Result> UpdateAsync(IEnumerable<ImageSet> sets)
        {
            var result = new Result();
            foreach (var set in sets)
            {
                Directory.CreateDirectory(set.ImageDirectory);
                foreach (var image in set.Images)
                {
                    var target = Path.Combine(set.ImageDirectory, image.CardId + ".jpg");
                    var temporary = target + ".download";
                    try
                    {
                        if (File.Exists(target) && String.Equals(Hash(File.ReadAllBytes(target)), image.Sha256, StringComparison.OrdinalIgnoreCase)) { result.Current++; continue; }
                        var bytes = await Client.GetByteArrayAsync(image.Url);
                        if (!String.Equals(Hash(bytes), image.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Checksum mismatch for " + image.CardId);
                        File.WriteAllBytes(temporary, bytes);
                        if (File.Exists(target)) File.Delete(target);
                        File.Move(temporary, target); result.Downloaded++;
                    }
                    catch { if (File.Exists(temporary)) File.Delete(temporary); result.Failed++; }
                }
            }
            return result;
        }

        private static string Hash(byte[] bytes) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }

    internal sealed class Catalog { public Catalog(List<ImageSet> sets) { Sets = sets; } public List<ImageSet> Sets { get; private set; } }
    internal sealed class ImageSet
    {
        public ImageSet(Set set, List<RemoteImage> images) { Id = set.Id; Name = set.Name; ImageDirectory = set.ImagePackUri; Images = images; }
        public Guid Id { get; private set; } public string Name { get; private set; } public string ImageDirectory { get; private set; } public List<RemoteImage> Images { get; private set; }
        public int Installed => Images.Count(i => File.Exists(Path.Combine(ImageDirectory, i.CardId + ".jpg")));
        public string DisplayName => String.Format("{0}    {1} of {2} installed", Name, Installed, Images.Count);
    }
    internal sealed class RemoteImage
    {
        private RemoteImage(Guid cardId, string url, string sha256) { CardId = cardId; Url = url; Sha256 = sha256; }
        public static RemoteImage Create(Guid setId, ManifestImage item) { Guid cardId; if (!Guid.TryParse(item.cardGuid, out cardId)) throw new InvalidDataException("Invalid card ID in image catalog."); return new RemoteImage(cardId, Base(setId, cardId), item.sha256); }
        private static string Base(Guid setId, Guid cardId) { return "https://raw.githubusercontent.com/JoePerry/Epic-Battles-Online/main/image-host/images/" + setId + "/" + cardId + ".jpg"; }
        public Guid CardId { get; private set; } public string Url { get; private set; } public string Sha256 { get; private set; }
    }
    internal sealed class Manifest { public string gameGuid { get; set; } public List<ManifestImage> images { get; set; } }
    internal sealed class ManifestImage { public string cardGuid { get; set; } public string setGuid { get; set; } public string sha256 { get; set; } }
    internal sealed class Result { public int Downloaded { get; set; } public int Current { get; set; } public int Failed { get; set; } }
}
