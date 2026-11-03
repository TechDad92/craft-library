using System.ComponentModel;
using System.Windows;
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
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(MainViewModel.PreviewPopout)) SyncPopout();
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

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var vm = Vm;
        if (vm == null) { base.OnPreviewKeyDown(e); return; }
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.Z && e.OriginalSource is not TextBox) { _ = vm.UndoStepsAsync(1); e.Handled = true; }
        else if (ctrl && e.Key == Key.F && vm.ShowSearch) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
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
