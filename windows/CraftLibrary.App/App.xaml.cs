using System.Windows;
using System.Windows.Threading;
using CraftLibrary.App.Services;
using CraftLibrary.App.ViewModels;

namespace CraftLibrary.App;

public partial class App : Application
{
    /// <summary>Where the app keeps its settings, thumbnails and the problem log: %LOCALAPPDATA%\Craft Library.</summary>
    public static string DataDir { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Craft Library");

    private Mutex? _single;
    private MainViewModel? _vm;
    private ThumbnailService? _thumbs;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Directory.CreateDirectory(DataDir);

        // Only one copy at a time, so two windows never change the same files at once.
        _single = new Mutex(true, @"Local\CraftLibrary.SingleInstance", out var first);
        if (!first)
        {
            MessageBox.Show("Craft Library is already open. Look for it on the taskbar.", "Craft Library", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnexpectedProblem;
        // For testing: CRAFT_TRACE=1 writes any screen-wiring problems to bindings.log.
        if (Environment.GetEnvironmentVariable("CRAFT_TRACE") == "1")
        {
            var listener = new System.Diagnostics.TextWriterTraceListener(Path.Combine(DataDir, "bindings.log"));
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Warning;
            System.Diagnostics.PresentationTraceSources.ResourceDictionarySource.Listeners.Add(listener);
            System.Diagnostics.Trace.AutoFlush = true;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, a) => Log(a.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, a) => { Log(a.Exception); a.SetObserved(); };

        _thumbs = new ThumbnailService(Path.Combine(DataDir, "thumbs"));
        FileItemVm.Thumbs = _thumbs;
        _vm = new MainViewModel(DataDir);
        var win = new MainWindow { DataContext = _vm };
        MainWindow = win;
        win.Show();
        await _vm.StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _vm?.Shutdown();
        _thumbs?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Anything unexpected is written to a log (for whoever looks after the app) and the app keeps going.</summary>
    private void OnUnexpectedProblem(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log(e.Exception);
        e.Handled = true;
        _vm?.ShowMessage("Something unexpected went wrong, but your files are safe. If it keeps happening, the details are in “problems.log” in " + DataDir + ".");
    }

    public static void Log(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var file = Path.Combine(DataDir, "problems.log");
            if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Delete(file);
            File.AppendAllText(file, $"---- {DateTime.Now:yyyy-MM-dd HH:mm:ss} (version {typeof(App).Assembly.GetName().Version})\n{ex}\n\n");
        }
        catch { /* nowhere to write: nothing more we can do */ }
    }
}
