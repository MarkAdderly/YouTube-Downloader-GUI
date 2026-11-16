using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace YT_DLP_GUI
{
    public partial class SettingsPage : Page
    {
        private bool isLoaded = false;

        public SettingsPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            string currentLang = Properties.Settings.Default.lang;
            foreach (ComboBoxItem item in LanguageComboBox.Items)
            {
                if (item.Tag?.ToString() == currentLang)
                {
                    LanguageComboBox.SelectedItem = item;
                    break;
                }
            }

            if (LanguageComboBox.SelectedIndex == -1)
                LanguageComboBox.SelectedIndex = 0;

            isLoaded = true;
            relang();
        }

        private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isLoaded) return;

            if (LanguageComboBox.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                Properties.Settings.Default.lang = selectedItem.Tag.ToString();
                Properties.Settings.Default.Save();

                relang();

                if (Application.Current.MainWindow is MainWindow mainWindow && !string.IsNullOrEmpty(Properties.Settings.Default.lang))
                {
                    mainWindow.LoadLang(Properties.Settings.Default.lang);
                }
            }
        }

        private void relang()
        {
            try
            {
                var locSet = MainWindow.Localization.LoadLocalization(Properties.Settings.Default.lang, "settings");

                SettingsTitle.Text = locSet["label"];
                backbutton.Label = locSet["back"];
                LangLabel.Text = locSet["lang"];
                CredLabel.Text = locSet["cred"];
                CrednikLabel.Text = locSet["credNik"];
                creditthxlabel.Text = locSet["credthx"];
                SiteButton.Content = locSet["sitebutton"];
                GitHubRepo.Content = locSet["githubrepo"];

                if (DlpCreditLabel != null)
                {
                    DlpCreditLabel.Text = $"{locSet["dlpcredit"]} | YouTube Downloader";
                }
            }
            catch { }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.CloseSettingsAnimation();
        }

        private void OpenBrowserLink(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"{ex.Message}", "YouTube Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SiteButton_Click(object sender, RoutedEventArgs e) => OpenBrowserLink("https://adderly.top/");
        private void Image_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => OpenBrowserLink("https://boosty.to/adderly");
        private void Image_MouseLeftButtonUp_2(object sender, MouseButtonEventArgs e) => OpenBrowserLink("https://t.me/adderly324");
        private void Image_MouseLeftButtonUp_3(object sender, MouseButtonEventArgs e) => OpenBrowserLink("https://youtube.com/@MakuAdarii");
        private void GitHubRepo_Click(object sender, RoutedEventArgs e) => OpenBrowserLink("https://github.com/MarkAdderly/YouTube-Downloader-GUI");
    }
}