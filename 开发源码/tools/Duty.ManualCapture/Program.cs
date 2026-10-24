using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Duty.Core;
using Duty.Desktop;
using Duty.Intake;
using Duty.UI;

namespace Duty.ManualCapture;

public static class Program
{
    private static readonly Dictionary<string, object> Regions = [];

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Supply the manual QA output directory.");
        var directory = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Ui.Initialize(app);
        var failure = 0;
        var host = new Window { Width = 10, Height = 10, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -12000, Top = -12000 };
        host.Loaded += async (_, _) =>
        {
            try { await Capture(directory); }
            catch (Exception e) { Console.Error.WriteLine(e); failure = 1; }
            finally { app.Shutdown(); }
        };
        app.Run(host);
        return failure;
    }

    private static T Field<T>(object owner, string name) =>
        (T)(owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner)
            ?? throw new InvalidOperationException("Missing capture field: " + name));

    private static void ShowOffscreen(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -12000; window.Top = -12000; window.ShowInTaskbar = false;
        window.Show(); Ui.Pump(); window.UpdateLayout();
    }

    private static void Save(Window window, string directory, string name,
        params (string Name, FrameworkElement Control)[] controls)
    {
        Ui.Screenshot(window, Path.Combine(directory, name + ".png"));
        var areas = new Dictionary<string, object>();
        foreach (var (label, element) in controls)
        {
            var point = element.TranslatePoint(new Point(0, 0), window);
            areas[label] = new { X = point.X, Y = point.Y, Width = element.ActualWidth, Height = element.ActualHeight };
        }
        Regions[name] = areas;
    }

    private static async Task Capture(string directory)
    {
        var fill = new FillWindow();
        ShowOffscreen(fill);
        Save(fill, directory, "01_fill_start", ("name", fill.Editor.NameBox), ("board", fill.Editor.Board),
            ("special", fill.Editor.SpecialBox));

        var names = new[] { "示例甲", "示例乙", "示例丙", "示例丁", "示例戊" };
        var people = names.Select((name, i) =>
        {
            var choices = Enumerable.Range(0, 20).Select(s => (byte)(s / 4 == i ? 2 : 1)).ToArray();
            if (i == 0) { choices[12] = 0; choices[13] = 0; }
            return new Person(name, 4, choices);
        }).ToArray();
        File.WriteAllText(Path.Combine(directory, "demo-records.json"), JsonSerializer.Serialize(
            people.Select(person => new { person.Name, Code = SubmissionCodec.Encode(person) }),
            new JsonSerializerOptions { WriteIndented = true }));
        fill.Editor.LoadPerson(people[0]);
        fill.Editor.SelectBrush(0);
        Save(fill, directory, "02_fill_choices", ("name", fill.Editor.NameBox), ("board", fill.Editor.Board),
            ("special", fill.Editor.SpecialBox));
        fill.Generate();
        Save(fill, directory, "03_fill_code", ("code", Field<TextBox>(fill, "_code")),
            ("copy", Field<Button>(fill, "_copy")), ("board", fill.Editor.Board));
        fill.Close();

        var admin = new AdminWindow(true);
        ShowOffscreen(admin);
        var import = new ImportDialog(admin);
        ShowOffscreen(import);
        Field<TextBox>(import, "_input").Text = string.Join(Environment.NewLine, people.Select(SubmissionCodec.Encode));
        import.Validate();
        if (import.Valid.Count != people.Length) throw new Exception("Manual import screenshot validation failed.");
        Save(import, directory, "04_import", ("input", Field<TextBox>(import, "_input")),
            ("accept", Field<Button>(import, "_accept")));
        admin.ApplyImported(import.Valid, false);
        import.Close();
        Field<CheckBox>(admin, "_confirmed").IsChecked = true;
        Save(admin, directory, "05_ready", ("roster", Field<DataGrid>(admin, "_roster")),
            ("confirmed", Field<CheckBox>(admin, "_confirmed")), ("solve", Field<Button>(admin, "_solve")));

        await admin.SolveAsync(false);
        var schedule = Field<Schedule>(admin, "_schedule");
        if (!ScheduleRules.Evaluate(people, schedule).PersonalSatisfied) throw new Exception("Manual sample solve failed.");
        Save(admin, directory, "06_schedule", ("roster", Field<DataGrid>(admin, "_roster")),
            ("board", Field<ScheduleBoard>(admin, "_board")), ("export", Field<Button>(admin, "_export")),
            ("solve", Field<Button>(admin, "_solve")));

        var personDialog = new PersonDialog(admin, people[0]);
        ShowOffscreen(personDialog);
        Save(personDialog, directory, "07_edit_person");
        personDialog.Close();

        var toolbar = Field<ScheduleBoard>(admin, "_board");
        var first = toolbar.Children.OfType<Border>()
            .SelectMany(frame => ((Grid)frame.Child).Children.OfType<Button>())
            .First(button => button.Tag is SlotDrag { Slot: 0, Position: 0 });
        first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var menu = first.ContextMenu ?? throw new Exception("No real candidate menu was created.");
        menu.UpdateLayout(); Ui.Pump();
        var menuFile = Path.Combine(directory, "08_candidates.png");
        CaptureElement(menu, menuFile);
        menu.IsOpen = false;

        admin.Place(1, 0, null);
        Save(admin, directory, "09_manual", ("board", toolbar), ("undo", Field<Button>(admin, "_undo")),
            ("redo", Field<Button>(admin, "_redo")));

        _ = admin.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            var issues = Application.Current.Windows.Cast<Window>().Single(window => window.Title == "检查与提醒");
            Save(issues, directory, "10_issues");
            issues.Close();
        }));
        admin.GetType().GetMethod("ShowIssues", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(admin, null);
        admin.Close();

        File.WriteAllText(Path.Combine(directory, "regions.json"), JsonSerializer.Serialize(Regions,
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("MANUAL_SCREENSHOTS=10");
    }

    private static void CaptureElement(FrameworkElement element, string path)
    {
        var width = (int)Math.Ceiling(element.ActualWidth);
        var height = (int)Math.Ceiling(element.ActualHeight);
        if (width <= 0 || height <= 0) throw new Exception("Candidate menu did not render.");
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96,
            System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
