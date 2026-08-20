namespace JmComic.App.Controls;

public static class GridCellSizer
{
    public const double ReferenceWidth = 930;
    public const double BaseCardWidth = 168;
    public const double MinCardWidth = 90;
    public const double MaxCardWidth = 240;
    public const double Spacing = 14;
    public const double MinCellScale = 0.8;
    public const double MaxCellScale = 1.4;

    public const int MinColumns = 3;
    public const int MaxColumns = 8;
    public const int DefaultColumns = 5;

    [Obsolete("Use ComputeByColumns instead")]
    public static (double CardWidth, double SlotWidth) Compute(double cellScale, double availableWidth)
    {
        var ratio = availableWidth / ReferenceWidth;
        var cardWidth = Math.Clamp(BaseCardWidth * cellScale * ratio, MinCardWidth, MaxCardWidth);
        return (cardWidth, cardWidth + Spacing);
    }

    public static (double CardWidth, double SlotWidth) ComputeByColumns(int columns, double availableWidth)
    {
        if (availableWidth <= 0)
            return (BaseCardWidth, BaseCardWidth + Spacing);
        columns = Math.Clamp(columns, MinColumns, MaxColumns);
        var slotWidth = availableWidth / columns;
        var cardWidth = slotWidth - Spacing;
        cardWidth = Math.Clamp(cardWidth, 80, 320);
        slotWidth = cardWidth + Spacing;
        return (cardWidth, slotWidth);
    }
}