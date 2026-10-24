using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Duty.Core;

namespace Duty.UI;

public sealed class PaintBoard : Grid
{
    private readonly Border[] _cells = new Border[20];
    private readonly byte[] _values = PreferenceMarks.CreateDefault();
    private readonly HashSet<int> _visited = [];
    private bool _painting;
    private byte _strokeValue;
    private byte _brushState = 2;
    public byte BrushState
    {
        get => _brushState;
        set
        {
            if (value is not (0 or 2)) throw new ArgumentException("请选择优先安排或不可安排。");
            _brushState = value;
        }
    }
    public event Action? BeginningEdit;
    public event Action? Changed;
    public event Action? Completed;
    public byte[] Values => (byte[])_values.Clone();

    public PaintBoard()
    {
        Background = Brushes.White;
        ColumnDefinitions.Add(new() { Width = new GridLength(86) });
        for (var d = 0; d < 5; d++) ColumnDefinitions.Add(new());
        RowDefinitions.Add(new() { Height = new GridLength(42) });
        for (var p = 0; p < 4; p++) RowDefinitions.Add(new() { Height = new GridLength(88) });
        for (var d = 0; d < 5; d++)
        {
            var day = d;
            var header = Header(Week.Days[d], () => PaintMany(Enumerable.Range(day * 4, 4)));
            SetColumn(header, d + 1); Children.Add(header);
        }
        for (var p = 0; p < 4; p++)
        {
            var period = p;
            var header = Header((p < 2 ? "上午\n" : "下午\n") + Week.Periods[p],
                () => PaintMany(Enumerable.Range(0, 5).Select(d => d * 4 + period)));
            SetRow(header, p + 1); Children.Add(header);
            for (var d = 0; d < 5; d++)
            {
                var slot = d * 4 + p;
                var cell = new Border
                {
                    Margin = new Thickness(3), CornerRadius = new CornerRadius(4), Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(slot == 7 ? 2 : 1), Tag = slot,
                    ToolTip = Week.Label(slot) + (slot == 7 ? "（特殊班，2人）" : "")
                };
                cell.MouseLeftButtonDown += (_, e) =>
                {
                    BeginningEdit?.Invoke();
                    _painting = true; _visited.Clear();
                    _strokeValue = PreferenceMarks.Toggle(_values[slot], BrushState);
                    CaptureMouse(); Paint(slot); e.Handled = true;
                };
                _cells[slot] = cell;
                SetColumn(cell, d + 1); SetRow(cell, p + 1); Children.Add(cell);
            }
        }
        MouseMove += (_, e) =>
        {
            if (!_painting) return;
            if (e.LeftButton != MouseButtonState.Pressed) { CompleteStroke(); return; }
            var hit = InputHitTest(e.GetPosition(this)) as DependencyObject;
            while (hit is not null && hit != this)
            {
                if (hit is Border { Tag: int slot }) { Paint(slot); return; }
                hit = VisualTreeHelper.GetParent(hit);
            }
        };
        MouseLeftButtonUp += (_, e) =>
        {
            if (!_painting) return;
            CompleteStroke(); e.Handled = true;
        };
        LostMouseCapture += (_, _) => CompleteStroke();
        Refresh();
    }
    private static Button Header(string name, Action paint) => new Button
    {
        Content = new TextBlock { Text = name, TextAlignment = TextAlignment.Center, FontSize = 13 },
        Background = Ui.Brush("#F7F9F7"), BorderThickness = new Thickness(0),
        ToolTip = "批量设置" + name.Replace("\n", ""), Tag = paint
    }.WithClick(paint);

    public void Load(byte[] values)
    {
        if (values.Length != 20 || values.Any(v => v > 2)) throw new ArgumentException("时间格无效。");
        values.CopyTo(_values, 0); Refresh();
    }
    public void SetSpecial(bool enabled)
    {
        _values[7] = enabled ? (byte)Math.Max(1, (int)_values[7]) : (byte)0;
        Refresh(); Changed?.Invoke();
    }
    public void PaintMany(IEnumerable<int> slots)
    {
        var selected = slots.Distinct().ToArray();
        if (selected.Any(s => s is < 0 or >= Week.SlotCount)) throw new ArgumentException("时间格无效。");
        if (selected.Length == 0) return;
        BeginningEdit?.Invoke();
        var value = selected.All(s => _values[s] == BrushState) ? (byte)1 : BrushState;
        foreach (var s in selected) _values[s] = value;
        Refresh(); Changed?.Invoke(); Completed?.Invoke();
    }
    private void CompleteStroke()
    {
        if (!_painting) return;
        _painting = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Completed?.Invoke();
    }
    private void Paint(int slot)
    {
        if (!_visited.Add(slot)) return;
        // One gesture paints or erases consistently, including when it crosses mixed states.
        _values[slot] = _strokeValue;
        Refresh(); Changed?.Invoke();
    }
    private void Refresh()
    {
        for (var s = 0; s < 20; s++)
        {
            var value = _values[s];
            var cell = _cells[s];
            cell.Background = value == 1 ? Brushes.White : Ui.Brush(Ui.StateColors[value]);
            cell.BorderBrush = Ui.Brush(s == 7 ? "#8EA99A" : "#E0E6E3");
            cell.ToolTip = Week.Label(s) + "：" + (value == 1 ? "可排" : Week.Preferences[value])
                + (s == Week.SpecialSlot ? "（特殊班，2人）" : "");
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (value != 1)
            {
                var icon = Ui.Icon(Ui.Glyphs[value], 19); icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.Foreground = Ui.Brush(Ui.StateInks[value]); stack.Children.Add(icon);
                stack.Children.Add(new TextBlock { Text = Week.Preferences[value], FontSize = 12,
                    Foreground = Ui.Brush(Ui.StateInks[value]), TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 7, 0, 0) });
            }
            if (s == 7) stack.Children.Add(new TextBlock { Text = "特殊班 · 2人", FontSize = 10,
                Foreground = Ui.Muted, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 2, 0, 0) });
            cell.Child = stack;
        }
    }
}

internal static class ButtonExtensions
{
    public static Button WithClick(this Button button, Action action) { button.Click += (_, _) => action(); return button; }
}
