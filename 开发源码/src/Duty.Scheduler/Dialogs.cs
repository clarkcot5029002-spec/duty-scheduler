using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Duty.Core;
using Duty.UI;

namespace Duty.Desktop;

public sealed class PersonDialog : Window
{
    private readonly PreferenceEditor _editor = new();
    private readonly TextBlock _error = Ui.Text("", 12, Ui.Error);
    public Person? Result { get; private set; }
    public PersonDialog(Window owner, Person? person)
    {
        Ui.StyleWindow(this);
        Owner = owner; Title = person is null ? "新增填报" : "编辑填报";
        Width = 890; Height = 800; MinWidth = 830; MinHeight = 740; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var bottom = new DockPanel { Margin = new Thickness(0, 18, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        actions.Children.Add(Ui.Button("\uE73E", "确认本次修改", Save, "确认", true));
        actions.Children.Add(Ui.Button("\uE711", "取消", () => DialogResult = false, "取消"));
        DockPanel.SetDock(actions, Dock.Right); bottom.Children.Add(actions); bottom.Children.Add(_error);
        DockPanel.SetDock(bottom, Dock.Bottom); root.Children.Add(bottom);
        root.Children.Add(new ScrollViewer { Content = _editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        if (person is not null) _editor.LoadPerson(person);
        Ui.FitWorkArea(this, 800, 600);
    }
    private void Save()
    {
        try { Result = _editor.GetPerson(); DialogResult = true; }
        catch (ArgumentException e) { _error.Text = e.Message; }
    }
}

public sealed class ImportDialog : Window
{
    private readonly TextBox _input = new()
    {
        AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"),
        FontSize = 14, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxLength = 256000
    };
    private readonly ListBox _results = new() { BorderThickness = new Thickness(0), Background = Brushes.White };
    private readonly TextBlock _status = Ui.Text("待校验", 13, Ui.Muted);
    private readonly Button _accept;
    public List<Person> Valid { get; } = [];
    public ImportDialog(Window owner)
    {
        Ui.StyleWindow(this);
        Owner = owner; Title = "导入填报码"; Width = 800; Height = 720; MinWidth = 650; MinHeight = 590;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(24) };
        var heading = Ui.Text("填报码", 19); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0, 0, 0, 15);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(Ui.Button("\uE77F", "校验所有行", Validate, "校验"));
        _accept = Ui.Button("\uE8B5", "导入已通过校验的记录", () => DialogResult = true, "导入有效记录", true);
        _accept.IsEnabled = false; buttons.Children.Add(_accept);
        buttons.Children.Add(Ui.Button("\uE711", "取消导入", () => DialogResult = false));
        DockPanel.SetDock(buttons, Dock.Right); footer.Children.Add(buttons); footer.Children.Add(_status);
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var grid = new Grid();
        grid.RowDefinitions.Add(new() { Height = new GridLength(3, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = new GridLength(2, GridUnitType.Star) });
        grid.Children.Add(_input); Grid.SetRow(_results, 1); _results.Margin = new Thickness(0, 16, 0, 0); grid.Children.Add(_results);
        root.Children.Add(grid); Content = root;
        Ui.FitWorkArea(this, 650, 550);
        _input.TextChanged += (_, _) => { Valid.Clear(); _results.Items.Clear(); _accept.IsEnabled = false; _status.Text = "待校验"; };
    }
    public void Validate()
    {
        Valid.Clear(); _results.Items.Clear();
        var lines = _input.Text.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        var nonEmpty = lines.Count(l => !string.IsNullOrWhiteSpace(l));
        if (nonEmpty > 500) { _status.Text = "单次最多校验500条，请分批导入。"; return; }
        var errors = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            try
            {
                var person = SubmissionCodec.Decode(lines[i]);
                Valid.Add(person);
                _results.Items.Add(Ui.Text($"第{i + 1}行  {person.Name}  每周{person.Target}节  可排{person.Available}格", 12, Ui.Accent));
            }
            catch (FormatException e)
            {
                errors++;
                _results.Items.Add(Ui.Text($"第{i + 1}行  {e.Message}", 12, Ui.Error));
            }
        }
        _status.Text = $"通过{Valid.Count}条，错误{errors}条";
        _accept.IsEnabled = Valid.Count > 0;
    }
}
