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
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
        public Version Version => Version.Parse("0.9.0.0");
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
        private readonly Canvas _progressPanel = new Canvas { Height = 104, Margin = new Thickness(10, 8, 10, 0), Visibility = Visibility.Collapsed };
        private readonly Border _progressTrail = new Border { Height = 18, Background = new SolidColorBrush(Color.FromRgb(116, 220, 255)), CornerRadius = new CornerRadius(9), Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(35, 170, 255), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.95 } };
        private readonly Image _progressFireball = new Image { Width = 70, Height = 70, Stretch = Stretch.Uniform };
        private readonly TextBlock _progressText = new TextBlock { Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center };
        private double _progressPercent;

        public DownloaderWindow(Catalog catalog)
        {
            _catalog = catalog; Title = "Epic Battles Image Downloader"; Width = 560; Height = 420;
            MinWidth = 480; MinHeight = 320; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new ImageBrush(new BitmapImage(new Uri("pack://application:,,,/EpicBattlesImageDownloader;component/Assets/poster-mvc1_big.jpg")))
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            var root = new DockPanel
            {
                Margin = new Thickness(12),
                Background = new SolidColorBrush(Color.FromArgb(210, 12, 12, 18))
            };
            Content = root;
            var heading = new TextBlock { Text = "Epic Battles Online", Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(10, 8, 10, 8) };
            DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            _status.Text = "Select a set to download its card images.";
            _status.Foreground = Brushes.White; _status.Margin = new Thickness(10, 8, 10, 10);
            DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            DockPanel.SetDock(buttons, Dock.Bottom); root.Children.Add(buttons);
            buttons.Children.Add(_selected); buttons.Children.Add(_all);
            var progressSource = new BitmapImage(new Uri("pack://application:,,,/EpicBattlesImageDownloader;component/Assets/ryu-hadoken-progress.png"));
            var authenticSource = new BitmapImage(new Uri("pack://application:,,,/EpicBattlesImageDownloader;component/Assets/ryu-authentic-progress.png"));
            var ryu = new Image { Source = new CroppedBitmap(authenticSource, new Int32Rect(0, 0, 850, 783)), Width = 132, Height = 100, Stretch = Stretch.Uniform };
            _progressFireball.Source = new CroppedBitmap(progressSource, new Int32Rect(1740, 75, 432, 574));
            _progressPanel.Children.Add(_progressTrail); _progressPanel.Children.Add(ryu); _progressPanel.Children.Add(_progressFireball); _progressPanel.Children.Add(_progressText);
            Canvas.SetLeft(ryu, 0); Canvas.SetTop(ryu, 2); Canvas.SetTop(_progressTrail, 43); Canvas.SetTop(_progressFireball, 17);
            _progressPanel.SizeChanged += (s, e) => UpdateProgressAnimation();
            DockPanel.SetDock(_progressPanel, Dock.Bottom); root.Children.Add(_progressPanel);
            _selected.Click += async (s, e) => { var set = _sets.SelectedItem as ImageSet; if (set != null) await UpdateAsync(new[] { set }); };
            _all.Click += async (s, e) => await UpdateAsync(_catalog.Sets);
            _sets.SelectionChanged += (s, e) => _selected.IsEnabled = _sets.SelectedItem != null;
            foreach (var set in catalog.Sets) _sets.Items.Add(set);
            if (_sets.Items.Count > 0) _sets.SelectedIndex = 0;
            _sets.Margin = new Thickness(10, 0, 10, 0);
            _sets.Background = new SolidColorBrush(Color.FromArgb(225, 248, 248, 248));
            root.Children.Add(_sets);
        }

        private async Task UpdateAsync(IEnumerable<ImageSet> sets)
        {
            SetBusy(true);
            try
            {
                _status.Text = "Downloading images...";
                _progressPanel.Visibility = Visibility.Visible;
                SetProgress(0);
                var progress = new Progress<DownloadProgress>(p => SetProgress(p.Percent));
                var result = await Downloader.UpdateAsync(sets, progress);
                _sets.Items.Refresh();
                _status.Text = String.Format("Downloaded: {0}. Already current: {1}. Failed: {2}.", result.Downloaded, result.Current, result.Failed);
                if (result.Failed > 0) MessageBox.Show(_status.Text, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex) { _status.Text = "The image update did not complete."; MessageBox.Show(ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error); }
            finally { SetBusy(false); }
        }

        private void SetProgress(double percent)
        {
            _progressPercent = Math.Max(0, Math.Min(100, percent));
            _progressText.Text = String.Format("{0:0}%", _progressPercent);
            UpdateProgressAnimation();
        }

        private void UpdateProgressAnimation()
        {
            if (_progressPanel.ActualWidth <= 0 || _progressPanel.ActualHeight <= 0) return;
            const double start = 108;
            const double fireballRadius = 35;
            var end = Math.Max(start, _progressPanel.ActualWidth - fireballRadius);
            var position = start + ((_progressPercent / 100.0) * (end - start));
            _progressTrail.Width = Math.Max(0, position - start);
            Canvas.SetLeft(_progressTrail, start);
            Canvas.SetLeft(_progressFireball, position - fireballRadius);
            _progressText.Width = _progressPanel.ActualWidth;
            Canvas.SetLeft(_progressText, 0); Canvas.SetTop(_progressText, 40);
        }

        private void SetBusy(bool busy) { _sets.IsEnabled = !busy; _selected.IsEnabled = !busy && _sets.SelectedItem != null; _all.IsEnabled = !busy; }
    }

    internal static class Downloader
    {
        private const string BaseUrl = "https://raw.githubusercontent.com/JoePerry/Epic-Battles-Online/main/image-host/";
        private static readonly HttpClient Client = CreateClient();
        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("OCTGN-Epic-Battles-Image-Downloader/0.9.0");
            c.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true, NoStore = true };
            return c;
        }

        public static async Task<Catalog> LoadAsync(Game game)
        {
            var json = await Client.GetStringAsync(BaseUrl + "manifest.json?v=0.9.0.0");
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

        public static async Task<Result> UpdateAsync(IEnumerable<ImageSet> sets, IProgress<DownloadProgress> progress)
        {
            var result = new Result();
            var selectedSets = sets.ToList();
            var total = selectedSets.Sum(s => s.Images.Count);
            var processed = 0;
            if (progress != null) progress.Report(new DownloadProgress(0));
            foreach (var set in selectedSets)
            {
                Directory.CreateDirectory(set.ImageDirectory);
                foreach (var image in set.Images)
                {
                    var target = Path.Combine(set.ImageDirectory, image.CardId + ".jpg");
                    var temporary = target + ".download";
                    try
                    {
                        if (File.Exists(target) && String.Equals(Hash(File.ReadAllBytes(target)), image.Sha256, StringComparison.OrdinalIgnoreCase)) result.Current++;
                        else
                        {
                            var bytes = await Client.GetByteArrayAsync(image.Url);
                            if (!String.Equals(Hash(bytes), image.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Checksum mismatch for " + image.CardId);
                            File.WriteAllBytes(temporary, bytes);
                            if (File.Exists(target)) File.Delete(target);
                            File.Move(temporary, target); result.Downloaded++;
                        }
                    }
                    catch { if (File.Exists(temporary)) File.Delete(temporary); result.Failed++; }
                    finally
                    {
                        processed++;
                        if (progress != null) progress.Report(new DownloadProgress(total == 0 ? 100 : (processed * 100.0 / total)));
                    }
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
    internal sealed class DownloadProgress { public DownloadProgress(double percent) { Percent = percent; } public double Percent { get; private set; } }
}
