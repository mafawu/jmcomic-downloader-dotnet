using System.Windows;
using System.Windows.Controls;
using JmComic.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace JmComic.App.Views;

/// <summary>右侧下载面板。</summary>
public partial class DownloadPanel : UserControl
{
    public DownloadPanel()
    {
        InitializeComponent();
        var viewModel = App.Services.GetRequiredService<DownloadPanelViewModel>();
        DataContext = viewModel;
        EmptyHint.Visibility = viewModel.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(DownloadPanelViewModel.HasDownloads))
            {
                EmptyHint.Visibility = viewModel.HasDownloads ? Visibility.Collapsed : Visibility.Visible;
            }
        };
    }
}


