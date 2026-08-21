using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace JmComic.App.Views;

/// <summary>本地搜索/筛选面板：仿 Green FilterSidebar - 标签/作者分组、计数、左键包含/右键排除</summary>
public partial class LocalSearchPanel : UserControl
{
    private Dictionary<string, int> _tagCounts = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _authorCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _includedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _includedAuthors = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _excludedAuthors = new(StringComparer.OrdinalIgnoreCase);

    public event Action<string, IReadOnlyCollection<string>>? SearchChanged;
    public event Action<LocalFilterState>? FilterChanged;

    public LocalSearchPanel()
    {
        InitializeComponent();
        UpdateInputUi();
    }

    public record LocalFilterState(
        string Keyword,
        IReadOnlyCollection<string> IncludedTags,
        IReadOnlyCollection<string> ExcludedTags,
        IReadOnlyCollection<string> IncludedAuthors,
        IReadOnlyCollection<string> ExcludedAuthors);

    public void SetTags(IEnumerable<string> tags)
    {
        var counts = tags.Where(t => !string.IsNullOrWhiteSpace(t))
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        SetTagCounts(counts);
    }

    public void SetTagCounts(IReadOnlyDictionary<string, int> counts)
    {
        _tagCounts = new Dictionary<string, int>(counts, StringComparer.OrdinalIgnoreCase);
        RebuildTagFilters();
        UpdateClearButtons();
    }

    public void SetAuthorCounts(IReadOnlyDictionary<string, int> counts)
    {
        _authorCounts = new Dictionary<string, int>(counts, StringComparer.OrdinalIgnoreCase);
        RebuildAuthorFilters();
        UpdateClearButtons();
    }

    public void SetFilters(IReadOnlyDictionary<string, int> tagCounts, IReadOnlyDictionary<string, int> authorCounts)
    {
        _tagCounts = new Dictionary<string, int>(tagCounts, StringComparer.OrdinalIgnoreCase);
        _authorCounts = new Dictionary<string, int>(authorCounts, StringComparer.OrdinalIgnoreCase);
        RebuildTagFilters();
        RebuildAuthorFilters();
        UpdateClearButtons();
    }

    private void RebuildTagFilters()
    {
        var ordered = _tagCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Take(60).ToList();
        TagCountText.Text = ordered.Count == 0 ? "" : $"({ordered.Count})";
        TagEmptyText.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var items = new List<UIElement>();
        foreach (var kv in ordered)
        {
            var name = kv.Key;
            var count = kv.Value;
            var isIncluded = _includedTags.Contains(name);
            var isExcluded = _excludedTags.Contains(name);
            var btn = new ToggleButton
            {
                Style = (Style)FindResource("LocalTagToggleStyle"),
                Margin = new Thickness(0, 0, 6, 6),
                IsChecked = isIncluded,
                Tag = isExcluded ? "excluded" : null,
                ToolTip = isExcluded ? "已排除，右键取消" : isIncluded ? "已包含，右键可改为排除" : "左键包含 · 右键排除",
            };
            var prefix = isIncluded ? "✓ " : isExcluded ? "∅ " : "";
            btn.Content = $"{prefix}{name} ({count})";
            if (isExcluded)
            {
                btn.IsChecked = false;
            }
            btn.Click += (_, _) => ToggleIncludeTag(name);
            btn.MouseRightButtonUp += (_, e) => { ToggleExcludeTag(name); e.Handled = true; };
            items.Add(btn);
        }
        TagItems.ItemsSource = items;
    }

    private void RebuildAuthorFilters()
    {
        var ordered = _authorCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Take(30).ToList();
        AuthorCountText.Text = ordered.Count == 0 ? "" : $"({ordered.Count})";
        AuthorEmptyText.Visibility = ordered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var items = new List<UIElement>();
        foreach (var kv in ordered)
        {
            var name = kv.Key;
            var count = kv.Value;
            var isIncluded = _includedAuthors.Contains(name);
            var isExcluded = _excludedAuthors.Contains(name);
            var btn = new ToggleButton
            {
                Style = (Style)FindResource("LocalTagToggleStyle"),
                Margin = new Thickness(0, 0, 6, 6),
                IsChecked = isIncluded,
                Tag = isExcluded ? "excluded" : null,
                ToolTip = isExcluded ? "已排除" : isIncluded ? "已包含" : "左键包含 · 右键排除",
            };
            var prefix = isIncluded ? "✓ " : isExcluded ? "∅ " : "";
            btn.Content = $"{prefix}{name} ({count})";
            if (isExcluded) btn.IsChecked = false;
            btn.Click += (_, _) => ToggleIncludeAuthor(name);
            btn.MouseRightButtonUp += (_, e) => { ToggleExcludeAuthor(name); e.Handled = true; };
            items.Add(btn);
        }
        AuthorItems.ItemsSource = items;
    }

    private void ToggleIncludeTag(string name)
    {
        if (_excludedTags.Contains(name)) _excludedTags.Remove(name);
        if (_includedTags.Contains(name)) _includedTags.Remove(name);
        else _includedTags.Add(name);
        RebuildTagFilters();
        UpdateClearButtons();
        NotifyChanged();
    }

    private void ToggleExcludeTag(string name)
    {
        if (_includedTags.Contains(name)) _includedTags.Remove(name);
        if (_excludedTags.Contains(name)) _excludedTags.Remove(name);
        else _excludedTags.Add(name);
        RebuildTagFilters();
        UpdateClearButtons();
        NotifyChanged();
    }

    private void ToggleIncludeAuthor(string name)
    {
        if (_excludedAuthors.Contains(name)) _excludedAuthors.Remove(name);
        if (_includedAuthors.Contains(name)) _includedAuthors.Remove(name);
        else _includedAuthors.Add(name);
        RebuildAuthorFilters();
        UpdateClearButtons();
        NotifyChanged();
    }

    private void ToggleExcludeAuthor(string name)
    {
        if (_includedAuthors.Contains(name)) _includedAuthors.Remove(name);
        if (_excludedAuthors.Contains(name)) _excludedAuthors.Remove(name);
        else _excludedAuthors.Add(name);
        RebuildAuthorFilters();
        UpdateClearButtons();
        NotifyChanged();
    }

    private void ClearTagFilter_Click(object sender, RoutedEventArgs e)
    {
        _includedTags.Clear();
        _excludedTags.Clear();
        RebuildTagFilters();
        UpdateClearButtons();
        NotifyChanged();
    }

    private void ClearAuthorFilter_Click(object sender, RoutedEventArgs e)
    {
        _includedAuthors.Clear();
        _excludedAuthors.Clear();
        RebuildAuthorFilters();
        UpdateClearButtons();
        NotifyChanged();
    }

    private void UpdateClearButtons()
    {
        ClearTagFilterButton.Visibility = _includedTags.Count > 0 || _excludedTags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearAuthorFilterButton.Visibility = _includedAuthors.Count > 0 || _excludedAuthors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void KeywordBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateInputUi();
        NotifyChanged();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        KeywordBox.Clear();
        _includedTags.Clear();
        _excludedTags.Clear();
        _includedAuthors.Clear();
        _excludedAuthors.Clear();
        RebuildTagFilters();
        RebuildAuthorFilters();
        UpdateClearButtons();
        UpdateInputUi();
        NotifyChanged();
    }

    private void KeywordBox_GotFocus(object sender, RoutedEventArgs e)
    {
        UpdateInputUi();
        KeywordBox.CaretIndex = 0;
    }

    private void KeywordBox_LostFocus(object sender, RoutedEventArgs e) => UpdateInputUi();

    private void UpdateInputUi()
    {
        var hasText = !string.IsNullOrEmpty(KeywordBox.Text);
        var hasFilter = _includedTags.Count > 0 || _excludedTags.Count > 0 || _includedAuthors.Count > 0 || _excludedAuthors.Count > 0;
        PlaceholderText.Visibility = hasText || KeywordBox.IsKeyboardFocused ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = hasText || hasFilter ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NotifyChanged()
    {
        UpdateInputUi();
        var kw = KeywordBox.Text.Trim();
        SearchChanged?.Invoke(kw, _includedTags.ToList());
        FilterChanged?.Invoke(new LocalFilterState(kw, _includedTags.ToList(), _excludedTags.ToList(), _includedAuthors.ToList(), _excludedAuthors.ToList()));
    }
}
