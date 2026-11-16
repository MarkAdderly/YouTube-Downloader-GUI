using System;
using System.Windows;
using MicaWPF.Controls;

namespace YT_DLP_GUI
{
    public partial class MicaDialogWindow : MicaWindow
    {
        public bool Result { get; private set; } = false;

        public MicaDialogWindow(Window? owner, string title, string message, string primaryButtonText, string? secondaryButtonText = null)
        {
            InitializeComponent();
            if (owner != null && owner.IsVisible)
            {
                try
                {
                    Owner = owner;
                    WindowStartupLocation = WindowStartupLocation.CenterOwner;
                }
                catch
                {
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            Title = title;
            DialogTitleText.Text = title;
            DialogMessageText.Text = message;
            PrimaryButton.Content = primaryButtonText;

            if (string.IsNullOrWhiteSpace(secondaryButtonText))
            {
                SecondaryButton.Visibility = Visibility.Collapsed;
                PrimaryButton.IsCancel = true;
            }
            else
            {
                SecondaryButton.Content = secondaryButtonText;
            }

            Loaded += (s, e) =>
            {
                if (SecondaryButton.Visibility == Visibility.Visible)
                {
                    double w = Math.Max(Math.Max(PrimaryButton.ActualWidth, SecondaryButton.ActualWidth), 110);
                    PrimaryButton.Width = w;
                    SecondaryButton.Width = w;
                }
            };
        }

        public static bool Show(Window? owner, string title, string message, string primaryButtonText, string? secondaryButtonText = null)
        {
            var dlg = new MicaDialogWindow(owner, title, message, primaryButtonText, secondaryButtonText);
            dlg.ShowDialog();
            return dlg.Result;
        }

        private void PrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            DialogResult = true;
            Close();
        }

        private void SecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
            DialogResult = false;
            Close();
        }
    }
}
