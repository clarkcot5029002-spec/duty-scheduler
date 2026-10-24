using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Duty.Core;

namespace Duty.UI;

public sealed record FormDraft(string Name, int Target, byte[] Preferences)
{
    public FormDraft Copy() => new(Name, Target, (byte[])Preferences.Clone());
    public bool Same(FormDraft other) => Name == other.Name && Target == other.Target && Preferences.SequenceEqual(other.Preferences);
}

public sealed class PreferenceEditor : UserControl
{
    public TextBox NameBox { get; } = new() { MaxLength = 64, MinWidth = 155 };
    public PaintBoard Board { get; } = new();
    public CheckBox SpecialBox { get; } = new() { Content = "周二第四节特殊班" };
    private readonly ToggleButton _three = new() { Content = "3节", Width = 57 };
    private readonly ToggleButton _four = new() { Content = "4节", Width = 57, Margin = new Thickness(5, 0, 0, 0) };
    private readonly TextBlock _summary = Ui.Text("");
    private readonly TextBlock _warnings = Ui.Text("", 12, Ui.Warning);
    private readonly ToggleButton?[] _brushes = new ToggleButton?[3];
    private bool _loading;
    private int _target = 3;
    private FormDraft _committed = new("", 3, PreferenceMarks.CreateDefault());
    public event Action? Changed;
    public event Action<FormDraft>? Committed;
    public FormDraft Draft => new(NameBox.Text, _target, Board.Values);

    public PreferenceEditor()
    {
        var root = new StackPanel();
        var top = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        top.ColumnDefinitions.Add(new()); top.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var namePanel = new StackPanel { Margin = new Thickness(0, 0, 22, 0) };
        namePanel.Children.Add(Ui.Text("姓名", 12, Ui.Muted));
        NameBox.Margin = new Thickness(0, 7, 0, 0); namePanel.Children.Add(NameBox);
        top.Children.Add(namePanel);
        var loadPanel = new StackPanel();
        loadPanel.Children.Add(Ui.Text("每周班量", 12, Ui.Muted));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 0) };
        buttons.Children.Add(_three); buttons.Children.Add(_four); loadPanel.Children.Add(buttons);
        Grid.SetColumn(loadPanel, 1); top.Children.Add(loadPanel); root.Children.Add(top);
        var brushRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        foreach (var state in new byte[] { 2, 0 })
        {
            var selected = state;
            var toggle = new ToggleButton { Content = Week.Preferences[state], Margin = new Thickness(0, 0, 7, 0), MinWidth = 106 };
            toggle.Click += (_, _) => SelectBrush((byte)selected);
            _brushes[state] = toggle; brushRow.Children.Add(toggle);
        }
        root.Children.Add(brushRow);
        root.Children.Add(Board);
        SpecialBox.Margin = new Thickness(0, 12, 0, 0); root.Children.Add(SpecialBox);
        _summary.Margin = new Thickness(0, 17, 0, 0); root.Children.Add(_summary);
        _warnings.Margin = new Thickness(0, 8, 0, 0); root.Children.Add(_warnings);
        Content = root;
        NameBox.TextChanged += (_, _) => NotifyChanged();
        NameBox.LostKeyboardFocus += (_, _) => CommitPending();
        _three.Click += (_, _) => SelectTarget(3);
        _four.Click += (_, _) => SelectTarget(4);
        SpecialBox.Checked += (_, _) => ChangeSpecial(true);
        SpecialBox.Unchecked += (_, _) => ChangeSpecial(false);
        Board.BeginningEdit += CommitPending;
        Board.Changed += () => { SyncSpecial(); NotifyChanged(); };
        Board.Completed += CommitPending;
        SelectBrush(2);
        LoadDraft(new("", 3, PreferenceMarks.CreateDefault()));
    }
    public Person GetPerson() => new(NameBox.Text, _target, Board.Values);
    public void LoadPerson(Person person) => LoadDraft(new(person.Name, person.Target, person.CopyPreferences()));
    public void LoadDraft(FormDraft draft)
    {
        _loading = true;
        NameBox.Text = draft.Name; _target = draft.Target; Board.Load(draft.Preferences);
        _three.IsChecked = _target == 3; _four.IsChecked = _target == 4;
        SpecialBox.IsChecked = draft.Preferences[7] > 0;
        _loading = false; _committed = Draft.Copy(); UpdateSummary();
    }
    public void CommitPending()
    {
        if (_loading) return;
        var current = Draft;
        if (current.Same(_committed)) return;
        var previous = _committed.Copy(); _committed = current.Copy();
        Committed?.Invoke(previous);
    }
    public void SelectTarget(int target)
    {
        CommitPending();
        _target = target;
        _three.IsChecked = target == 3; _four.IsChecked = target == 4;
        NotifyChanged(); CommitPending();
    }
    public void SelectBrush(byte state)
    {
        Board.BrushState = state;
        for (var s = 0; s < 3; s++)
            if (_brushes[s] is { } toggle) toggle.IsChecked = s == state;
    }
    private void ChangeSpecial(bool enabled)
    {
        if (_loading) return;
        CommitPending(); Board.SetSpecial(enabled); CommitPending();
    }
    private void SyncSpecial()
    {
        _loading = true; SpecialBox.IsChecked = Board.Values[7] > 0; _loading = false;
    }
    private void NotifyChanged()
    {
        if (_loading) return;
        UpdateSummary(); Changed?.Invoke();
    }
    private void UpdateSummary()
    {
        var values = Board.Values;
        var available = values.Count(v => v > 0);
        var preferred = values.Count(v => v == 2);
        var mornings = Enumerable.Range(0, 5).Count(d => values[d * 4] > 0);
        _summary.Text = $"可排 {available}格     优先 {preferred}格     可排早一 {mornings}格";
        var messages = new List<string>();
        if (available < _target) messages.Add($"可排时段少于目标{_target}节");
        if (mornings == 0) messages.Add("没有可排早一");
        _warnings.Text = messages.Count == 0 ? "填报条件已具备，实际安排以排班结果为准。" : string.Join("；", messages);
        _warnings.Foreground = messages.Count == 0 ? Ui.Muted : Ui.Warning;
    }
}
