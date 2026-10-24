using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Duty.UI;

public static class Ui
{
    public static readonly Brush Accent = Brush("#18764F");
    public static readonly Brush Muted = Brush("#64726B");
    public static readonly Brush Warning = Brush("#A65F15");
    public static readonly Brush Error = Brush("#A63B3B");
    public static readonly string[] Glyphs = ["\uE711", "\uE73E", "\uE734"];
    public static readonly string[] StateColors = ["#F0F2F3", "#EDF2FE", "#E5F3EA"];
    public static readonly string[] StateInks = ["#68757D", "#355FAD", "#18764F"];
    public static Brush Brush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    public static TextBlock Text(string text, double size = 14, Brush? color = null) =>
        new() { Text = text, FontSize = size, Foreground = color ?? Brush("#202824"), TextWrapping = TextWrapping.Wrap };
    public static TextBlock Icon(string glyph, double size = 17) =>
        new() { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size, VerticalAlignment = VerticalAlignment.Center };
    public static Button Button(string glyph, string tooltip, Action action, string? label = null, bool primary = false)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var icon = Icon(glyph);
        icon.Foreground = primary ? Brushes.White : Brush("#202824");
        content.Children.Add(icon);
        if (label is not null) content.Children.Add(new TextBlock { Text = label, Foreground = primary ? Brushes.White : Brush("#202824"),
            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, ToolTip = tooltip, Margin = new Thickness(0, 0, 7, 0) };
        if (primary) button.SetResourceReference(FrameworkElement.StyleProperty, "PrimaryButton");
        button.Click += (_, _) => action();
        return button;
    }
    public static Border Divider() => new() { Height = 1, Background = Brush("#DEE5E0"), Margin = new Thickness(0, 16, 0, 16) };
    public static void StyleWindow(Window window) => window.Style = (Style)Application.Current.FindResource(typeof(Window));
    public static void FitWorkArea(Window window, double minimumWidth = 900, double minimumHeight = 600)
    {
        var area = SystemParameters.WorkArea;
        var width = Math.Max(400, area.Width - 24);
        var height = Math.Max(400, area.Height - 24);
        window.MinWidth = Math.Min(minimumWidth, width);
        window.MinHeight = Math.Min(minimumHeight, height);
        window.Width = Math.Min(window.Width, width);
        window.Height = Math.Min(window.Height, height);
    }
    public static void Initialize(Application app)
    {
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Duty.UI;component/Theme.xaml") });
        ToolTipService.InitialShowDelayProperty.OverrideMetadata(typeof(DependencyObject), new FrameworkPropertyMetadata(350));
    }
    public static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    public static void AssertPortable()
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        var directory = Path.GetFullPath(AppContext.BaseDirectory);
        var core = typeof(object).Assembly.Location;
        if (!Environment.Is64BitProcess || !core.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("当前不是使用程序目录内运行时的64位自包含进程。");
        Console.WriteLine("LOCAL_RUNTIME=" + core);
        foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
        {
            var name = module.ModuleName.ToLowerInvariant();
            if (!(name.StartsWith("msvcp140") || name.StartsWith("vcruntime140"))) continue;
            Console.WriteLine("NATIVE_RUNTIME=" + module.FileName);
            if (!module.FileName.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("发现未打包到程序目录的C++运行库：" + module.FileName);
        }
        Console.WriteLine("PORTABLE_RUNTIME_CHECK=PASS");
    }
    public static void Screenshot(Window window, string path)
    {
        window.UpdateLayout();
        Pump();
        var content = (FrameworkElement)window.Content;
        var width = (int)Math.Ceiling(content.ActualWidth + content.Margin.Left + content.Margin.Right);
        var height = (int)Math.Ceiling(content.ActualHeight + content.Margin.Top + content.Margin.Bottom);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
