using System.Windows;
using System.Windows.Input;
using CraftLibrary.App.Services;
using CraftLibrary.Core;
using Microsoft.Win32;

namespace CraftLibrary.App.ViewModels;

public sealed partial class MainViewModel
{
    private bool _busy;

    /// <summary>Runs a file change away from the screen, so the window never freezes, and reports problems in plain words.</summary>
    private async Task<T?> Do<T>(Func<T> work)
    {
        try { return await Task.Run(work); }
        catch (FriendlyException ex) { ShowMessage(ex.Message); }
        catch (UnauthorizedAccessException) { ShowMessage("Windows didn’t allow that. The file may be read-only, or open in another program."); }
        catch (IOException ex) { ShowMessage("That didn’t work: " + ex.Message); }
        catch (Exception ex) { ShowMessage("Something went wrong: " + ex.Message); }
        return default;
    }

    /// <summary>Things she undid, newest last, so Redo can do them again. Any new action clears it.</summary>
    private readonly List<HistoryEntry> _redo = [];

    private void Record(HistoryEntry e, bool isRedo = false)
    {
        if (!isRedo) _redo.Clear();
        History.Add(e);
        ToastPlain = false;
        Toast = e.Msg;
        if (e.Type == "move")
        {
            var day = DateTime.Now.ToString("yyyy-MM-dd");
            Data.SortedPerDay[day] = Data.SortedPerDay.GetValueOrDefault(day) + 1;
            foreach (var old in Data.SortedPerDay.Keys.Where(k => k != day).ToList()) Data.SortedPerDay.Remove(old);
        }
        SaveSoon();
    }

    // ---------------- going places (with Back and Forward, like File Explorer) ----------------

    private readonly record struct Place(string View, string Folder, string FocusDup);
    private readonly List<Place> _back = [];
    private readonly List<Place> _forward = [];
    private bool _navigating;

    private Place Here => new(View, Folder, FocusDup);

    /// <summary>Called just before going somewhere new, so Back can return here.</summary>
    private void Remember(Place next)
    {
        if (_navigating || next == Here) return;
        _back.Add(Here);
        if (_back.Count > 100) _back.RemoveAt(0);
        _forward.Clear();
    }

    private void GoTo(Place p)
    {
        _navigating = true;
        try
        {
            if (p.View == "dupes") OpenDups(p.FocusDup);
            else if (p.View == "bin") GoBin.Execute(null);
            else if (p.Folder == Unsorted || p.Folder == LaterPile || FolderById(p.Folder) != null) SetFolder(p.Folder);
            else SetFolder(Data.Locations.FirstOrDefault() is { } l ? PathRules.Norm(l.Path) : Unsorted);
        }
        finally { _navigating = false; }
    }

    public ICommand GoBack => Cmd.Of(() =>
    {
        if (_back.Count == 0) return;
        var p = _back[^1];
        _back.RemoveAt(_back.Count - 1);
        _forward.Add(Here);
        GoTo(p);
    });

    public ICommand GoForward => Cmd.Of(() =>
    {
        if (_forward.Count == 0) return;
        var p = _forward[^1];
        _forward.RemoveAt(_forward.Count - 1);
        _back.Add(Here);
        GoTo(p);
    });

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;
    public string BackHint => _back.Count > 0 ? "Back to " + PlaceName(_back[^1]) : "Nothing to go back to yet";
    public string ForwardHint => _forward.Count > 0 ? "Forward to " + PlaceName(_forward[^1]) : "Nothing to go forward to";

    private string PlaceName(Place p) => p.View switch
    {
        "dupes" => "Duplicates",
        "bin" => "Recycle Bin",
        _ => FolderById(p.Folder)?.Label is { Length: > 0 } l ? "“" + l + "”" : "the folder",
    };

    public void SetFolder(string id)
    {
        Remember(new Place("browse", id, ""));
        FileItemVm.Thumbs?.ClearQueue();
        Folder = id;
        _query = "";
        Selected = null;
        SelectedTile = null;
        Renaming = false;
        View = "browse";
        EditingFolderNote = false;
        ColourOpen = false;
        _resetScroll = true;
        if (S.Remember) SaveSoon();
        RefreshAll();
    }

    public ICommand GoInbox => Cmd.Of(() => SetFolder(Unsorted));
    public ICommand GoLater => Cmd.Of(() => SetFolder(LaterPile));
    public ICommand GoBin => Cmd.Of(() => { Remember(new Place("bin", Folder, "")); View = "bin"; _query = ""; Renaming = false; RefreshAll(); });
    public ICommand GoBrowse => Cmd.Of(() => { Remember(new Place("browse", Folder, "")); View = "browse"; Renaming = false; RefreshAll(); });
    public ICommand GoDupes => Cmd.Of(() => OpenDups(""));

    public void OpenDups(string key)
    {
        Remember(new Place("dupes", Folder, key));
        MenuOpen = false;
        FocusDup = key;
        View = "dupes";
        Renaming = false;
        RefreshAll();
    }

    public void ShowInFolder(FileItemVm it)
    {
        Remember(new Place("browse", it.Folder, ""));
        View = "browse";
        Folder = it.Folder;
        _query = "";
        FocusDup = "";
        _resetScroll = true;
        RefreshAll();
        Selected = it;
    }

    public void Select(FileItemVm it)
    {
        SelectedTile = null;
        Selected = it;
    }

    public void SelectTile(FolderTileVm t)
    {
        Selected = null;
        SelectedTile = t;
    }

    /// <summary>Arrow keys move the selection through the files.</summary>
    public void MoveSelection(int dx, int dy)
    {
        var vis = Visible();
        if (vis.Count == 0) return;
        var cols = S.View == "grid" ? _cols : 1;
        var subs = SubfolderTiles().Count;
        var i = Selected != null ? vis.IndexOf(Selected) : -1;
        if (i < 0) { Select(vis[0]); return; }
        var pos = i + subs + dx + dy * cols;
        var j = Math.Clamp(pos - subs, 0, vis.Count - 1);
        Select(vis[j]);
    }

    public ICommand OpenSelected => Cmd.Of(() => { if (Selected != null) OpenFile(Selected, null); });
    public ICommand OpenTile => Cmd.Of(() => { if (SelectedTile != null) SetFolder(SelectedTile.Id); });

    // ---------------- focus mode, menus ----------------

    public ICommand ToggleFocus => Cmd.Of(() => { Focus = !Focus; MenuOpen = false; HiddenMenuOpen = false; ColourOpen = false; RefreshAll(); });
    public ICommand ToggleMenu => Cmd.Of(() => { MenuOpen = !MenuOpen; HiddenMenuOpen = false; });
    public ICommand ToggleHiddenMenu => Cmd.Of(() => { HiddenMenuOpen = !HiddenMenuOpen; MenuOpen = false; });
    public ICommand OpenSettings => Cmd.Of(() => { MenuOpen = false; SettingsOpen = true; });
    public ICommand CloseSettings => Cmd.Of(() => SettingsOpen = false);
    public ICommand OpenHistory => Cmd.Of(() => { MenuOpen = false; HistoryOpen = true; OnPropertyChanged(nameof(HistoryRows)); OnPropertyChanged(nameof(HistoryEmpty)); });
    public ICommand CloseHistory => Cmd.Of(() => HistoryOpen = false);
    public ICommand MenuUndo => Cmd.Of(() => { MenuOpen = false; _ = UndoStepsAsync(1); });
    public ICommand Undo => Cmd.Of(() => _ = UndoStepsAsync(1));
    public ICommand Redo => Cmd.Of(() => _ = RedoAsync());
    public bool CanRedo => _redo.Count > 0;
    public string RedoHint => _redo.Count > 0 ? _redo[^1].Msg : "Nothing to redo";
    public ICommand ShowAllHidden => Cmd.Of(() =>
    {
        foreach (var f in Settings.FeatureList) if (S.Feature(f.Key) == "hide") S.Features[f.Key] = "show";
        HiddenMenuOpen = false;
        SettingsChanged();
    });
    public ICommand ToggleFileOptions => Cmd.Of(() => { FileOptionsOpen = !FileOptionsOpen; OpenWithOpen = false; });
    public ICommand ToggleOpenWith => Cmd.Of(() => OpenWithOpen = !OpenWithOpen);
    public ICommand DismissInboxHint => Cmd.Of(() => { Data.InboxHintHidden = true; SaveSoon(); OnPropertyChanged(nameof(ShowInboxHint)); });

    public void SetFeature(string key, string value)
    {
        S.Features[key] = value;
        SettingsChanged();
    }

    // ---------------- preview position ----------------
    public ICommand PopOut => Cmd.Of(() => SetPreview("popout"));
    public ICommand DockPreview => Cmd.Of(() => SetPreview("right"));
    public ICommand HidePreview => Cmd.Of(() => SetPreview("hidden"));
    public ICommand ShowPreview => Cmd.Of(() => SetPreview("right"));

    private void SetPreview(string where)
    {
        S.Preview = where;
        SettingsChanged();
    }

    // ---------------- locations ----------------

    public ICommand ChooseCraftFolder => Cmd.Of(() => PickFolder("setup"));
    public ICommand AddLocation => Cmd.Of(() => PickFolder("add"));
    public ICommand ChangeInbox => Cmd.Of(() => PickFolder("inbox"));

    private void PickFolder(string mode)
    {
        MenuOpen = false;
        var titles = new Dictionary<string, string>
        {
            ["setup"] = "Choose your main craft folder",
            ["add"] = "Add a location (a portable drive or another folder)",
            ["inbox"] = "Choose the folder to sort from",
        };
        var start = mode == "inbox" ? Data.InboxPath : Data.Locations.FirstOrDefault()?.Path ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dlg = new OpenFolderDialog { Title = titles[mode], InitialDirectory = Directory.Exists(start) ? start : "" };
        if (dlg.ShowDialog(Application.Current.MainWindow) != true) return;
        var p = PathRules.Norm(dlg.FolderName);
        var problem = FolderRules.ForThisComputer(Path.GetDirectoryName(_dataFile)!).Problem(p);
        if (problem.Length > 0) { ShowMessage(problem); return; }

        var leaf = Path.GetFileName(p.TrimEnd('\\'));
        var drive = p.Length >= 2 && p[1] == ':' ? p[..2].ToUpperInvariant() : "";
        var label = leaf.Length > 0 ? leaf + (drive.Length > 0 && drive != "C:" ? " (" + drive + ")" : "") : "Drive " + (drive.Length > 0 ? drive : p);
        var clash = Data.Locations.FirstOrDefault(l => PathRules.IsInside(p, l.Path) || PathRules.IsInside(l.Path, p));

        if (mode == "setup")
        {
            Data.Locations = [new Location { Key = p, Label = label, Path = p }];
            if (string.IsNullOrEmpty(Data.InboxPath) || PathRules.IsInside(Data.InboxPath, p))
            {
                var dl = KnownFolders.Downloads();
                Data.InboxPath = PathRules.IsInside(dl, p) ? "" : dl;
            }
            IsSetup = false;
            _folder = p;
            ShowMessage("Craft folder set to " + p);
        }
        else if (mode == "add")
        {
            if (clash != null) { ShowMessage(PathRules.Same(clash.Path, p) ? label + " is already in your locations." : "That folder overlaps “" + clash.Label + "”, which is already in your locations."); return; }
            Data.Locations.Add(new Location { Key = p, Label = label, Path = p });
            ShowMessage("Added " + label + " to your locations");
        }
        else
        {
            Data.InboxPath = p;
            _folder = Unsorted;
            ShowMessage("New files to sort will be picked up from " + p);
        }
        Selected = null;
        SaveSoon();
        _lastScanSig = "";
        _resetScroll = true;
        RefreshAll();
        _ = RescanAsync();
        if (GuideOpen) Guide.FolderChosen();
    }

    public void ForgetLocation(string key)
    {
        var l = Data.Locations.FirstOrDefault(x => x.Key == key);
        if (l == null) return;
        Data.Locations.Remove(l);
        ShowMessage("Took “" + l.Label + "” off the list. Nothing in it was moved or deleted. You can add it again with Add location.");
        if (Data.Locations.Count == 0)
        {
            IsSetup = true;
            _items = [];
            _byPath.Clear();
            _folder = Unsorted;
        }
        else if (_folderIndex.GetValueOrDefault(Folder) is { } f && f.Loc == key) _folder = PathRules.Norm(Data.Locations[0].Path);
        SaveSoon();
        _lastScanSig = "";
        RefreshAll();
        _ = RescanAsync();
    }

    // ---------------- opening files ----------------

    public void OpenFile(FileItemVm it, string? exe)
    {
        exe ??= S.OpenWith.GetValueOrDefault(it.Ext, "");
        var name = exe.Length > 0 ? Data.Programs.FirstOrDefault(p => p.Exe == exe)?.Name ?? Path.GetFileNameWithoutExtension(exe) : Platform.DefaultProgramName(it.Ext);
        FileOptionsOpen = false;
        OpenWithOpen = false;
        ShowMessage("Opening “" + it.FullName + "”" + (name.Length > 0 ? " in " + name : "") + "…");
        try
        {
            _safe.AssertAllowed(it.Path);
            WindowsPlatform.Open(it.Path, exe);
        }
        catch (FriendlyException ex) { ShowMessage(ex.Message); }
        catch (Exception) { ShowMessage("Windows has no program set for ." + it.Ext + " files. Use “Open with…” to choose one."); }
    }

    public ICommand ChooseOtherProgram => Cmd.Of(() =>
    {
        if (Selected == null) return;
        FileOptionsOpen = false;
        OpenWithOpen = false;
        var hwnd = new System.Windows.Interop.WindowInteropHelper(Application.Current.MainWindow).Handle;
        WindowsPlatform.OpenWithDialog(hwnd, Selected.Path);
    });

    public ICommand ShowInExplorer => Cmd.Of(() =>
    {
        if (Selected == null) return;
        FileOptionsOpen = false;
        try { _safe.AssertAllowed(Selected.Path); WindowsPlatform.ShowInExplorer(Selected.Path); }
        catch (Exception ex) { ShowMessage(ex.Message); }
    });

    /// <summary>Right-click › "Hide all .xlsx files": hides that kind of file everywhere (nothing is moved or deleted).</summary>
    public void HideKind(string ext)
    {
        var e = ext.ToLowerInvariant();
        if (!S.IsHiddenExt(e)) S.HiddenExts.Add(e);
        SettingsChanged();
        ShowMessage("All ." + e + " files are now hidden. They’re still in their folders. To see them again: Menu › Settings › Layout › Hide kinds of files.");
    }

    /// <summary>Opens one of her folders in File Explorer.</summary>
    public void OpenFolderInExplorer(string folder)
    {
        try
        {
            _safe.AssertAllowed(folder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe") { ArgumentList = { folder }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception ex) { ShowMessage(ex is FriendlyException ? ex.Message : "File Explorer couldn’t open that folder."); }
    }

    public ICommand OpenWindowsBin => Cmd.Of(WindowsPlatform.OpenRecycleBin);

    // ---------------- Sort later and notes ----------------

    public ICommand ToggleLater => Cmd.Of(() =>
    {
        if (Selected is not { } it) return;
        FileOptionsOpen = false;
        var on = !it.Later;
        it.Later = on;
        Record(new HistoryEntry { Type = on ? "later" : "unlater", Path = it.Path, Msg = on ? "Set “" + it.FullName + "” aside to sort later" : "Took “" + it.FullName + "” out of Sort later" });
        RefreshAll();
    });

    /// <summary>Her own note on the selected file (typed in the preview panel).</summary>
    public string SelectedNote
    {
        get => Selected?.Note ?? "";
        set { if (Selected != null && Selected.Note != value) { Selected.Note = value; SaveSoon(); } }
    }

    public ICommand StartFolderNote => Cmd.Of(() => { FolderNoteDraft = FolderNote; EditingFolderNote = true; ColourOpen = false; OnPropertyChanged(string.Empty); });
    public ICommand CancelFolderNote => Cmd.Of(() => { EditingFolderNote = false; OnPropertyChanged(string.Empty); });
    public ICommand SaveFolderNote => Cmd.Of(() =>
    {
        var t = FolderNoteDraft.Trim();
        if (t.Length > 0) Data.FolderNotes[Folder] = t; else Data.FolderNotes.Remove(Folder);
        EditingFolderNote = false;
        SaveSoon();
        OnPropertyChanged(string.Empty);
    });

    // ---------------- colour labels ----------------

    public void OpenColourDialog()
    {
        var cur = CurrentColour;
        ColourTab = Palette.LabelColours.FirstOrDefault(kv => kv.Value.Contains(cur, StringComparer.OrdinalIgnoreCase)).Key ?? "pastel";
        if (cur.Length > 0) CustomColour = cur;
        ColourOpen = true;
        OnPropertyChanged(string.Empty);
    }

    public ICommand OpenColour => Cmd.Of(OpenColourDialog);
    public ICommand CloseColour => Cmd.Of(() => ColourOpen = false);
    public ICommand RemoveColour => Cmd.Of(() => SetFolderColour(""));
    public ICommand UseCustomColour => Cmd.Of(() =>
    {
        var hex = CustomColour.Trim();
        if (!hex.StartsWith('#')) hex = "#" + hex;
        try { Palette.Parse(hex); } catch { ShowMessage("That isn’t a colour code. It should look like #9ED6CF."); return; }
        if (hex.Length != 7) { ShowMessage("That isn’t a colour code. It should look like #9ED6CF."); return; }
        SetFolderColour(hex.ToUpperInvariant());
    });

    private void SetFolderColour(string hex)
    {
        if (hex.Length > 0) Data.FolderColors[Folder] = hex; else Data.FolderColors.Remove(Folder);
        SaveSoon();
        RefreshAll();
    }

    // ---------------- renaming ----------------

    public ICommand StartRename => Cmd.Of(() => { if (Selected != null && FeatRename) { Draft = Selected.Name; Renaming = true; } });
    public ICommand CancelRename => Cmd.Of(() => Renaming = false);
    public ICommand SaveName => Cmd.Of(() => _ = CommitRenameAsync());

    public async Task CommitRenameAsync()
    {
        if (Selected is not { } it || _busy) return;
        var name = Draft.Trim();
        if (name.Length == 0 || name == it.Name) { Renaming = false; return; }
        _busy = true;
        var oldPath = it.Path;
        var oldName = it.Name;
        var dest = await Do(() => _safe.Rename(oldPath, name));
        _busy = false;
        if (dest == null) return;
        Rekey(it, dest, it.Folder);
        Renaming = false;
        Record(new HistoryEntry { Type = "rename", Path = dest, FromPath = oldPath, FromName = oldName, Msg = "Renamed to “" + it.FullName + "”" });
        RefreshAll();
    }

    /// <summary>A file's path changed (moved or renamed): keep everything known about it.</summary>
    private void Rekey(FileItemVm it, string newPath, string folder)
    {
        _byPath.Remove(it.Path);
        if (Data.FileMeta.Remove(it.Path, out var meta)) Data.FileMeta[newPath] = meta;
        it.Path = newPath;
        it.Name = PathRules.SplitName(newPath).Base;
        it.Dir = Path.GetDirectoryName(newPath)!;
        it.Folder = folder;
        _byPath[newPath] = it;
        _counts = Shown.GroupBy(i => i.Folder, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
    }

    private string FolderForDir(string dir) => !string.IsNullOrEmpty(Data.InboxPath) && PathRules.Same(dir, Data.InboxPath) ? Unsorted : PathRules.Norm(dir);
    private string DirForFolder(string id) => id == Unsorted ? Data.InboxPath : id;

    // ---------------- moving ----------------

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _moveOpen;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private string? _moveTarget;

    /// <summary>The folder picker is shared: true when it is choosing where to put a copy rather than where to move.</summary>
    private bool _pickerCopies;

    public ICommand OpenMove => Cmd.Of(() => OpenPicker(copy: false));
    public ICommand OpenCopyTo => Cmd.Of(() => OpenPicker(copy: true));

    private void OpenPicker(bool copy)
    {
        if (Selected == null || (!copy && !FeatMove)) return;
        _pickerCopies = copy;
        FileOptionsOpen = false;
        MoveTarget = null;
        MoveOpen = true;
        Renaming = false;
        OnPropertyChanged(nameof(MoveRows));
        OnPropertyChanged(nameof(MoveTitle));
        OnPropertyChanged(nameof(MoveIntro));
        OnPropertyChanged(nameof(MoveButtonText));
    }

    public ICommand CloseMove => Cmd.Of(() => MoveOpen = false);
    public string MoveTitle => Selected is { } s ? (_pickerCopies ? "Copy “" : "Move “") + s.FullName + "” to…" : "";
    public string MoveIntro => _pickerCopies ? "Pick the folder to put a copy in. The original stays where it is." : "Pick the folder this file belongs in.";
    public string MoveButtonText => MoveTarget != null && FolderById(MoveTarget) is { } f
        ? (_pickerCopies ? "Copy to " : "Move to ") + f.Label : (_pickerCopies ? "Copy here" : "Move here");
    public bool CanMoveHere => MoveTarget != null;

    public List<MoveRowVm> MoveRows
    {
        get
        {
            var it = Selected;
            if (it == null) return [];
            var rows = new List<MoveRowVm>();
            MoveRowVm Row(FolderDef f, int depth, string? label = null)
            {
                // Moving to the folder it is already in does nothing; copying there is fine (the copy gets " (2)").
                var cur = !_pickerCopies && string.Equals(it.Folder, f.Id, StringComparison.OrdinalIgnoreCase);
                var id = f.Id;
                return new MoveRowVm
                {
                    Label = label ?? f.Label, Id = id, Indent = new Thickness(depth * 22, 0, 0, 0), IsCurrent = cur, IsTarget = MoveTarget == id,
                    Pick = Cmd.Of(() =>
                    {
                        if (cur) return;
                        MoveTarget = id;
                        OnPropertyChanged(nameof(MoveRows)); OnPropertyChanged(nameof(MoveButtonText)); OnPropertyChanged(nameof(CanMoveHere));
                    }),
                };
            }
            if (!string.IsNullOrEmpty(Data.InboxPath) && FeatInbox)
            {
                rows.Add(new MoveRowVm { IsHeader = true, Label = "Sorting", Path = Data.InboxPath });
                rows.Add(Row(FolderById(Unsorted)!, 0));
            }
            foreach (var l in Data.Locations)
            {
                if (_unavailable.Contains(l.Key)) continue;
                rows.Add(new MoveRowVm { IsHeader = true, Label = l.Label, Path = l.Path });
                var rootId = PathRules.Norm(l.Path);
                if (_folderIndex.GetValueOrDefault(rootId) is { } root) rows.Add(Row(root, 0, l.Label + " (top level)"));
                void Add(FolderDef f, int d) { rows.Add(Row(f, d)); foreach (var k in _kids.GetValueOrDefault(f.Id) ?? []) Add(k, d + 1); }
                foreach (var k in _kids.GetValueOrDefault(rootId) ?? []) Add(k, 1);
            }
            return rows;
        }
    }

    public ICommand MoveHere => Cmd.Of(() =>
    {
        if (MoveTarget == null || Selected == null) return;
        MoveOpen = false;
        if (_pickerCopies) _ = CopyAsync(Selected, MoveTarget);
        else _ = MoveAsync(Selected, MoveTarget);
    });

    // ---------------- copying ----------------

    /// <summary>Puts the file on the Windows clipboard, so it can be pasted into a folder, an email or another program.</summary>
    public void CopyToClipboard(FileItemVm it)
    {
        try
        {
            _safe.AssertAllowed(it.Path);
            var list = new System.Collections.Specialized.StringCollection { it.Path };
            System.Windows.Clipboard.SetFileDropList(list);
            ShowMessage("Copied “" + it.FullName + "”. You can paste it into a folder in File Explorer, an email or another program.");
        }
        catch (FriendlyException ex) { ShowMessage(ex.Message); }
        catch (Exception) { ShowMessage("Windows didn’t allow that copy just now. Please try again."); }
    }

    public async Task CopyAsync(FileItemVm it, string to)
    {
        var target = FolderById(to);
        var destDir = DirForFolder(to);
        if (_busy || target == null || string.IsNullOrEmpty(destDir)) return;
        _busy = true;
        ShowMessage("Copying “" + it.FullName + "” to " + target.Label + "…");
        var src = it.Path;
        var dest = await Do(() => _safe.Copy(src, destDir));
        _busy = false;
        if (dest == null) return;
        var copy = AddItemFor(dest);
        var named = PathRules.SplitName(dest).Base != it.Name ? ". There was already a file with that name, so the copy is called “" + Path.GetFileName(dest) + "”" : "";
        Record(new HistoryEntry { Type = "copy", Path = dest, FromPath = src, Msg = "Copied “" + it.FullName + "” to " + target.Label + named });
        RecountAndRefresh();
        if (copy != null && string.Equals(copy.Folder, Folder, StringComparison.OrdinalIgnoreCase)) Selected = copy;
    }

    /// <summary>Adds a file that has just appeared (a new copy) without waiting for the next look at the folders.</summary>
    private FileItemVm? AddItemFor(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var (b, ext) = PathRules.SplitName(path);
            var entry = new FileEntry(path, info.DirectoryName!, b, ext.ToLowerInvariant(), info.Length, info.CreationTimeUtc, info.LastWriteTimeUtc);
            var vm = new FileItemVm(entry, FolderForDir(info.DirectoryName!)) { BadgesOn = S.ShowBadges && S.Shows("dupes") };
            vm.MetaLine = MetaFor(vm);
            _items = _items.Append(vm).ToList();
            _byPath[path] = vm;
            return vm;
        }
        catch (IOException) { return null; }
    }

    private void RemoveItem(string path)
    {
        _items = _items.Where(i => !string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();
        _byPath.Remove(path);
        Data.FileMeta.Remove(path);
    }

    public async Task MoveAsync(FileItemVm it, string to)
    {
        if (_busy || string.Equals(it.Folder, to, StringComparison.OrdinalIgnoreCase)) return;
        var target = FolderById(to);
        var destDir = DirForFolder(to);
        if (target == null || string.IsNullOrEmpty(destDir)) return;
        var vis = Visible();
        var idx = vis.IndexOf(it);
        var next = Q.Length > 0 ? it : idx >= 0 && idx + 1 < vis.Count ? vis[idx + 1] : idx > 0 ? vis[idx - 1] : null;
        _busy = true;
        if (Platform.DriveKindOf(it.Path) != Platform.DriveKindOf(destDir) || !string.Equals(Path.GetPathRoot(it.Path), Path.GetPathRoot(destDir), StringComparison.OrdinalIgnoreCase))
            ShowMessage("Copying “" + it.FullName + "” to " + target.Label + "…");
        var from = it.Path;
        var fromFolder = it.Folder;
        var laterWas = it.Later;
        var dest = await Do(() => _safe.Move(from, destDir));
        _busy = false;
        if (dest == null) return;
        var newName = PathRules.SplitName(dest).Base;
        var oldName = it.Name;
        Rekey(it, dest, FolderForDir(Path.GetDirectoryName(dest)!));
        it.Later = false;
        var msg = "Moved “" + PathRules.WithExt(oldName, it.Ext) + "” to " + target.Label
            + (newName != oldName ? ". A file with that name was already there, so this one is now called “" + it.FullName + "”" : "");
        Record(new HistoryEntry { Type = "move", Path = dest, FromPath = from, FromName = oldName, LaterWas = laterWas, Msg = msg });
        if (next != null && !ReferenceEquals(next, it)) Selected = next;
        else if (Q.Length == 0) Selected = null;
        RefreshAll();
    }

    // ---------------- removing ----------------

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _confirmOpen;
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _confirmDontAsk;
    private List<FileItemVm> _confirmItems = [];
    private string _confirmMsg = "";
    public string ConfirmTitle => _confirmItems.Count == 1 ? "Remove “" + _confirmItems[0].FullName + "”?" : "Remove " + _confirmItems.Count + " files?";
    public bool ConfirmPortable => _confirmItems.Any(i => _remover.NeedsRemovedFolder(i.Path));

    public ICommand RecycleSelected => Cmd.Of(() => { FileOptionsOpen = false; if (Selected != null && FeatRecycle) Recycle([Selected]); });
    public ICommand ConfirmNo => Cmd.Of(() => ConfirmOpen = false);
    public ICommand ConfirmYes => Cmd.Of(() =>
    {
        ConfirmOpen = false;
        if (ConfirmDontAsk) { S.AskRecycle = false; SettingsChanged(); }
        _ = DoRecycleAsync(_confirmItems, _confirmMsg);
    });

    public void Recycle(List<FileItemVm> items, string msg = "")
    {
        if (items.Count == 0) return;
        if (S.AskRecycle)
        {
            _confirmItems = items;
            _confirmMsg = msg;
            ConfirmDontAsk = false;
            ConfirmOpen = true;
            OnPropertyChanged(nameof(ConfirmTitle));
            OnPropertyChanged(nameof(ConfirmPortable));
            return;
        }
        _ = DoRecycleAsync(items, msg);
    }

    private async Task DoRecycleAsync(List<FileItemVm> items, string msg)
    {
        if (_busy) return;
        _busy = true;
        Renaming = false;
        var paths = items.Select(i => i.Path).ToList();
        var (entries, errors) = await Task.Run(() =>
        {
            var done = new List<RemovedEntry>();
            var errs = new List<string>();
            foreach (var p in paths)
            {
                try { done.Add(_remover.Remove(p)); }
                catch (FriendlyException ex) { errs.Add(ex.Message); }
                catch (Exception ex) { errs.Add(ex.Message); }
            }
            return (done, errs);
        });
        _busy = false;
        if (entries.Count == 0) { ShowMessage(errors.FirstOrDefault() ?? "Nothing was removed."); return; }
        var gone = new HashSet<string>(entries.Select(e => e.OrigPath), StringComparer.OrdinalIgnoreCase);
        var vis = Visible();
        var sel = Selected;
        _items = _items.Where(i => !gone.Contains(i.Path)).ToList();
        foreach (var g in gone) { _byPath.Remove(g); Data.FileMeta.Remove(g); }
        Data.Removed.AddRange(entries);
        var text = msg.Length > 0 ? msg : entries.Count == 1 ? "Sent 1 file to the Recycle Bin" : "Sent " + entries.Count + " files to the Recycle Bin";
        if (entries.Any(e => e.Method == "folder")) text += " (files on portable drives went to the “" + SafeFiles.RemovedFolderName + "” folder on that drive)";
        if (errors.Count > 0) text += ". " + errors.Count + " could not be removed: " + errors[0];
        Record(new HistoryEntry { Type = "recycle", Entries = entries, Msg = text });
        if (sel != null && gone.Contains(sel.Path))
        {
            var idx = vis.IndexOf(sel);
            var next = vis.Skip(idx + 1).Concat(vis.Take(Math.Max(0, idx)).Reverse()).FirstOrDefault(v => !gone.Contains(v.Path));
            Selected = next;
        }
        RecountAndRefresh();
    }

    public void KeepOnly(string key, FileItemVm keep)
    {
        var others = Shown.Where(i => i.Dup == key && !ReferenceEquals(i, keep)).ToList();
        Recycle(others, "Kept 1 copy and sent " + others.Count + (others.Count == 1 ? " copy" : " copies") + " to the Recycle Bin");
    }

    // ---------------- putting back ----------------

    public ICommand RestoreAll => Cmd.Of(() => _ = RestoreAsync(Data.Removed.ToList()));

    private async Task<(List<(RemovedEntry Entry, string Back)> Back, List<string> Errors)> RestoreEntriesAsync(List<RemovedEntry> entries)
    {
        var res = await Task.Run(() =>
        {
            var back = new List<(RemovedEntry, string)>();
            var errs = new List<string>();
            var dropped = new List<RemovedEntry>();
            foreach (var e in entries)
            {
                try { back.Add((e, _remover.Restore(e))); }
                catch (FriendlyException ex) { errs.Add(ex.Message); if (ex.Message.StartsWith("Couldn’t find")) dropped.Add(e); }
                catch (Exception ex) { errs.Add(ex.Message); }
            }
            return (back, errs, dropped);
        });
        var done = res.back.Select(b => b.Item1.Id).Concat(res.dropped.Select(d => d.Id)).ToHashSet();
        Data.Removed.RemoveAll(e => done.Contains(e.Id));
        SaveSoon();
        return (res.back, res.errs);
    }

    private async Task RestoreAsync(List<RemovedEntry> entries)
    {
        if (_busy || entries.Count == 0) return;
        _busy = true;
        var (back, errors) = await RestoreEntriesAsync(entries);
        _busy = false;
        if (back.Count == 0) { ShowMessage(errors.FirstOrDefault() ?? "Nothing was put back."); RefreshAll(); return; }
        var msg = back.Count == 1 ? "Put “" + PathRules.WithExt(back[0].Entry.Name, back[0].Entry.Ext) + "” back" : "Put " + back.Count + " files back where they came from";
        if (errors.Count > 0) msg += ". " + errors.Count + " could not be put back: " + errors[0];
        Record(new HistoryEntry { Type = "restore", Entries = back.Select(b => { b.Entry.RemovedPath = b.Back; return b.Entry; }).ToList(), Msg = msg });
        _lastScanSig = "";
        await RescanAsync();
    }

    // ---------------- fonts ----------------

    public ICommand InstallFont => Cmd.Of(() => _ = InstallFontAsync());

    private async Task InstallFontAsync()
    {
        if (Selected is not { IsFont: true, Installed: false } it) return;
        ShowMessage("Installing “" + it.Name + "”…");
        var ok = await Do(() => { _safe.AssertAllowed(it.Path); FontService.Install(it.Path); return true; });
        if (!ok) return;
        it.Installed = true;
        Record(new HistoryEntry { Type = "install", Path = it.Path, Msg = "Installed “" + it.Name + "”. You can now use it in other programs, and choose it in Settings, under Fonts." });
        SettingsVm.SystemFonts = await Task.Run(FontService.InstalledFamilies);
        RefreshAll();
    }

    // ---------------- undo ----------------

    public async Task UndoStepsAsync(int count)
    {
        if (_busy || History.Count == 0 || count < 1) return;
        _busy = true;
        HistoryOpen = false;
        var steps = 0;
        string? problem = null;
        HistoryEntry? last = null;
        while (steps < count && History.Count > 0)
        {
            var e = History[^1];
            problem = await UndoOneAsync(e);
            if (problem != null) break;
            History.RemoveAt(History.Count - 1);
            _redo.Add(e);
            if (e.Type == "move")
            {
                var day = DateTime.Now.ToString("yyyy-MM-dd");
                if (Data.SortedPerDay.GetValueOrDefault(day) > 0) Data.SortedPerDay[day]--;
            }
            steps++;
            last = e;
        }
        _busy = false;
        ToastPlain = problem != null || steps > 1;
        Toast = problem != null ? (steps > 0 ? "Undid " + steps + (steps == 1 ? " step" : " steps") + ", then stopped: " + problem : problem)
            : steps > 1 ? "Undid " + steps + " steps" : "Undone: " + last?.Msg;
        SaveSoon();
        RecountAndRefresh();
        RescanSoon();
    }

    private async Task<string?> UndoOneAsync(HistoryEntry e)
    {
        try
        {
            switch (e.Type)
            {
                case "move":
                {
                    if (!_byPath.TryGetValue(e.Path, out var it)) return "“" + Path.GetFileName(e.Path) + "” isn’t there any more.";
                    var back = await Task.Run(() => _safe.Move(e.Path, Path.GetDirectoryName(e.FromPath)!, PathRules.SplitName(e.FromPath).Base));
                    Rekey(it, back, FolderForDir(Path.GetDirectoryName(back)!));
                    it.Later = e.LaterWas;
                    e.UndonePath = back;
                    return null;
                }
                case "rename":
                {
                    if (!_byPath.TryGetValue(e.Path, out var it)) return "“" + Path.GetFileName(e.Path) + "” isn’t there any more.";
                    var back = await Task.Run(() => _safe.Rename(e.Path, e.FromName));
                    Rekey(it, back, it.Folder);
                    Selected = it;
                    e.UndonePath = back;
                    return null;
                }
                case "recycle":
                {
                    var (back, errors) = await RestoreEntriesAsync(e.Entries);
                    if (back.Count == 0) return errors.FirstOrDefault() ?? "Could not put the files back.";
                    e.UndonePaths = back.Select(b => b.Back).ToList();
                    _lastScanSig = "";
                    await RescanAsync();
                    return null;
                }
                case "restore":
                {
                    var again = await Task.Run(() =>
                    {
                        var list = new List<RemovedEntry>();
                        foreach (var en in e.Entries)
                        {
                            var p = en.RemovedPath ?? en.OrigPath;
                            try { list.Add(_remover.Remove(p)); } catch { }
                        }
                        return list;
                    });
                    if (again.Count == 0) return "Could not remove the files again.";
                    var gone = again.Select(x => x.OrigPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    Data.Removed.AddRange(again);
                    _items = _items.Where(i => !gone.Contains(i.Path)).ToList();
                    foreach (var g in gone) _byPath.Remove(g);
                    e.Entries = again;
                    return null;
                }
                case "copy":
                {
                    // Undoing a copy sends the copy (never the original) to the Recycle Bin.
                    var removed = await Task.Run(() => _remover.Remove(e.Path));
                    RemoveItem(e.Path);
                    e.Entries = [removed];
                    return null;
                }
                case "install":
                {
                    await Task.Run(() => FontService.Uninstall(e.Path));
                    if (_byPath.TryGetValue(e.Path, out var it)) it.Installed = false;
                    return null;
                }
                case "later":
                case "unlater":
                    if (_byPath.TryGetValue(e.Path, out var item)) item.Later = e.Type == "unlater";
                    return null;
            }
            return null;
        }
        catch (FriendlyException ex) { return ex.Message; }
        catch (Exception ex) { return ex.Message; }
    }

    // ---------------- redo ----------------

    /// <summary>Does the most recently undone step again, on the real files, with the same safety rules.</summary>
    public async Task RedoAsync()
    {
        if (_busy || _redo.Count == 0) return;
        _busy = true;
        var e = _redo[^1];
        var problem = await RedoOneAsync(e);
        _busy = false;
        if (problem != null)
        {
            ShowMessage("Couldn’t redo that: " + problem);
            _redo.Clear(); // the files have changed since, so the rest can't be redone safely
        }
        else
        {
            _redo.RemoveAt(_redo.Count - 1);
            Record(e, isRedo: true);
            Toast = "Redone: " + e.Msg;
        }
        SaveSoon();
        RecountAndRefresh();
        RescanSoon();
    }

    private async Task<string?> RedoOneAsync(HistoryEntry e)
    {
        try
        {
            switch (e.Type)
            {
                case "move":
                {
                    if (!_byPath.TryGetValue(e.UndonePath, out var it)) return "“" + Path.GetFileName(e.UndonePath) + "” isn’t there any more.";
                    var dest = await Task.Run(() => _safe.Move(e.UndonePath, Path.GetDirectoryName(e.Path)!, PathRules.SplitName(e.Path).Base));
                    Rekey(it, dest, FolderForDir(Path.GetDirectoryName(dest)!));
                    it.Later = false;
                    e.Path = dest;
                    return null;
                }
                case "rename":
                {
                    if (!_byPath.TryGetValue(e.UndonePath, out var it)) return "“" + Path.GetFileName(e.UndonePath) + "” isn’t there any more.";
                    var dest = await Task.Run(() => _safe.Rename(e.UndonePath, PathRules.SplitName(e.Path).Base));
                    Rekey(it, dest, it.Folder);
                    Selected = it;
                    e.Path = dest;
                    return null;
                }
                case "recycle":
                {
                    var removed = await Task.Run(() =>
                    {
                        var list = new List<RemovedEntry>();
                        foreach (var p in e.UndonePaths) { try { list.Add(_remover.Remove(p)); } catch { } }
                        return list;
                    });
                    if (removed.Count == 0) return "the files couldn’t be removed again.";
                    var gone = removed.Select(x => x.OrigPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    Data.Removed.AddRange(removed);
                    _items = _items.Where(i => !gone.Contains(i.Path)).ToList();
                    foreach (var g in gone) { _byPath.Remove(g); Data.FileMeta.Remove(g); }
                    e.Entries = removed;
                    return null;
                }
                case "restore":
                {
                    var (back, errors) = await RestoreEntriesAsync(e.Entries);
                    if (back.Count == 0) return errors.FirstOrDefault() ?? "the files couldn’t be put back.";
                    e.Entries = back.Select(b => { b.Entry.RemovedPath = b.Back; return b.Entry; }).ToList();
                    _lastScanSig = "";
                    await RescanAsync();
                    return null;
                }
                case "copy":
                {
                    var dest = await Task.Run(() => _safe.Copy(e.FromPath, Path.GetDirectoryName(e.Path)!));
                    AddItemFor(dest);
                    e.Path = dest;
                    return null;
                }
                case "install":
                {
                    _safe.AssertAllowed(e.Path);
                    await Task.Run(() => FontService.Install(e.Path));
                    if (_byPath.TryGetValue(e.Path, out var it)) it.Installed = true;
                    return null;
                }
                case "later":
                case "unlater":
                    if (_byPath.TryGetValue(e.Path, out var item)) item.Later = e.Type == "later";
                    return null;
            }
            return null;
        }
        catch (FriendlyException ex) { return ex.Message; }
        catch (Exception ex) { return ex.Message; }
    }
}
