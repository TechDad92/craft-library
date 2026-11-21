using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Controls;
using System.Windows.Input;
using CraftLibrary.App.ViewModels;
using CraftLibrary.App.Views;

namespace CraftLibrary.App;

public partial class MainWindow : Window
{
    private Window? _popout;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel old) old.PropertyChanged -= OnVmChanged;
            if (e.NewValue is MainViewModel vm) { vm.PropertyChanged += OnVmChanged; SyncPopout(); }
        };
        Activated += (_, _) => Vm?.WindowActivated();
        Closed += (_, _) => _popout?.Close();
        StateChanged += (_, _) => SyncWindowButtons();
        Banner.SizeChanged += (_, _) => { SyncCaptionHeight(); FitSearchBox(); };
        BannerLeft.SizeChanged += (_, _) => FitSearchBox();
        BannerRight.SizeChanged += (_, _) => FitSearchBox();
        MenuArea.SizeChanged += (_, _) => FitSearchBox();
    }

    // ---------------- the banner is the title bar ----------------

    private void OnMinimise(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void OnCloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);
    private void OnMaximise(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }

    private void SyncWindowButtons()
    {
        var max = WindowState == WindowState.Maximized;
        MaxIcon.Glyph = (System.Windows.Media.Geometry)FindResource(max ? "Icon.WinRestore" : "Icon.WinMax");
        MaxButton.ToolTip = max ? "Restore down" : "Maximise";
        System.Windows.Automation.AutomationProperties.SetName(MaxButton, max ? "Restore down" : "Maximise");
    }

    /// <summary>
    /// The Menu button and search box sit centred on the window. The search box is 480 wide when there is room,
    /// and narrower when the window is small, so it never pushes into the title or the window buttons.
    /// </summary>
    private void FitSearchBox()
    {
        var width = Banner.ActualWidth - Banner.Padding.Left - Banner.Padding.Right;
        if (width <= 0) return;
        var side = Math.Max(BannerLeft.DesiredSize.Width, BannerRight.DesiredSize.Width);
        var room = width - 2 * side - MenuArea.DesiredSize.Width - 16;
        var w = Math.Clamp(room, 160, 480);
        if (Math.Abs(SearchArea.Width - w) > 0.5) SearchArea.Width = w;
    }

    /// <summary>The draggable strip is exactly as tall as the banner (which grows with the text size setting).</summary>
    private void SyncCaptionHeight()
    {
        var zoom = Application.Current.Resources["Zoom"] is double z ? z : 1.0;
        if (WindowChrome.GetWindowChrome(this) is { } chrome && Banner.ActualHeight > 0)
            chrome.CaptionHeight = Banner.ActualHeight * zoom;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        // Windows 11: ask for rounded corners like other windows (ignored on Windows 10).
        try { var round = 2; DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); } catch { }
    }

    /// <summary>When maximised, fill the screen's working area exactly, so nothing hides behind the taskbar or spills off the edge.</summary>
    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_GETMINMAXINFO = 0x0024;
        if (msg != WM_GETMINMAXINFO) return IntPtr.Zero;
        var monitor = MonitorFromWindow(hwnd, 2 /* nearest */);
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info)) return IntPtr.Zero;
        var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
        mmi.ptMaxPosition.X = info.rcWork.Left - info.rcMonitor.Left;
        mmi.ptMaxPosition.Y = info.rcWork.Top - info.rcMonitor.Top;
        mmi.ptMaxSize.X = info.rcWork.Right - info.rcWork.Left;
        mmi.ptMaxSize.Y = info.rcWork.Bottom - info.rcWork.Top;
        Marshal.StructureToPtr(mmi, lParam, false);
        return IntPtr.Zero; // let WPF apply its own minimum size as well
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MainViewModel.PreviewPopout)) SyncPopout();
        if (string.IsNullOrEmpty(e.PropertyName)) SyncCaptionHeight(); // text size may have changed
    }

    /// <summary>"Own window" preview: a small window that stays on top of this one.</summary>
    private void SyncPopout()
    {
        var want = Vm?.PreviewPopout == true;
        if (want && _popout == null)
        {
            _popout = new Window
            {
                Title = "Preview · Craft Library", Owner = this, Width = 420, Height = 720, DataContext = Vm, Content = new PreviewView(),
                ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Left = Left + ActualWidth - 460, Top = Top + 80, Icon = Icon,
            };
            _popout.SetResourceReference(BackgroundProperty, "B.panel");
            _popout.SetResourceReference(ForegroundProperty, "B.text");
            _popout.Closing += (_, _) => { if (Vm?.PreviewPopout == true) Vm.HidePreview.Execute(null); _popout = null; };
            _popout.Show();
        }
        else if (!want && _popout != null)
        {
            var w = _popout;
            _popout = null;
            w.Close();
        }
    }

    /// <summary>The side buttons on a mouse go Back and Forward, as in File Explorer.</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (Vm is { } vm)
        {
            if (e.ChangedButton == MouseButton.XButton1) { vm.GoBack.Execute(null); e.Handled = true; }
            else if (e.ChangedButton == MouseButton.XButton2) { vm.GoForward.Execute(null); e.Handled = true; }
        }
        base.OnPreviewMouseDown(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) { base.OnPreviewKeyDown(e); return; }
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        var key = e.Key == Key.System ? e.SystemKey : e.Key; // Alt+key arrives as a "system" key
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        if (e.OriginalSource is not TextBox && ((ctrl && key == Key.Y) || (ctrl && shift && key == Key.Z))) { _ = vm.RedoAsync(); e.Handled = true; }
        else if (ctrl && key == Key.Z && e.OriginalSource is not TextBox) { _ = vm.UndoStepsAsync(1); e.Handled = true; }
        else if (alt && key == Key.Left) { vm.GoBack.Execute(null); e.Handled = true; }
        else if (alt && key == Key.Right) { vm.GoForward.Execute(null); e.Handled = true; }
        else if (key == Key.BrowserBack) { vm.GoBack.Execute(null); e.Handled = true; }
        else if (key == Key.BrowserForward) { vm.GoForward.Execute(null); e.Handled = true; }
        else if (ctrl && e.Key == Key.F && vm.ShowSearch) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
        else if (e.Key == Key.Escape && vm.GuideOpen) { vm.Guide.GoBack(); e.Handled = true; }
        else if (e.Key == Key.Escape)
        {
            // Escape closes whatever is on top, one thing at a time.
            if (vm.SettingsVm.PickerOpen) vm.SettingsVm.PickerOpen = false;
            else if (vm.ConfirmOpen) vm.ConfirmOpen = false;
            else if (vm.MoveOpen) vm.MoveOpen = false;
            else if (vm.ColourOpen) vm.ColourOpen = false;
            else if (vm.HistoryOpen) vm.HistoryOpen = false;
            else if (vm.Renaming) vm.Renaming = false;
            else if (vm.EditingFolderNote) vm.CancelFolderNote.Execute(null);
            else if (vm.SettingsOpen) vm.SettingsOpen = false;
            else if (vm.Query.Length > 0) vm.Query = "";
            else { base.OnPreviewKeyDown(e); return; }
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }
}
