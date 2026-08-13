using System.Windows;
using System.Windows.Controls;

namespace JmComic.App.Controls;

/// <summary>
/// 漫画网格页基类：统一「分格」规则（0.8~1.4 滑块 + 随窗口宽度自动缩放），
/// 本地模式与在线各页（多数据源共用）保持一致。派生页只需把网格/卡片的宽度绑定到
/// <see cref="SlotWidth"/> / <see cref="CardWidth"/>，并在页头放置 <see cref="CardSizeSlider"/>。
/// </summary>
public class CardGridViewBase : UserControl
{
    /// <summary>分格系数（0.8~1.4）：决定卡片基准大小，窗口宽度变化时按比例自动缩放。</summary>
    public static readonly DependencyProperty CellScaleProperty = DependencyProperty.Register(
        nameof(CellScale), typeof(double), typeof(CardGridViewBase),
        new PropertyMetadata(1.0, (d, _) => ((CardGridViewBase)d).UpdateCellSize()));

    /// <summary>随分格系数与窗口宽度变化的卡片实际宽度。</summary>
    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(CardGridViewBase), new PropertyMetadata(168.0));

    /// <summary>随分格系数与窗口宽度变化的网格槽位宽度（卡片宽 + 间距 14），用于计算列数。</summary>
    public static readonly DependencyProperty SlotWidthProperty = DependencyProperty.Register(
        nameof(SlotWidth), typeof(double), typeof(CardGridViewBase), new PropertyMetadata(182.0));

    public double CellScale
    {
        get => (double)GetValue(CellScaleProperty);
        set => SetValue(CellScaleProperty, value);
    }

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public double SlotWidth
    {
        get => (double)GetValue(SlotWidthProperty);
        set => SetValue(SlotWidthProperty, value);
    }

    protected CardGridViewBase()
    {
        SizeChanged += (_, _) => UpdateCellSize();
        Loaded += (_, _) => UpdateCellSize();
    }

    /// <summary>按当前窗口可用宽度与分格系数刷新卡片/槽位宽度（与本地模式同一套规则）。</summary>
    protected void UpdateCellSize()
    {
        if (ActualWidth <= 0)
        {
            return;
        }
        var (cardWidth, slotWidth) = GridCellSizer.Compute(CellScale, ActualWidth);
        CardWidth = cardWidth;
        SlotWidth = slotWidth;
    }
}
