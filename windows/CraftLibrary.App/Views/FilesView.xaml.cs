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

    // ---------------- right-click menus ----------------

    /// <summary>Right-click on a file or folder: select it, then show a menu of what can be done with it.</summary>
    private void OnTileMenu(object sender, ContextMenuEventArgs e)
    {
        if (_vm == null || sender is not FrameworkElement fe) return;
        e.Handled = true; // the menu is built here instead
        List.Focus();
        var menu = fe.Tag switch
        {
            FileItemVm f => FileMenu(f),
            FolderTileVm t => FolderMenu(t),
            _ => null,
        };
        if (menu == null) return;
        menu.PlacementTarget = fe;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, string icon, Action run, string keys = "")
    {
        var mi = new MenuItem
        {
            Header = header,
            InputGestureText = keys,
            Icon = new Controls.Icon { Glyph = (Geometry)Application.Current.FindResource("Icon." + icon), Size = 18 },
        };
        mi.Click += (_, _) => run();
        return mi;
    }

    private ContextMenu FileMenu(FileItemVm f)
    {
        var vm = _vm!;
        vm.Select(f);
        var m = new ContextMenu();
        if (vm.FeatOpen)
        {
            m.Items.Add(Item(vm.OpenLabel, "PopOut", () => vm.OpenFile(f, null), "Enter"));
            m.Items.Add(Item("Open with…", "Grid4", () => vm.ChooseOtherProgram.Execute(null)));
            m.Items.Add(new Separator());
        }
        m.Items.Add(Item("Copy", "Copy", () => vm.CopyToClipboard(f), "Ctrl+C"));
        m.Items.Add(Item("Copy to…", "Copy", () => vm.OpenCopyTo.Execute(null)));
        if (vm.FeatMove) m.Items.Add(Item("Move to…", "FolderMove", () => vm.OpenMove.Execute(null)));
        if (vm.FeatRename) m.Items.Add(Item("Rename", "Pencil", () => vm.StartRename.Execute(null), "F2"));
        m.Items.Add(new Separator());
        if (vm.FeatLater) m.Items.Add(Item(f.LaterLabel, "Clock", () => vm.ToggleLater.Execute(null)));
        if (vm.FeatExplorer) m.Items.Add(Item("Show in File Explorer", "FolderSearch", () => vm.ShowInExplorer.Execute(null)));
        if (f.FontNotInstalled) m.Items.Add(Item("Install font", "Download", () => vm.InstallFont.Execute(null)));
        if (f.Ext.Length > 0) m.Items.Add(Item("Hide all ." + f.Ext + " files", "EyeOff", () => vm.HideKind(f.Ext)));
        if (vm.FeatRecycle)
        {
            m.Items.Add(new Separator());
            m.Items.Add(Item("Send to Recycle Bin", "Trash", () => vm.RecycleSelected.Execute(null), "Delete"));
        }
        // Tidy up: no separator at the very end, and never two in a row.
        for (var i = m.Items.Count - 1; i >= 0; i--)
            if (m.Items[i] is Separator && (i == m.Items.Count - 1 || i == 0 || m.Items[i - 1] is Separator)) m.Items.RemoveAt(i);
        return m;
    }

    private ContextMenu FolderMenu(FolderTileVm t)
    {
        var vm = _vm!;
        vm.SelectTile(t);
        var m = new ContextMenu();
        m.Items.Add(Item("Open", "Folder", () => vm.SetFolder(t.Id), "Enter"));
        if (vm.FeatExplorer) m.Items.Add(Item("Show in File Explorer", "FolderSearch", () => vm.OpenFolderInExplorer(t.Id)));
        return m;
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
            case Key.C when (Keyboard.Modifiers & ModifierKeys.Control) != 0:
                if (_vm.Selected != null) _vm.CopyToClipboard(_vm.Selected);
                break;
            case Key.Apps:
            case Key.F10 when (Keyboard.Modifiers & ModifierKeys.Shift) != 0:
                // The keyboard's menu key (or Shift+F10) opens the right-click menu for the selected file.
                if (_vm.Selected is { } sel)
                {
                    var menu = FileMenu(sel);
                    menu.PlacementTarget = List;
                    menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Center;
                    menu.IsOpen = true;
                }
                break;
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
