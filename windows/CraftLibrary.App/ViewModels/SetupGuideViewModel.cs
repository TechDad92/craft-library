using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CraftLibrary.App.Themes;
using CraftLibrary.Core;

namespace CraftLibrary.App.ViewModels;

/// <summary>
/// The setup guide shown on first install: 8 short screens, in plain words. Every choice is saved as soon as it is
/// made (through the same path as Settings), so the app behind the guide changes straight away and nothing is lost
/// if the app is closed partway through. The choices themselves come from <see cref="SettingsViewModel"/>, so the
/// guide and Settings always offer exactly the same options.
/// See docs/plans/first-run-wizard.md for the agreed design and wording.
/// </summary>
public sealed partial class SetupGuideViewModel(MainViewModel main) : ObservableObject
{
    public const int Steps = 9;

    private Settings S => main.S;
    public SettingsViewModel Options => main.SettingsVm;

    [ObservableProperty] private int _step = 1;

    partial void OnStepChanged(int value)
    {
        main.Data.SetupGuideStep = value;
        main.SaveSoon();
        Refresh();
    }

    public void Refresh() => OnPropertyChanged(string.Empty);

    // ---------------- moving between screens ----------------
    public string StepText => "Step " + Step + " of " + Steps;
    public bool IsFirst => Step == 1;
    public bool IsLast => Step == Steps;
    public bool HasFolder => main.Data.Locations.Count > 0;
    /// <summary>The craft folder screen can't be passed without a folder: the app needs one to work.</summary>
    public bool CanGoNext => Step != 2 || HasFolder;
    public string NextText => Step switch { 1 => "Let’s start", Steps - 1 => "Nearly done", Steps => "Start using Craft Library", _ => "Next" };
    public bool ShowBack => Step > 1;
    public bool ShowSkip => Step != 2 && Step != Steps;
    public string SkipText => Step == 1 ? "Skip for now" : "Skip this step";
    public List<bool> Dots => Enumerable.Range(1, Steps).Select(i => i <= Step).ToList();

    public bool Is1 => Step == 1; public bool Is2 => Step == 2; public bool Is3 => Step == 3; public bool Is4 => Step == 4;
    public bool Is5 => Step == 5; public bool Is6 => Step == 6; public bool Is7 => Step == 7; public bool Is8 => Step == 8;
    public bool Is9 => Step == 9;

    public ICommand Next => Cmd.Of(() =>
    {
        if (!CanGoNext) return;
        if (Step == Steps) Finish();
        else Step++;
    });

    public ICommand Back => Cmd.Of(GoBack);

    public void GoBack() { if (Step > 1) Step--; }

    /// <summary>
    /// "Skip for now" on the welcome screen goes straight to the craft folder (which is needed), then to the end.
    /// "Skip this step" on any other screen moves on, leaving that screen's choices as they are.
    /// </summary>
    public ICommand Skip => Cmd.Of(() =>
    {
        if (Step == 1) { _skipToEnd = true; Step = 2; return; }
        Step++;
    });

    private bool _skipToEnd;

    /// <summary>After choosing the craft folder via "Skip for now", jump to the summary.</summary>
    public void FolderChosen()
    {
        Refresh();
        if (_skipToEnd && HasFolder) { _skipToEnd = false; Step = Steps; }
    }

    public ICommand GoToStep(int n) => Cmd.Of(() => Step = n);

    private void Finish()
    {
        main.Data.SetupGuideDone = true;
        main.Data.SetupGuideStep = 1;
        main.GuideOpen = false;
        main.SaveSoon();
        main.ShowMessage("All set! You can change any of these choices later in Menu › Settings.");
    }

    // ---------------- 2. craft folder and "To sort" ----------------
    public string CraftFolder => main.Data.Locations.FirstOrDefault()?.Path ?? "";
    public string CraftFolderButton => HasFolder ? "Choose a different folder" : "Choose craft folder";
    public string InboxFolder => main.Data.InboxPath;
    public bool HasInbox => InboxFolder.Length > 0;
    public ICommand ChooseCraftFolder => main.ChooseCraftFolder;
    public ICommand ChooseInbox => main.ChangeInbox;

    // ---------------- 3. easy to read ----------------
    /// <summary>A font in the drop-down: its name (shown in the font itself) and, for the easy-reading ones, a short note.</summary>
    public sealed record FontOption(string Id, string Label, string About, FontFamily Family)
    {
        public bool HasAbout => About.Length > 0;
        public override string ToString() => Label;
    }

    private static readonly (string Id, string Label, string About)[] FontInfo =
    [
        ("figtree", "Figtree", "Clean and simple. This is the standard font."),
        ("atkinson", "Atkinson Hyperlegible", "Designed so every letter is easy to tell apart."),
        ("lexend", "Lexend", "Wider letters with more space between them, easy on tired eyes."),
        ("opendyslexic", "OpenDyslexic", "Shaped to help some people with dyslexia."),
    ];

    private List<FontOption>? _fontOptions;
    private int _fontOptionsFrom = -1;

    /// <summary>
    /// Every font: the four easy-reading ones first (with a note each), then every font installed on the computer, A to Z.
    /// Kept between redraws so the drop-down doesn't reset; rebuilt only when the list of fonts changes.
    /// </summary>
    public List<FontOption> FontOptions
    {
        get
        {
            var all = Options.AvailableFonts();
            if (_fontOptions != null && _fontOptionsFrom == all.Count) return _fontOptions;
            var list = FontInfo.Select(f => new FontOption(f.Id, f.Label, f.About, ThemeService.FontFor(f.Id))).ToList();
            list.AddRange(all.Where(f => f.Group == "installed").Select(f => new FontOption(f.Id, f.Label, "", ThemeService.FontFor(f.Id))));
            _fontOptionsFrom = all.Count;
            return _fontOptions = list;
        }
    }

    /// <summary>The chosen font, used for every part of the app (Settings can still set each part separately).</summary>
    public FontOption? SelectedFont
    {
        get
        {
            var id = S.Fonts.GetValueOrDefault("files", "figtree");
            return FontOptions.FirstOrDefault(f => f.Id == id) ?? FontOptions[0];
        }
        set
        {
            if (value == null || Settings.FontSections.All(x => S.Fonts.GetValueOrDefault(x.Key) == value.Id)) return;
            foreach (var (k, _) in Settings.FontSections) S.Fonts[k] = value.Id;
            main.SettingsChanged();
        }
    }

    // ---------------- 6. opening files ----------------
    /// <summary>Only the craft file types actually found in her folders, so there's nothing to choose for files she doesn't have.</summary>
    public List<OpenWithRowVm> OpenWithRows =>
        Options.BuildOpenWithRows(main.AllExtensions().Select(e => e.ToLowerInvariant()).Where(FileKinds.IsCraft).Distinct().Order(), "Windows’ choice");

    public bool HasCraftTypes => OpenWithRows.Count > 0;

    // ---------------- 7. files you'd rather not see ----------------
    public List<CheckVm> HideRows => Options.HideRows;
    public bool HasHideRows => HideRows.Count > 0;

    // ---------------- 8. comfort and safety ----------------
    public List<CheckVm> ComfortChecks =>
    [
        Check("Ask before removing files", "Asks “Are you sure?” before anything goes to the Recycle Bin.", S.AskRecycle, s => s.AskRecycle = !s.AskRecycle),
        Check("Messages disappear by themselves", "Messages at the bottom of the window fade away after a few seconds.", S.FadeMessages, s => s.FadeMessages = !s.FadeMessages),
        Check("Explain buttons when I point at them", "A short note appears when you rest the mouse on a button.", S.Tips, s => s.Tips = !s.Tips),
        Check("Open where I left off", "The app reopens in the same folder, with the same file picked.", S.Remember, s => s.Remember = !s.Remember),
        Check("Stronger contrast", "Darker text and lines, so everything is easier to see.", S.HighContrast, s => s.HighContrast = !s.HighContrast),
    ];

    private CheckVm Check(string label, string help, bool on, Action<Settings> flip) =>
        new() { Label = label, Help = help, On = on, Toggle = Cmd.Of(() => { flip(S); main.SettingsChanged(); }) };

    // ---------------- 9. summary ----------------
    public sealed record SummaryRow(string Label, string Value, ICommand Change);

    public List<SummaryRow> Summary
    {
        get
        {
            var fontId = S.Fonts.GetValueOrDefault("files", "figtree");
            var font = FontOptions.FirstOrDefault(f => f.Id == fontId)?.Label ?? Options.FontLabel(fontId);
            var size = S.TextSize switch { "s" => "Small", "l" => "Large", _ => "Medium" };
            var changedPrograms = S.OpenWith.Count;
            var comfortOn = ComfortChecks.Where(c => c.On).Select(c => c.Label).ToList();
            return
            [
                new("Craft folder", HasFolder ? CraftFolder : "Not chosen yet", GoToStep(2)),
                new("New files arrive in", HasInbox ? InboxFolder : "Not chosen", GoToStep(2)),
                new("Text", size + " text, " + font, GoToStep(3)),
                new("Colours", Options.Colours.Summary, GoToStep(4)),
                new("Files", Options.Layout.Summary, GoToStep(5)),
                new("Opening files", changedPrograms == 0 ? "Windows’ choice for every kind of file" : changedPrograms + (changedPrograms == 1 ? " kind of file uses a program you chose" : " kinds of file use programs you chose"), GoToStep(6)),
                new("Hidden files", S.HiddenExts.Count == 0 ? "Nothing hidden" : string.Join(", ", S.HiddenExts.Order().Select(e => "." + e)), GoToStep(7)),
                new("Comfort", comfortOn.Count == 0 ? "All switched off" : string.Join(", ", comfortOn), GoToStep(8)),
            ];
        }
    }
}
