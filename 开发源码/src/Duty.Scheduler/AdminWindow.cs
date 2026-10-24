using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Duty.Core;
using Duty.Excel;
using Duty.Solver;
using Duty.UI;

namespace Duty.Desktop;

internal sealed record Session(Person[] People, Schedule Schedule, bool Calculated, bool Confirmed,
    string Status, string Note, bool Manual);

public sealed class AdminWindow : Window
{
    private List<Person> _people = [];
    private Schedule _schedule = new();
    private readonly UndoHistory<Session> _history = new();
    private readonly DataGrid _roster = new();
    private readonly TextBox _search = new();
    private readonly ScheduleBoard _board = new();
    private readonly ListBox _issues = new() { BorderThickness = new Thickness(0), Background = Brushes.White };
    private readonly TextBlock _totals = Ui.Text("", 13);
    private readonly TextBlock _status = Ui.Text("待导入填报", 12, Ui.Muted);
    private readonly TextBlock _detail = Ui.Text("", 12, Ui.Muted);
    private readonly TextBlock _peopleCount = Ui.Text("", 12, Ui.Muted);
    private readonly Grid _body;
    private readonly DockPanel _issuesPanel;
    private readonly CheckBox _confirmed = new() { Content = "已确认记录收齐" };
    private readonly ComboBox _seconds = new() { Width = 94, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _solve;
    private readonly Button _cancel;
    private readonly Button _export;
    private readonly Button _undo;
    private readonly Button _redo;
    private readonly List<Button> _dataButtons = [];
    private readonly bool _verification;
    private CancellationTokenSource? _cancellation;
    private bool _busy;
    private bool _refreshing;
    private bool _calculated;
    private bool _manual;
    private bool _collectionConfirmed;
    private bool _closeAfterCancel;
    private string _statusText = "待导入填报";
    private string _note = "";
    private string? _selected;
    private int? _focusSlot;
    private Point _dragStart;
    private int _generation;

    public AdminWindow(bool verification = false)
    {
        Ui.StyleWindow(this);
        _verification = verification;
        Title = "排班系统"; Width = 1480; Height = 880; MinWidth = 1200; MinHeight = 770;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(22, 18, 22, 15) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 17) };
        var export = Ui.Button("\uE74E", "生成Excel排班表", Export, "生成 Excel", true);
        _export = export; DockPanel.SetDock(export, Dock.Right); header.Children.Add(export);
        var showIssues = Ui.Button("\uE7BA", "查看检查与提醒", ShowIssues);
        DockPanel.SetDock(showIssues, Dock.Right); header.Children.Add(showIssues);
        var title = Ui.Text("排班工作台", 25); title.FontWeight = FontWeights.SemiBold; header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var import = Ui.Button("\uE8B5", "批量导入填报码", Import, "导入填报码"); controls.Children.Add(import); _dataButtons.Add(import);
        _undo = Ui.Button("\uE7A7", "撤销", Undo); controls.Children.Add(_undo);
        _redo = Ui.Button("\uE7A6", "重做", Redo); controls.Children.Add(_redo);
        var clear = Ui.Button("\uE74D", "清空本次人员和排班", Clear); controls.Children.Add(clear); _dataButtons.Add(clear);
        _confirmed.Margin = new Thickness(13, 0, 0, 0); controls.Children.Add(_confirmed);
        toolbar.Children.Add(controls);
        var run = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        run.Children.Add(Ui.Text("计算时限", 12, Ui.Muted));
        foreach (var time in new[] { 5, 15, 30, 60 }) _seconds.Items.Add(new ComboBoxItem { Content = $"{time}秒", Tag = time });
        _seconds.SelectedIndex = 1; _seconds.Margin = new Thickness(10, 0, 10, 0); run.Children.Add(_seconds);
        _solve = Ui.Button("\uE768", "按当前填报自动排班", () => _ = SolveAsync(), "自动排班", true); run.Children.Add(_solve);
        _cancel = Ui.Button("\uE71A", "取消当前计算", () => _cancellation?.Cancel()); run.Children.Add(_cancel);
        DockPanel.SetDock(run, Dock.Right); toolbar.Children.Remove(controls); toolbar.Children.Add(run); toolbar.Children.Add(controls);
        DockPanel.SetDock(toolbar, Dock.Top); root.Children.Add(toolbar);
        var footer = new Border { BorderBrush = Ui.Brush("#DFE6E1"), BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 12, 0, 0), Margin = new Thickness(0, 16, 0, 0), Child = _status };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var body = new Grid(); _body = body;
        body.ColumnDefinitions.Add(new() { Width = new GridLength(260) });
        body.ColumnDefinitions.Add(new());
        body.ColumnDefinitions.Add(new() { Width = new GridLength(260) });
        var peoplePanel = CreatePeoplePanel(); body.Children.Add(peoplePanel);
        var center = new DockPanel { Margin = new Thickness(18, 0, 18, 0) };
        _totals.Margin = new Thickness(0, 0, 0, 14); DockPanel.SetDock(_totals, Dock.Top); center.Children.Add(_totals);
        _detail.Margin = new Thickness(0, 14, 0, 0); _detail.MinHeight = 36; DockPanel.SetDock(_detail, Dock.Bottom); center.Children.Add(_detail);
        center.Children.Add(new ScrollViewer { Content = _board, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(center, 1); body.Children.Add(center);
        var issuesPanel = new DockPanel { Background = Brushes.White }; _issuesPanel = issuesPanel;
        var issuesTitle = Ui.Text("检查与提醒", 16); issuesTitle.FontWeight = FontWeights.SemiBold;
        issuesTitle.Margin = new Thickness(12, 12, 12, 15); DockPanel.SetDock(issuesTitle, Dock.Top); issuesPanel.Children.Add(issuesTitle);
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new Binding(nameof(Issue.Message)));
        factory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        factory.SetValue(TextBlock.FontSizeProperty, 12.0);
        factory.SetValue(TextBlock.ForegroundProperty, Ui.Warning);
        factory.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 8, 6, 8));
        _issues.ItemTemplate = new DataTemplate { VisualTree = factory };
        var itemStyle = new Style(typeof(ListBoxItem)); itemStyle.Setters.Add(new Setter(HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        _issues.ItemContainerStyle = itemStyle;
        ScrollViewer.SetHorizontalScrollBarVisibility(_issues, ScrollBarVisibility.Disabled);
        issuesPanel.Children.Add(_issues); Grid.SetColumn(issuesPanel, 2); body.Children.Add(issuesPanel);
        root.Children.Add(body); Content = root;
        _board.PlaceRequested += Place;
        _board.SwapRequested += Swap;
        _search.TextChanged += (_, _) => Refresh();
        _confirmed.Checked += (_, _) => ConfirmCollection(true);
        _confirmed.Unchecked += (_, _) => ConfirmCollection(false);
        _roster.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _selected = (_roster.SelectedItem as PersonSummary)?.Name; _focusSlot = null; RefreshBoardAndDetail();
        };
        _roster.MouseDoubleClick += (_, _) => { if (!_busy && _roster.SelectedItem is not null) Edit(); };
        _roster.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(_roster);
        _roster.PreviewMouseMove += (_, e) =>
        {
            if (_busy || e.LeftButton != MouseButtonState.Pressed || _roster.SelectedItem is not PersonSummary selected) return;
            var offset = e.GetPosition(_roster) - _dragStart;
            if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            DragDrop.DoDragDrop(_roster, new DataObject("Duty.Person", selected.Name), DragDropEffects.Copy);
        };
        _issues.SelectionChanged += (_, _) =>
        {
            if (_refreshing || _issues.SelectedItem is not Issue issue) return;
            FocusIssue(issue);
        };
        PreviewKeyDown += (_, e) =>
        {
            if (_busy || Keyboard.Modifiers != ModifierKeys.Control || e.OriginalSource is TextBox) return;
            if (e.Key == Key.Z) { Undo(); e.Handled = true; }
            if (e.Key == Key.Y) { Redo(); e.Handled = true; }
        };
        Closing += OnClosing;
        SizeChanged += (_, _) =>
        {
            var narrow = ActualWidth < 1250;
            _body.ColumnDefinitions[2].Width = new GridLength(narrow ? 0 : 260);
            _issuesPanel.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
            _body.ColumnDefinitions[0].Width = new GridLength(narrow ? 230 : 260);
        };
        Ui.FitWorkArea(this);
        Refresh();
    }

    private void FocusIssue(Issue issue)
    {
        if (issue.Name is not null)
        {
            _selected = issue.Name;
            if (!string.IsNullOrEmpty(_search.Text)) _search.Clear();
            _refreshing = true; _roster.SelectedItem = _roster.Items.Cast<PersonSummary>().FirstOrDefault(p => p.Name == _selected); _refreshing = false;
        }
        _focusSlot = issue.Slot; RefreshBoardAndDetail();
    }
    private void ShowIssues()
    {
        var window = new Window { Owner = this, Title = "检查与提醒", Width = 480, Height = 600, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        Ui.FitWorkArea(window, 420, 400);
        var layout = new DockPanel { Margin = new Thickness(16) };
        var close = Ui.Button("\uE711", "关闭检查窗口", window.Close, "关闭");
        close.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(close, Dock.Bottom); layout.Children.Add(close);
        var list = new ListBox { ItemsSource = _issues.ItemsSource, ItemTemplate = _issues.ItemTemplate, ItemContainerStyle = _issues.ItemContainerStyle,
            BorderThickness = new Thickness(0), Background = Brushes.White };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.SelectionChanged += (_, _) => { if (list.SelectedItem is Issue issue) { FocusIssue(issue); window.Close(); } };
        layout.Children.Add(list); window.Content = layout; window.ShowDialog();
    }
    private FrameworkElement CreatePeoplePanel()
    {
        var panel = new DockPanel { Background = Brushes.White };
        var top = new StackPanel { Margin = new Thickness(12, 12, 12, 12) };
        var heading = new DockPanel();
        _peopleCount.HorizontalAlignment = HorizontalAlignment.Right; DockPanel.SetDock(_peopleCount, Dock.Right); heading.Children.Add(_peopleCount);
        var title = Ui.Text("本次人员", 16); title.FontWeight = FontWeights.SemiBold; heading.Children.Add(title); top.Children.Add(heading);
        var row = new DockPanel { Margin = new Thickness(0, 14, 0, 12) };
        var icon = Ui.Icon("\uE721", 15); icon.Margin = new Thickness(0, 0, 8, 0); row.Children.Add(icon);
        _search.ToolTip = "搜索姓名"; row.Children.Add(_search); top.Children.Add(row);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var button in new[] { Ui.Button("\uE710", "新增填报", Add), Ui.Button("\uE70F", "编辑所选同学填报", Edit),
                     Ui.Button("\uE74D", "删除所选同学", Delete) })
        { actions.Children.Add(button); _dataButtons.Add(button); }
        top.Children.Add(actions); DockPanel.SetDock(top, Dock.Top); panel.Children.Add(top);
        var cellText = new Style(typeof(TextBlock));
        cellText.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        _roster.Columns.Add(new DataGridTextColumn { Header = "姓名", Binding = new Binding(nameof(PersonSummary.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = cellText });
        _roster.Columns.Add(new DataGridTextColumn { Header = "班量", Binding = new Binding(nameof(PersonSummary.Load)), Width = 61 });
        _roster.Columns.Add(new DataGridTextColumn { Header = "早一", Binding = new Binding(nameof(PersonSummary.MorningLabel)), Width = 62 });
        panel.Children.Add(_roster); return panel;
    }

    private Session Snapshot() => new(_people.ToArray(), _schedule.Copy(), _calculated, _collectionConfirmed, _statusText, _note, _manual);
    private void Restore(Session session)
    {
        _people = session.People.ToList(); _schedule = session.Schedule.Copy(); _calculated = session.Calculated;
        _collectionConfirmed = session.Confirmed; _statusText = session.Status; _note = session.Note; _manual = session.Manual;
        _generation++; Refresh();
    }
    private void Refresh()
    {
        _refreshing = true;
        var evaluation = ScheduleRules.Evaluate(_people, _schedule);
        var query = _search.Text.Trim();
        _roster.ItemsSource = evaluation.People.Where(p => p.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        _roster.SelectedItem = _roster.Items.Cast<PersonSummary>().FirstOrDefault(p => p.Name == _selected);
        _peopleCount.Text = $"{_people.Count}人";
        _confirmed.IsChecked = _collectionConfirmed;
        _totals.Text = _calculated
            ? $"已排 {evaluation.Assigned}/21人次    缺员 {evaluation.Gaps}    优先 {evaluation.Preferred}    到岗 {evaluation.Days}人天"
            : $"已收集 {_people.Count}人    目标 {_people.Sum(p => p.Target)}节    待排岗位 21人次";
        var issues = new List<Issue>();
        if (_people.Count == 0) issues.Add(new("暂无填报记录。"));
        else
        {
            if (!_collectionConfirmed) issues.Add(new("尚未确认记录已经收齐。"));
            if (_calculated) issues.AddRange(evaluation.Issues);
            else
            {
                foreach (var person in _people)
                {
                    if (person.Available < person.Target) issues.Add(new($"{person.Name}仅{person.Available}个可排时段，少于目标{person.Target}节。", person.Name));
                    if (person.AvailableMornings == 0) issues.Add(new($"{person.Name}没有可排早一。", person.Name));
                }
                if (_people.Count > 5) issues.Add(new("只有5个早一岗位，无法让超过5人都满足早一要求。"));
                if (_people.Sum(p => p.Target) < 21) issues.Add(new($"总目标班量{_people.Sum(p => p.Target)}节，至少缺{21 - _people.Sum(p => p.Target)}人次。"));
                if (_people.Count(p => p.Special) < 2) issues.Add(new("特殊班愿意参加的人数不足2人。", Slot: 7));
            }
        }
        _issues.ItemsSource = issues;
        _status.Text = _statusText; _status.ToolTip = _note;
        _status.Foreground = _busy ? Ui.Accent : Ui.Muted;
        _refreshing = false;
        RefreshBoardAndDetail(); RefreshButtons();
    }
    private void RefreshBoardAndDetail()
    {
        _board.Load(_people, _schedule, _selected, _focusSlot, _calculated);
        var person = _people.FirstOrDefault(p => p.Name == _selected);
        _detail.Text = person is null ? (_calculated ? _note : "")
            : $"{person.Name}    可排{person.Available}格    优先{person.Preferred}格    早一可排{person.AvailableMornings}格";
    }
    private void RefreshButtons()
    {
        _solve.IsEnabled = !_busy && _people.Count > 0;
        _cancel.IsEnabled = _busy;
        _export.IsEnabled = !_busy && _calculated && _people.Count > 0;
        _undo.IsEnabled = !_busy && _history.CanUndo; _redo.IsEnabled = !_busy && _history.CanRedo;
        foreach (var button in _dataButtons) button.IsEnabled = !_busy;
        _board.IsEnabled = !_busy; _roster.IsEnabled = !_busy; _seconds.IsEnabled = !_busy;
        _confirmed.IsEnabled = !_busy;
    }
    private void ConfirmCollection(bool value)
    {
        if (_refreshing) return;
        _collectionConfirmed = value; Refresh();
    }
    private bool ConfirmDataChange() => !_calculated || _verification || MessageBox.Show(this,
        "修改填报记录会清空当前排班，可通过撤销恢复。是否继续？", "更新填报", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
    private void SetPeople(List<Person> people, string status)
    {
        _history.Push(Snapshot()); _people = people; _schedule = new(); _calculated = false; _manual = false; _note = "";
        _collectionConfirmed = false; _statusText = status; _generation++; Refresh();
    }
    private void Import()
    {
        var dialog = new ImportDialog(this);
        if (dialog.ShowDialog() != true || !ConfirmDataChange()) return;
        ApplyImported(dialog.Valid, true);
    }
    public void ApplyImported(IEnumerable<Person> records, bool confirmReplacements)
    {
        var proposed = _people.ToList(); var added = 0; var replaced = 0; var skipped = 0;
        foreach (var person in records)
        {
            var index = proposed.FindIndex(p => p.Name == person.Name);
            if (index < 0) { proposed.Add(person); added++; continue; }
            if (SubmissionCodec.Encode(proposed[index]) == SubmissionCodec.Encode(person)) { skipped++; continue; }
            if (confirmReplacements)
            {
                var answer = MessageBox.Show(this, $"{person.Name}已有不同的填报。\n旧：每周{proposed[index].Target}节，可排{proposed[index].Available}格\n新：每周{person.Target}节，可排{person.Available}格\n是否替换？",
                    "同名记录核对", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (answer == MessageBoxResult.Cancel) return;
                if (answer == MessageBoxResult.No) { skipped++; continue; }
            }
            proposed[index] = person; replaced++;
        }
        if (added + replaced > 0) SetPeople(proposed, $"导入完成：新增{added}人，替换{replaced}人，跳过{skipped}条");
        else { _statusText = $"没有新增或替换记录，跳过{skipped}条"; Refresh(); }
    }
    private void Add()
    {
        var dialog = new PersonDialog(this, null);
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        if (_people.Any(p => p.Name == dialog.Result.Name)) { MessageBox.Show(this, "该姓名已存在，请编辑原记录或核对同名情况。"); return; }
        if (!ConfirmDataChange()) return;
        SetPeople([.. _people, dialog.Result], "已新增填报，请重新排班");
    }
    private void Edit()
    {
        var selected = _people.FirstOrDefault(p => p.Name == (_roster.SelectedItem as PersonSummary)?.Name);
        if (selected is null) return;
        var dialog = new PersonDialog(this, selected);
        if (dialog.ShowDialog() != true || dialog.Result is null) return;
        if (_people.Any(p => p != selected && p.Name == dialog.Result.Name)) { MessageBox.Show(this, "修改后的姓名与其他记录重复。"); return; }
        if (!ConfirmDataChange()) return;
        var proposed = _people.ToList(); proposed[proposed.IndexOf(selected)] = dialog.Result;
        _selected = dialog.Result.Name; SetPeople(proposed, "填报已更新，请重新排班");
    }
    private void Delete()
    {
        var name = (_roster.SelectedItem as PersonSummary)?.Name;
        if (name is null) return;
        if (MessageBox.Show(this, $"将{name}移出本次排班？", "删除记录", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes || !ConfirmDataChange()) return;
        SetPeople(_people.Where(p => p.Name != name).ToList(), "已删除记录，请重新排班");
    }
    private void Clear()
    {
        if (_people.Count == 0) return;
        if (MessageBox.Show(this, "清空本次所有人员和排班结果？", "清空", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetPeople([], "本次数据已清空");
    }
    private void Undo() { if (!_busy && _history.CanUndo) Restore(_history.Undo(Snapshot())); }
    private void Redo() { if (!_busy && _history.CanRedo) Restore(_history.Redo(Snapshot())); }
    public void Place(int slot, int position, string? name)
    {
        if (_busy || _schedule.Cells[slot][position] == name) return;
        if (!ScheduleRules.TryPlace(_people, _schedule, slot, position, name, out var next, out var error))
        { _statusText = error; Refresh(); return; }
        _history.Push(Snapshot()); _schedule = next; MarkManual();
    }
    public void Swap(int fromSlot, int fromPosition, int toSlot, int toPosition)
    {
        if (_busy || (fromSlot == toSlot && fromPosition == toPosition)) return;
        if (!ScheduleRules.TrySwap(_people, _schedule, fromSlot, fromPosition, toSlot, toPosition, out var next, out var error))
        { _statusText = error; Refresh(); return; }
        _history.Push(Snapshot()); _schedule = next; MarkManual();
    }
    private void MarkManual()
    {
        _calculated = true; _manual = true; _note = "当前为人工调整结果，已重新检查规则；不沿用自动求解的最优性标记。";
        _statusText = "已人工调整，检查信息已更新"; Refresh();
    }
    public async Task SolveAsync(bool interactive = true)
    {
        if (_busy || _people.Count == 0) return;
        if (interactive && _calculated && MessageBox.Show(this, "重新计算将替换当前排班结果，可撤销。是否继续？",
            "自动排班", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        var before = Snapshot(); var generation = _generation; var input = _people.ToArray();
        _busy = true; _cancellation = new(); RefreshButtons();
        var progress = new Progress<string>(stage => { _status.Text = stage; _status.Foreground = Ui.Accent; });
        var seconds = (int)((ComboBoxItem)_seconds.SelectedItem).Tag;
        try
        {
            var result = await Task.Run(() => Scheduler.Solve(input, TimeSpan.FromSeconds(seconds), _cancellation.Token, progress));
            if (generation != _generation) { _statusText = "输入已变化，未采用旧计算结果"; return; }
            _history.Push(before); _schedule = result.Schedule; _calculated = true; _manual = false; _note = result.Explanation;
            _statusText = (result.Draft ? "草案" : "个人要求已满足") + (result.ProvenOptimal ? " · 分层最优" : " · 当前可行，未证全部最优");
        }
        catch (OperationCanceledException) { _statusText = "计算已取消，保留原排班"; }
        catch (Exception e) { _statusText = "计算失败：" + e.Message; }
        finally
        {
            _busy = false; _cancellation.Dispose(); _cancellation = null; Refresh();
            if (_closeAfterCancel) { _closeAfterCancel = false; Close(); }
        }
    }
    private void Export()
    {
        var evaluation = ScheduleRules.Evaluate(_people, _schedule);
        if (!_collectionConfirmed || !evaluation.PersonalSatisfied || evaluation.Gaps > 0)
        {
            var text = $"当前已排{evaluation.Assigned}/21人次，缺{evaluation.Gaps}人次。\n" +
                (!_collectionConfirmed ? "尚未确认收齐记录。\n" : "") +
                (!evaluation.PersonalSatisfied ? "部分同学的班量或早一未满足。\n" : "") +
                "这些提醒会写入生成表，是否继续？";
            if (MessageBox.Show(this, text, "带提醒导出", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        }
        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "输出");
            string path;
            try { path = ExportTo(folder); }
            catch (Exception e) when (e is UnauthorizedAccessException or DirectoryNotFoundException)
            {
                MessageBox.Show(this, "默认输出目录不可写，请选择本次输出文件夹。");
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "选择排班表输出文件夹" };
                if (dialog.ShowDialog(this) != true) return;
                path = ExportTo(dialog.FolderName);
            }
            _statusText = "已生成：" + Path.GetFileName(path); Refresh();
            if (MessageBox.Show(this, $"已生成：{path}\n是否打开输出文件夹？", "导出完成",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception e) { MessageBox.Show(this, "导出失败：" + e.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    public string ExportTo(string folder) => ExcelExporter.Export(Path.Combine(AppContext.BaseDirectory, "排班模板.xlsx"),
        folder, _people, _schedule, _collectionConfirmed, _note);
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_busy) { _closeAfterCancel = true; _cancellation?.Cancel(); e.Cancel = true; return; }
        if (!_verification && _people.Count > 0 && MessageBox.Show(this, "关闭后本次输入与排班会清空。是否关闭？",
            "关闭程序", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) e.Cancel = true;
    }

    public async Task VerifyUiAsync(string directory)
    {
        if (_people.Count != 0 || _calculated) throw new Exception("Fresh scheduler contains retained data.");
        Ui.Screenshot(this, Path.Combine(directory, "排班端_初始.png"));
        var people = Enumerable.Range(0, 5).Select(i => new Person("测试" + (char)('A' + i), 4,
            Enumerable.Range(0, 20).Select(s => (byte)(s / 4 == i ? 2 : 1)))).ToArray();
        ApplyImported(people.Select(p => SubmissionCodec.Decode(SubmissionCodec.Encode(p))), false);
        _collectionConfirmed = true;
        await SolveAsync(false);
        if (!_calculated || !ScheduleRules.Evaluate(_people, _schedule).PersonalSatisfied) throw new Exception("UI solve failed.");
        _selected = people[1].Name; Refresh();
        Ui.Screenshot(this, Path.Combine(directory, "排班端_已排班.png"));
        var occupied = Enumerable.Range(0, 20).First(s => _schedule.Cells[s][0] is not null);
        var removed = _schedule.Cells[occupied][0];
        Place(occupied, 0, null);
        if (!_manual || _schedule.Cells[occupied][0] is not null) throw new Exception("Manual adjustment failed.");
        Undo(); if (_schedule.Cells[occupied][0] != removed) throw new Exception("Undo failed.");
        Redo(); if (_schedule.Cells[occupied][0] is not null) throw new Exception("Redo failed.");
        Undo();
        var generated = ExportTo(Path.Combine(directory, "导出验证"));
        if (!File.Exists(generated)) throw new Exception("Excel was not created.");
        Width = MinWidth; Height = MinHeight; Ui.Pump();
        Ui.Screenshot(this, Path.Combine(directory, "排班端_最小窗口.png"));
        _history.Push(Snapshot()); _people = [new Person("不可排测试", 3, new byte[20])]; _calculated = false; _schedule = new(); _generation++;
        await SolveAsync(false);
        if (!_calculated || _schedule.Assigned != 0 || !_statusText.Contains("草案")) throw new Exception("Draft warning UI failed.");
        Ui.Screenshot(this, Path.Combine(directory, "排班端_草案.png"));
    }
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application(); Ui.Initialize(app);
        var verify = args.Length == 2 && args[0] is "--verify-ui" or "--verify-portable";
        var window = new AdminWindow(verify);
        if (verify)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -12000; window.Top = -12000; window.ShowInTaskbar = false;
            var exit = 0;
            window.Loaded += async (_, _) =>
            {
                try { await window.VerifyUiAsync(args[1]); if (args[0] == "--verify-portable") Ui.AssertPortable(); }
                catch (Exception e) { Console.Error.WriteLine(e); exit = 1; }
                finally { window.Close(); }
            };
            app.Run(window); return exit;
        }
        app.Run(window); return 0;
    }
}
