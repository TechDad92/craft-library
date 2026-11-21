using System.Text.Json;
using System.Text.Json.Serialization;

namespace CraftLibrary.Core;

/// <summary>
/// Everything that can be changed in Settings. Defaults stay calm and simple: comfort options start switched off.
/// </summary>
public sealed class Settings
{
    // How it looks
    public string Theme { get; set; } = "light";            // light | dark
    public string Accent { get; set; } = "teal";             // see Palette.Accents
    public string Strength { get; set; } = "pastel";         // pastel | muted | bold
    public string Tint { get; set; } = "neutral";            // see Palette.Tints
    public string Texture { get; set; } = "solid";           // solid | dots | mat | stripes | wash
    public Dictionary<string, string> Fonts { get; set; } = DefaultFonts();
    public string TextSize { get; set; } = "m";              // s | m | l
    public string LineSpacing { get; set; } = "normal";      // normal | relaxed | loose
    public string LetterSpacing { get; set; } = "normal";    // normal | wide | wider

    // Layout
    public string View { get; set; } = "grid";               // grid | list
    public string Thumb { get; set; } = "m";                 // s | m | l | xl
    public string Preview { get; set; } = "right";           // right | bottom | popout
    /// <summary>The folder list. Hidden with the small arrow at its top, brought back with the tab at the window's edge.</summary>
    public bool ShowFolders { get; set; } = true;
    /// <summary>The preview panel (wherever it is). Hidden with its small arrow, brought back with the tab at the window's edge.</summary>
    public bool ShowPreview { get; set; } = true;
    public string Sidebar { get; set; } = "left";            // left | right
    public string Density { get; set; } = "comfortable";     // comfortable | compact | spacious
    public bool ShowName { get; set; } = true;
    public bool ShowType { get; set; } = true;
    public bool ShowSize { get; set; } = true;
    public bool ShowDate { get; set; }
    public string Order { get; set; } = "name";              // name | added | type | size
    public string BtnLabels { get; set; } = "both";          // both | words | icons
    /// <summary>The toolbar under the banner (Back, Undo, Duplicates, Settings…). Shown to start with.</summary>
    public bool ShowToolbar { get; set; } = true;
    /// <summary>File endings she has chosen not to see (e.g. "xlsx"). The files are never touched, just not shown.</summary>
    public List<string> HiddenExts { get; set; } = [];

    public bool IsHiddenExt(string ext) => HiddenExts.Contains((ext ?? "").ToLowerInvariant());

    // Comfort & focus (all off to start with)
    public bool HighContrast { get; set; }
    public bool ReduceMotion { get; set; }
    public bool FadeMessages { get; set; }
    public bool Tips { get; set; }
    public bool ShowProgress { get; set; }
    public bool Remember { get; set; }

    // Files & safety
    public bool AskRecycle { get; set; } = true;
    public bool ShowBadges { get; set; } = true;
    /// <summary>Look on the Releases page for a newer version when the app opens. It only ever asks; nothing installs by itself.</summary>
    public bool CheckUpdates { get; set; } = true;
    /// <summary>File ending (e.g. "pdf") → the program chosen for it. Missing means the Windows default.</summary>
    public Dictionary<string, string> OpenWith { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // Buttons & features: show | hide | off
    public Dictionary<string, string> Features { get; set; } = DefaultFeatures();

    public static readonly (string Key, string Label)[] FeatureList =
    [
        ("search", "Search box"), ("move", "Move to…"), ("rename", "Rename"), ("recycle", "Send to Recycle Bin"),
        ("open", "Open button"), ("explorer", "Show in File Explorer"), ("dupes", "Duplicates"), ("undo", "Undo"),
        ("inbox", "To sort folder"), ("addloc", "Add location"), ("recyclecount", "Recycle Bin count"),
        ("later", "Sort later pile"), ("notes", "Notes on files and folders"),
        ("labels", "Folder colour labels"), ("history", "History")
    ];

    public static readonly (string Key, string Label)[] FontSections =
    [
        ("banner", "Top banner"), ("folders", "Folder panel"), ("files", "Files area"), ("preview", "Preview panel"),
        ("popups", "Menus, settings and pop-ups")
    ];

    public static Dictionary<string, string> DefaultFeatures() => FeatureList.ToDictionary(f => f.Key, _ => "show");
    public static Dictionary<string, string> DefaultFonts() => FontSections.ToDictionary(f => f.Key, _ => "figtree");

    public string Feature(string key) => Features.TryGetValue(key, out var v) ? v : "show";
    public bool Shows(string key) => Feature(key) == "show";

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, AppData.Json), AppData.Json)!;

    /// <summary>Fills anything missing (for example after an update adds a new setting) with its default.</summary>
    public void Repair()
    {
        Fonts ??= DefaultFonts();
        foreach (var (k, _) in FontSections) Fonts.TryAdd(k, "figtree");
        Features ??= DefaultFeatures();
        foreach (var (k, _) in FeatureList) Features.TryAdd(k, "show");
        Features.Remove("focus"); // Focus mode was taken out in 0.8.0
        // Before 0.8.0 "hidden" was one of the preview positions; now hiding is separate, so the position is kept.
        if (Preview == "hidden") { Preview = "right"; ShowPreview = false; }
        if (Preview is not ("right" or "bottom" or "popout")) Preview = "right";
        OpenWith = new Dictionary<string, string>(OpenWith ?? [], StringComparer.OrdinalIgnoreCase);
        HiddenExts = (HiddenExts ?? []).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e.Trim().TrimStart('.').ToLowerInvariant()).Distinct().ToList();
    }
}

public sealed class Location
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class FileMeta
{
    public bool Later { get; set; }
    public string Note { get; set; } = "";
}

public sealed class SavedProgram
{
    public string Exe { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>Everything the app remembers between visits.</summary>
public sealed class AppData
{
    public int Version { get; set; } = 1;
    public Settings Settings { get; set; } = new();
    public List<Location> Locations { get; set; } = [];
    public string InboxPath { get; set; } = "";
    public Dictionary<string, string> FolderColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> FolderNotes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool InboxHintHidden { get; set; }
    public Dictionary<string, FileMeta> FileMeta { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<RemovedEntry> Removed { get; set; } = [];
    public List<SavedProgram> Programs { get; set; } = [];
    public Dictionary<string, bool> Expanded { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? LastFolder { get; set; }
    public string? LastSelected { get; set; }
    /// <summary>
    /// Whether the first-run setup guide has been finished (or skipped). Missing in older saved data:
    /// then it counts as done when a craft folder was already chosen, so existing users never see it unasked.
    /// </summary>
    public bool? SetupGuideDone { get; set; }
    /// <summary>The guide's screen (1–9) to reopen at, if the app was closed partway through.</summary>
    public int SetupGuideStep { get; set; } = 1;
    /// <summary>Count of files moved per day ("yyyy-MM-dd"), for "Show today's progress".</summary>
    public Dictionary<string, int> SortedPerDay { get; set; } = [];

    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static AppData Load(string file)
    {
        foreach (var candidate in new[] { file, file + ".bak" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                var d = JsonSerializer.Deserialize<AppData>(File.ReadAllText(candidate), Json);
                if (d != null) { d.Repair(); return d; }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { /* try the backup */ }
        }
        var fresh = new AppData();
        fresh.Repair();
        return fresh;
    }

    /// <summary>Saves safely: writes a new copy first, keeps the previous one as a backup, then swaps.</summary>
    public void Save(string file)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        if (Removed.Count > 500) Removed = Removed.Skip(Removed.Count - 500).ToList();
        var tmp = file + ".new";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
        if (File.Exists(file)) File.Replace(tmp, file, file + ".bak", ignoreMetadataErrors: true);
        else File.Move(tmp, file);
    }

    private void Repair()
    {
        Settings ??= new Settings();
        Settings.Repair();
        Locations ??= [];
        InboxPath ??= "";
        FolderColors = new(FolderColors ?? [], StringComparer.OrdinalIgnoreCase);
        FolderNotes = new(FolderNotes ?? [], StringComparer.OrdinalIgnoreCase);
        FileMeta = new(FileMeta ?? [], StringComparer.OrdinalIgnoreCase);
        Removed ??= [];
        Programs ??= [];
        Expanded = new(Expanded ?? [], StringComparer.OrdinalIgnoreCase);
        SortedPerDay ??= [];
        SetupGuideDone ??= Locations.Count > 0;
        SetupGuideStep = Math.Clamp(SetupGuideStep, 1, 9);
    }
}
