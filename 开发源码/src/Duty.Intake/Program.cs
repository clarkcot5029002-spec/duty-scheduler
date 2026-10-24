using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Duty.Core;
using Duty.UI;

namespace Duty.Intake;

public sealed class FillWindow : Window
{
    public PreferenceEditor Editor { get; } = new();
    private readonly UndoHistory<FormDraft> _history = new();
    private readonly TextBox _code = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"),
        FontSize = 16, MinHeight = 150, VerticalContentAlignment = VerticalAlignment.Top, Background = Ui.Brush("#F5F8F6"), BorderThickness = new Thickness(0) };
    private readonly TextBlock _status = Ui.Text("尚未生成", 13, Ui.Muted);
    private readonly TextBlock _preview = Ui.Text("", 13);
    private readonly Button _copy;
    private readonly Button _undo;
    private readonly Button _redo;

    public FillWindow()
    {
        Ui.StyleWindow(this);
        Title = "意愿填写系统"; Width = 1200; Height = 820; MinWidth = 1030; MinHeight = 755;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new DockPanel { Margin = new Thickness(26, 20, 26, 20) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 20) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _undo = Ui.Button("\uE7A7", "撤销", Undo);
        _redo = Ui.Button("\uE7A6", "重做", Redo);
        actions.Children.Add(_undo); actions.Children.Add(_redo);
        actions.Children.Add(Ui.Button("\uE74D", "清空本次填写", Clear));
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        var title = Ui.Text("意愿填写", 25); title.FontWeight = FontWeights.SemiBold; header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var body = new Grid();
        body.ColumnDefinitions.Add(new());
        body.ColumnDefinitions.Add(new() { Width = new GridLength(292) });
        var form = new ScrollViewer { Content = Editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 0, 25, 0) };
        body.Children.Add(form);
        var side = new Border { Background = Brushes.White, BorderBrush = Ui.Brush("#DFE6E1"),
            BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(22, 10, 15, 10) };
        var sideStack = new StackPanel();
        var codeHeading = Ui.Text("个人填报码", 17); codeHeading.FontWeight = FontWeights.SemiBold; sideStack.Children.Add(codeHeading);
        _status.Margin = new Thickness(0, 10, 0, 15); sideStack.Children.Add(_status);
        sideStack.Children.Add(_code);
        _preview.Margin = new Thickness(0, 14, 0, 20); sideStack.Children.Add(_preview);
        var generate = Ui.Button("\uE713", "生成填报码", Generate, "生成填报码", true);
        generate.HorizontalAlignment = HorizontalAlignment.Stretch; generate.Margin = new Thickness(0, 0, 0, 10); sideStack.Children.Add(generate);
        _copy = Ui.Button("\uE8C8", "复制填报码", CopyCode, "复制代码");
        _copy.HorizontalAlignment = HorizontalAlignment.Stretch; _copy.Margin = new Thickness(0); sideStack.Children.Add(_copy);
        side.Child = new ScrollViewer { Content = sideStack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetColumn(side, 1); body.Children.Add(side);
        root.Children.Add(body); Content = root;
        Editor.Committed += previous => { _history.Push(previous); RefreshActions(); };
        Editor.Changed += () => { _code.Clear(); _copy.IsEnabled = false; _preview.Text = ""; _status.Text = "填写已修改，待生成"; _status.Foreground = Ui.Muted; };
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            if (e.Key == Key.Z) { Undo(); e.Handled = true; }
            if (e.Key == Key.Y) { Redo(); e.Handled = true; }
        };
        Ui.FitWorkArea(this);
        RefreshActions();
    }

    public void Generate()
    {
        try
        {
            Editor.CommitPending();
            var person = Editor.GetPerson();
            _code.Text = SubmissionCodec.Encode(person);
            _preview.Text = $"{person.Name}\n每周{person.Target}节\n特殊班：{(person.Special ? "愿意参加" : "不参加")}";
            var shortInput = person.Available < person.Target || person.AvailableMornings == 0;
            _status.Text = shortInput ? "已生成，存在待核对的填报条件" : "已生成，校验信息已附加";
            _status.Foreground = shortInput ? Ui.Warning : Ui.Accent;
            _copy.IsEnabled = true;
        }
        catch (ArgumentException e) { _status.Text = e.Message; _status.Foreground = Ui.Error; Editor.NameBox.Focus(); }
    }
    private void CopyCode()
    {
        try { Clipboard.SetText(_code.Text); _status.Text = "已复制"; _status.Foreground = Ui.Accent; }
        catch (System.Runtime.InteropServices.COMException) { _status.Text = "剪贴板暂时忙，请重试。"; }
    }
    private void Undo()
    {
        Editor.CommitPending();
        if (!_history.CanUndo) return;
        Editor.LoadDraft(_history.Undo(Editor.Draft.Copy()));
        InvalidateCode(); RefreshActions();
    }
    private void Redo()
    {
        Editor.CommitPending();
        if (!_history.CanRedo) return;
        Editor.LoadDraft(_history.Redo(Editor.Draft.Copy()));
        InvalidateCode(); RefreshActions();
    }
    private void Clear()
    {
        if (MessageBox.Show(this, "清空本次姓名和所有时间选择？", "清空填写", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Editor.CommitPending(); _history.Push(Editor.Draft.Copy()); Editor.LoadDraft(new("", 3, PreferenceMarks.CreateDefault()));
        InvalidateCode(); RefreshActions();
    }
    private void InvalidateCode() { _code.Clear(); _preview.Text = ""; _status.Text = "待生成"; _copy.IsEnabled = false; }
    private void RefreshActions() { _undo.IsEnabled = _history.CanUndo; _redo.IsEnabled = _history.CanRedo; _copy.IsEnabled = _code.Text.Length > 0; }

    public void VerifyUi(string directory)
    {
        var passed = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            passed++; Console.WriteLine("UI_PASS=" + message);
        }
        Border Cell(int slot) => Editor.Board.Children.OfType<Border>().Single(cell => (int)cell.Tag == slot);
        void ClickSlot(int slot)
        {
            Cell(slot).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = MouseLeftButtonDownEvent });
            Editor.Board.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                { RoutedEvent = MouseLeftButtonUpEvent });
        }
        Check(Editor.NameBox.Text == "" && Editor.Board.Values.SequenceEqual(PreferenceMarks.CreateDefault()),
            "fresh form has unmarked ordinary availability and special opt out");
        Check(((StackPanel)Cell(0).Child).Children.Count == 0, "unmarked ordinary cells have no normal-state label");
        Ui.Screenshot(this, Path.Combine(directory, "填写端_初始.png"));
        Editor.NameBox.Text = "测试同学";
        Editor.SelectTarget(4);
        ClickSlot(0);
        Check(Editor.Board.Values[0] == 2, "first click applies priority marker");
        ClickSlot(0);
        Check(Editor.Board.Values[0] == 1, "second priority click clears to available");
        ClickSlot(0); Editor.SelectBrush(0); ClickSlot(0);
        Check(Editor.Board.Values[0] == 0, "unavailable marker directly replaces priority");
        ClickSlot(0);
        Check(Editor.Board.Values[0] == 1, "second unavailable click clears to available");
        ClickSlot(1); Editor.SelectBrush(2); ClickSlot(1);
        Check(Editor.Board.Values[1] == 2, "priority marker directly replaces unavailable");
        ClickSlot(1);
        Check(Editor.Board.Values[1] == 1, "replaced priority can be cleared");
        Editor.Board.PaintMany([0, 1, 2, 3]);
        Check(Editor.Board.Values.Take(4).All(value => value == 2), "batch priority applies");
        Editor.Board.PaintMany([0, 1, 2, 3]);
        Check(Editor.Board.Values.Take(4).All(value => value == 1), "second batch priority clears");
        Editor.SelectBrush(2); Editor.Board.PaintMany([0, 1, 2, 3]);
        Editor.SpecialBox.IsChecked = true;
        Check(Editor.Board.Values[7] == 1, "special checkbox enables unmarked availability");
        Editor.SpecialBox.IsChecked = false;
        Check(Editor.Board.Values[7] == 0, "special checkbox opts out");
        ClickSlot(7);
        Check(Editor.Board.Values[7] == 2 && Editor.SpecialBox.IsChecked == true, "priority special cell synchronizes checkbox");
        ClickSlot(7);
        Check(Editor.Board.Values[7] == 1 && Editor.SpecialBox.IsChecked == true, "clearing special priority retains participation");
        Editor.SelectBrush(0); ClickSlot(7);
        Check(Editor.Board.Values[7] == 0 && Editor.SpecialBox.IsChecked == false, "unavailable special cell opts out");
        ClickSlot(7);
        Check(Editor.Board.Values[7] == 1 && Editor.SpecialBox.IsChecked == true, "clearing special unavailable opts in");
        Undo();
        Check(Editor.Board.Values[7] == 0 && Editor.SpecialBox.IsChecked == false, "undo restores special opt out");
        Redo();
        Check(Editor.Board.Values[7] == 1 && Editor.SpecialBox.IsChecked == true, "redo restores special participation");
        Editor.SelectBrush(2); ClickSlot(7);
        Generate();
        var person = SubmissionCodec.Decode(_code.Text);
        Check(person.Name == "测试同学" && person.Preferences.SequenceEqual(Editor.Board.Values),
            "generated PB1 code preserves implicit availability and explicit markers");
        Editor.SelectBrush(0); ClickSlot(19);
        Check(_code.Text == "" && !_copy.IsEnabled, "editing markers invalidates old code");
        Undo(); Generate();
        Ui.Screenshot(this, Path.Combine(directory, "填写端_完成.png"));
        Width = MinWidth; Height = MinHeight; Ui.Pump();
        Ui.Screenshot(this, Path.Combine(directory, "填写端_最小窗口.png"));
        Console.WriteLine("UI_MARKER_ASSERTIONS=" + passed);
    }
}

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var app = new Application(); Ui.Initialize(app);
        var window = new FillWindow();
        if (args.Length == 2 && args[0] is "--verify-ui" or "--verify-portable")
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -12000; window.Top = -12000; window.ShowInTaskbar = false;
            window.Show();
            try { window.VerifyUi(args[1]); if (args[0] == "--verify-portable") Ui.AssertPortable(); return 0; }
            finally { window.Close(); }
        }
        app.Run(window); return 0;
    }
}
