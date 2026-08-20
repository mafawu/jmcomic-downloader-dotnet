using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace JmComic.App.Controls;

public partial class VirtualizedCardGrid : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(VirtualizedCardGrid),
        new PropertyMetadata(null, OnItemsSourceChanged));

    public static readonly DependencyProperty SlotWidthProperty = DependencyProperty.Register(
        nameof(SlotWidth), typeof(double), typeof(VirtualizedCardGrid),
        new PropertyMetadata(182.0, OnLayoutPropertyChanged));

    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(VirtualizedCardGrid),
        new PropertyMetadata(168.0));

    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(VirtualizedCardGrid),
        new PropertyMetadata(null));

    public static readonly DependencyProperty DesiredColumnsProperty = DependencyProperty.Register(
        nameof(DesiredColumns), typeof(int), typeof(VirtualizedCardGrid),
        new PropertyMetadata(0, OnLayoutPropertyChanged));

    public IEnumerable? ItemsSource
    {
        get => (IEnumerable?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public double SlotWidth
    {
        get => (double)GetValue(SlotWidthProperty);
        set => SetValue(SlotWidthProperty, value);
    }

    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public DataTemplate? ItemTemplate
    {
        get => (DataTemplate?)GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public int DesiredColumns
    {
        get => (int)GetValue(DesiredColumnsProperty);
        set => SetValue(DesiredColumnsProperty, value);
    }

    public int Columns { get; private set; } = 1;

    public double VerticalOffset { get; private set; }

    public event EventHandler? Scrolled;

    private readonly ObservableCollection<List<object>> _rows = new();
    private bool _dirty = true;
    private bool _rebuildScheduled;
    private int _lastCount = -1;
    private ScrollViewer? _scroller;

    public VirtualizedCardGrid()
    {
        InitializeComponent();
        RowHost.ItemsSource = _rows;
        Loaded += (_, _) =>
        {
            Subscribe();
            ScheduleRebuild();
            AttachScroller();
        };
        Unloaded += (_, _) => Unsubscribe();
        SizeChanged += (_, _) =>
        {
            ScheduleRebuild();
            AttachScroller();
        };
        IsVisibleChanged += (_, _) => ScheduleRebuild();
    }

    public void ScrollToTop() => _scroller?.ScrollToTop();

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var grid = (VirtualizedCardGrid)d;
        grid.Subscribe();
        grid._dirty = true;
        grid.ScheduleRebuild();
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((VirtualizedCardGrid)d).ScheduleRebuild();

    private void Subscribe()
    {
        if (ItemsSource is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged -= OnSourceCollectionChanged;
            ncc.CollectionChanged += OnSourceCollectionChanged;
        }
    }

    private void Unsubscribe()
    {
        if (ItemsSource is INotifyCollectionChanged ncc)
        {
            ncc.CollectionChanged -= OnSourceCollectionChanged;
        }
    }

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _dirty = true;
        ScheduleRebuild();
    }

    private void ScheduleRebuild()
    {
        _dirty = true;
        if (_rebuildScheduled)
        {
            return;
        }
        _rebuildScheduled = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _rebuildScheduled = false;
            RebuildRows();
        });
    }

    private void RebuildRows()
    {
        var items = ItemsSource?.Cast<object>().ToList() ?? new List<object>();
        var width = ActualWidth;
        int columns;
        if (DesiredColumns >= GridCellSizer.MinColumns && DesiredColumns <= GridCellSizer.MaxColumns)
        {
            columns = DesiredColumns;
        }
        else
        {
            var slot = SlotWidth > 0 ? SlotWidth : 182;
            columns = width <= 0 ? 1 : Math.Max(1, (int)Math.Floor((width + GridCellSizer.Spacing) / slot));
        }
        if (!_dirty && columns == Columns && items.Count == _lastCount)
        {
            return;
        }
        _dirty = false;
        Columns = columns;
        _lastCount = items.Count;
        _rows.Clear();
        for (var i = 0; i < items.Count; i += columns)
        {
            _rows.Add(items.GetRange(i, Math.Min(columns, items.Count - i)));
        }
    }

    private void AttachScroller()
    {
        if (_scroller is not null)
        {
            return;
        }
        if (RowHost.Template?.FindName("Scroller", RowHost) is ScrollViewer sv)
        {
            _scroller = sv;
            sv.ScrollChanged += (_, e) =>
            {
                VerticalOffset = e.VerticalOffset;
                Scrolled?.Invoke(this, EventArgs.Empty);
            };
        }
    }
}