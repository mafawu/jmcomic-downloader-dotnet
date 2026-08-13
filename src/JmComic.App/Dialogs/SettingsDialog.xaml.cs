using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
        // 归一化为 apiDomains 列表；旧字段 apiDomain 保持为空（读取时仍兼容旧配置）
        config.ApiDomain = "";
        if (FormatBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && Enum.TryParse<DownloadFormat>(tag, true, out var format))
        {
            config.DownloadFormat = format;
        }

        config.TitleTranslate.Enabled = TranslateEnabledBox.IsChecked == true;
        config.TitleTranslate.BaseUrl = TranslateBaseUrlBox.Text.Trim();
        config.TitleTranslate.ApiKey = TranslateApiKeyBox.Text.Trim();
        config.TitleTranslate.Model = TranslateModelBox.Text.Trim();

        _configService.Save();
        DialogResult = true;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;

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