using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using JmComic.App.ViewModels;

namespace JmComic.App.Controls;

/// <summary>漫画卡片：封面 + 标题 + 作者，悬停显示一键下载，点击打开详情。</summary>
public partial class AlbumCard : UserControl
{
    /// <summary>封面宽高比 166:222，高度随卡片宽度自适应，窗口缩放时保持不变形。</summary>
    private const double CoverAspect = 222.0 / 166.0;

    public AlbumCard()
    {
        InitializeComponent();
    }

    private void Card_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (CoverHost.ActualWidth > 0)
        {
            CoverHost.Height = CoverHost.ActualWidth * CoverAspect;
        }
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
