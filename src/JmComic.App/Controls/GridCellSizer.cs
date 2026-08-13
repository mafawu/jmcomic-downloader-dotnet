namespace JmComic.App.Controls;

/// <summary>
/// 卡片「分格」尺寸计算规则：本地模式与在线模式（搜索/分类/排行/周榜/收藏，多个数据源）共用同一套基准。
/// 滑块调整基准系数（0.8~1.4），窗口宽度变化时按比例自动缩放。
/// </summary>
public static class GridCellSizer
{
    /// <summary>基准可用宽度：在此宽度下，分格 100% 对应卡片宽 168px（1280 全屏时本地列表页的可用宽度）。</summary>
    public const double ReferenceWidth = 930;

    /// <summary>基准卡片宽度（分格 100%、窗口宽度等于基准宽度时的卡片宽）。</summary>
    public const double BaseCardWidth = 168;

    /// <summary>卡片宽度上下限，避免窗口极端大小时分格过大或过小。</summary>
    public const double MinCardWidth = 90;
    public const double MaxCardWidth = 340;

    /// <summary>卡片间距（含在槽位宽度中）。</summary>
    public const double Spacing = 14;

    /// <summary>分格系数可调范围。</summary>
    public const double MinCellScale = 0.8;
    public const double MaxCellScale = 1.4;

    /// <summary>
    /// 按可用宽度与分格系数计算卡片宽度与槽位宽度：
    /// 卡片目标宽度 = 168 * 分格系数 * (可用宽度 / 基准宽度)。
    /// </summary>
    public static (double CardWidth, double SlotWidth) Compute(double cellScale, double availableWidth)
    {
        var ratio = availableWidth / ReferenceWidth;
        var cardWidth = Math.Clamp(BaseCardWidth * cellScale * ratio, MinCardWidth, MaxCardWidth);
        return (cardWidth, cardWidth + Spacing);
    }
}
