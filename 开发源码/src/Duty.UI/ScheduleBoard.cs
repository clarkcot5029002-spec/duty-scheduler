using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Duty.Core;

namespace Duty.UI;

public sealed record SlotDrag(int Slot, int Position);
public sealed class ScheduleBoard : Grid
{
    private IReadOnlyList<Person> _people = [];
    private Schedule _schedule = new();
    private string? _selected;
    private int? _focusedSlot;
    private bool _computed;
    private readonly Border[] _cells = new Border[20];
    private readonly List<Button> _positions = [];
    private Point _dragStart;
    public event Action<int, int, string?>? PlaceRequested;
    public event Action<int, int, int, int>? SwapRequested;

    public ScheduleBoard()
    {
        Background = Brushes.White;
        ColumnDefinitions.Add(new() { Width = new GridLength(68) });
        for (var d = 0; d < 5; d++) ColumnDefinitions.Add(new());
        RowDefinitions.Add(new() { Height = new GridLength(42) });
        for (var p = 0; p < 4; p++) RowDefinitions.Add(new() { Height = new GridLength(106) });
        for (var d = 0; d < 5; d++)
        {
            var day = Ui.Text(Week.Days[d], 13, Ui.Muted); day.TextAlignment = TextAlignment.Center;
            SetColumn(day, d + 1); Children.Add(day);
        }
        for (var p = 0; p < 4; p++)
        {
            var label = Ui.Text((p < 2 ? "上午\n" : "下午\n") + Week.Periods[p], 12, Ui.Muted);
            label.TextAlignment = TextAlignment.Center; SetRow(label, p + 1); Children.Add(label);
            for (var d = 0; d < 5; d++)
            {
                var s = d * 4 + p;
                var frame = new Border { Margin = new Thickness(3), Background = Brushes.White,
                    BorderBrush = Ui.Brush("#DCE4DF"), BorderThickness = new Thickness(s == 7 ? 2 : 1), CornerRadius = new CornerRadius(4) };
                _cells[s] = frame;
                var content = new Grid();
                for (var position = 0; position < Week.Capacity(s); position++)
                {
                    content.RowDefinitions.Add(new());
                    var slot = s; var index = position;
                    var button = new Button { BorderThickness = new Thickness(0), Background = Brushes.Transparent,
                        AllowDrop = true, Padding = new Thickness(4), Margin = new Thickness(1), Tag = new SlotDrag(slot, index) };
                    button.Click += (_, _) => ShowCandidates(button, slot, index);
                    button.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(button);
                    button.PreviewMouseMove += (_, e) =>
                    {
                        if (e.LeftButton != MouseButtonState.Pressed || _schedule.Cells[slot][index] is null) return;
                        var offset = e.GetPosition(button) - _dragStart;
                        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                        DragDrop.DoDragDrop(button, new DataObject("Duty.Slot", new SlotDrag(slot, index)), DragDropEffects.Move);
                    };
                    button.DragOver += (_, e) =>
                    {
                        e.Effects = CanDrop(e.Data, slot, index)
                            ? (e.Data.GetDataPresent("Duty.Person") ? DragDropEffects.Copy : DragDropEffects.Move)
                            : DragDropEffects.None; e.Handled = true;
                        frame.BorderBrush = e.Effects == DragDropEffects.None ? Ui.Error : Ui.Accent;
                    };
                    button.DragLeave += (_, _) => RefreshFrames();
                    button.Drop += (_, e) =>
                    {
                        if (e.Data.GetData("Duty.Slot") is SlotDrag source)
                            SwapRequested?.Invoke(source.Slot, source.Position, slot, index);
                        else if (e.Data.GetData("Duty.Person") is string name) PlaceRequested?.Invoke(slot, index, name);
                        RefreshFrames(); e.Handled = true;
                    };
                    SetRow(button, position); content.Children.Add(button); _positions.Add(button);
                }
                frame.Child = content; SetColumn(frame, d + 1); SetRow(frame, p + 1); Children.Add(frame);
            }
        }
        Refresh();
    }
    public void Load(IReadOnlyList<Person> people, Schedule schedule, string? selected = null, int? focusedSlot = null, bool computed = true)
    {
        _people = people; _schedule = schedule; _selected = selected; _focusedSlot = focusedSlot; _computed = computed; Refresh();
    }
    private bool CanDrop(IDataObject data, int slot, int position)
    {
        if (data.GetData("Duty.Slot") is SlotDrag from)
            return ScheduleRules.TrySwap(_people, _schedule, from.Slot, from.Position, slot, position, out _, out _);
        return data.GetData("Duty.Person") is string name
            && ScheduleRules.TryPlace(_people, _schedule, slot, position, name, out _, out _);
    }
    private void ShowCandidates(Button button, int slot, int position)
    {
        var menu = new ContextMenu();
        var clear = new MenuItem { Header = "无（清空岗位）" };
        clear.Click += (_, _) => PlaceRequested?.Invoke(slot, position, null); menu.Items.Add(clear);
        menu.Items.Add(new Separator());
        var summaries = ScheduleRules.Evaluate(_people, _schedule).People.ToDictionary(p => p.Name);
        foreach (var person in _people.OrderByDescending(p => p.Preferences[slot]).ThenBy(p => p.Name, StringComparer.Ordinal))
        {
            var summary = summaries[person.Name];
            var allowed = ScheduleRules.TryPlace(_people, _schedule, slot, position, person.Name, out _, out var error);
            var item = new MenuItem { Header = $"{person.Name}    {summary.Load}    {Week.Preferences[person.Preferences[slot]]}",
                IsEnabled = allowed, ToolTip = allowed ? Week.Label(slot) : error };
            var name = person.Name; item.Click += (_, _) => PlaceRequested?.Invoke(slot, position, name);
            menu.Items.Add(item);
        }
        button.ContextMenu = menu; menu.PlacementTarget = button; menu.IsOpen = true;
    }
    private void RefreshFrames()
    {
        var selected = _people.FirstOrDefault(p => p.Name == _selected);
        for (var s = 0; s < 20; s++)
        {
            _cells[s].Background = selected is null ? Brushes.White : Ui.Brush(Ui.StateColors[selected.Preferences[s]]);
            var active = _schedule.Cells[s].Contains(_selected) && _selected is not null;
            _cells[s].BorderBrush = _focusedSlot == s ? Ui.Warning : active ? Ui.Accent
                : Ui.Brush(s == 7 ? "#A1B4A8" : "#DCE4DF");
            _cells[s].BorderThickness = new Thickness(active || _focusedSlot == s || s == 7 ? 2 : 1);
        }
    }
    private void Refresh()
    {
        RefreshFrames();
        foreach (var button in _positions)
        {
            var location = (SlotDrag)button.Tag;
            var name = _schedule.Cells[location.Slot][location.Position];
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var text = Ui.Text(name ?? (_computed ? "无" : "待排"), name is null ? 14 : 16, name is null ? Ui.Muted : Ui.Brush("#24372B"));
            text.TextAlignment = TextAlignment.Center; text.TextTrimming = TextTrimming.CharacterEllipsis;
            text.MaxHeight = location.Slot == 7 ? 27 : 49; stack.Children.Add(text);
            if (location.Slot == 7)
                stack.Children.Add(new TextBlock { Text = $"特殊班 {location.Position + 1}/2", FontSize = 10,
                    Foreground = Ui.Muted, TextAlignment = TextAlignment.Center });
            else if (name is not null)
            {
                var person = _people.FirstOrDefault(p => p.Name == name);
                stack.Children.Add(new TextBlock { Text = person?.Preferences[location.Slot] == 2 ? "优先时段" : "正常时段",
                    FontSize = 11, Foreground = person?.Preferences[location.Slot] == 2 ? Ui.Accent : Ui.Muted,
                    TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 6, 0, 0) });
            }
            button.Content = stack;
            button.ToolTip = Week.Label(location.Slot) + "：" + (name ?? "未安排");
        }
    }
}
