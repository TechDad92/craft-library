using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using CraftLibrary.Core;

namespace CraftLibrary.App.ViewModels;

/// <summary>A simple action a button can run.</summary>
public static class Cmd
{
    public static ICommand Of(Action a) => new RelayCommand(a);
}

// ---------------- Folder list (left panel) ----------------

public abstract class SideRow;

/// <summary>A location's heading (e.g. "Craft Files", with its path and a ✕ to take it off the list).</summary>
public sealed class SideHeaderRow : SideRow
{
    public required string Label { get; init; }
    public required string Path { get; init; }
    public string Count { get; init; } = "";
    public bool Active { get; init; }
    public bool IsPortable { get; init; }
    public required ICommand Pick { get; init; }
    public required ICommand Forget { get; init; }
}

public sealed class SideFolderRow : SideRow
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string Count { get; init; } = "";
    public Thickness Indent { get; init; }
    public bool HasKids { get; init; }
    public bool Open { get; init; }
    public bool Active { get; init; }
    public string LabelColour { get; init; } = "";
    public string LabelLine { get; init; } = "";
    public bool ShowColourButton { get; init; }
    public string KidsHint => (Open ? "Hide folders inside " : "Show folders inside ") + Label;
    public required ICommand Pick { get; init; }
    public required ICommand ToggleKids { get; init; }
    public ICommand? ColourLabel { get; init; }
}

public sealed class SideNoteRow : SideRow
{
    public string Text { get; init; } = "No folders inside yet";
}

// ---------------- File area ----------------

/// <summary>The heading at the top of the files: path, title, count, note and buttons. It scrolls with the files.</summary>
public sealed class HeaderRow(MainViewModel main)
{
    public MainViewModel Main { get; } = main;
}

/// <summary>A row of tiles in the grid.</summary>
public sealed class GridRow
{
    public required IReadOnlyList<object> Tiles { get; init; }
    public int Cols { get; init; }
}

public sealed class EmptyRow
{
    public required string Text { get; init; }
}

/// <summary>A folder inside the folder she is looking at, shown as a tile or a list row.</summary>
public sealed class FolderTileVm : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Meta { get; init; }
    public string Fill { get; init; } = "#F6D58E";
    public string Line { get; init; } = "#B88A2E";
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
}

// ---------------- Duplicates ----------------

public sealed class DupGroupVm
{
    public required string Key { get; init; }
    public required FileItemVm First { get; init; }
    public required string Title { get; init; }
    public required string Sub { get; init; }
    public bool Focused { get; init; }
    public required IReadOnlyList<DupCopyVm> Copies { get; init; }
}

public sealed class DupCopyVm
{
    public required FileItemVm Item { get; init; }
    public required string FolderPath { get; init; }
    public required ICommand Keep { get; init; }
    public required ICommand Show { get; init; }
    public required ICommand Recycle { get; init; }
}

// ---------------- Recycle Bin ----------------

public sealed class BinRowVm
{
    public required RemovedEntry Entry { get; init; }
    public string FullName => PathRules.WithExt(Entry.Name, Entry.Ext);
    public string TypeBadge => (Entry.Ext.Length > 0 ? Entry.Ext : "file").ToUpperInvariant();
    public required string RemovedText { get; init; }
    public string FromText => Entry.OrigPath;
    public required ICommand Restore { get; init; }
}

// ---------------- History ----------------

/// <summary>Something she did, which Undo can reverse.</summary>
public sealed class HistoryEntry
{
    public required string Type { get; init; }       // move | rename | recycle | restore | install | later | unlater
    public required string Msg { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;
    public string Path { get; set; } = "";          // where the file is now
    public string FromPath { get; init; } = "";     // where it was before (move / rename)
    public string FromName { get; init; } = "";
    public bool LaterWas { get; init; }
    public List<RemovedEntry> Entries { get; set; } = [];
}

public sealed class HistoryRowVm
{
    public required string Time { get; init; }
    public required string Msg { get; init; }
    public required string UndoLabel { get; init; }
    public required ICommand Undo { get; init; }
}

// ---------------- Move picker ----------------

public sealed class MoveRowVm
{
    public bool IsHeader { get; init; }
    public required string Label { get; init; }
    public string Path { get; init; } = "";
    public string Id { get; init; } = "";
    public Thickness Indent { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsTarget { get; init; }
    public ICommand? Pick { get; init; }
}

// ---------------- Settings bits ----------------

/// <summary>One choice in a set of buttons, e.g. "Light" in Light / Dark.</summary>
public sealed class OptionVm
{
    public required string Label { get; init; }
    public bool On { get; init; }
    public required ICommand Pick { get; init; }
}

/// <summary>A colour circle.</summary>
public sealed class SwatchVm
{
    public required string Label { get; init; }
    public required string Hex { get; init; }
    public bool On { get; init; }
    public required ICommand Pick { get; init; }
}

/// <summary>A switch with a short explanation underneath.</summary>
public sealed class CheckVm
{
    public required string Label { get; init; }
    public string Help { get; init; } = "";
    public bool On { get; init; }
    public required ICommand Toggle { get; init; }
}

public sealed class FeatureRowVm
{
    public required string Label { get; init; }
    public required IReadOnlyList<OptionVm> Options { get; init; }
}

public sealed class FontRowVm
{
    public required string Label { get; init; }
    public bool Bold { get; init; }
    public required string FontLabel { get; init; }
    public required System.Windows.Media.FontFamily Family { get; init; }
    public required ICommand Open { get; init; }
}

public sealed class FontPickRowVm
{
    public bool IsHeader { get; init; }
    public required string Label { get; init; }
    public System.Windows.Media.FontFamily? Family { get; init; }
    public bool IsCurrent { get; init; }
    public ICommand? Pick { get; init; }
}

public sealed class OpenWithRowVm
{
    public required string Ext { get; init; }
    public required string Label { get; init; }
    public required IReadOnlyList<ChoiceVm> Choices { get; init; }
    public ChoiceVm? Selected { get; set; }
}

public sealed class ChoiceVm
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public override string ToString() => Label;
}

/// <summary>A program in the "Open with…" list under File options.</summary>
public sealed class ProgramChoiceVm
{
    public required string Label { get; init; }
    public string Initial => Label.Length > 0 ? Label[..1].ToUpperInvariant() : "?";
    public bool IsDefault { get; init; }
    public required ICommand Open { get; init; }
}

public sealed class HiddenFeatureVm
{
    public required string Label { get; init; }
    public required ICommand Show { get; init; }
}
