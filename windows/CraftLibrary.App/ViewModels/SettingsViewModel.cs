using System.Text.Json;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftLibrary.App.Themes;
using CraftLibrary.Core;
using Microsoft.Win32;

namespace CraftLibrary.App.ViewModels;

/// <summary>Everything in the Settings panel. Changes show straight away and are saved for next time.</summary>
public sealed partial class SettingsViewModel(MainViewModel main) : ObservableObject
{
    private Settings S => main.S;
    private static readonly Settings Defaults = new();

    public List<string> SystemFonts { get; set; } = [];

    private void Set(Action<Settings> change)
    {
        change(S);
        main.SettingsChanged();
    }

    private ICommand SetCmd(Action<Settings> change) => Cmd.Of(() => Set(change));

    public void Refresh() => OnPropertyChanged(string.Empty);

    public ICommand Close => main.CloseSettings;
    public ICommand RunGuide => main.RunGuide;

    private List<OptionVm> Seg(string current, params (string Value, string Label, Action<Settings> Apply)[] opts) =>
        opts.Select(o => new OptionVm { Label = o.Label, On = current == o.Value, Pick = SetCmd(o.Apply) }).ToList();

    // ---------------- sections ----------------
    public sealed class Section
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required string Summary { get; init; }
        public required System.Windows.Media.Geometry Glyph { get; init; }
        public bool Changed { get; init; }
        public bool Visible { get; init; }
        public bool Open { get; init; }
        public required ICommand Toggle { get; init; }
        public required ICommand Reset { get; init; }
    }

    [ObservableProperty] private string _query = "";
    private readonly HashSet<string> _open = [];

    partial void OnQueryChanged(string value) => Refresh();

    private static readonly (string Id, string Title, string Icon, string[] Keys, string Words)[] SectionDefs =
    [
        ("colours", "Colours", "Palette", ["Theme", "Accent", "Strength", "Tint"], "colours colors theme light dark accent pastel muted bold background tint"),
        ("style", "Background", "Layers", ["Texture"], "background pattern plain dots cutting mat pinstripe wash"),
        ("text", "Text & fonts", "TextAa", ["Fonts", "TextSize", "LineSpacing"], "text fonts font size line spacing reading dyslexic easy"),
        ("layout", "Layout", "Layout", ["View", "Thumb", "Preview", "Sidebar", "Density", "ShowName", "ShowType", "ShowSize", "ShowDate", "Order", "BtnLabels", "ShowToolbar"], "layout grid list thumbnails thumbnail size preview panel window folder list spacing compact show name type date order icons words toolbar"),
        ("comfort", "Comfort & focus", "Heart", ["HighContrast", "ReduceMotion", "FadeMessages", "Tips", "ShowProgress", "Remember"], "comfort focus contrast motion messages fade hover explain progress remember left off"),
        ("files", "Files & safety", "Shield", ["AskRecycle", "ShowBadges", "OpenWith", "HiddenExts"], "files safety recycle remove ask sure duplicates badges open programs program hide hidden kinds types"),
        ("features", "Buttons & features", "Toggles", ["Features"], "buttons features hide hidden show off turn"),
    ];

    private static object? Get(Settings s, string key) => typeof(Settings).GetProperty(key)!.GetValue(s);

    private string SummaryFor(string id)
    {
        string Name<T>(IEnumerable<T> list, Func<T, string> idOf, Func<T, string> label, string v) => list.Where(x => idOf(x) == v).Select(label).FirstOrDefault() ?? v;
        switch (id)
        {
            case "colours":
                return (S.Theme == "dark" ? "Dark" : "Light") + " · " + Name(Palette.Accents, a => a.Id, a => a.Label, S.Accent)
                    + (S.Strength == "muted" ? " (muted)" : S.Strength == "pastel" ? " (pastel)" : "") + " · " + Name(Palette.Tints, t => t.Id, t => t.Label, S.Tint);
            case "style":
                return Name(Palette.Textures, t => t.Id, t => t.Label, S.Texture) + " background";
            case "text":
            {
                var ids = Settings.FontSections.Select(x => S.Fonts.GetValueOrDefault(x.Key, "figtree")).Distinct().ToList();
                return (ids.Count == 1 ? FontLabel(ids[0]).Replace(" (app default)", "") : "Mixed fonts") + " · "
                    + (S.TextSize switch { "s" => "Small", "l" => "Large", _ => "Medium" }) + " text" + (S.LineSpacing != "normal" ? " · extra spacing" : "");
            }
            case "layout":
                return (S.View == "grid" ? "Grid" : "List") + " · " + (S.Thumb switch { "s" => "small", "l" => "large", "xl" => "extra large", _ => "medium" }) + " thumbnails · "
                    + (S.Preview switch { "bottom" => "preview at the bottom", "popout" => "preview in its own window", "hidden" => "preview hidden", _ => "preview on the right" });
            case "comfort":
            {
                var n = new[] { S.HighContrast, S.ReduceMotion, S.FadeMessages, S.Tips, S.ShowProgress, S.Remember }.Count(b => b);
                return n > 0 ? n + " switched on" : "All switched off";
            }
            case "files":
            {
                var n = S.OpenWith.Count;
                return (S.AskRecycle ? "Asks before removing" : "Removes without asking") + " · " + (n > 0 ? n + (n == 1 ? " program changed" : " programs changed") : "Windows default programs")
                    + (S.HiddenExts.Count > 0 ? " · " + S.HiddenExts.Count + (S.HiddenExts.Count == 1 ? " kind of file hidden" : " kinds of file hidden") : "");
            }
            default:
            {
                var hidden = S.Features.Values.Count(v => v == "hide");
                var off = S.Features.Values.Count(v => v == "off");
                return hidden + off == 0 ? "Everything shown" : string.Join(" · ", new[] { hidden > 0 ? hidden + " hidden" : "", off > 0 ? off + " off" : "" }.Where(x => x.Length > 0));
            }
        }
    }

    private static bool SameValue(object? a, object? b) => JsonSerializer.Serialize(a) == JsonSerializer.Serialize(b);

    private Section MakeSection((string Id, string Title, string Icon, string[] Keys, string Words) d)
    {
        var q = Query.Trim().ToLowerInvariant();
        var summary = SummaryFor(d.Id);
        var visible = q.Length == 0 || d.Words.Contains(q) || summary.ToLowerInvariant().Contains(q) || d.Title.ToLowerInvariant().Contains(q);
        var open = q.Length > 0 ? visible : _open.Contains(d.Id);
        return new Section
        {
            Id = d.Id, Title = d.Title, Summary = summary, Glyph = (System.Windows.Media.Geometry)System.Windows.Application.Current.Resources["Icon." + d.Icon],
            Changed = d.Keys.Any(k => !SameValue(Get(S, k), Get(Defaults, k))), Visible = visible, Open = open,
            Toggle = Cmd.Of(() => { if (!_open.Remove(d.Id)) _open.Add(d.Id); Query = ""; Refresh(); }),
            Reset = SetCmd(s =>
            {
                var fresh = new Settings();
                foreach (var k in d.Keys) typeof(Settings).GetProperty(k)!.SetValue(s, Get(fresh, k));
            }),
        };
    }

    public Section Colours => MakeSection(SectionDefs[0]);
    public Section Style => MakeSection(SectionDefs[1]);
    public Section Text => MakeSection(SectionDefs[2]);
    public Section Layout => MakeSection(SectionDefs[3]);
    public Section Comfort => MakeSection(SectionDefs[4]);
    public Section Files => MakeSection(SectionDefs[5]);
    public Section Features => MakeSection(SectionDefs[6]);
    public bool NoResults => SectionDefs.All(d => !MakeSection(d).Visible);

    public ICommand ExpandAll => Cmd.Of(() => { foreach (var d in SectionDefs) _open.Add(d.Id); Query = ""; Refresh(); });
    public ICommand CollapseAll => Cmd.Of(() => { _open.Clear(); Query = ""; Refresh(); });
    public ICommand ResetAll => SetCmd(s =>
    {
        var fresh = new Settings();
        foreach (var p in typeof(Settings).GetProperties().Where(p => p.CanWrite)) p.SetValue(s, p.GetValue(fresh));
    });

    // ---------------- Colours ----------------
    public List<OptionVm> Theme => Seg(S.Theme, ("light", "Light", s => s.Theme = "light"), ("dark", "Dark", s => s.Theme = "dark"));
    public List<OptionVm> Strength => Seg(S.Strength, ("pastel", "Pastel", s => s.Strength = "pastel"), ("muted", "Muted", s => s.Strength = "muted"), ("bold", "Bold", s => s.Strength = "bold"));
    public List<SwatchVm> Accents => Palette.Accents.Select(a => new SwatchVm { Label = a.Label, Hex = Palette.SwatchFor(a, S.Strength), On = S.Accent == a.Id, Pick = SetCmd(s => s.Accent = a.Id) }).ToList();
    public List<SwatchVm> Tints => Palette.Tints.Select(t => new SwatchVm { Label = t.Label, Hex = S.Theme == "dark" ? t.Dark : t.Light, On = S.Tint == t.Id, Pick = SetCmd(s => s.Tint = t.Id) }).ToList();

    // ---------------- Background ----------------
    public List<OptionVm> Textures => Palette.Textures.Select(t => new OptionVm { Label = t.Label, On = S.Texture == t.Id, Pick = SetCmd(s => s.Texture = t.Id) }).ToList();

    // ---------------- Text & fonts ----------------
    public List<OptionVm> TextSize => Seg(S.TextSize, ("s", "Small", s => s.TextSize = "s"), ("m", "Medium", s => s.TextSize = "m"), ("l", "Large", s => s.TextSize = "l"));
    public List<OptionVm> LineSpacing => Seg(S.LineSpacing, ("normal", "Standard", s => s.LineSpacing = "normal"), ("relaxed", "Relaxed", s => s.LineSpacing = "relaxed"), ("loose", "Loose", s => s.LineSpacing = "loose"));

    public sealed record FontChoice(string Id, string Label, string Group);

    public List<FontChoice> AvailableFonts()
    {
        var list = ThemeService.BuiltInFonts.Select(f => new FontChoice(f.Id, f.Label, f.Group)).ToList();
        var installed = SystemFonts.Select(n => new FontChoice("sys-" + n, n, "installed")).ToList();
        foreach (var it in main.InstalledLibraryFonts()) installed.Add(new FontChoice("lib-" + it.Path, it.Name, "installed"));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        list.AddRange(installed.Where(f => seen.Add(f.Label)).OrderBy(f => f.Label, StringComparer.CurrentCultureIgnoreCase));
        return list;
    }

    public string FontLabel(string id) => AvailableFonts().FirstOrDefault(f => f.Id == id)?.Label ?? "Figtree (app default)";

    public List<FontRowVm> FontRows
    {
        get
        {
            var ids = Settings.FontSections.Select(x => S.Fonts.GetValueOrDefault(x.Key, "figtree")).ToList();
            var same = ids.Distinct().Count() == 1;
            var rows = new List<FontRowVm>
            {
                new() { Label = "Every section", Bold = true, FontLabel = same ? FontLabel(ids[0]) : "Mixed", Family = ThemeService.FontFor(same ? ids[0] : "figtree"), Open = Cmd.Of(() => OpenPicker("all")) },
            };
            foreach (var (key, label) in Settings.FontSections)
            {
                var id = S.Fonts.GetValueOrDefault(key, "figtree");
                rows.Add(new FontRowVm { Label = label, FontLabel = FontLabel(id), Family = ThemeService.FontFor(id), Open = Cmd.Of(() => OpenPicker(key)) });
            }
            return rows;
        }
    }

    // The font picker pop-up
    [ObservableProperty] private bool _pickerOpen;
    [ObservableProperty] private string _pickerQuery = "";
    private string _pickerFor = "all";

    partial void OnPickerQueryChanged(string value) { OnPropertyChanged(nameof(PickerRows)); OnPropertyChanged(nameof(PickerEmpty)); }

    private void OpenPicker(string section)
    {
        _pickerFor = section;
        PickerQuery = "";
        PickerOpen = true;
        OnPropertyChanged(nameof(PickerTitle));
        OnPropertyChanged(nameof(PickerRows));
    }

    public ICommand ClosePicker => Cmd.Of(() => PickerOpen = false);
    public string PickerTitle => _pickerFor == "all" ? "Font for every section" : "Font for " + Settings.FontSections.First(x => x.Key == _pickerFor).Label.ToLowerInvariant();

    public List<FontPickRowVm> PickerRows
    {
        get
        {
            if (!PickerOpen) return [];
            var q = PickerQuery.Trim();
            var cur = _pickerFor == "all" ? null : S.Fonts.GetValueOrDefault(_pickerFor);
            var fonts = AvailableFonts().Where(f => q.Length == 0 || f.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
            var rows = new List<FontPickRowVm>();
            foreach (var (g, title) in new[] { ("default", "App default"), ("easy", "Easy-reading fonts"), ("installed", "Installed on this computer") })
            {
                var inG = fonts.Where(f => f.Group == g).ToList();
                if (inG.Count == 0) continue;
                rows.Add(new FontPickRowVm { IsHeader = true, Label = title });
                rows.AddRange(inG.Select(f => new FontPickRowVm
                {
                    Label = f.Label, Family = ThemeService.FontFor(f.Id), IsCurrent = f.Id == cur,
                    Pick = Cmd.Of(() =>
                    {
                        PickerOpen = false;
                        Set(s => { if (_pickerFor == "all") foreach (var (k, _) in Settings.FontSections) s.Fonts[k] = f.Id; else s.Fonts[_pickerFor] = f.Id; });
                    }),
                }));
            }
            return rows;
        }
    }

    public bool PickerEmpty => PickerOpen && PickerRows.Count == 0;

    // ---------------- Layout ----------------
    public List<OptionVm> View => Seg(S.View, ("grid", "Grid of thumbnails", s => s.View = "grid"), ("list", "List with details", s => s.View = "list"));
    public List<OptionVm> Thumb => Seg(S.Thumb, ("s", "Small", s => s.Thumb = "s"), ("m", "Medium", s => s.Thumb = "m"), ("l", "Large", s => s.Thumb = "l"), ("xl", "Extra large", s => s.Thumb = "xl"));
    public List<OptionVm> Preview => Seg(S.Preview, ("right", "Right side", s => s.Preview = "right"), ("bottom", "Bottom", s => s.Preview = "bottom"), ("popout", "Own window", s => s.Preview = "popout"), ("hidden", "Hidden", s => s.Preview = "hidden"));
    public List<OptionVm> Sidebar => Seg(S.Sidebar, ("left", "Left", s => s.Sidebar = "left"), ("right", "Right", s => s.Sidebar = "right"));
    public List<OptionVm> Density => Seg(S.Density, ("comfortable", "Standard", s => s.Density = "comfortable"), ("compact", "Compact", s => s.Density = "compact"), ("spacious", "Spacious", s => s.Density = "spacious"));
    public List<OptionVm> Order => Seg(S.Order, ("name", "Name", s => s.Order = "name"), ("added", "Newest first", s => s.Order = "added"), ("type", "File type", s => s.Order = "type"), ("size", "Largest first", s => s.Order = "size"));
    public List<OptionVm> BtnLabels => Seg(S.BtnLabels, ("both", "Icons and words", s => s.BtnLabels = "both"), ("words", "Words only", s => s.BtnLabels = "words"), ("icons", "Icons only", s => s.BtnLabels = "icons"));
    public List<CheckVm> ToolbarCheck =>
    [
        new() { Label = "Show the toolbar", Help = "The row of buttons under the top banner: Back, Undo, Duplicates, Settings and more. You can also hide or show it with the button at the top.", On = S.ShowToolbar, Toggle = SetCmd(s => s.ShowToolbar = !s.ShowToolbar) },
    ];

    public List<CheckVm> ShowWith =>
    [
        new() { Label = "Name", On = S.ShowName, Toggle = SetCmd(s => s.ShowName = !s.ShowName) },
        new() { Label = "File type", On = S.ShowType, Toggle = SetCmd(s => s.ShowType = !s.ShowType) },
        new() { Label = "Size", On = S.ShowSize, Toggle = SetCmd(s => s.ShowSize = !s.ShowSize) },
        new() { Label = "Date added", On = S.ShowDate, Toggle = SetCmd(s => s.ShowDate = !s.ShowDate) },
    ];

    // ---------------- Hide kinds of files ----------------

    /// <summary>
    /// One tick-box per kind of file found in her folders (ticked = hidden), with how many there are.
    /// Kinds that aren't craft files come first, as those are the ones people usually want out of the way.
    /// </summary>
    public List<CheckVm> HideRows
    {
        get
        {
            var counts = main.CountByExt();
            foreach (var e in S.HiddenExts) counts.TryAdd(e, 0);
            return counts.Keys.OrderBy(e => FileKinds.IsCraft(e)).ThenBy(e => e, StringComparer.OrdinalIgnoreCase).Select(e => new CheckVm
            {
                Label = "." + e + " · " + FileKinds.TypeLabel(e),
                Help = counts[e] == 1 ? "1 file" : counts[e] + " files",
                On = S.IsHiddenExt(e),
                Toggle = SetCmd(s => { if (!s.HiddenExts.Remove(e)) s.HiddenExts.Add(e); }),
            }).ToList();
        }
    }

    public bool HasHideRows => main.CountByExt().Count > 0 || S.HiddenExts.Count > 0;

    /// <summary>The quiet note about hidden files (shown here rather than next to the files, to keep that area calm).</summary>
    public string HiddenNote
    {
        get
        {
            var n = main.HiddenFileCount;
            return S.HiddenExts.Count == 0 ? "" : n == 0 ? "No files are hidden right now." : n == 1 ? "1 file is hidden right now." : n + " files are hidden right now.";
        }
    }

    // ---------------- Comfort & focus ----------------
    public List<CheckVm> ComfortChecks =>
    [
        new() { Label = "High contrast", Help = "Stronger text, borders and lines.", On = S.HighContrast, Toggle = SetCmd(s => s.HighContrast = !s.HighContrast) },
        new() { Label = "Reduce motion", Help = "Turns off animations and anything that moves.", On = S.ReduceMotion, Toggle = SetCmd(s => s.ReduceMotion = !s.ReduceMotion) },
        new() { Label = "Messages fade away", Help = "Messages at the bottom disappear after a few seconds, instead of staying until you close them.", On = S.FadeMessages, Toggle = SetCmd(s => s.FadeMessages = !s.FadeMessages) },
        new() { Label = "Explain buttons on hover", Help = "Shows a short description when you rest the mouse on a main button.", On = S.Tips, Toggle = SetCmd(s => s.Tips = !s.Tips) },
        new() { Label = "Show today's progress", Help = "A count of files sorted today, at the top of the app.", On = S.ShowProgress, Toggle = SetCmd(s => s.ShowProgress = !s.ShowProgress) },
        new() { Label = "Open where I left off", Help = "Reopens in the same folder with the same file selected.", On = S.Remember, Toggle = SetCmd(s => s.Remember = !s.Remember) },
    ];

    // ---------------- Files & safety ----------------
    public List<CheckVm> SafetyChecks =>
    [
        new() { Label = "Ask “Are you sure?” before removing files", On = S.AskRecycle, Toggle = SetCmd(s => s.AskRecycle = !s.AskRecycle) },
        new() { Label = "Show duplicate badges on files", On = S.ShowBadges, Toggle = SetCmd(s => s.ShowBadges = !s.ShowBadges) },
    ];

    /// <summary>File endings found in her folders, plus any she has already chosen a program for.</summary>
    private SortedSet<string> KnownExts()
    {
        var exts = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in main.AllExtensions()) exts.Add(e.ToLowerInvariant());
        foreach (var e in S.OpenWith.Keys) exts.Add(e.ToLowerInvariant());
        return exts;
    }

    /// <summary>Craft files (pictures, PDFs, cut files, Word, zips, fonts) found in her folders. The common ones are
    /// listed until the first look at her folders has found something.</summary>
    public List<OpenWithRowVm> CraftOpenWithRows
    {
        get
        {
            var craft = KnownExts().Where(FileKinds.IsCraft).ToList();
            return BuildOpenWithRows(craft.Count > 0 ? craft : FileKinds.CommonExts.Order(), "Windows default");
        }
    }

    /// <summary>Every other kind of file found in her folders, kept out of the way under a button.</summary>
    public List<OpenWithRowVm> OtherOpenWithRows => BuildOpenWithRows(KnownExts().Where(e => !FileKinds.IsCraft(e)), "Windows default");
    public bool HasOtherTypes => KnownExts().Any(e => !FileKinds.IsCraft(e));

    /// <summary>The "other kinds of files" list is open. Starts closed each time Settings opens.</summary>
    public bool ShowOtherTypes { get; set; }
    public string OtherTypesButton => ShowOtherTypes ? "Hide other kinds of files" : "Change how other kinds of files open";
    public ICommand ToggleOtherTypes => Cmd.Of(() => { ShowOtherTypes = !ShowOtherTypes; Refresh(); });

    /// <summary>One row per file ending, each with "Windows default", her saved programs and "Choose a program…".</summary>
    public List<OpenWithRowVm> BuildOpenWithRows(IEnumerable<string> exts, string defaultWord) => exts.Select(e =>
    {
        var def = main.Platform.DefaultProgramName(e);
        var choices = new List<ChoiceVm> { new() { Value = "", Label = defaultWord + (def.Length > 0 ? " (" + def + ")" : " (none set)") } };
        choices.AddRange(main.Data.Programs.Select(p => new ChoiceVm { Value = p.Exe, Label = p.Name }));
        choices.Add(new ChoiceVm { Value = "__choose", Label = "Choose a program…" });
        var cur = S.OpenWith.GetValueOrDefault(e, "");
        return new OpenWithRowVm { Ext = e, Label = "." + e + " · " + FileKinds.TypeLabel(e), Choices = choices, Selected = choices.FirstOrDefault(c => string.Equals(c.Value, cur, StringComparison.OrdinalIgnoreCase)) ?? choices[0] };
    }).ToList();

    /// <summary>Called when she picks a program for a file type in Settings.</summary>
    public void ChooseProgram(string ext, string value)
    {
        if (value == "__choose")
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choose a program", Filter = "Programs|*.exe",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            };
            if (dlg.ShowDialog() != true) { Refresh(); return; }
            var exe = dlg.FileName;
            main.Data.Programs.RemoveAll(p => string.Equals(p.Exe, exe, StringComparison.OrdinalIgnoreCase));
            main.Data.Programs.Add(new SavedProgram { Exe = exe, Name = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe).FileDescription is { Length: > 0 } d ? d : Path.GetFileNameWithoutExtension(exe) });
            value = exe;
        }
        Set(s => { if (value.Length > 0) s.OpenWith[ext] = value; else s.OpenWith.Remove(ext); });
    }

    // ---------------- Buttons & features ----------------
    public List<FeatureRowVm> FeatureRows => Settings.FeatureList.Select(f => new FeatureRowVm
    {
        Label = f.Label,
        Options = new[] { ("show", "Show"), ("hide", "Hide"), ("off", "Off") }
            .Select(o => new OptionVm { Label = o.Item2, On = S.Feature(f.Key) == o.Item1, Pick = Cmd.Of(() => main.SetFeature(f.Key, o.Item1)) }).ToList(),
    }).ToList();
}
