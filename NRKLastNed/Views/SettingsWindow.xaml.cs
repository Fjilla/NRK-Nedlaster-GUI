using System;
using System.Windows;
using System.Collections.Generic;
using Microsoft.Win32;
using NRKLastNed.Models;
using NRKLastNed.Services;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;

namespace NRKLastNed.Views
{
    public partial class SettingsWindow : Window
    {
        private AppSettings _settings;
        private UpdateService _toolUpdateService;
        private AppUpdateService _appUpdateService;

        private AppUpdateService.AppUpdateInfo _pendingAppUpdate;

        public SettingsWindow()
        {
            InitializeComponent();
            _settings = AppSettings.Load();
            _toolUpdateService = new UpdateService();
            _appUpdateService = new AppUpdateService();

            InitializeUI();

            _ = CheckVersionsAsync();
        }

        private void InitializeUI()
        {
            cmbResolution.ItemsSource = new List<string> { "2160", "1440", "1080", "720", "540", "480", "best" };
            cmbTheme.ItemsSource = new List<string> { "System", "Light", "Dark" };
            cmbLogLevel.ItemsSource = Enum.GetValues(typeof(LogLevel));

            txtOutput.Text = _settings.OutputFolder;
            txtTemp.Text = _settings.TempFolder;
            chkUseSystemTemp.IsChecked = _settings.UseSystemTemp;
            pnlCustomTemp.IsEnabled = !_settings.UseSystemTemp;

            cmbResolution.SelectedItem = _settings.DefaultResolution;
            cmbTheme.SelectedItem = _settings.AppTheme;

            chkEnableLog.IsChecked = _settings.EnableLogging;
            cmbLogLevel.SelectedItem = _settings.LogLevel;
        }

        private async Task CheckVersionsAsync()
        {
            // 1. Sjekk APP oppdatering
            lblAppVersion.Text = "Sjekker...";
            btnAppUpdate.IsEnabled = false;

            // #if DEBUG er en "preprocessor directive". 
            // Denne koden kjøres KUN når du utvikler i Visual Studio (Debug configuration).
            // Når du lager en ferdig versjon (Release), fjernes denne koden automatisk.
#if DEBUG
            lblAppVersion.Text = "Dev Mode (Deaktivert)";
            btnAppUpdate.IsEnabled = false;
            btnAppUpdate.ToolTip = "Oppdatering er deaktivert i utviklermodus for å hindre overskriving av lokale filer.";
#else
            // Denne koden kjøres kun i den ferdige appen (Release)
            _pendingAppUpdate = await _appUpdateService.CheckForAppUpdatesAsync();

            if (_pendingAppUpdate.IsNewVersionAvailable)
            {
                lblAppVersion.Text = $"Ny versjon: {_pendingAppUpdate.LatestVersion}";
                btnAppUpdate.IsEnabled = true;
                btnAppUpdate.ToolTip = "Klikk for å laste ned og installere ny versjon.";
            }
            else
            {
                lblAppVersion.Text = "Oppdatert";
                btnAppUpdate.IsEnabled = false;
                btnAppUpdate.ToolTip = null;
            }
#endif

            // 2. Sjekk yt-dlp versjon
            string ytVersion = await _toolUpdateService.GetYtDlpVersionAsync();
            lblYtDlpVersion.Text = $"Installert versjon: {ytVersion}";
        }

        private async void AppUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingAppUpdate == null || !_pendingAppUpdate.IsNewVersionAvailable) return;

            var res = MessageBox.Show($"En ny versjon ({_pendingAppUpdate.LatestVersion}) er tilgjengelig!\n\nEndringer:\n{_pendingAppUpdate.ReleaseNotes}\n\nVil du laste ned og oppdatere nå?",
                                      "Oppdatering", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (res == MessageBoxResult.Yes)
            {
                btnAppUpdate.IsEnabled = false;
                lblAppVersion.Text = "Laster ned...";
                await _appUpdateService.PerformAppUpdateAsync(_pendingAppUpdate);
            }
        }

        private async void UpdateYtDlp_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null) btn.IsEnabled = false;

            lblYtDlpVersion.Text = "Oppdaterer... vennligst vent.";

            string result = await _toolUpdateService.UpdateYtDlpAsync();

            MessageBox.Show(result, "Oppdatering Status", MessageBoxButton.OK, MessageBoxImage.Information);

            string ytVersion = await _toolUpdateService.GetYtDlpVersionAsync();
            lblYtDlpVersion.Text = $"Installert versjon: {ytVersion}";

            if (btn != null) btn.IsEnabled = true;
        }

        private void GetFfmpeg_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://ffmpeg.org/download.html",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void BrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true) txtOutput.Text = dialog.FolderName;
        }

        private void BrowseTemp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true) txtTemp.Text = dialog.FolderName;
        }

        private void chkUseSystemTemp_Checked(object sender, RoutedEventArgs e) => pnlCustomTemp.IsEnabled = false;
        private void chkUseSystemTemp_Unchecked(object sender, RoutedEventArgs e) => pnlCustomTemp.IsEnabled = true;

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _settings.OutputFolder = txtOutput.Text;
            _settings.TempFolder = txtTemp.Text;
            _settings.UseSystemTemp = chkUseSystemTemp.IsChecked == true;

            if (cmbResolution.SelectedItem != null)
                _settings.DefaultResolution = cmbResolution.SelectedItem.ToString();

            if (cmbTheme.SelectedItem != null)
            {
                _settings.AppTheme = cmbTheme.SelectedItem.ToString();
                ThemeService.ApplyTheme(_settings.AppTheme);
            }

            _settings.EnableLogging = chkEnableLog.IsChecked == true;
            if (cmbLogLevel.SelectedItem is LogLevel level) _settings.LogLevel = level;

            AppSettings.Save(_settings);
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}