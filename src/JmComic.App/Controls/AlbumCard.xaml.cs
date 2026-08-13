using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JmComic.App.ViewModels;

namespace JmComic.App.Controls;

/// <summary>漫画卡片：封面 + 标题 + 作者，悬停显示一键下载，点击打开详情。</summary>
public partial class AlbumCard : UserControl
{
    public AlbumCard()
    {
        InitializeComponent();
    }

    private void Root_MouseEnter(object sender, MouseEventArgs e) => Overlay.Visibility = Visibility.Visible;

    private void Root_MouseLeave(object sender, MouseEventArgs e) => Overlay.Visibility = Visibility.Collapsed;

    private void Root_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // 按钮自身已处理点击（Handled），不会走到这里
        if (DataContext is AlbumCardViewModel { OpenCommand: { } open })
        {
            open.Execute(null);
        }
    }
}
