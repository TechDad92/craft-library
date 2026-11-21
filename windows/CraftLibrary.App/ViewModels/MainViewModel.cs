using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftLibrary.App.Services;
using CraftLibrary.App.Themes;
using CraftLibrary.Core;

namespace CraftLibrary.App.ViewModels;

/// <summary>
/// Everything the main window shows and does. Split over several files:
/// this one (starting up, saving, reading folders), MainViewModel.Screen.cs (what is on screen)
/// and MainViewModel.Actions.cs (moving, renaming, removing, undo and the rest).
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    public const string Unsorted = "unsorted";
    public const string LaterPile = "__later";

    private readonly string _dataFile;
    public AppData Data { get; }
    public Settings S => Data.Settings;

    private readonly SafeFiles _safe;
    public WindowsPlatform Platform { get; } = new();
    private readonly Remover _remover;
    private readonly FolderWatcher _watcher;
    private readonly Duplicates _duplicates = new();
    private readonly Dispatcher _ui;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _clock;

    private List<FileItemVm> _items = [];
    private readonly Dictionary<string, FileItemVm> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private List<FolderDef> _folderDefs = [];
    private Dictionary<string, FolderDef> _folderIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<FolderDef>> _kids = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _unavailable = [];
    private string _lastScanSig = "";
    private int _scanToken;
    private DateTime _lastFocusScan = DateTime.MinValue;

    public List<HistoryEntry> History { get; } = [];
    public SettingsViewModel SettingsVm { get; }
    public SetupGuideViewModel Guide { get; }
    public HeaderRow Header { get; }

    public MainViewModel(string dataDir)
    {
        _ui = Dispatcher.CurrentDispatcher;
        _dataFile = Path.Combine(dataDir, "data.json");
        Data = AppData.Load(_dataFile);
        _safe = new SafeFiles(Path.Combine(dataDir, "thumbs"));
        _remover = new Remover(_safe, Platform);
        _watcher = new FolderWatcher(Platform);
        _watcher.Changed += () => _ui.BeginInvoke(() => RescanSoon());
        Header = new HeaderRow(this);
        SettingsVm = new SettingsViewModel(this);
        Guide = new SetupGuideViewModel(this);

        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { _saveTimer!.Stop(); SaveNow(); }, _ui);
        _saveTimer.Stop();
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), _ui);

        IsSetup = Data.Locations.Count == 0;
        _folder = Data.Locations.Count > 0 ? PathRules.Norm(Data.Locations[0].Path) : Unsorted;
        if (S.Remember && !string.IsNullOrEmpty(Data.LastFolder)) _folder = Data.LastFolder!;
        ApplyTheme();
    }

    /// <summary>Called once the window is showing.</summary>
    public async Task StartAsync()
    {
        _clock.Start();
        if (string.IsNullOrEmpty(Data.InboxPath))
        {
            var downloads = KnownFolders.Downloads();
            if (Directory.Exists(downloads)) Data.InboxPath = downloads;
        }
        RefreshAll();
        // First install: the setup guide, reopening at the screen it was left on.
        if (Data.SetupGuideDone != true)
        {
            var step = Data.SetupGuideStep;
            if (step > 2 && Data.Locations.Count == 0) step = 2;
            Guide.Step = step;
            GuideOpen = true;
        }
        if (Data.Locations.Count > 0) await RescanAsync();
        _ = Task.Run(() =>
        {
            var fonts = FontService.InstalledFamilies();
            foreach (var ext in FileKinds.CommonExts) Platform.DefaultProgramName(ext);
            _ui.BeginInvoke(() => { SettingsVm.SystemFonts = fonts; SettingsVm.Refresh(); });
        });
    }

    // ---------------- saving ----------------

    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveNow()
    {
        Data.LastFolder = S.Remember ? Folder : null;
        Data.LastSelected = S.Remember ? Selected?.Path : null;
        foreach (var it in _items)
        {
            if (it.Later || it.Note.Length > 0) Data.FileMeta[it.Path] = new FileMeta { Later = it.Later, Note = it.Note };
            else Data.FileMeta.Remove(it.Path);
        }
        try { Data.Save(_dataFile); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* try again next time */ }
    }

    public void Shutdown()
    {
        _clock.Stop();
        SaveNow();
        _watcher.Dispose();
    }

    // ---------------- look ----------------

    public void ApplyTheme() => ThemeService.Apply(Application.Current.Resources, S);

    /// <summary>After any change in Settings.</summary>
    public void SettingsChanged()
    {
        ApplyTheme();
        foreach (var it in _items) { it.MetaLine = MetaFor(it); it.BadgesOn = S.ShowBadges && S.Shows("dupes"); }
        SaveSoon();
        RefreshAll();
    }

    // ---------------- the clock: fading messages ----------------
    private DateTime _toastAt;

    private void Tick()
    {
        if (S.FadeMessages && Toast.Length > 0 && DateTime.Now - _toastAt > TimeSpan.FromSeconds(6)) Toast = "";
    }

    // ---------------- reading her folders ----------------

    private void ApplyRoots()
    {
        var roots = Data.Locations.Select(l => l.Path).ToList();
        if (!string.IsNullOrEmpty(Data.InboxPath)) roots.Add(Data.InboxPath);
        _safe.SetRoots(roots);
        _watcher.Watch(roots);
    }

    private DispatcherTimer? _rescanTimer;

    public void RescanSoon()
    {
        _rescanTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background, async (_, _) => { _rescanTimer!.Stop(); await RescanAsync(); }, _ui);
        _rescanTimer.Stop();
        _rescanTimer.Start();
    }

    /// <summary>When the window comes back to the front: pick up portable drives plugged in meanwhile.</summary>
    public void WindowActivated()
    {
        if (DateTime.Now - _lastFocusScan < TimeSpan.FromSeconds(45) || Data.Locations.Count == 0) return;
        _lastFocusScan = DateTime.Now;
        Platform.ForgetDrives();
        RescanSoon();
    }

    private sealed record ScanAll(List<(Location Loc, ScanResult Result)> Locations, ScanResult? Inbox);

    public async Task RescanAsync()
    {
        if (Data.Locations.Count == 0) return;
        var token = ++_scanToken;
        Scanning = true;
        ApplyRoots();
        var locs = Data.Locations.ToList();
        var inbox = Data.InboxPath;
        ScanAll all;
        try
        {
            all = await Task.Run(() => new ScanAll(
                locs.Select(l => (l, Scanner.Scan(l.Path, recursive: true))).ToList(),
                string.IsNullOrEmpty(inbox) ? null : Scanner.Scan(inbox, recursive: false, filterInbox: true)));
        }
        catch (Exception ex)
        {
            Scanning = false;
            ShowMessage("Could not read your folders: " + ex.Message);
            return;
        }
        if (token != _scanToken) return;
        ApplyScan(all);
    }

    private static string Signature(ScanAll all)
    {
        long Sum(IEnumerable<FileEntry> fs) => fs.Aggregate(0L, (a, f) => unchecked(a * 31 + f.ModifiedUtc.Ticks + f.Size + f.Path.GetHashCode()));
        var parts = all.Locations.Select(x => x.Result.Ok ? $"{x.Loc.Path}:{x.Result.Folders.Count}:{x.Result.Files.Count}:{Sum(x.Result.Files)}" : x.Loc.Path + ":gone");
        var inbox = all.Inbox is { Ok: true } i ? i.Files.Count + ":" + Sum(i.Files) : "-";
        return string.Join("|", parts) + "|" + inbox;
    }

    private void ApplyScan(ScanAll all)
    {
        var sig = Signature(all);
        if (sig == _lastScanSig && _items.Count > 0) { Scanning = false; return; }
        _lastScanSig = sig;

        var defs = new List<FolderDef>();
        var items = new List<FileItemVm>();
        var unavailable = new List<string>();
        var capped = false;
        var inbox = Data.InboxPath;
        var oldByPath = new Dictionary<string, FileItemVm>(_byPath, StringComparer.OrdinalIgnoreCase);

        FileItemVm Make(FileEntry f, string folder)
        {
            if (oldByPath.TryGetValue(f.Path, out var old) && old.SizeBytes == f.Size && old.ModifiedUtc == f.ModifiedUtc)
            {
                old.Folder = folder;
                old.Dir = f.Dir;
                return old;
            }
            var it = new FileItemVm(f, folder);
            if (Data.FileMeta.TryGetValue(f.Path, out var m)) { it.Later = m.Later; it.Note = m.Note; }
            it.MetaLine = MetaFor(it);
            it.BadgesOn = S.ShowBadges && S.Shows("dupes");
            if (oldByPath.TryGetValue(f.Path, out var prev)) { it.Dup = prev.Dup; it.Installed = prev.Installed; }
            return it;
        }

        foreach (var (loc, r) in all.Locations)
        {
            if (!r.Ok) { unavailable.Add(loc.Key); continue; }
            capped |= r.Capped;
            var rootId = PathRules.Norm(loc.Path);
            defs.Add(new FolderDef { Id = rootId, Label = loc.Label, Loc = loc.Key, IsRoot = true, Depth = -1 });
            foreach (var f in r.Folders)
            {
                var rel = Path.GetRelativePath(rootId, f.Path).Replace(Path.DirectorySeparatorChar.ToString(), " › ");
                defs.Add(new FolderDef { Id = f.Path, Label = f.Name, RelPath = rel, Depth = f.Depth, Loc = loc.Key, Parent = f.Parent });
            }
            foreach (var f in r.Files)
            {
                if (!string.IsNullOrEmpty(inbox) && PathRules.Same(f.Dir, inbox)) continue;
                items.Add(Make(f, f.Dir));
            }
        }
        if (all.Inbox is { Ok: true } ib) foreach (var f in ib.Files) items.Add(Make(f, Unsorted));

        _folderDefs = defs;
        _folderIndex = defs.GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _kids = defs.Where(d => d.Parent != null).GroupBy(d => d.Parent!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Label, NameOrder.Instance).ToList(), StringComparer.OrdinalIgnoreCase);
        _items = items;
        _byPath.Clear();
        foreach (var it in items) _byPath[it.Path] = it;
        _unavailable = unavailable;
        ScanCapped = capped;
        Scanning = false;

        if (Folder != Unsorted && Folder != LaterPile && !_folderIndex.ContainsKey(Folder))
            _folder = Data.Locations.FirstOrDefault(l => !_unavailable.Contains(l.Key)) is { } first ? PathRules.Norm(first.Path) : Unsorted;
        if (Selected != null && !_byPath.ContainsKey(Selected.Path)) Selected = null;
        if (Selected == null && S.Remember && Data.LastSelected != null && _byPath.TryGetValue(Data.LastSelected, out var keep)) Selected = keep;

        RecountAndRefresh();
        _ = AfterScanAsync(items);
    }

    /// <summary>Slower checks done after the files are on screen: duplicates and installed fonts.</summary>
    private async Task AfterScanAsync(List<FileItemVm> items)
    {
        DupScanning = true;
        var entries = items.Select(i => new FileEntry(i.Path, i.Dir, i.Name, i.Ext, i.SizeBytes, i.CreatedUtc, i.ModifiedUtc)).ToList();
        var fonts = items.Where(i => i.IsFont).Select(i => i.Path).ToList();
        Dictionary<string, string> dups;
        Dictionary<string, bool> installed;
        try
        {
            (dups, installed) = await Task.Run(() => (_duplicates.Find(entries, _safe.IsAllowed), fonts.ToDictionary(f => f, FontService.IsInstalled)));
        }
        catch { DupScanning = false; return; }
        if (!ReferenceEquals(items, _items)) return; // a newer scan has taken over
        foreach (var it in items)
        {
            it.Dup = dups.GetValueOrDefault(it.Path, "");
            if (it.IsFont) it.Installed = installed.GetValueOrDefault(it.Path);
        }
        DupScanning = false;
        RecountDuplicates();
        RefreshAll();
    }

    private void RecountDuplicates()
    {
        var n = _items.Where(i => i.Dup.Length > 0).GroupBy(i => i.Dup).ToDictionary(g => g.Key, g => g.Count());
        foreach (var it in _items) it.DupCount = it.Dup.Length > 0 ? n.GetValueOrDefault(it.Dup) : 0;
    }

    private void RecountAndRefresh()
    {
        _counts = _items.GroupBy(i => i.Folder, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        RecountDuplicates();
        RefreshAll();
    }

    private string MetaFor(FileItemVm it)
    {
        var bits = new List<string>();
        if (S.ShowSize) bits.Add(it.Size);
        if (S.ShowDate) bits.Add(it.Added);
        return string.Join(" · ", bits);
    }
}

/// <summary>Sorts names the way File Explorer does ("Card 2" before "Card 10").</summary>
public sealed class NameOrder : IComparer<string>
{
    public static readonly NameOrder Instance = new();
    public int Compare(string? x, string? y) => WindowsPlatform.CompareNames(x ?? "", y ?? "");
}

public static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);

    public static string Downloads()
    {
        try
        {
            if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var p) == 0)
            {
                var s = System.Runtime.InteropServices.Marshal.PtrToStringUni(p) ?? "";
                System.Runtime.InteropServices.Marshal.FreeCoTaskMem(p);
                return s;
            }
        }
        catch { }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
}
