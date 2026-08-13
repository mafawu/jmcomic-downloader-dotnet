using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace JmComic.App.Views;

/// <summary>右侧本地搜索工具：关键字（漫画名/作者/标签）输入 + 标签筛选列表。</summary>
public partial class LocalSearchPanel : UserControl
{
    private readonly List<string> _tags = new();
    private readonly HashSet<string> _selectedTags = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>筛选条件变化（关键字 + 已选标签）。</summary>
    public event Action<string, IReadOnlyCollection<string>>? SearchChanged;

    public LocalSearchPanel()
    {
        InitializeComponent();
        UpdateInputUi();
    }

    /// <summary>设置可筛选标签列表（保留已选状态，不触发筛选通知）。</summary>
    public void SetTags(IEnumerable<string> tags)
    {
        _tags.Clear();
        _tags.AddRange(tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase));
        RebuildTags();
    }

    private void RebuildTags()
    {
        var items = new List<UIElement>();
        foreach (var tag in _tags)
        {
            var captured = tag;
            var toggle = new ToggleButton
            {
                Content = tag,
                IsChecked = _selectedTags.Contains(tag),
                Style = (Style)FindResource("LocalTagToggleStyle"),
                Margin = new Thickness(0, 0, 6, 6),
            };
            toggle.Checked += (_, _) =>
            {
                _selectedTags.Add(captured);
                NotifyChanged();
            };
            toggle.Unchecked += (_, _) =>
            {
                _selectedTags.Remove(captured);
                NotifyChanged();
            };
            items.Add(toggle);
        }
        TagItems.ItemsSource = items;
    }

    private void KeywordBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateInputUi();
        NotifyChanged();
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        KeywordBox.Clear();
        _selectedTags.Clear();
        if (TagItems.ItemsSource is List<UIElement> items)
        {
            foreach (var item in items)
            {
                if (item is ToggleButton toggle)
                {
                    toggle.IsChecked = false;
                }
            }
        }
        UpdateInputUi();
        NotifyChanged();
    }

    private void UpdateInputUi()
    {
        var hasText = !string.IsNullOrEmpty(KeywordBox.Text);
        PlaceholderText.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
        ClearButton.Visibility = hasText || _selectedTags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void NotifyChanged() => SearchChanged?.Invoke(KeywordBox.Text.Trim(), _selectedTags.ToList());
}