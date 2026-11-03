using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftLibrary.App.Services;
using CraftLibrary.Core;

namespace CraftLibrary.App.ViewModels;

/// <summary>One file in her library, as shown on a tile, in a list row and in the preview.</summary>
public sealed partial class FileItemVm : ObservableObject, IThumbTarget
{
    /// <summary>Set once when the app starts.</summary>
    public static ThumbnailService? Thumbs { get; set; }

    public FileItemVm(FileEntry f, string folder)
    {
        _path = f.Path;
        Dir = f.Dir;
        _name = f.Name;
        Ext = f.Ext;
        Kind = FileKinds.KindFor(f.Ext);
        SizeBytes = f.Size;
        ModifiedUtc = f.ModifiedUtc;
        CreatedUtc = f.CreatedUtc;
        Size = FileKinds.HumanSize(f.Size);
        Added = FileKinds.ShortDate(f.CreatedUtc);
        _folder = folder;
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FullName))] private string _path;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FullName))] private string _name;
    [ObservableProperty] private string _folder;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowCopies), nameof(CopiesText))] private string _dup = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowCopies), nameof(CopiesText))] private int _dupCount;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(LaterLabel))] private bool _later;
    [ObservableProperty] private string _note = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FontNotInstalled))] private bool _installed;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string _metaLine = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ShowCopies))] private bool _badgesOn = true;

    public string Dir { get; set; }
    public string Ext { get; }
    public FileKind Kind { get; }
    public long SizeBytes { get; }
    public DateTime ModifiedUtc { get; }
    public DateTime CreatedUtc { get; }
    public string Size { get; }
    public string Added { get; }

    public string FullName => PathRules.WithExt(Name, Ext);
    public string TypeBadge => (Ext.Length > 0 ? Ext : "file").ToUpperInvariant();
    public string TypeLabel => FileKinds.TypeLabel(Ext);
    public bool IsFont => Kind == FileKind.Font;
    public bool FontNotInstalled => IsFont && !Installed;
    /// <summary>Paper patterns fill their tile; everything else is shown whole.</summary>
    public bool IsCover => Kind == FileKind.Paper;
    public bool ShowCopies => BadgesOn && DupCount > 1;
    public string CopiesText => DupCount + " copies";
    public string LaterLabel => Later ? "Remove from Sort later" : "Sort later";

    public FontFamily? FontPreview => IsFont ? FontService.FamilyFromFile(Path) : null;

    // ---------- thumbnail (asked for only when the tile is actually on screen) ----------
    private bool _thumbAsked;
    private BitmapSource? _thumb;
    private bool _thumbDone;

    public BitmapSource? Thumb
    {
        get
        {
            if (!_thumbAsked && Thumbs != null && !IsFont)
            {
                _thumbAsked = true;
                var known = Thumbs.Peek(this);
                if (known != null) { _thumb = known.Image; Inner = known.Inner; _thumbDone = true; }
                else Thumbs.Request(this);
            }
            return _thumb;
        }
    }

    public int Inner { get; private set; }
    public bool HasInner => Inner > 0;
    public string InnerText => Inner + (Inner == 1 ? " file inside" : " files inside");
    public bool ThumbLoading => !IsFont && !_thumbDone;
    /// <summary>No picture: show the file-type drawing and "No preview".</summary>
    public bool ShowGeneric => _thumbDone && _thumb == null && !IsFont;

    public void ThumbReady(BitmapSource? image, int innerCount)
    {
        _thumb = image;
        Inner = innerCount;
        _thumbDone = true;
        OnPropertyChanged(nameof(Thumb));
        OnPropertyChanged(nameof(ThumbLoading));
        OnPropertyChanged(nameof(ShowGeneric));
        OnPropertyChanged(nameof(HasInner));
        OnPropertyChanged(nameof(InnerText));
        ThumbChanged?.Invoke(this);
    }

    public event Action<FileItemVm>? ThumbChanged;

    public void ThumbCancelled() { if (!_thumbDone) _thumbAsked = false; }

    /// <summary>After a move or rename: ask for the thumbnail again only if it is needed.</summary>
    public void ResetThumbRequest()
    {
        _thumbAsked = false;
        if (_thumbDone) return;
        OnPropertyChanged(nameof(Thumb));
    }

    string IThumbTarget.Path => Path;
}

/// <summary>A folder, as known from the last scan.</summary>
public sealed class FolderDef
{
    public required string Id { get; init; }          // its full path (or "unsorted" / "__later")
    public required string Label { get; init; }
    public string RelPath { get; init; } = "";         // "Patterned Papers › Christmas"
    public int Depth { get; init; }
    public required string Loc { get; init; }         // which location it belongs to ("inbox" / "virtual" for the special ones)
    public string? Parent { get; init; }
    public bool IsRoot { get; init; }
}
