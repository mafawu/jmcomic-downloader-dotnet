using System.Windows;
using System.Windows.Controls.Primitives;

namespace JmComic.App.Controls;

/// <summary>根据可用宽度自动计算列数的均匀网格，用于结果卡片随窗口宽度自适应排列。</summary>
public class AutoColumnsUniformGrid : UniformGrid
{
    /// <summary>单个条目占位宽度（含卡片宽度与间距），用于计算列数。</summary>
    public double ItemSlotWidth
    {
        get => (double)GetValue(ItemSlotWidthProperty);
        set => SetValue(ItemSlotWidthProperty, value);
    }

    public static readonly DependencyProperty ItemSlotWidthProperty = DependencyProperty.Register(
        nameof(ItemSlotWidth),
        typeof(double),
        typeof(AutoColumnsUniformGrid),
        new FrameworkPropertyMetadata(182.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    protected override Size MeasureOverride(Size constraint)
    {
        var width = constraint.Width;
        if (!double.IsInfinity(width) && width > 0 && ItemSlotWidth > 0)
        {
            var columns = System.Math.Max(1, (int)System.Math.Floor((width + 14) / ItemSlotWidth));
            if (Columns != columns)
            {
                Columns = columns;
            }
        }
        return base.MeasureOverride(constraint);
    }
}
