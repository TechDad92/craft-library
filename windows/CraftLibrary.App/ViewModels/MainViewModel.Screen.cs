using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftLibrary.App.Services;
using CraftLibrary.Core;

namespace CraftLibrary.App.ViewModels;

public sealed partial class MainViewModel
{
    // ---------------- what she is looking at ----------------
    [ObservableProperty] private string _folder = Unsorted;
    [ObservableProperty] private string _view = "browse";          // browse | dupes | bin
    [ObservableProperty] private FileItemVm? _selected;
    [ObservableProperty] private FolderTileVm? _selectedTile;
    [ObservableProperty] private bool _isSetup;
    [ObservableProperty] private bool _focus;
    [ObservableProperty] private bool _scanning;
    [ObservableProperty] private bool _dupScanning;
    [ObservableProperty] private bool _scanCapped;
    [ObservableProperty] private string _focusDup = "";

    // ---------------- pop-ups ----------------
    [ObservableProperty] private bool _settingsOpen;
    [ObservableProperty] private bool _menuOpen;
    [ObservableProperty] private bool _hiddenMenuOpen;
    [ObservableProperty] private bool _fileOptionsOpen;
    [ObservableProperty] private bool _openWithOpen;
    [ObservableProperty] private bool _historyOpen;
    [ObservableProperty] private bool _colourOpen;
    [ObservableProperty] private string _colourTab = "pastel";
    [ObservableProperty] private string _customColour = "#9ED6CF";
    [ObservableProperty] private bool _editingFolderNote;
    [ObservableProperty] private string _folderNoteDraft = "";
    [ObservableProperty] private bool _renaming;
    [ObservableProperty] private string _draft = "";

    // ---------------- the message at the bottom ----------------
    [ObservableProperty] private string _toast = "";
    [ObservableProperty] private bool _toastPlain;

    partial void OnToastChanged(string value) => _toastAt = DateTime.Now;

    partial void OnSettingsOpenChanged(bool value) { if (value) { SettingsVm.ShowOtherTypes = false; SettingsVm.Refresh(); } }

    /// <summary>The first-run setup guide is showing.</summary>
    [ObservableProperty] private bool _guideOpen;

    partial void OnGuideOpenChanged(bool value) { if (value) { SettingsVm.Refresh(); Guide.Refresh(); } }

    public ICommand RunGuide => Cmd.Of(() =>
    {
        SettingsOpen = false;
        MenuOpen = false;
        Guide.Step = 1;
        GuideOpen = true;
        Guide.Refresh();
    });

    public bool HasToast => Toast.Length > 0;
    public bool ToastUndo => History.Count > 0 && !ToastPlain && S.Shows("undo");

    /// <summary>A plain message (no Undo button).</summary>
    public void ShowMessage(string msg)
    {
        ToastPlain = true;
        Toast = msg;
        OnPropertyChanged(nameof(HasToast));
        OnPropertyChanged(nameof(ToastUndo));
    }

    public ICommand DismissToast => Cmd.Of(() => { Toast = ""; OnPropertyChanged(nameof(HasToast)); });

    // ---------------- search ----------------
    private string _query = "";
    private System.Windows.Threading.DispatcherTimer? _searchTimer;

    public string Query
    {
        get => _query;
        set
        {
            if (!SetProperty(ref _query, value)) return;
            FileItemVm.Thumbs?.ClearQueue();
            _searchTimer ??= new System.Windows.Threading.DispatcherTimer(TimeSpan.FromMilliseconds(180), System.Windows.Threading.DispatcherPriority.Background,
                (_, _) => { _searchTimer!.Stop(); View = "browse"; Renaming = false; _resetScroll = true; RefreshAll(); }, _ui);
            _searchTimer.Stop();
            _searchTimer.Start();
        }
    }

    private string Q => Query.Trim();

    // ---------------- rows in the file area ----------------
    public ObservableCollection<object> Rows { get; private set; } = [];
    private object[] _rowsKey = [];
    private int _cols = 4;
    private bool _resetScroll = true;
    /// <summary>Tells the window to go back to the top (a different folder) or keep its place (same folder).</summary>
    public event Action<bool>? RowsReplaced;

    /// <summary>The window reports how wide the file area is, so the grid knows how many tiles fit in a row.</summary>
    public void SetFilesWidth(double width)
    {
        var (min, _) = TileSize();
        var gap = (double)System.Windows.Application.Current.Resources["TileGap"];
        var cols = Math.Max(1, (int)Math.Floor((width - 48 + gap) / (min + gap)));
        if (cols == _cols) return;
        _cols = cols;
        RebuildRows(force: true);
    }

    public (double MinWidth, double ThumbHeight) TileSize() => S.Thumb switch
    {
        "s" => (130, 96),
        "l" => (220, 165),
        "xl" => (280, 210),
        _ => (170, 130),
    };

    public double ThumbHeight => TileSize().ThumbHeight;
    public bool ShowNameOn => S.ShowName;

    // ---------------- toolbar: quick view controls ----------------
    public static readonly List<ChoiceVm> OrderChoices =
    [
        new() { Value = "name", Label = "Name" }, new() { Value = "added", Label = "Newest first" },
        new() { Value = "type", Label = "File type" }, new() { Value = "size", Label = "Largest first" },
    ];

    public ChoiceVm OrderChoice
    {
        get => OrderChoices.FirstOrDefault(c => c.Value == S.Order) ?? OrderChoices[0];
        set { if (value != null && value.Value != S.Order) { S.Order = value.Value; SettingsChanged(); } }
    }

    public bool ViewIsGrid => S.View == "grid";
    public bool ViewIsList => S.View == "list";
    public ICommand ShowAsGrid => Cmd.Of(() => { if (S.View != "grid") { S.View = "grid"; SettingsChanged(); } });
    public ICommand ShowAsList => Cmd.Of(() => { if (S.View != "list") { S.View = "list"; SettingsChanged(); } });
    public bool ShowToolbar => !IsSetup;
    public bool ShowViewControls => View == "browse";
    public bool ShowTypeOn => S.ShowType;

    private void RebuildRows(bool force = false)
    {
        var vis = Visible();
        var subs = SubfolderTiles();
        var key = new object[] { S.View, _cols, View, Q, Folder }.Concat(subs.Select(s => (object)s.Id + "|" + s.Meta + "|" + s.Fill)).Concat(vis).ToArray();
        if (!force && key.SequenceEqual(_rowsKey)) return;
        _rowsKey = key;

        var rows = new List<object> { Header };
        if (vis.Count == 0 && subs.Count == 0) rows.Add(new EmptyRow { Text = Q.Length > 0 ? "No files match “" + Q + "”." : "This folder is empty." });
        else if (S.View == "grid")
        {
            var tiles = subs.Cast<object>().Concat(vis).ToList();
            for (var i = 0; i < tiles.Count; i += _cols)
                rows.Add(new GridRow { Tiles = tiles.Skip(i).Take(_cols).ToList(), Cols = _cols });
        }
        else
        {
            rows.AddRange(subs);
            rows.AddRange(vis);
        }
        Rows = new ObservableCollection<object>(rows);
        OnPropertyChanged(nameof(Rows));
        RowsReplaced?.Invoke(_resetScroll);
        _resetScroll = false;
    }

    private List<FileItemVm>? _visCache;

    /// <summary>The files in the folder she is looking at (or matching her search), in her chosen order.</summary>
    public List<FileItemVm> Visible() => _visCache ??= ComputeVisible();

    private List<FileItemVm> ComputeVisible()
    {
        IEnumerable<FileItemVm> list = Shown;
        var q = Q;
        if (q.Length > 0) list = list.Where(i => i.FullName.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        else if (Folder == LaterPile) list = list.Where(i => i.Later);
        else list = list.Where(i => string.Equals(i.Folder, Folder, StringComparison.OrdinalIgnoreCase));
        return S.Order switch
        {
            "added" => list.OrderByDescending(i => i.CreatedUtc).ToList(),
            "type" => list.OrderBy(i => i.Ext, StringComparer.OrdinalIgnoreCase).ThenBy(i => i.Name, NameOrder.Instance).ToList(),
            "size" => list.OrderByDescending(i => i.SizeBytes).ToList(),
            _ => list.OrderBy(i => i.Name, NameOrder.Instance).ThenBy(i => i.Ext, StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private (string Fill, string Line) FolderLook(string id)
    {
        var c = S.Shows("labels") ? Data.FolderColors.GetValueOrDefault(id, "") : "";
        return c.Length > 0 ? (c, Palette.Darker(c)) : ("#F6D58E", "#B88A2E");
    }

    private string FolderMeta(string id)
    {
        var files = _counts.GetValueOrDefault(id);
        var subs = _kids.GetValueOrDefault(id)?.Count ?? 0;
        var bits = new List<string>();
        if (subs > 0) bits.Add(subs + (subs == 1 ? " folder" : " folders"));
        bits.Add(files + (files == 1 ? " file" : " files"));
        return string.Join(" · ", bits);
    }

    private List<FolderTileVm> SubfolderTiles()
    {
        if (Q.Length > 0 || Folder == Unsorted || Folder == LaterPile || !_kids.TryGetValue(Folder, out var kids)) return [];
        return kids.Select(k =>
        {
            var (fill, line) = FolderLook(k.Id);
            return new FolderTileVm { Id = k.Id, Label = k.Label, Meta = FolderMeta(k.Id), Fill = fill, Line = line, IsSelected = SelectedTile?.Id == k.Id };
        }).ToList();
    }

    /// <summary>Redraws everything that depends on her files or settings.</summary>
    public void RefreshAll()
    {
        _visCache = null;
        if (Selected != null && (!_byPath.ContainsKey(Selected.Path) || S.IsHiddenExt(Selected.Ext))) Selected = null;
        RebuildRows();
        BuildSideRows();
        if (SettingsOpen || GuideOpen) { SettingsVm.Refresh(); Guide.Refresh(); }
        OnPropertyChanged(string.Empty);
    }

    // ---------------- folders ----------------
    public FolderDef? FolderById(string id)
    {
        if (id == Unsorted) return new FolderDef { Id = Unsorted, Label = "To sort", RelPath = Data.InboxPath, Loc = "inbox" };
        if (id == LaterPile) return new FolderDef { Id = LaterPile, Label = "Sort later", RelPath = "Files you set aside to sort later", Loc = "virtual" };
        if (!_folderIndex.TryGetValue(id, out var f)) return null;
        if (f.IsRoot && Data.Locations.FirstOrDefault(l => l.Key == f.Loc) is { } l) return new FolderDef { Id = f.Id, Label = l.Label, Loc = f.Loc, IsRoot = true, Depth = -1 };
        return f;
    }

    public string FullPath(FolderDef? f)
    {
        if (f == null) return "";
        if (f.Loc == "inbox") return "To sort (" + Data.InboxPath + ")";
        if (f.Loc == "virtual") return f.RelPath;
        var l = Data.Locations.FirstOrDefault(x => x.Key == f.Loc);
        if (f.IsRoot) return l?.Label ?? f.Label;
        return (l != null ? l.Label + " › " : "") + f.RelPath;
    }

    public string FolderPathOf(FileItemVm it) => FullPath(FolderById(it.Folder));

    private IEnumerable<FolderDef> ActiveFolders() => _folderDefs.Where(f => !f.IsRoot);

    // ---------------- banner ----------------
    public bool ShowSearch => !Focus && S.Shows("search") && !IsSetup;
    public bool ShowHome => !IsSetup && View != "browse";
    public int DupGroupCount => Shown.Where(i => i.Dup.Length > 0).GroupBy(i => i.Dup).Count(g => g.Count() > 1);
    public bool HasDupGroups => DupGroupCount > 0;
    public bool CanUndo => History.Count > 0;
    public string UndoHint => History.Count > 0 ? History[^1].Msg : "Nothing to undo yet";
    public string FocusState => Focus ? "On" : "Off";
    public bool ShowProgressPill => S.ShowProgress && !IsSetup && !Focus;
    public string ProgressText { get { var n = Data.SortedPerDay.GetValueOrDefault(DateTime.Now.ToString("yyyy-MM-dd")); return n + (n == 1 ? " file sorted today" : " files sorted today"); } }
    public List<HiddenFeatureVm> HiddenFeatures => Settings.FeatureList.Where(f => S.Feature(f.Key) == "hide")
        .Select(f => new HiddenFeatureVm { Label = f.Label, Show = Cmd.Of(() => SetFeature(f.Key, "show")) }).ToList();
    public bool HasHiddenShown => HiddenFeatures.Count > 0 && !Focus;
    public string HiddenCount => HiddenFeatures.Count.ToString();
    public bool Feat(string key) => S.Shows(key);
    // One property per feature, so the screens can show or hide each one.
    public bool FeatMove => S.Shows("move");
    public bool FeatRename => S.Shows("rename");
    public bool FeatRecycle => S.Shows("recycle");
    public bool FeatOpen => S.Shows("open");
    public bool FeatExplorer => S.Shows("explorer");
    public bool FeatDupes => S.Shows("dupes");
    public bool FeatUndo => S.Shows("undo");
    public bool FeatInbox => S.Shows("inbox");
    public bool FeatAddLoc => S.Shows("addloc");
    public bool FeatRecycleCount => S.Shows("recyclecount");
    public bool FeatFocus => S.Shows("focus");
    public bool FeatLater => S.Shows("later");
    public bool FeatNotes => S.Shows("notes");
    public bool FeatLabels => S.Shows("labels");
    public bool FeatHistory => S.Shows("history");
    public bool ShowSortingHead => FeatInbox || FeatLater;

    // ---------------- folder list (left) ----------------
    public List<SideRow> SideRows { get; private set; } = [];
    public bool ShowSidebar => !Focus;
    public bool SidebarRight => S.Sidebar == "right";
    public bool InboxActive => Q.Length == 0 && View == "browse" && Folder == Unsorted;
    public bool LaterActive => Q.Length == 0 && View == "browse" && Folder == LaterPile;
    public bool BinActive => View == "bin";
    public string InboxCount => _counts.GetValueOrDefault(Unsorted).ToString();
    public string LaterCount => Shown.Count(i => i.Later).ToString();
    public string InboxPathShort => Data.InboxPath;
    public string BinCount => Data.Removed.Count.ToString();
    public List<string> OfflineNotes => Data.Locations.Where(l => _unavailable.Contains(l.Key))
        .Select(l => "“" + l.Label + "” isn’t plugged in right now, so it’s hidden. It comes back when the drive is plugged in.").ToList();
    public string ScanNote => Scanning && _items.Count == 0 ? "Reading your folders…" : Scanning ? "Checking for changes…" : DupScanning ? "Looking for duplicates…"
        : ScanCapped ? "There are a lot of files here, so only the first 60,000 are shown." : "";

    private void BuildSideRows()
    {
        var rows = new List<SideRow>();
        var autoOpen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var f = _folderIndex.GetValueOrDefault(Folder); f != null; f = f.Parent != null ? _folderIndex.GetValueOrDefault(f.Parent) : null) autoOpen.Add(f.Id);
        bool IsOpen(string id) => Data.Expanded.TryGetValue(id, out var e) ? e : autoOpen.Contains(id);

        foreach (var l in Data.Locations)
        {
            if (_unavailable.Contains(l.Key)) continue;
            var rootId = PathRules.Norm(l.Path);
            var key = l.Key;
            rows.Add(new SideHeaderRow
            {
                Label = l.Label, Path = l.Path, Count = _counts.GetValueOrDefault(rootId) is var n && n > 0 ? n.ToString() : "",
                Active = Q.Length == 0 && View == "browse" && PathRules.Same(Folder, rootId) && Folder != Unsorted,
                IsPortable = Platform.DriveKindOf(l.Path) is DriveKind.Removable,
                Pick = Cmd.Of(() => SetFolder(rootId)), Forget = Cmd.Of(() => ForgetLocation(key)),
            });
            var top = _kids.GetValueOrDefault(rootId) ?? [];
            if (top.Count == 0) rows.Add(new SideNoteRow());
            void Add(FolderDef f, int level)
            {
                var kids = _kids.GetValueOrDefault(f.Id) ?? [];
                var open = kids.Count > 0 && IsOpen(f.Id);
                var active = Q.Length == 0 && View == "browse" && string.Equals(Folder, f.Id, StringComparison.OrdinalIgnoreCase);
                var colour = S.Shows("labels") ? Data.FolderColors.GetValueOrDefault(f.Id, "") : "";
                var id = f.Id;
                rows.Add(new SideFolderRow
                {
                    Id = id, Label = f.Label, Count = _counts.GetValueOrDefault(id).ToString(), Indent = new System.Windows.Thickness(level * 18, 0, 0, 0),
                    HasKids = kids.Count > 0, Open = open, Active = active, LabelColour = colour, LabelLine = colour.Length > 0 ? Palette.Darker(colour) : "",
                    ShowColourButton = active && S.Shows("labels"),
                    Pick = Cmd.Of(() => SetFolder(id)),
                    ToggleKids = Cmd.Of(() => { Data.Expanded[id] = !open; SaveSoon(); BuildSideRows(); OnPropertyChanged(nameof(SideRows)); }),
                    ColourLabel = Cmd.Of(OpenColourDialog),
                });
                if (open) foreach (var k in kids) Add(k, level + 1);
            }
            foreach (var f in top) Add(f, 0);
        }
        SideRows = rows;
    }

    // ---------------- heading above the files ----------------
    public FolderDef? CurrentFolder => FolderById(Folder);
    public string ListTitle => Q.Length > 0 ? "Search results" : CurrentFolder?.Label ?? "";
    public string ListCount
    {
        get
        {
            var n = Visible().Count;
            var subs = SubfolderTiles().Count;
            return (subs > 0 ? subs + (subs == 1 ? " folder · " : " folders · ") : "") + n + (n == 1 ? " file" : " files") + (Q.Length > 0 ? " matching “" + Q + "”" : "");
        }
    }

    public sealed record Crumb(string Label, bool IsLink, bool Sep, ICommand? Open);

    public List<Crumb> Crumbs
    {
        get
        {
            var f = CurrentFolder;
            if (Q.Length > 0) return [new Crumb("Searching every location", false, false, null)];
            if (f == null) return [];
            if (f.Loc == "inbox") return [new Crumb("Sorting · " + Data.InboxPath, false, false, null)];
            if (f.Loc == "virtual") return [new Crumb(f.RelPath, false, false, null)];
            var chain = new List<FolderDef>();
            for (var p = f.Parent != null ? FolderById(f.Parent) : null; p != null; p = p.Parent != null ? FolderById(p.Parent) : null) chain.Insert(0, p);
            var crumbs = new List<Crumb>();
            if (f.IsRoot) crumbs.Add(new Crumb(Data.Locations.FirstOrDefault(l => l.Key == f.Loc)?.Path ?? "", false, false, null));
            foreach (var c in chain) { var id = c.Id; crumbs.Add(new Crumb(c.Label, true, crumbs.Count > 0, Cmd.Of(() => SetFolder(id)))); }
            if (!f.IsRoot) crumbs.Add(new Crumb(f.Label, false, crumbs.Count > 0, null));
            return crumbs;
        }
    }

    public bool FolderExtras => Q.Length == 0 && CurrentFolder is { } f && f.Loc != "virtual" && !Focus;
    public string FolderNote => Data.FolderNotes.GetValueOrDefault(Folder, "");
    public bool ShowFolderNote => FolderNote.Length > 0 && !EditingFolderNote && FeatNotes;
    public string FolderNoteButton => FolderNote.Length > 0 ? "Edit note" : "Add note";
    public string CurrentColour => Data.FolderColors.GetValueOrDefault(Folder, "");
    public string ColourButtonText => CurrentColour.Length > 0 ? Palette.LabelName(CurrentColour) + " label" : "Colour label";
    public bool ShowInboxHint => Q.Length == 0 && Folder == Unsorted && !Data.InboxHintHidden && Visible().Count > 0 && !Focus;
    public bool PreviewHidden => S.Preview == "hidden";

    // ---------------- preview (right or bottom) ----------------
    public bool ShowPreviewPanel => S.Preview is "right" or "bottom" && !IsSetup && View == "browse";
    public bool PreviewRight => S.Preview == "right";
    public bool PreviewBottom => S.Preview == "bottom";
    public bool PreviewPopout => S.Preview == "popout" && !IsSetup;
    public bool HasSel => Selected != null && SelectedTile == null;
    public bool NoSel => Selected == null && SelectedTile == null;
    public bool HasTileSel => SelectedTile != null;

    private BitmapSource? _large;
    private string? _largeFor;
    public BitmapSource? PreviewImage => _large ?? Selected?.Thumb;

    partial void OnSelectedChanged(FileItemVm? oldValue, FileItemVm? newValue)
    {
        if (oldValue != null) { oldValue.IsSelected = false; oldValue.ThumbChanged -= OnSelectedThumb; }
        if (newValue != null) { newValue.IsSelected = true; newValue.ThumbChanged += OnSelectedThumb; }
        Renaming = false;
        FileOptionsOpen = false;
        OpenWithOpen = false;
        _large = null;
        _largeFor = newValue?.Path;
        if (newValue != null && (newValue.Kind is FileKind.Paper or FileKind.Pdf || newValue.Ext is "svg" or "docx"))
        {
            var path = newValue.Path;
            _ = ThumbnailService.LargeAsync(path, newValue.Ext, newValue.Kind, 900).ContinueWith(t =>
            {
                if (_largeFor != path || t.Result == null) return;
                _large = t.Result;
                OnPropertyChanged(nameof(PreviewImage));
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
        if (S.Remember) SaveSoon();
        OnPropertyChanged(string.Empty);
    }

    private void OnSelectedThumb(FileItemVm it) { OnPropertyChanged(nameof(PreviewImage)); OnPropertyChanged(nameof(PreviewNote)); }

    partial void OnSelectedTileChanged(FolderTileVm? oldValue, FolderTileVm? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
        OnPropertyChanged(nameof(HasSel));
        OnPropertyChanged(nameof(NoSel));
        OnPropertyChanged(nameof(HasTileSel));
    }

    public string PreviewFolderPath => Selected != null ? FolderPathOf(Selected) : "";
    public bool PreviewIsCover => Selected?.IsCover == true && _large == null;

    public string PreviewNote
    {
        get
        {
            var it = Selected;
            if (it == null) return "";
            var t = it.Thumb;
            if (it.Kind == FileKind.Doc && t != null) return "The thumbnail is the biggest picture inside this document, so there is no need to open Word to see it.";
            if (it.Kind == FileKind.Pdf && t != null) return "Showing page 1.";
            if (it.Kind == FileKind.Zip && it.Inner > 0) return it.Inner + " files inside this bundle." + (t != null ? " The preview shows the biggest picture inside." : "");
            if (it.IsFont) return "Preview is written in the font itself.";
            if (it.ShowGeneric) return "Windows has no preview for ." + it.Ext + " files, so the file type is shown instead.";
            return "";
        }
    }

    public bool PreviewIsDup => Selected is { DupCount: > 1 } && FeatDupes;
    public string PreviewDupText => Selected is { } s ? "Also saved in " + (s.DupCount - 1) + (s.DupCount - 1 == 1 ? " other place." : " other places.") : "";
    public string OpenLabel => Selected is { } s && ProgramFor(s.Ext) is { Length: > 0 } p ? "Open in " + p : "Open";

    public string ProgramFor(string ext)
    {
        if (S.OpenWith.TryGetValue(ext, out var exe) && exe.Length > 0)
            return Data.Programs.FirstOrDefault(p => string.Equals(p.Exe, exe, StringComparison.OrdinalIgnoreCase))?.Name ?? Path.GetFileNameWithoutExtension(exe);
        return Platform.DefaultProgramName(ext);
    }

    public List<ProgramChoiceVm> ProgramChoices
    {
        get
        {
            var it = Selected;
            if (it == null) return [];
            var over = S.OpenWith.GetValueOrDefault(it.Ext, "");
            var def = Platform.DefaultProgramName(it.Ext);
            var list = new List<ProgramChoiceVm> { new() { Label = def.Length > 0 ? def : "Windows default program", IsDefault = over.Length == 0, Open = Cmd.Of(() => OpenFile(it, "")) } };
            list.AddRange(Data.Programs.Select(p => new ProgramChoiceVm { Label = p.Name, IsDefault = string.Equals(over, p.Exe, StringComparison.OrdinalIgnoreCase), Open = Cmd.Of(() => OpenFile(it, p.Exe)) }));
            return list;
        }
    }

    /// <summary>Fonts from her library that are installed, so they can be chosen in Settings.</summary>
    public IEnumerable<FileItemVm> InstalledLibraryFonts() => _items.Where(i => i.IsFont && i.Installed);

    /// <summary>
    /// Her files minus the kinds she has chosen to hide (Settings › Layout › Hide kinds of files). Everything she sees
    /// (the file area, counts, search, Duplicates) uses this; the hidden files themselves are never touched.
    /// </summary>
    private IEnumerable<FileItemVm> Shown => S.HiddenExts.Count == 0 ? _items : _items.Where(i => !S.IsHiddenExt(i.Ext));

    public int HiddenFileCount => S.HiddenExts.Count == 0 ? 0 : _items.Count(i => S.IsHiddenExt(i.Ext));

    /// <summary>How many files of each kind there are (including hidden ones), for the "Hide kinds of files" lists.</summary>
    public Dictionary<string, int> CountByExt() => _items.Where(i => i.Ext.Length > 0)
        .GroupBy(i => i.Ext.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.Count());

    public IEnumerable<string> AllExtensions() => _items.Select(i => i.Ext).Where(e => e.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase);

    public string TileSelLabel => SelectedTile?.Label ?? "";
    public string TileSelMeta => SelectedTile?.Meta ?? "";
    public string TileSelFill => SelectedTile?.Fill ?? "#F6D58E";
    public string TileSelLine => SelectedTile?.Line ?? "#B88A2E";

    // ---------------- duplicates ----------------
    public bool IsBrowse => View == "browse";
    public bool ShowFiles => View == "browse" && !IsSetup;
    public bool IsDupes => View == "dupes";
    public bool IsBin => View == "bin";

    public List<DupGroupVm> DupGroups
    {
        get
        {
            if (View != "dupes") return [];
            return Shown.Where(i => i.Dup.Length > 0).GroupBy(i => i.Dup).Where(g => g.Count() > 1).Select(g =>
            {
                var copies = g.OrderBy(i => FolderPathOf(i), NameOrder.Instance).ToList();
                var key = g.Key;
                return new DupGroupVm
                {
                    Key = key, First = copies[0], Focused = FocusDup == key,
                    Title = copies.Count + " copies of the same file",
                    Sub = "Identical contents. " + (copies.Count - 1) + (copies.Count - 1 == 1 ? " copy could go." : " copies could go.") + " Keep the one in the folder you want.",
                    Copies = copies.Select(c => new DupCopyVm
                    {
                        Item = c, FolderPath = FolderPathOf(c),
                        Keep = Cmd.Of(() => KeepOnly(key, c)), Show = Cmd.Of(() => ShowInFolder(c)), Recycle = Cmd.Of(() => Recycle([c])),
                    }).ToList(),
                };
            }).OrderByDescending(g => g.Focused).ToList();
        }
    }

    public string DupSummary
    {
        get
        {
            var groups = Shown.Where(i => i.Dup.Length > 0).GroupBy(i => i.Dup).Where(g => g.Count() > 1).ToList();
            if (groups.Count == 0) return "Nothing to clean up";
            var total = groups.Sum(g => g.Count());
            return groups.Count + (groups.Count == 1 ? " group · " : " groups · ") + total + " copies · " + (total - groups.Count) + " could go to the Recycle Bin";
        }
    }

    // ---------------- Recycle Bin view ----------------
    public List<BinRowVm> BinRows => View != "bin" ? [] : Data.Removed.AsEnumerable().Reverse().Select(e => new BinRowVm
    {
        Entry = e,
        RemovedText = "Removed " + e.RemovedAtUtc.ToLocalTime().ToString("d MMM, h:mm tt", System.Globalization.CultureInfo.GetCultureInfo("en-GB")).Replace("AM", "am").Replace("PM", "pm")
            + (e.Method == "folder" ? " · kept in “" + SafeFiles.RemovedFolderName + "” on that drive" : ""),
        Restore = Cmd.Of(() => _ = RestoreAsync([e])),
    }).ToList();
    public bool BinEmpty => Data.Removed.Count == 0;
    public string BinCountText => Data.Removed.Count == 1 ? "1 file" : Data.Removed.Count + " files";

    // ---------------- history ----------------
    public List<HistoryRowVm> HistoryRows => History.Select((e, i) =>
    {
        var steps = History.Count - i;
        return new HistoryRowVm { Time = e.Time.ToString("h:mm tt").ToLowerInvariant(), Msg = e.Msg, UndoLabel = steps == 1 ? "Undo" : "Undo back to here", Undo = Cmd.Of(() => _ = UndoStepsAsync(steps)) };
    }).Reverse().ToList();
    public bool HistoryEmpty => History.Count == 0;

    // ---------------- folder colour labels ----------------
    public string ColourDialogTitle => CurrentFolder is { } f ? "Colour label for “" + f.Label + "”" : "";
    public string CurrentColourName => Palette.LabelName(CurrentColour);
    public List<OptionVm> ColourTabs => new[] { ("pastel", "Pastel"), ("muted", "Muted"), ("bold", "Bold") }
        .Select(t => new OptionVm { Label = t.Item2, On = ColourTab == t.Item1, Pick = Cmd.Of(() => { ColourTab = t.Item1; OnPropertyChanged(nameof(ColourTabs)); OnPropertyChanged(nameof(ColourSwatches)); }) }).ToList();
    public List<SwatchVm> ColourSwatches => Palette.LabelColours[ColourTab].Select((hex, i) => new SwatchVm
    {
        Label = Palette.LabelNames[i], Hex = hex, On = string.Equals(hex, CurrentColour, StringComparison.OrdinalIgnoreCase), Pick = Cmd.Of(() => SetFolderColour(hex)),
    }).ToList();
}
