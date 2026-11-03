using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CraftLibrary.App.ViewModels;

namespace CraftLibrary.App.Views;

public partial class FilesView : UserControl
{
    private MainViewModel? _vm;
    private ScrollViewer? _scroller;

    public FilesView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm != null) _vm.RowsReplaced -= OnRowsReplaced;
            _vm = DataContext as MainViewModel;
            if (_vm != null) _vm.RowsReplaced += OnRowsReplaced;
        };
    }

    private ScrollViewer? Scroller => _scroller ??= FindChild<ScrollViewer>(List);

    /// <summary>A different folder starts at the top; the same folder (after a move or rename) keeps its place.</summary>
    private void OnRowsReplaced(bool toTop)
    {
        var sv = Scroller;
        if (sv == null) return;
        var offset = sv.VerticalOffset;
        Dispatcher.BeginInvoke(() => sv.ScrollToVerticalOffset(toTop ? 0 : offset), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _vm?.SetFilesWidth(e.NewSize.Width - 14); // leave room for the scroll bar
    }

    /// <summary>One click selects; a double-click opens a file (or goes into a folder).</summary>
    private void OnTileDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm == null || sender is not FrameworkElement fe) return;
        List.Focus();
        switch (fe.Tag)
        {
            case FileItemVm f:
                _vm.Select(f);
                if (e.ClickCount == 2) _vm.OpenFile(f, null);
                break;
            case FolderTileVm t:
                _vm.SelectTile(t);
                if (e.ClickCount == 2) _vm.SetFolder(t.Id);
                break;
        }
        e.Handled = true;
    }

    /// <summary>Arrow keys move between files, Enter opens, F2 renames, Delete removes.</summary>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_vm == null || e.OriginalSource is TextBox) return;
        switch (e.Key)
        {
            case Key.Left: _vm.MoveSelection(-1, 0); break;
            case Key.Right: _vm.MoveSelection(1, 0); break;
            case Key.Up: _vm.MoveSelection(0, -1); break;
            case Key.Down: _vm.MoveSelection(0, 1); break;
            case Key.Enter:
                if (_vm.SelectedTile != null) _vm.SetFolder(_vm.SelectedTile.Id);
                else if (_vm.Selected != null) _vm.OpenFile(_vm.Selected, null);
                break;
            case Key.F2: _vm.StartRename.Execute(null); break;
            case Key.Delete: _vm.RecycleSelected.Execute(null); break;
            default: return;
        }
        e.Handled = true;
        if (_vm.Selected != null) BringIntoView(_vm.Selected);
    }

    /// <summary>Scrolls so the selected file can be seen.</summary>
    private void BringIntoView(FileItemVm item)
    {
        var row = _vm!.Rows.FirstOrDefault(r => r == item || (r is GridRow g && g.Tiles.Contains(item)));
        if (row != null) List.ScrollIntoView(row);
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var c = VisualTreeHelper.GetChild(parent, i);
            if (c is T t) return t;
            if (FindChild<T>(c) is { } deeper) return deeper;
        }
        return null;
    }
}
