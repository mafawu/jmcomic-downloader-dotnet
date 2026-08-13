using System.Windows;
using System.Windows.Controls;

namespace JmComic.App.Controls;

/// <summary>
/// 「分格」滑块：与卡片网格页的 CellScale 双向绑定。
/// 本地模式与在线各页共用同一控件，保证调整方式一致（0.8~1.4，百分比显示）。
/// </summary>
public partial class CardSizeSlider : UserControl
{
    /// <summary>分格系数，双向绑定到所属网格页的 CellScale。</summary>
    public static readonly DependencyProperty CellScaleProperty = DependencyProperty.Register(
        nameof(CellScale), typeof(double), typeof(CardSizeSlider),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((CardSizeSlider)d).OnCellScaleChanged()));

    public double CellScale
    {
        get => (double)GetValue(CellScaleProperty);
        set => SetValue(CellScaleProperty, value);
    }

    private bool _syncing;

    public CardSizeSlider()
    {
        InitializeComponent();
    }

    private void OnCellScaleChanged()
    {
        if (_syncing)
        {
            return;
        }
        _syncing = true;
        // XAML 加载过程中 Slider 的 ValueChanged 可能先于命名字段赋值触发，需判空
        if (ScaleSlider is not null)
        {
            ScaleSlider.Value = CellScale;
        }
        if (ScaleText is not null)
        {
            ScaleText.Text = $"{CellScale:P0}";
        }
        _syncing = false;
    }

    private void ScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing)
        {
            return;
        }
        CellScale = Math.Round(ScaleSlider.Value, 2);
    }
}
