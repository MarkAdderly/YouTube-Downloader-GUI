using iNKORE.UI.WPF.TrayIcons;
using MicaWPF.Controls;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NewtonsoftJson = Newtonsoft.Json;

namespace YT_DLP_GUI
{
    public partial class MainWindow : MicaWindow
    {
        private static readonly HttpClient sharedHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static readonly HttpClient downloadHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(15)
        };

        static MainWindow()
        {
            sharedHttpClient.DefaultRequestHeaders.Add("User-Agent", "YouTubeDownloader");
            downloadHttpClient.DefaultRequestHeaders.Add("User-Agent", "YouTubeDownloader");
        }

        private bool isDownloading = false;
        private bool isUpdatingYtDlp = false;
        private Process? currentProcess = null;
        private DispatcherTimer clipboardTimer;
        private int currentMaxHeight = 2160;
        private int currentMaxAudioBitrate = 320;
        private int currentMaxSampleRate = 48000;
        private Dictionary<string, string> locMain = new Dictionary<string, string>();
        private Dictionary<string, string> locGui = new Dictionary<string, string>();
        private string latestYtDlpDownloadUrl = "";
        private string latestYtDlpVersion = "";
        private string lastErrorMessage = "";
        private int currentLoadId = 0;
        private string currentVideoTitle = "";
        private bool isCurrentPlaylist = false;
        private int currentPlaylistItemIndex = 0;
        private int currentPlaylistItemTotal = 0;
        private string currentDownloadType = "";

        public static class Localization
        {
            public static Dictionary<string, string> LoadLocalization(string language, string category)
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = $"YT_DLP_GUI.loc.{language}.json";

                using Stream? stream = assembly.GetManifestResourceStream(resourceName);

                if (stream == null)
                {
                    Properties.Settings.Default.lang = "en";
                    throw new FileNotFoundException($"Cannot find embedded localization {resourceName}.");
                }

                using StreamReader reader = new StreamReader(stream);
                var jsonContent = reader.ReadToEnd();
                var localizationData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Dictionary<string, Dictionary<string, string>>>>>(jsonContent);
                if (localizationData != null &&
                    localizationData.ContainsKey("categories") &&
                    localizationData["categories"].ContainsKey("loc") &&
                    localizationData["categories"]["loc"].ContainsKey(category))
                {
                    return localizationData["categories"]["loc"][category];
                }

                Properties.Settings.Default.lang = "en";
                throw new KeyNotFoundException($"Cannot find category '{category}' in localization {resourceName}");
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            UpdateBuildInfo();
            UpdateComboBoxItems();

            if (string.IsNullOrEmpty(Properties.Settings.Default.lang))
            {
                string systemLang = CultureInfo.CurrentUICulture.Name.ToLower();
                string isoLang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLower();

                Properties.Settings.Default.lang = systemLang switch
                {
                    "zh-tw" or "zh-hk" or "zh-mo" => "tw",
                    "zh-cn" or "zh-sg" or "zh-chs" => "zh",

                    _ => isoLang switch
                    {
                        "uk" => "uk",
                        "be" => "be",
                        "kk" => "kk",
                        "uz" => "uz",
                        "tk" => "tk",
                        "ky" => "ky",
                        "tg" => "tg",
                        "az" => "az",
                        "hy" => "hy",
                        "ka" => "ka",
                        "bg" => "bg",
                        "ro" => "ro",
                        "pl" => "pl",
                        "cs" => "cs",
                        "sk" => "sk",
                        "hu" => "hu",
                        "el" => "el",
                        "lv" => "lv",
                        "lt" => "lt",
                        "et" => "et",
                        "fi" => "fi",
                        "de" => "de",
                        "fr" => "fr",
                        "es" => "es",
                        "it" => "it",
                        "pt" => "pt",
                        "tr" => "tr",
                        "ja" => "ja",
                        "ko" => "ko",
                        "vi" => "vi",
                        "id" => "id",
                        "tl" => "tl",
                        "fil" => "tl",
                        "hi" => "hi",
                        "th" => "th",
                        "ru" => "ru",
                        _ => "en"
                    }
                };
                Properties.Settings.Default.Save();
            }

            LoadLang(Properties.Settings.Default.lang);

            if (string.IsNullOrEmpty(Properties.Settings.Default.savedFolder))
            {
                string defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "YouTube");
                Properties.Settings.Default.savedFolder = defaultPath;
                Properties.Settings.Default.Save();
            }
            SavePathTextBox.Text = Properties.Settings.Default.savedFolder;
            clipboardTimer = new DispatcherTimer();
            clipboardTimer.Interval = TimeSpan.FromSeconds(1);
            clipboardTimer.Tick += CheckClipboardText;
            clipboardTimer.Start();

            Loaded += async (s, e) =>
            {
                await RunUpdateChecksAsync();
            };
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            clipboardTimer?.Stop();
            try
            {
                if (currentProcess != null && !currentProcess.HasExited)
                {
                    currentProcess.Kill(entireProcessTree: true);
                }
            }
            catch { }
            base.OnClosing(e);
        }

        private void UpdateBuildInfo()
        {
            try
            {
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("YT_DLP_GUI.BuildNumber.txt");
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    string build = reader.ReadToEnd().Trim();
                    RemainValue.Text = $"v1.1.0 (build {build})";
                    return;
                }
            }
            catch { }
            RemainValue.Text = "v1.1.0";
        }

        public void LoadLang(string langCode)
        {
            try
            {
                locMain = Localization.LoadLocalization(langCode, "main");
                locGui = Localization.LoadLocalization(langCode, "gui");

                UrlLabel.Text = locGui["videolink"];
                SaveLabel.Text = locGui["savelocation"];
                ModeLabel.Text = locGui["savemode"];
                OptionsLabel.Text = locGui["options"];

                RadioVideo.Content = locGui["video"];
                RadioAudio.Content = locGui["audio"];

                VideoOnlyCheckBox.Content = locGui["onlyvideo"];
                OpenFolderButton.ToolTip = locGui["open"];
                SavePathTextBox.ToolTip = locMain["openfoldertitle"];
                settingsButton.Label = locGui["set"];
                updateButton.Label = locGui["update_ytdlp"];

                if (!isDownloading && !isUpdatingYtDlp)
                {
                    StatusTextBlock.Text = "";
                    StatusTextBlock.Visibility = Visibility.Collapsed;
                    OperationProgressBar.Visibility = Visibility.Collapsed;
                    DownloadButton.Content = locGui["button_start"];
                }
                else if (isDownloading)
                {
                    DownloadButton.Content = locGui["button_cancel"];
                }

                UpdateComboBoxItems();
            }
            catch { }
        }

        private string GetYtDlpPath()
        {
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
            if (File.Exists(localPath)) return localPath;
            return "yt-dlp.exe";
        }

        private string GetFfmpegDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
            string localFfmpeg = Path.Combine(baseDir, "ffmpeg.exe");
            if (File.Exists(localFfmpeg)) return baseDir;
            return "";
        }

        private static string NormalizeUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            string u = url.Trim().Trim('"', '\'');
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                u = "https://" + u;
            }
            return u;
        }

        private static bool IsPlaylistUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            string u = url.Trim().Trim('"', '\'').ToLowerInvariant();
            if (u.Contains("/playlist") || u.Contains("playlist?"))
                return true;
            if (u.Contains("list="))
            {
                if (u.Contains("list=rd") || u.Contains("list=ul"))
                    return false;
                if (!u.Contains("v=") || u.Contains("list=pl") || u.Contains("list=olak") || u.Contains("list=uu") || u.Contains("list=fl"))
                    return true;
            }
            return false;
        }

        private bool IsYouTubeUrl(string url)
        {
            string checkUrl = NormalizeUrl(url);
            if (string.IsNullOrEmpty(checkUrl)) return false;

            if (!Uri.TryCreate(checkUrl, UriKind.Absolute, out var uri))
                return false;

            string host = uri.Host.ToLowerInvariant();
            bool isYtHost = host == "youtube.com" || host.EndsWith(".youtube.com") || host == "youtu.be";
            if (!isYtHost) return false;

            string pathAndQuery = uri.PathAndQuery;
            if (string.IsNullOrEmpty(pathAndQuery) || pathAndQuery == "/")
                return false;

            return true;
        }

        private void CheckClipboardText(object? sender, EventArgs e)
        {
            bool showPasteButton = false;

            try
            {
                if (Clipboard.ContainsText())
                {
                    string txt = Clipboard.GetText().Trim();

                    if (IsYouTubeUrl(txt))
                    {
                        if (UrlTextBox.Text.Trim() != txt)
                        {
                            showPasteButton = true;
                        }
                    }
                }
            }
            catch
            {
            }

            PasteButton.Visibility = showPasteButton ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            if (Clipboard.ContainsText())
            {
                UrlTextBox.Text = Clipboard.GetText();
                UrlTextBox.CaretIndex = UrlTextBox.Text.Length;
                PasteButton.Visibility = Visibility.Collapsed;
            }
        }

        private async void UrlTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!isDownloading && !isUpdatingYtDlp)
            {
                StatusTextBlock.Text = "";
                StatusTextBlock.Visibility = Visibility.Collapsed;
                OperationProgressBar.Visibility = Visibility.Collapsed;
                OperationProgressBar.Value = 0;
                OperationProgressBar.IsIndeterminate = false;
            }

            string rawUrl = UrlTextBox.Text;
            currentVideoTitle = "";
            isCurrentPlaylist = false;
            currentPlaylistItemIndex = 0;
            currentPlaylistItemTotal = 0;

            if (IsYouTubeUrl(rawUrl))
            {
                string url = NormalizeUrl(rawUrl);
                int loadId = Interlocked.Increment(ref currentLoadId);
                PreviewPanel.Visibility = Visibility.Visible;
                PreviewTitle.Inlines.Clear();
                PreviewTitle.Text = locMain["status_info"];
                PreviewImage.Source = null;
                OperationProgressBar.Visibility = Visibility.Visible;
                OperationProgressBar.IsIndeterminate = true;

                currentMaxHeight = 2160;
                currentMaxAudioBitrate = 320;
                currentMaxSampleRate = 48000;
                UpdateComboBoxItems();

                await LoadVideoInfoAsync(url, loadId);
            }
            else
            {
                Interlocked.Increment(ref currentLoadId);
                PreviewPanel.Visibility = Visibility.Collapsed;
            }
        }

        private Task LoadVideoInfoAsync(string url, int loadId)
        {
            return Task.Run(async () =>
            {
                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = GetYtDlpPath(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                bool isPlaylistReq = IsPlaylistUrl(url);

                if (isPlaylistReq)
                {
                    info.ArgumentList.Add("--flat-playlist");
                    info.ArgumentList.Add("--dump-single-json");
                    info.ArgumentList.Add("--no-warnings");
                    info.ArgumentList.Add(url);
                }
                else
                {
                    info.ArgumentList.Add("--dump-json");
                    info.ArgumentList.Add("--no-playlist");
                    info.ArgumentList.Add("--no-warnings");
                    info.ArgumentList.Add(url);
                }

                info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                info.EnvironmentVariables["PYTHONUTF8"] = "1";

                try
                {
                    using (Process? p = Process.Start(info))
                    {
                        if (p == null) return;

                        var outTask = p.StandardOutput.ReadToEndAsync();
                        var errTask = p.StandardError.ReadToEndAsync();
                        await Task.WhenAll(outTask, errTask);
                        await p.WaitForExitAsync();

                        if (loadId != currentLoadId) return;

                        string output = outTask.Result;
                        string error = errTask.Result;

                        if (!string.IsNullOrWhiteSpace(output))
                        {
                            using (JsonDocument doc = JsonDocument.Parse(output))
                            {
                                string docType = doc.RootElement.TryGetProperty("_type", out var typeProp) && typeProp.ValueKind == JsonValueKind.String
                                    ? typeProp.GetString() ?? ""
                                    : "";
                                bool isPlaylist = isPlaylistReq || docType.Equals("playlist", StringComparison.OrdinalIgnoreCase);
                                isCurrentPlaylist = isPlaylist;

                                string title = doc.RootElement.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String
                                    ? titleElement.GetString() ?? ""
                                    : locMain["status_noname"];
                                currentVideoTitle = title;
                                string channel = doc.RootElement.TryGetProperty("uploader", out var uploaderElement) && uploaderElement.ValueKind == JsonValueKind.String
                                    ? uploaderElement.GetString() ?? ""
                                    : (doc.RootElement.TryGetProperty("channel", out var chElement) && chElement.ValueKind == JsonValueKind.String ? chElement.GetString() ?? "" : "");
                                
                                int playlistCount = 0;
                                if (isPlaylist)
                                {
                                    if (doc.RootElement.TryGetProperty("playlist_count", out var plcProp) && plcProp.ValueKind == JsonValueKind.Number)
                                    {
                                        plcProp.TryGetInt32(out playlistCount);
                                    }
                                    if (playlistCount == 0 && doc.RootElement.TryGetProperty("entries", out var entriesProp) && entriesProp.ValueKind == JsonValueKind.Array)
                                    {
                                        playlistCount = entriesProp.GetArrayLength();
                                    }
                                    currentPlaylistItemTotal = playlistCount;
                                    currentPlaylistItemIndex = 0;
                                }

                                string thumbUrl = "";
                                if (doc.RootElement.TryGetProperty("thumbnail", out var thumbElement) && thumbElement.ValueKind == JsonValueKind.String)
                                {
                                    thumbUrl = thumbElement.GetString() ?? "";
                                }
                                if (string.IsNullOrEmpty(thumbUrl) && doc.RootElement.TryGetProperty("thumbnails", out var thumbsElement) && thumbsElement.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var thumb in thumbsElement.EnumerateArray())
                                    {
                                        if (thumb.TryGetProperty("url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String)
                                        {
                                            thumbUrl = urlProp.GetString() ?? "";
                                        }
                                    }
                                }

                                int maxRes = 0;
                                int maxAbr = 0;
                                int maxAsr = 0;

                                if (isPlaylist)
                                {
                                    maxRes = 2160;
                                    maxAbr = 320;
                                    maxAsr = 48000;
                                }
                                else if (doc.RootElement.TryGetProperty("formats", out var formats) && formats.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var format in formats.EnumerateArray())
                                    {
                                        if (format.TryGetProperty("vcodec", out var vcodec) && vcodec.ValueKind == JsonValueKind.String && vcodec.GetString() != "none")
                                        {
                                            if (format.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number)
                                            {
                                                if (hProp.TryGetInt32(out int h) && h > maxRes) maxRes = h;
                                            }
                                        }

                                        if (format.TryGetProperty("acodec", out var acodec) && acodec.ValueKind == JsonValueKind.String && acodec.GetString() != "none")
                                        {
                                            if (format.TryGetProperty("abr", out var abrProp) && abrProp.ValueKind == JsonValueKind.Number)
                                            {
                                                int abr = (int)abrProp.GetDouble();
                                                if (abr > maxAbr) maxAbr = abr;
                                            }
                                            if (format.TryGetProperty("asr", out var asrProp) && asrProp.ValueKind == JsonValueKind.Number)
                                            {
                                                if (asrProp.TryGetInt32(out int asr))
                                                {
                                                    if (asr > maxAsr) maxAsr = asr;
                                                }
                                                else
                                                {
                                                    int asrF = (int)asrProp.GetDouble();
                                                    if (asrF > maxAsr) maxAsr = asrF;
                                                }
                                            }
                                        }
                                    }
                                }

                                if (loadId != currentLoadId) return;

                                Dispatcher.Invoke(() =>
                                {
                                    if (loadId != currentLoadId) return;
                                    OperationProgressBar.IsIndeterminate = false;
                                    OperationProgressBar.Visibility = Visibility.Collapsed;
                                    PreviewTitle.Inlines.Clear();
                                    PreviewTitle.Inlines.Add(new System.Windows.Documents.Run(title));

                                    string subText = channel;
                                    if (isPlaylist)
                                    {
                                        string videoUnit = locGui["video"];
                                        string countText = playlistCount > 0 ? $"{playlistCount} {videoUnit.ToLower()}" : "";
                                        if (!string.IsNullOrEmpty(channel) && !string.IsNullOrEmpty(countText))
                                        {
                                            subText = $"{channel} • {countText}";
                                        }
                                        else if (!string.IsNullOrEmpty(countText))
                                        {
                                            subText = countText;
                                        }
                                    }

                                    if (!string.IsNullOrEmpty(subText))
                                    {
                                        PreviewTitle.Inlines.Add(new System.Windows.Documents.LineBreak());
                                        var channelRun = new System.Windows.Documents.Run(subText);
                                        channelRun.FontSize = PreviewTitle.FontSize + (isPlaylist ? 2 : 4);
                                        channelRun.FontWeight = FontWeights.SemiBold;
                                        PreviewTitle.Inlines.Add(channelRun);
                                    }

                                    if (maxRes > 0) currentMaxHeight = maxRes;
                                    if (maxAbr > 0) currentMaxAudioBitrate = maxAbr;
                                    if (maxAsr > 0) currentMaxSampleRate = maxAsr;

                                    UpdateComboBoxItems();

                                    if (!string.IsNullOrEmpty(thumbUrl))
                                    {
                                        try
                                        {
                                            BitmapImage bitmap = new BitmapImage();
                                            bitmap.BeginInit();
                                            bitmap.UriSource = new Uri(thumbUrl, UriKind.Absolute);
                                            bitmap.EndInit();
                                            PreviewImage.Source = bitmap;
                                        }
                                        catch { }
                                    }
                                });
                            }
                        }
                        else
                        {
                            string errDetail = "";
                            if (!string.IsNullOrWhiteSpace(error))
                            {
                                foreach (var line in error.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                                {
                                    if (line.Contains("ERROR:"))
                                    {
                                        errDetail = line.Replace("ERROR:", "").Trim();
                                        break;
                                    }
                                }
                                if (string.IsNullOrEmpty(errDetail))
                                {
                                    errDetail = error.Trim();
                                }
                            }

                            if (loadId != currentLoadId) return;

                            Dispatcher.Invoke(() =>
                            {
                                if (loadId != currentLoadId) return;
                                string baseErr = locMain["error1"];
                                PreviewTitle.Inlines.Clear();
                                PreviewTitle.Text = string.IsNullOrEmpty(errDetail) ? baseErr : $"{baseErr} ({errDetail})";
                                OperationProgressBar.IsIndeterminate = false;
                                OperationProgressBar.Visibility = Visibility.Collapsed;
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (loadId != currentLoadId) return;

                    Dispatcher.Invoke(() =>
                    {
                        if (loadId != currentLoadId) return;
                        string baseErr = locMain["error2"];
                        PreviewTitle.Inlines.Clear();
                        PreviewTitle.Text = $"{baseErr}: {ex.Message}";
                        OperationProgressBar.IsIndeterminate = false;
                        OperationProgressBar.Visibility = Visibility.Collapsed;
                    });
                }
            });
        }

        private void SavePathTextBox_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            SelectSaveFolder();
        }

        private void SelectSaveFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = locMain["openfoldertitle"],
                InitialDirectory = Directory.Exists(SavePathTextBox.Text) ? SavePathTextBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dialog.ShowDialog() == true)
            {
                SavePathTextBox.Text = dialog.FolderName;
                Properties.Settings.Default.savedFolder = dialog.FolderName;
                Properties.Settings.Default.Save();
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            string folderPath = SavePathTextBox.Text.Trim();
            if (Directory.Exists(folderPath))
            {
                Process.Start(new ProcessStartInfo { FileName = folderPath, UseShellExecute = true, Verb = "open" });
            }
            else
            {
                string errTitle = locMain["error"];
                string errMsg = locMain["error3"];
                MicaDialogWindow.Show(this, errTitle, errMsg, "OK");
            }
        }

        private void SavePathTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            Properties.Settings.Default.savedFolder = SavePathTextBox.Text.Trim();
            Properties.Settings.Default.Save();
        }

        private void RadioMode_Checked(object sender, RoutedEventArgs e)
        {
            UpdateComboBoxItems();
        }

        private void UpdateComboBoxItems()
        {
            if (SettingsComboBox == null || ComboLabel == null || OptionsPanel == null) return;
            if (locGui.Count == 0) return;

            string prevTag = (SettingsComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
            SettingsComboBox.Items.Clear();

            if (RadioVideo.IsChecked == true)
            {
                ComboLabel.Text = locGui["quality"];
                SettingsComboBox.Items.Add(new ComboBoxItem { Content = locGui["max"], Tag = "max" });

                if (currentMaxHeight >= 2160)
                    SettingsComboBox.Items.Add(new ComboBoxItem { Content = "4K", Tag = "4k" });

                if (currentMaxHeight >= 1440)
                    SettingsComboBox.Items.Add(new ComboBoxItem { Content = "1440p", Tag = "1440" });

                if (currentMaxHeight >= 1080)
                    SettingsComboBox.Items.Add(new ComboBoxItem { Content = "1080p", Tag = "1080" });

                if (currentMaxHeight >= 720)
                    SettingsComboBox.Items.Add(new ComboBoxItem { Content = "720p", Tag = "720" });

                if (currentMaxHeight < 720)
                {
                    if (currentMaxHeight >= 480)
                        SettingsComboBox.Items.Add(new ComboBoxItem { Content = "480p", Tag = "480" });

                    if (currentMaxHeight >= 360)
                        SettingsComboBox.Items.Add(new ComboBoxItem { Content = "360p", Tag = "360" });
                }

                OptionsPanel.Visibility = Visibility.Visible;
            }
            else if (RadioAudio.IsChecked == true)
            {
                ComboLabel.Text = locGui["extension"];
                string maxLabel = locGui["max"];
                string origLabel = locGui["audio_orig"];

                SettingsComboBox.Items.Add(new ComboBoxItem { Content = $"MP3 - {maxLabel}", Tag = "mp3_max" });

                if (currentMaxAudioBitrate >= 250)
                    SettingsComboBox.Items.Add(new ComboBoxItem { Content = "MP3 - 320 kbps", Tag = "mp3_320" });

                if (currentMaxAudioBitrate >= 190)
                    SettingsComboBox.Items.Add(new ComboBoxItem { Content = "MP3 - 240 kbps", Tag = "mp3_240" });

                if (currentMaxAudioBitrate >= 140)
                    SettingsComboBox.Items.Add(new ComboBoxItem { Content = "MP3 - 192 kbps", Tag = "mp3_192" });

                if (currentMaxAudioBitrate >= 90)
                    SettingsComboBox.Items.Add(new ComboBoxItem { Content = "MP3 - 128 kbps", Tag = "mp3_128" });

                SettingsComboBox.Items.Add(new ComboBoxItem { Content = $"WAV - {maxLabel}", Tag = "wav_max" });

                if (currentMaxSampleRate >= 48000)
                    SettingsComboBox.Items.Add(new ComboBoxItem { Content = "WAV - 48 kHz", Tag = "wav_48k" });

                if (currentMaxSampleRate >= 44100)
                    SettingsComboBox.Items.Add(new ComboBoxItem { Content = "WAV - 44.1 kHz", Tag = "wav_44k" });

                SettingsComboBox.Items.Add(new ComboBoxItem { Content = $"M4A - {origLabel}", Tag = "m4a" });

                OptionsPanel.Visibility = Visibility.Collapsed;
            }

            int selectedIndex = 0;
            for (int i = 0; i < SettingsComboBox.Items.Count; i++)
            {
                if ((SettingsComboBox.Items[i] as ComboBoxItem)?.Tag?.ToString() == prevTag)
                {
                    selectedIndex = i;
                    break;
                }
            }
            SettingsComboBox.SelectedIndex = selectedIndex;
        }

        private async void DownloadButton_Click(object sender, RoutedEventArgs e)
        {
            if (isUpdatingYtDlp) return;

            if (isDownloading)
            {
                try
                {
                    if (currentProcess != null && !currentProcess.HasExited)
                    {
                        currentProcess.Kill(entireProcessTree: true);
                        StatusTextBlock.Text = locMain["status_canceled"];
                        StatusTextBlock.Visibility = Visibility.Visible;
                        OperationProgressBar.Visibility = Visibility.Collapsed;
                    }
                }
                catch { }

                ResetUiState();
                return;
            }

            string url = NormalizeUrl(UrlTextBox.Text);
            if (string.IsNullOrEmpty(url) || !IsYouTubeUrl(url))
            {
                string errTitle = locMain["error"];
                string errPaste = locMain["pastefirst"];
                MicaDialogWindow.Show(this, errTitle, errPaste, "OK");
                return;
            }

            string saveDir = SavePathTextBox.Text.Trim();
            if (string.IsNullOrEmpty(saveDir)) return;

            if (!Directory.Exists(saveDir))
            {
                try { Directory.CreateDirectory(saveDir); }
                catch { return; }
            }

            isDownloading = true;
            lastErrorMessage = "";
            currentDownloadType = "";
            DownloadButton.Content = locGui["button_cancel"];
            OperationProgressBar.Visibility = Visibility.Visible;
            OperationProgressBar.IsIndeterminate = true;
            OperationProgressBar.Value = 0;
            StatusTextBlock.Visibility = Visibility.Visible;
            StatusTextBlock.Text = locMain["status_preparing"];

            var selectedSetting = (ComboBoxItem)SettingsComboBox.SelectedItem;
            string tag = selectedSetting?.Tag?.ToString() ?? "max";
            bool isVideoOnly = VideoOnlyCheckBox.IsChecked == true;

            var args = new List<string>();

            bool isPlaylist = isCurrentPlaylist || IsPlaylistUrl(url);

            if (isPlaylist)
            {
                args.Add("--yes-playlist");
                args.Add("--ignore-errors");
            }
            else
            {
                args.Add("--no-playlist");
            }

            args.Add("--windows-filenames");

            string ffmpegDir = GetFfmpegDir();
            if (!string.IsNullOrEmpty(ffmpegDir))
            {
                args.Add("--ffmpeg-location");
                args.Add(ffmpegDir);
            }

            if (RadioVideo.IsChecked == true)
            {
                args.Add("--embed-thumbnail");
                args.Add("--convert-thumbnails");
                args.Add("png");
                args.Add("--postprocessor-args");
                args.Add("ThumbnailsConvertor:-vf pad=iw*23/20:ih*23/20:(ow-iw)/2:(oh-ih)/2:black");
                string dlFormat = "";
                if (isVideoOnly)
                {
                    dlFormat = tag switch
                    {
                        "4k" => "bestvideo[height<=2160]/best[height<=2160]",
                        "1440" => "bestvideo[height<=1440]/best[height<=1440]",
                        "1080" => "bestvideo[height<=1080]/best[height<=1080]",
                        "720" => "bestvideo[height<=720]/best[height<=720]",
                        "480" => "bestvideo[height<=480]/best[height<=480]",
                        "360" => "bestvideo[height<=360]/best[height<=360]",
                        _ => "bestvideo/best"
                    };
                }
                else
                {
                    dlFormat = tag switch
                    {
                        "4k" => "bestvideo[height<=2160]+bestaudio[ext=m4a]/bestvideo[height<=2160]+bestaudio/best[height<=2160]",
                        "1440" => "bestvideo[height<=1440]+bestaudio[ext=m4a]/bestvideo[height<=1440]+bestaudio/best[height<=1440]",
                        "1080" => "bestvideo[height<=1080]+bestaudio[ext=m4a]/bestvideo[height<=1080]+bestaudio/best[height<=1080]",
                        "720" => "bestvideo[height<=720]+bestaudio[ext=m4a]/bestvideo[height<=720]+bestaudio/best[height<=720]",
                        "480" => "bestvideo[height<=480]+bestaudio[ext=m4a]/bestvideo[height<=480]+bestaudio/best[height<=480]",
                        "360" => "bestvideo[height<=360]+bestaudio[ext=m4a]/bestvideo[height<=360]+bestaudio/best[height<=360]",
                        _ => "bestvideo+bestaudio[ext=m4a]/bestvideo+bestaudio/best"
                    };
                }

                args.Add("-f");
                args.Add(dlFormat);

                if (isVideoOnly)
                {
                    args.Add("--remux-video");
                    args.Add("mp4");
                }
                else
                {
                    args.Add("--merge-output-format");
                    args.Add("mp4");
                }
            }
            else
            {
                if (!tag.StartsWith("wav"))
                {
                    args.Add("--embed-thumbnail");
                }

                args.Add("-f");
                args.Add(tag == "m4a" ? "bestaudio[ext=m4a]/bestaudio/best" : "bestaudio/best");
                args.Add("-x");

                if (tag == "mp3_max")
                {
                    args.Add("--audio-format");
                    args.Add("mp3");
                    args.Add("--audio-quality");
                    args.Add("0");
                }
                else if (tag == "mp3_320")
                {
                    args.Add("--audio-format");
                    args.Add("mp3");
                    args.Add("--audio-quality");
                    args.Add("320K");
                }
                else if (tag == "mp3_240")
                {
                    args.Add("--audio-format");
                    args.Add("mp3");
                    args.Add("--audio-quality");
                    args.Add("240K");
                }
                else if (tag == "mp3_192")
                {
                    args.Add("--audio-format");
                    args.Add("mp3");
                    args.Add("--audio-quality");
                    args.Add("192K");
                }
                else if (tag == "mp3_128")
                {
                    args.Add("--audio-format");
                    args.Add("mp3");
                    args.Add("--audio-quality");
                    args.Add("128K");
                }
                else if (tag == "wav_max")
                {
                    args.Add("--audio-format");
                    args.Add("wav");
                }
                else if (tag == "wav_48k")
                {
                    args.Add("--audio-format");
                    args.Add("wav");
                    args.Add("--postprocessor-args");
                    args.Add("ExtractAudio:-ar 48000");
                }
                else if (tag == "wav_44k")
                {
                    args.Add("--audio-format");
                    args.Add("wav");
                    args.Add("--postprocessor-args");
                    args.Add("ExtractAudio:-ar 44100");
                }
                else if (tag == "m4a")
                {
                    args.Add("--audio-format");
                    args.Add("m4a");
                }
            }

            string expectedExt = "mp4";
            if (RadioAudio.IsChecked == true)
            {
                if (tag.StartsWith("mp3")) expectedExt = "mp3";
                else if (tag.StartsWith("wav")) expectedExt = "wav";
                else if (tag == "m4a") expectedExt = "m4a";
                else expectedExt = "mp3";
            }
            else
            {
                expectedExt = "mp4";
            }

            if (isPlaylist)
            {
                string playlistOutput = Path.Combine(saveDir, "%(playlist_title|playlist)s", "%(title)s.%(ext)s");
                args.Add("-o");
                args.Add(playlistOutput);
            }
            else
            {
                string titleToUse = !string.IsNullOrWhiteSpace(currentVideoTitle) ? currentVideoTitle : PreviewTitle.Text;
                if (!string.IsNullOrEmpty(titleToUse))
                {
                    var lines = titleToUse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    if (lines.Length > 0) titleToUse = lines[0].Trim();
                }

                int dupIndex = GetNextDuplicateIndex(saveDir, titleToUse, expectedExt);
                string outputPath = dupIndex == 0
                    ? Path.Combine(saveDir, "%(title)s.%(ext)s")
                    : Path.Combine(saveDir, $"%(title)s ({dupIndex}).%(ext)s");
                args.Add("-o");
                args.Add(outputPath);
            }
            args.Add(url);

            int exitCode = await RunYtDlpAsync(args);

            if (isDownloading)
            {
                if (exitCode == 0)
                {
                    StatusTextBlock.Visibility = Visibility.Visible;
                    StatusTextBlock.Text = locMain["status_done"];
                    OperationProgressBar.Visibility = Visibility.Visible;
                    OperationProgressBar.Value = 100;
                }
                else
                {
                    string errPrefix = locMain["error"];
                    if (!string.IsNullOrEmpty(lastErrorMessage))
                    {
                        StatusTextBlock.Text = $"{errPrefix}: {lastErrorMessage}";
                    }
                    else
                    {
                        StatusTextBlock.Text = errPrefix;
                    }
                    StatusTextBlock.Visibility = Visibility.Visible;
                    OperationProgressBar.Visibility = Visibility.Collapsed;
                    OperationProgressBar.Value = 0;
                }
                ResetUiState();
            }
        }

        private Task<int> RunYtDlpAsync(List<string> argumentList)
        {
            return Task.Run(async () =>
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = GetYtDlpPath(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                foreach (var arg in argumentList)
                {
                    startInfo.ArgumentList.Add(arg);
                }

                startInfo.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
                startInfo.EnvironmentVariables["PYTHONUTF8"] = "1";

                currentProcess = new Process { StartInfo = startInfo };
                currentProcess.OutputDataReceived += Process_OutputDataReceived;
                currentProcess.ErrorDataReceived += Process_ErrorDataReceived;

                int exitCode = -1;
                try
                {
                    currentProcess.Start();
                    currentProcess.BeginOutputReadLine();
                    currentProcess.BeginErrorReadLine();
                    await currentProcess.WaitForExitAsync();
                    exitCode = currentProcess.ExitCode;
                }
                catch (Exception ex)
                {
                    lastErrorMessage = ex.Message;
                }
                finally
                {
                    currentProcess?.Dispose();
                    currentProcess = null;
                }

                return exitCode;
            });
        }

        private void ResetUiState()
        {
            isDownloading = false;
            DownloadButton.Content = locGui["button_start"];
            OperationProgressBar.IsIndeterminate = false;
        }

        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Dispatcher.Invoke(() =>
                {
                    if (e.Data.Contains("[download]"))
                    {
                        OperationProgressBar.IsIndeterminate = false;

                        Match itemMatch = Regex.Match(e.Data, @"Downloading item (\d+) of (\d+)");
                        if (itemMatch.Success)
                        {
                            int.TryParse(itemMatch.Groups[1].Value, out currentPlaylistItemIndex);
                            int.TryParse(itemMatch.Groups[2].Value, out currentPlaylistItemTotal);
                            string dlStr = locMain["status_downloading"];
                            StatusTextBlock.Text = $"[{currentPlaylistItemIndex}/{currentPlaylistItemTotal}] {dlStr}";
                            return;
                        }

                        if (e.Data.Contains("Destination:"))
                        {
                            if (RadioVideo.IsChecked == true && VideoOnlyCheckBox.IsChecked != true)
                            {
                                string lower = e.Data.ToLowerInvariant();
                                if (lower.Contains(".m4a") || lower.Contains(".opus") || lower.Contains(".aac") || lower.Contains(".f140") || lower.Contains(".f251") || lower.Contains(".ba.") || lower.Contains(".ba_"))
                                {
                                    currentDownloadType = "audio";
                                }
                                else
                                {
                                    currentDownloadType = "video";
                                }
                            }
                            else if (RadioAudio.IsChecked == true)
                            {
                                currentDownloadType = "audio";
                            }
                            else if (RadioVideo.IsChecked == true && VideoOnlyCheckBox.IsChecked == true)
                            {
                                currentDownloadType = "video";
                            }
                            else
                            {
                                currentDownloadType = "";
                            }

                            string dlStr = currentDownloadType switch
                            {
                                "video" => locMain["status_downloading_video"].Trim(),
                                "audio" => locMain["status_downloading_audio"].Trim(),
                                _ => locMain["status_downloading"]
                            };

                            if (isCurrentPlaylist && currentPlaylistItemTotal > 0)
                            {
                                StatusTextBlock.Text = $"[{currentPlaylistItemIndex}/{currentPlaylistItemTotal}] {dlStr}";
                            }
                            else
                            {
                                StatusTextBlock.Text = dlStr;
                            }
                            return;
                        }

                        Match match = Regex.Match(e.Data, @"(\d+\.\d+)%");
                        if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double percent))
                        {
                            OperationProgressBar.Value = percent;
                            string fetchPrefix = currentDownloadType switch
                            {
                                "video" => locMain["status_downloading_video"],
                                "audio" => locMain["status_downloading_audio"],
                                _ => locMain["status_fetching"]
                            };

                            if (isCurrentPlaylist && currentPlaylistItemTotal > 0)
                            {
                                StatusTextBlock.Text = $"[{currentPlaylistItemIndex}/{currentPlaylistItemTotal}] {fetchPrefix}{percent}%";
                            }
                            else
                            {
                                StatusTextBlock.Text = $"{fetchPrefix}{percent}%";
                            }
                        }
                        else if (e.Data.Contains("has already been downloaded"))
                        {
                            if (isCurrentPlaylist && currentPlaylistItemTotal > 0)
                            {
                                OperationProgressBar.Value = 100;
                                string doneStr = locMain["status_done"];
                                StatusTextBlock.Text = $"[{currentPlaylistItemIndex}/{currentPlaylistItemTotal}] {doneStr}";
                            }
                        }
                    }
                    else if (e.Data.Contains("[Merger]") ||
                             e.Data.Contains("[ExtractAudio]") ||
                             e.Data.Contains("[VideoConvertor]") ||
                             e.Data.Contains("[FixupMux]") ||
                             e.Data.Contains("[ModifyChapters]") ||
                             e.Data.Contains("[ThumbnailsConvertor]") ||
                             e.Data.Contains("[EmbedThumbnail]"))
                    {
                        OperationProgressBar.IsIndeterminate = true;
                        string ffmpegStr = locMain["status_ffmpeg"];
                        if (isCurrentPlaylist && currentPlaylistItemTotal > 0)
                        {
                            StatusTextBlock.Text = $"[{currentPlaylistItemIndex}/{currentPlaylistItemTotal}] {ffmpegStr}";
                        }
                        else
                        {
                            StatusTextBlock.Text = ffmpegStr;
                        }
                    }
                });
            }
        }

        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                if (e.Data.Contains("ERROR:") || e.Data.Contains("error:"))
                {
                    lastErrorMessage = e.Data.Replace("ERROR: ", "").Trim();
                }
            }
        }

        private int GetNextDuplicateIndex(string saveDir, string title, string ext)
        {
            if (string.IsNullOrWhiteSpace(title) || !Directory.Exists(saveDir))
                return 0;

            string normTitle = NormalizeForComparison(title);
            if (string.IsNullOrEmpty(normTitle))
                return 0;

            try
            {
                string extFilter = "." + ext.TrimStart('.').ToLowerInvariant();
                var files = Directory.GetFiles(saveDir);
                var usedNumbers = new HashSet<int>();
                var dupRegex = new Regex(@"^(.*?)\s*\((\d+)\)$");

                foreach (var f in files)
                {
                    string fName = Path.GetFileName(f);
                    if (!fName.EndsWith(extFilter, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string nameWithoutExt = fName.Substring(0, fName.Length - extFilter.Length);

                    if (NormalizeForComparison(nameWithoutExt) == normTitle)
                    {
                        usedNumbers.Add(0);
                        continue;
                    }

                    var m = dupRegex.Match(nameWithoutExt);
                    if (m.Success)
                    {
                        string basePart = m.Groups[1].Value;
                        if (int.TryParse(m.Groups[2].Value, out int num))
                        {
                            if (NormalizeForComparison(basePart) == normTitle)
                            {
                                usedNumbers.Add(num);
                            }
                        }
                    }
                }

                if (!usedNumbers.Contains(0))
                    return 0;

                int nextIndex = 1;
                while (usedNumbers.Contains(nextIndex))
                {
                    nextIndex++;
                }
                return nextIndex;
            }
            catch
            {
                return 0;
            }
        }

        private static string NormalizeForComparison(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var slideOut = new DoubleAnimation(-500, TimeSpan.FromMilliseconds(350)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            var slideIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };

            MainTranslate.BeginAnimation(TranslateTransform.XProperty, slideOut);
            SettingsTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }

        public void CloseSettingsAnimation()
        {
            var slideOut = new DoubleAnimation(500, TimeSpan.FromMilliseconds(350)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
            var slideIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(350)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };

            SettingsTranslate.BeginAnimation(TranslateTransform.XProperty, slideOut);
            MainTranslate.BeginAnimation(TranslateTransform.XProperty, slideIn);
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            await DownloadOrUpdateYtDlpAsync();
        }

        private async Task DownloadOrUpdateYtDlpAsync()
        {
            if (isDownloading || isUpdatingYtDlp) return;

            isUpdatingYtDlp = true;
            DownloadButton.IsEnabled = false;
            updateButton.IsEnabled = false;
            OperationProgressBar.IsIndeterminate = false;
            OperationProgressBar.Value = 0;

            string downloadUrl = !string.IsNullOrEmpty(latestYtDlpDownloadUrl)
                ? latestYtDlpDownloadUrl
                : "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";

            string destPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
            string tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe.download");

            string downloadingPrefix = locMain["ytdlp_downloading"];
            StatusTextBlock.Visibility = Visibility.Visible;
            StatusTextBlock.Text = $"{downloadingPrefix}0%";
            OperationProgressBar.Visibility = Visibility.Visible;

            bool success = false;
            try
            {
                using (var response = await downloadHttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                    {
                        var buffer = new byte[65536];
                        long totalRead = 0;
                        int bytesRead;
                        int lastReportedPercent = -1;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                int percent = (int)((double)totalRead / totalBytes * 100);
                                if (percent != lastReportedPercent)
                                {
                                    lastReportedPercent = percent;
                                    OperationProgressBar.Value = percent;
                                    StatusTextBlock.Text = $"{downloadingPrefix}{percent}%";
                                }
                            }
                            else
                            {
                                OperationProgressBar.IsIndeterminate = true;
                            }
                        }
                    }
                }

                if (File.Exists(tempPath))
                {
                    if (File.Exists(destPath))
                    {
                        string backupPath = destPath + ".bak";
                        if (File.Exists(backupPath)) File.Delete(backupPath);
                        try
                        {
                            File.Replace(tempPath, destPath, backupPath);
                            try { File.Delete(backupPath); } catch { }
                        }
                        catch
                        {
                            File.Copy(tempPath, destPath, true);
                            try { File.Delete(tempPath); } catch { }
                        }
                    }
                    else
                    {
                        File.Move(tempPath, destPath);
                    }
                    success = true;
                }
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
                success = false;
            }
            finally
            {
                isUpdatingYtDlp = false;
                DownloadButton.IsEnabled = true;
                updateButton.IsEnabled = true;
            }

            if (success)
            {
                updateButton.Visibility = Visibility.Collapsed;
                OperationProgressBar.IsIndeterminate = false;
                OperationProgressBar.Value = 100;
                StatusTextBlock.Text = locMain["ytdlp_done"];
                await Task.Delay(3000);
                StatusTextBlock.Text = "";
                StatusTextBlock.Visibility = Visibility.Collapsed;
                OperationProgressBar.Visibility = Visibility.Collapsed;
                OperationProgressBar.Value = 0;
            }
            else
            {
                OperationProgressBar.IsIndeterminate = false;
                StatusTextBlock.Text = locMain["ytdlp_error"];
            }
        }

        private async Task RunUpdateChecksAsync()
        {
            await CheckForUpd();
            await CheckForYtDlpUpd();
        }

        private static bool IsNewerVersion(string latest, string current)
        {
            if (string.IsNullOrWhiteSpace(latest)) return false;
            if (string.IsNullOrWhiteSpace(current)) return true;

            string l = latest.Trim().TrimStart('v', 'V');
            string c = current.Trim().TrimStart('v', 'V');
            if (string.Equals(l, c, StringComparison.OrdinalIgnoreCase)) return false;

            if (Version.TryParse(l, out var vLatest) && Version.TryParse(c, out var vCurrent))
            {
                return vLatest > vCurrent;
            }

            var lParts = l.Split('.', '-', '_');
            var cParts = c.Split('.', '-', '_');
            int len = Math.Min(lParts.Length, cParts.Length);
            for (int i = 0; i < len; i++)
            {
                if (int.TryParse(lParts[i], out int lNum) && int.TryParse(cParts[i], out int cNum))
                {
                    if (lNum != cNum) return lNum > cNum;
                }
                else
                {
                    int cmp = string.Compare(lParts[i], cParts[i], StringComparison.OrdinalIgnoreCase);
                    if (cmp != 0) return cmp > 0;
                }
            }
            return lParts.Length > cParts.Length;
        }

        private async Task CheckForYtDlpUpd()
        {
            string localVersion = "";
            try
            {
                string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "yt-dlp.exe");
                if (File.Exists(localPath))
                {
                    using var proc = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = localPath,
                            Arguments = "--version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            CreateNoWindow = true
                        }
                    };
                    proc.Start();
                    localVersion = (await proc.StandardOutput.ReadToEndAsync()).Trim();
                    await proc.WaitForExitAsync();
                }
            }
            catch { }

            string apiUrl = "https://api.github.com/repos/yt-dlp/yt-dlp/releases/latest";
            try
            {
                var response = await sharedHttpClient.GetAsync(apiUrl);
                if (response.IsSuccessStatusCode)
                {
                    string jsonStr = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonStr);
                    if (doc.RootElement.TryGetProperty("tag_name", out var tagProp))
                    {
                        latestYtDlpVersion = tagProp.GetString() ?? "";
                    }

                    if (doc.RootElement.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            if (asset.TryGetProperty("name", out var nameProp) && nameProp.GetString() == "yt-dlp.exe")
                            {
                                if (asset.TryGetProperty("browser_download_url", out var dlProp))
                                {
                                    latestYtDlpDownloadUrl = dlProp.GetString() ?? "";
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(latestYtDlpDownloadUrl))
            {
                latestYtDlpDownloadUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
            }

            bool hasUpdate = false;
            if (string.IsNullOrEmpty(localVersion))
            {
                hasUpdate = true;
            }
            else if (!string.IsNullOrEmpty(latestYtDlpVersion))
            {
                hasUpdate = IsNewerVersion(latestYtDlpVersion, localVersion);
            }

            if (hasUpdate)
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    updateButton.Visibility = Visibility.Visible;

                    string title = locMain.TryGetValue("ytdlp_update_title", out var tVal) ? tVal : "Обновление yt-dlp";
                    string template = locMain.TryGetValue("ytdlp_update_dialog", out var tmplVal) ? tmplVal : "Вышла новая версия библиотеки yt-dlp ({0}). Обновить её прямо сейчас?";
                    string msg = string.Format(template, string.IsNullOrEmpty(latestYtDlpVersion) ? "latest" : latestYtDlpVersion);
                    string btnUpdate = locMain.TryGetValue("updatebutton", out var bUpd) ? bUpd : "Обновить";
                    string btnCancel = locMain.TryGetValue("updatecancel", out var bCan) ? bCan : "Отложить";

                    try
                    {
                        if (MicaDialogWindow.Show(this, title, msg, btnUpdate, btnCancel))
                        {
                            await DownloadOrUpdateYtDlpAsync();
                        }
                    }
                    catch { }
                });
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    updateButton.Visibility = Visibility.Collapsed;
                });
            }
        }

        private async Task CheckForUpd()
        {
            int thisBuild = 0;
            try
            {
                var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("YT_DLP_GUI.BuildNumber.txt");
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream))
                    {
                        string txt = reader.ReadToEnd().Trim();
                        int.TryParse(txt, out thisBuild);
                    }
                }
            }
            catch { thisBuild = 1; }

            string url = $"https://raw.githubusercontent.com/MarkAdderly/YouTube-Downloader-GUI/main/ver.json?_={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                HttpResponseMessage response = await sharedHttpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();
                string jsonString = await response.Content.ReadAsStringAsync();
                var jsonData = NewtonsoftJson.JsonConvert.DeserializeObject<Dictionary<string, string>>(jsonString);

                if (jsonData != null && jsonData.TryGetValue("build", out var buildVal) && int.TryParse(buildVal.Trim(), out int latestBuild))
                {
                    if (latestBuild > thisBuild)
                    {
                        await Dispatcher.InvokeAsync(async () =>
                        {
                            string title = "YouTube Downloader";
                            string msg = locMain.TryGetValue("updatedialog", out var dMsg) ? dMsg : "Доступно обновление YouTube Downloader.\nРекомендуется установить актуальную версию.";
                            string btnUpdate = locMain.TryGetValue("updatebutton", out var bUpd) ? bUpd : "Обновить";
                            string btnCancel = locMain.TryGetValue("updatecancel", out var bCan) ? bCan : "Отложить";

                            try
                            {
                                if (MicaDialogWindow.Show(this, title, msg, btnUpdate, btnCancel))
                                {
                                    Process.Start(new ProcessStartInfo("https://adderly.top/YouTubeDownloader") { UseShellExecute = true });
                                }
                            }
                            catch { }
                        });
                    }
                }
            }
            catch { }
        }
    }
}
