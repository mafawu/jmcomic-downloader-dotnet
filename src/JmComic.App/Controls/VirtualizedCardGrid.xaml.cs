using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace JmComic.App.Controls;

/// <summary>
/// 虚拟化卡片网格：把条目按槽位宽切成等宽行，行用 VirtualizingStackPanel 做真正的 UI 虚拟化，
/// 滚动时只实例化可视行并回收复用容器，行内卡片由 <see cref="ItemTemplate"/> 渲染。
/// 相比「ScrollViewer + ItemsControl + WrapPanel/UniformGrid」（全部一次性实例化），
/// 大列表的内存占用与滚动开销大幅降低。
/// </summary>
public partial class VirtualizedCardGrid : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(VirtualizedCardGrid),
        new PropertyMetadata(null, OnItemsSourceChanged));

    /// <summary>单个条目占位宽度（卡片宽 + 间距），用于计算列数并切行。</summary>
    public static readonly DependencyProperty SlotWidthProperty = DependencyProperty.Register(
        nameof(SlotWidth), typeof(double), typeof(VirtualizedCardGrid),
        new PropertyMetadata(182.0, OnLayoutPropertyChanged));

    /// <summary>卡片宽度（供行内卡片模板绑定）。</summary>
    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(VirtualizedCardGrid),
        new PropertyMetadata(168.0));

    /// <summary>卡片数据模板（由宿主编排，如 AlbumCard / LocalComicCard / 章节卡）。</summary>
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(
        nameof(ItemTemplate), typeof(DataTemplate), typeof(VirtualizedCardGrid),
        new PropertyMetadata(null));

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

    /// <summary>当前列数（供宿主编排/命中计算使用，如章节框选）。</summary>
    public int Columns { get; private set; } = 1;

    /// <summary>内部滚动容器当前垂直偏移（供宿主编排/命中计算使用）。</summary>
    public double VerticalOffset { get; private set; }

    /// <summary>内部滚动位置变化时触发。</summary>
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

    /// <summary>回到顶部（内部滚动容器）。</summary>
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
        var slot = SlotWidth > 0 ? SlotWidth : 182;
        var columns = width <= 0 ? 1 : Math.Max(1, (int)Math.Floor((width + 14) / slot));
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
