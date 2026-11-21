using System.Windows.Controls;
using CraftLibrary.App.ViewModels;

namespace CraftLibrary.App.Views;

public partial class SetupGuideView : UserControl
{
    public SetupGuideView()
    {
        InitializeComponent();
        // Each new screen starts at the top.
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is SetupGuideViewModel vm)
                vm.PropertyChanged += (_, p) => { if (p.PropertyName == nameof(SetupGuideViewModel.Step)) Scroller.ScrollToTop(); };
        };
    }

    /// <summary>She picked a different program for a kind of file (screen 6).</summary>
    private void OnProgramChosen(object sender, SelectionChangedEventArgs e)
    {
        // Ignore the list filling itself in; only react to her choosing something.
        if (e.RemovedItems.Count == 0 || e.AddedItems.Count == 0) return;
        if (sender is not ComboBox { Tag: string ext } || e.AddedItems[0] is not ChoiceVm choice) return;
        if (DataContext is SetupGuideViewModel vm) vm.Options.ChooseProgram(ext, choice.Value);
    }
}
