using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CraftLibrary.App.ViewModels;

namespace CraftLibrary.App.Views;

public partial class PreviewView : UserControl
{
    public PreviewView() => InitializeComponent();

    private MainViewModel? Vm => DataContext as MainViewModel;

    private void OnRenameKey(object sender, KeyEventArgs e)
    {
        if (Vm == null) return;
        if (e.Key == Key.Enter) { e.Handled = true; _ = Vm.CommitRenameAsync(); }
        else if (e.Key == Key.Escape) { e.Handled = true; Vm.Renaming = false; }
    }

    private void OnSeeDuplicates(object sender, RoutedEventArgs e)
    {
        if (Vm?.Selected is { } s) Vm.OpenDups(s.Dup);
    }
}
