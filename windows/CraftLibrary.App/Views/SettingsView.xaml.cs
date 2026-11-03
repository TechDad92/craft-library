using System.Windows.Controls;
using CraftLibrary.App.ViewModels;

namespace CraftLibrary.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    /// <summary>She picked a different program for a kind of file.</summary>
    private void OnProgramChosen(object sender, SelectionChangedEventArgs e)
    {
        // Ignore the list filling itself in; only react to her choosing something.
        if (e.RemovedItems.Count == 0 || e.AddedItems.Count == 0) return;
        if (sender is not ComboBox { Tag: string ext } || e.AddedItems[0] is not ChoiceVm choice) return;
        if (DataContext is SettingsViewModel vm) vm.ChooseProgram(ext, choice.Value);
    }
}
