using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JmComic.App.Common;
using JmComic.Core.Models;
using JmComic.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace JmComic.App.Dialogs;

/// <summary>设置弹窗：下载目录 / 图片格式 / 接口域名 / 标题翻译，保存后写入 config.json。</summary>
public partial class SettingsDialog : Window
{
    private readonly ConfigService _configService;

    public SettingsDialog()
    {
        InitializeComponent();
        _configService = App.Services.GetRequiredService<ConfigService>();
        var config = _configService.Current;

        DownloadDirBox.Text = config.DownloadDir;
        var domains = config.ApiDomains.Count > 0
            ? config.ApiDomains
            : (string.IsNullOrWhiteSpace(config.ApiDomain) ? null : new List<string> { config.ApiDomain });
        ApiDomainsBox.Text = domains is null ? "" : string.Join(", ", domains);
        foreach (ComboBoxItem item in FormatBox.Items)
        {
            if (string.Equals((string)item.Tag, config.DownloadFormat.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                FormatBox.SelectedItem = item;
                break;
            }
        }

        TranslateEnabledBox.IsChecked = config.TitleTranslate.Enabled;
        TranslateBaseUrlBox.Text = config.TitleTranslate.BaseUrl;
        TranslateApiKeyBox.Text = config.TitleTranslate.ApiKey;
        TranslateModelBox.Text = config.TitleTranslate.Model;

        ScrollSpeedSlider.Value = ConfigService.NormalizeScrollSpeed(config.ReaderScrollSpeed);
        UpdateScrollSpeedText(ScrollSpeedSlider.Value);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DwmWindowCorner.Apply(this);
    }

    private void ScrollSpeedSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateScrollSpeedText(e.NewValue);
    }

    private void UpdateScrollSpeedText(double v)
    {
        if (ScrollSpeedValueText is null) return;
        ScrollSpeedValueText.Text = $"{v:0.0}x";
    }

    private void BrowseDownloadDir_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择下载保存目录",
        };
        if (!string.IsNullOrWhiteSpace(DownloadDirBox.Text) && Directory.Exists(DownloadDirBox.Text))
        {
            dialog.InitialDirectory = DownloadDirBox.Text;
        }
        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            DownloadDirBox.Text = dialog.FolderName;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(DownloadDirBox.Text))
        {
            ShowError("下载目录不能为空");
            return;
        }

        var config = _configService.Current;
        config.DownloadDir = Path.GetFullPath(DownloadDirBox.Text.Trim());
        config.ApiDomains = ApiDomainsBox.Text
            .Split(new[] { ',', '，', ';', '；', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        config.ApiDomain = "";
        if (FormatBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && Enum.TryParse<DownloadFormat>(tag, true, out var format))
        {
            config.DownloadFormat = format;
        }

        config.ReaderScrollSpeed = ConfigService.NormalizeScrollSpeed(ScrollSpeedSlider.Value);

        config.TitleTranslate.Enabled = TranslateEnabledBox.IsChecked == true;
        config.TitleTranslate.BaseUrl = TranslateBaseUrlBox.Text.Trim();
        config.TitleTranslate.ApiKey = TranslateApiKeyBox.Text.Trim();
        config.TitleTranslate.Model = TranslateModelBox.Text.Trim();

        _configService.Save();
        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ManageLocalDirs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new LocalDirsDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            if (Owner is MainWindow mw)
            {
                _ = mw.TriggerLocalRefreshAsync();
            }
        }
    }

    private async void RefreshLocal_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "手动刷新将对全部本地目录进行全量重新扫描，目录较多时可能较慢。是否继续？", "全量重新扫描", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            if (Owner is MainWindow mw)
            {
                await mw.TriggerLocalRefreshAsync();
            }
            else
            {
                var localView = new Views.LocalView();
                await localView.RequestRefreshAsync();
            }
        }
        catch (Exception ex)
        {
            ShowError($"刷新失败：{ex.Message}");
        }
    }

    private void ViewLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = JmComic.Core.AppPaths.AppDataDir;
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError($"打开日志目录失败：{ex.Message}");
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
