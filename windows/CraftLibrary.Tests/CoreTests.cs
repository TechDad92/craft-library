using CraftLibrary.Core;

namespace CraftLibrary.Tests;

[Collection("paths")]
public sealed class ScannerTests : IDisposable
{
    private readonly TestFolder t = new();
    public void Dispose() => t.Dispose();

    [Fact]
    public void Finds_files_and_folders_with_sizes()
    {
        t.File("Papers/Christmas/red.jpg", "12345");
        t.File("Papers/blue.png", "12");
        t.File("top.svg", "1");
        var r = Scanner.Scan(t.Root, recursive: true);
        Assert.True(r.Ok);
        Assert.Equal(["Christmas", "Papers"], r.Folders.Select(f => f.Name).Order().ToArray());
        var red = Assert.Single(r.Files, f => f.Name == "red");
        Assert.Equal("jpg", red.Ext);
        Assert.Equal(5, red.Size);
        Assert.Equal(1, r.Folders.Single(f => f.Name == "Christmas").Depth);
        Assert.Equal(3, r.Files.Count);
    }

    [Fact]
    public void Skips_junk_and_hidden_system_folders()
    {
        t.File("~$Document.docx"); t.File("desktop.ini"); t.File("Thumbs.db"); t.File("._mac.png"); t.File(".DS_Store");
        t.File(".git/config"); t.File("$RECYCLE.BIN/x.png"); t.File("Removed files (Craft Library)/old.png");
        t.File("real.png");
        var r = Scanner.Scan(t.Root, recursive: true);
        Assert.Equal(["real"], r.Files.Select(f => f.Name).ToArray());
        Assert.Empty(r.Folders);
    }

    [Fact]
    public void To_sort_folder_is_one_level_and_leaves_out_programs_and_downloads_in_progress()
    {
        t.File("pattern.png"); t.File("setup.exe"); t.File("half.crdownload"); t.File("inner/deep.png");
        var r = Scanner.Scan(t.Root, recursive: false, filterInbox: true);
        Assert.Equal(["pattern"], r.Files.Select(f => f.Name).ToArray());
        Assert.Empty(r.Folders);
    }

    [Fact]
    public void Missing_folder_is_reported_not_thrown()
    {
        var r = Scanner.Scan(Path.Combine(t.Root, "unplugged"), recursive: true);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Ten_thousand_files_scan_quickly()
    {
        for (var d = 0; d < 20; d++) { var dir = t.Dir("f" + d); for (var i = 0; i < 500; i++) File.WriteAllText(Path.Combine(dir, i + ".png"), "x"); }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = Scanner.Scan(t.Root, recursive: true);
        sw.Stop();
        Assert.Equal(10000, r.Files.Count);
        Assert.True(sw.ElapsedMilliseconds < 3000, "took " + sw.ElapsedMilliseconds + " ms");
    }
}

public sealed class DuplicateTests : IDisposable
{
    private readonly TestFolder t = new();
    public void Dispose() => t.Dispose();

    [Fact]
    public void Exact_copies_are_grouped_and_lookalikes_are_not()
    {
        var big = new string('a', 300_000);
        var bigDifferentMiddle = big[..150_000] + "b" + big[150_001..];
        t.File("a/one.jpg", "same contents"); t.File("b/two.jpg", "same contents"); t.File("c/three.jpg", "diff contents");
        t.File("a/big1.psd", big); t.File("b/big2.psd", big); t.File("c/big3.psd", bigDifferentMiddle);
        var files = Scanner.Scan(t.Root, recursive: true).Files;
        var map = new Duplicates().Find(files, _ => true);
        string K(string n) => map.GetValueOrDefault(files.Single(f => f.Name == n).Path, "");
        Assert.NotEqual("", K("one"));
        Assert.Equal(K("one"), K("two"));
        Assert.Equal("", K("three"));
        Assert.NotEqual("", K("big1"));
        Assert.Equal(K("big1"), K("big2"));
        Assert.Equal("", K("big3"));
    }

    [Fact]
    public void Files_outside_chosen_folders_are_not_read()
    {
        t.File("a/one.jpg", "same"); t.File("b/two.jpg", "same");
        var files = Scanner.Scan(t.Root, recursive: true).Files;
        Assert.Empty(new Duplicates().Find(files, p => !p.Contains("/b/") && !p.Contains("\\b\\")));
    }
}

[Collection("paths")]
public sealed class RemoveAndRestoreTests : IDisposable
{
    private readonly TestFolder t = new();
    private readonly SafeFiles safe = new();
    private readonly FakePlatform platform;
    private readonly Remover remover;

    public RemoveAndRestoreTests()
    {
        platform = new FakePlatform(Path.Combine(t.Root, "bin"));
        safe.SetRoots([t.Dir("craft"), t.Dir("usb")]);
        remover = new Remover(safe, platform);
    }

    public void Dispose() => t.Dispose();

    [Fact]
    public void Remove_then_put_back_from_Recycle_Bin()
    {
        var f = t.File("craft/flower.png", "petals");
        var e = remover.Remove(f);
        Assert.Equal("recycle", e.Method);
        Assert.False(File.Exists(f));
        Assert.Equal(f, remover.Restore(e));
        Assert.Equal("petals", File.ReadAllText(f));
        Assert.Empty(Directory.GetFiles(Path.Combine(t.Root, "bin")));
    }

    [Fact]
    public void Put_back_never_overwrites_a_new_file_with_the_same_name()
    {
        var f = t.File("craft/flower.png", "old");
        var e = remover.Remove(f);
        File.WriteAllText(f, "new");
        var back = remover.Restore(e);
        Assert.Equal(Path.Combine(t.Root, "craft", "flower (2).png"), back);
        Assert.Equal("new", File.ReadAllText(f));
        Assert.Equal("old", File.ReadAllText(back));
    }

    [Fact]
    public void Put_back_picks_the_latest_removal()
    {
        var f = t.File("craft/flower.png", "first");
        remover.Remove(f);
        Thread.Sleep(20);
        t.File("craft/flower.png", "second");
        var e2 = remover.Remove(f);
        remover.Restore(e2);
        Assert.Equal("second", File.ReadAllText(f));
    }

    [Fact]
    public void Portable_drive_files_go_to_the_Removed_files_folder()
    {
        platform.PortableRoots.Add(t.Root);
        var f = t.File("usb/card.svg", "cut");
        var e = remover.Remove(f);
        Assert.Equal("folder", e.Method);
        Assert.False(File.Exists(f));
        Assert.Equal(Path.Combine(Path.GetPathRoot(f)!, SafeFiles.RemovedFolderName), Path.GetDirectoryName(e.RemovedPath));
        Assert.Equal(f, remover.Restore(e));
        Assert.Equal("cut", File.ReadAllText(f));
    }

    [Fact]
    public void Cannot_remove_outside_chosen_folders()
    {
        var f = t.File("elsewhere/x.png");
        Assert.Throws<FriendlyException>(() => remover.Remove(f));
        Assert.True(File.Exists(f));
    }

    [Fact]
    public void Missing_from_bin_gives_a_friendly_message()
    {
        var f = t.File("craft/flower.png");
        var e = remover.Remove(f);
        foreach (var x in Directory.GetFiles(Path.Combine(t.Root, "bin"))) File.Delete(x);
        var ex = Assert.Throws<FriendlyException>(() => remover.Restore(e));
        Assert.Contains("Recycle Bin", ex.Message);
    }

    [Fact]
    public void Recycle_bin_index_reads_version_1_and_2()
    {
        var when = new DateTime(2026, 10, 3, 9, 30, 0, DateTimeKind.Utc);
        var v2 = RecycleBinFiles.Parse(RecycleBinFiles.Build(@"C:\Users\Mum\Downloads\pink.png", 1234, when))!;
        Assert.Equal(@"C:\Users\Mum\Downloads\pink.png", v2.OriginalPath);
        Assert.Equal(1234, v2.Size);
        Assert.Equal(when, v2.DeletedUtc);
        var v1 = new byte[24 + 520];
        BitConverter.GetBytes(1L).CopyTo(v1, 0);
        BitConverter.GetBytes(when.ToFileTimeUtc()).CopyTo(v1, 16);
        System.Text.Encoding.Unicode.GetBytes(@"D:\old.svg").CopyTo(v1, 24);
        Assert.Equal(@"D:\old.svg", RecycleBinFiles.Parse(v1)!.OriginalPath);
        Assert.Null(RecycleBinFiles.Parse([1, 2, 3]));
    }
}

[Collection("paths")]
public sealed class FolderRuleTests
{
    private static FolderRules Rules(string root) => new(Path.Combine(root, "home", "mum"), Path.GetPathRoot(root)!,
        [Path.Combine(root, "Windows"), Path.Combine(root, "Program Files")]);

    [Fact]
    public void Refuses_system_places_and_allows_craft_folders()
    {
        var root = Path.GetTempPath();
        var r = Rules(root);
        Assert.Contains("belongs to Windows", r.Problem(Path.Combine(root, "Windows", "System32")));
        Assert.Contains("belongs to Windows", r.Problem(Path.Combine(root, "Program Files")));
        Assert.Contains("whole user folder", r.Problem(Path.Combine(root, "home", "mum")));
        Assert.Contains("not the whole", r.Problem(Path.GetPathRoot(root)!));
        Assert.Equal("", r.Problem(Path.Combine(root, "home", "mum", "Documents", "Craft Files")));
    }
}

public sealed class DataTests : IDisposable
{
    private readonly TestFolder t = new();
    public void Dispose() => t.Dispose();

    [Fact]
    public void Settings_and_data_survive_a_save_and_load()
    {
        var file = Path.Combine(t.Root, "data.json");
        var d = AppData.Load(file);
        Assert.Equal("teal", d.Settings.Accent);
        Assert.True(d.Settings.AskRecycle);
        Assert.False(d.Settings.FadeMessages);
        d.Settings.Accent = "rose";
        d.Settings.Features["dupes"] = "hide";
        d.Settings.OpenWith["PDF"] = @"C:\Acrobat.exe";
        d.Locations.Add(new Location { Key = "k", Label = "Craft Files", Path = @"C:\Craft" });
        d.FileMeta[@"C:\Craft\a.png"] = new FileMeta { Later = true, Note = "for Ruby's card" };
        d.Save(file);
        d.Save(file); // second save makes a backup
        var back = AppData.Load(file);
        Assert.Equal("rose", back.Settings.Accent);
        Assert.Equal("hide", back.Settings.Feature("dupes"));
        Assert.Equal(@"C:\Acrobat.exe", back.Settings.OpenWith["pdf"]);
        Assert.Equal("for Ruby's card", back.FileMeta[@"c:\craft\A.PNG"].Note);
        Assert.True(File.Exists(file + ".bak"));
    }

    [Fact]
    public void Setup_guide_shows_for_new_users_only()
    {
        var fresh = AppData.Load(Path.Combine(t.Root, "none.json"));
        Assert.False(fresh.SetupGuideDone);
        Assert.Equal(1, fresh.SetupGuideStep);

        // Saved by an older version, with a craft folder already chosen: the guide counts as done.
        var file = Path.Combine(t.Root, "old.json");
        File.WriteAllText(file, """{ "locations": [ { "key": "C:\\Craft", "label": "Craft", "path": "C:\\Craft" } ] }""");
        Assert.True(AppData.Load(file).SetupGuideDone);

        // Closed partway through: remembers the screen.
        var d = AppData.Load(Path.Combine(t.Root, "part.json"));
        d.SetupGuideStep = 4;
        d.Save(Path.Combine(t.Root, "part.json"));
        var back = AppData.Load(Path.Combine(t.Root, "part.json"));
        Assert.False(back.SetupGuideDone);
        Assert.Equal(4, back.SetupGuideStep);
    }

    [Fact]
    public void Hidden_file_types_are_saved_and_tidied()
    {
        var file = Path.Combine(t.Root, "hidden.json");
        File.WriteAllText(file, """{ "settings": { "hiddenExts": [ "XLSX", ".exe", "xlsx", " " ] } }""");
        var d = AppData.Load(file);
        Assert.Equal(["xlsx", "exe"], d.Settings.HiddenExts);
        Assert.True(d.Settings.IsHiddenExt("XLSX"));
        Assert.False(d.Settings.IsHiddenExt("png"));
        Assert.Empty(new Settings().HiddenExts); // nothing hidden to start with
    }

    [Fact]
    public void A_damaged_file_falls_back_to_the_backup()
    {
        var file = Path.Combine(t.Root, "data.json");
        var d = AppData.Load(file);
        d.Settings.Theme = "dark";
        d.Save(file); d.Save(file);
        File.WriteAllText(file, "{ not json");
        Assert.Equal("dark", AppData.Load(file).Settings.Theme);
    }

    [Fact]
    public void Missing_new_settings_get_their_defaults()
    {
        var file = Path.Combine(t.Root, "data.json");
        File.WriteAllText(file, """{ "settings": { "accent": "plum", "features": { "dupes": "off" } } }""");
        var d = AppData.Load(file);
        Assert.Equal("plum", d.Settings.Accent);
        Assert.Equal("off", d.Settings.Feature("dupes"));
        Assert.Equal("show", d.Settings.Feature("history"));
        Assert.Equal("figtree", d.Settings.Fonts["preview"]);
    }

    [Fact]
    public void Old_hidden_preview_and_focus_button_are_brought_up_to_date()
    {
        var file = Path.Combine(t.Root, "data.json");
        File.WriteAllText(file, """{ "settings": { "preview": "hidden", "features": { "focus": "hide" } } }""");
        var d = AppData.Load(file);
        Assert.Equal("right", d.Settings.Preview);
        Assert.False(d.Settings.ShowPreview);
        Assert.True(d.Settings.ShowFolders);
        Assert.False(d.Settings.Features.ContainsKey("focus"));
        Assert.Equal(Settings.DefaultFeatures(), d.Settings.Features); // so "Reset this section" doesn't show for nothing
    }
}

public sealed class PaletteTests
{
    [Fact]
    public void Default_look_matches_the_original_design()
    {
        var c = Palette.Resolve(new Settings());
        Assert.Equal("#9ED6CF", c["accent"]);       // pastel teal
        Assert.Equal("#2F6E67", c["accent-line"]);
        Assert.Equal("#1C2430", c["on-accent"]);
        Assert.Equal("#F2F4F7", c["bg"]);
        Assert.Equal("#D3D9E1", c["chrome"]);
        Assert.Equal("#FFFFFF", c["panel"]);
    }

    [Fact]
    public void Dark_bold_and_high_contrast_combine_correctly()
    {
        var c = Palette.Resolve(new Settings { Theme = "dark", Strength = "bold", Accent = "rose", HighContrast = true, Tint = "sage" });
        Assert.Equal("#B4235A", c["accent"]);
        Assert.Equal("#4A1F31", c["accent-soft"]);   // dark + rose beats plain rose
        Assert.Equal("#B4235A", c["accent-line"]);   // follows var(--accent)
        Assert.Equal("#FFFFFF", c["text"]);          // high contrast
        Assert.Equal("#131A14", c["bg"]);
    }

    [Fact]
    public void Every_combination_resolves_to_real_colours()
    {
        foreach (var theme in new[] { "light", "dark" })
        foreach (var strength in new[] { "pastel", "muted", "bold" })
        foreach (var a in Palette.Accents)
        foreach (var tint in Palette.Tints)
        foreach (var hc in new[] { false, true })
        {
            var c = Palette.Resolve(new Settings { Theme = theme, Strength = strength, Accent = a.Id, Tint = tint.Id, HighContrast = hc });
            foreach (var key in new[] { "bg", "panel", "text", "muted", "border", "accent", "accent-soft", "accent-strong", "accent-line", "on-accent", "chrome", "divider" })
            {
                Assert.True(c.ContainsKey(key), $"{theme}/{strength}/{a.Id}/{tint.Id}/{hc} is missing {key}");
                Palette.Parse(c[key]);
            }
        }
    }

    [Fact]
    public void Darker_and_label_names()
    {
        Assert.Equal("#5F3B48", Palette.Darker("#9E6378"));
        Assert.Equal("Teal (pastel)", Palette.LabelName("#9ed6cf"));
        Assert.Equal("Custom colour #123456", Palette.LabelName("#123456"));
    }
}

public sealed class FileKindTests
{
    [Theory]
    [InlineData("JPG", FileKind.Paper)] [InlineData("docx", FileKind.Doc)] [InlineData("studio3", FileKind.Vector)]
    [InlineData("scut4", FileKind.Vector)] [InlineData("7z", FileKind.Zip)] [InlineData("otf", FileKind.Font)] [InlineData("xyz", FileKind.Unknown)]
    public void Kinds(string ext, FileKind kind) => Assert.Equal(kind, FileKinds.KindFor(ext));

    [Theory]
    [InlineData("JPG", true)] [InlineData("psd", true)] [InlineData("studio3", true)] [InlineData("docx", true)] [InlineData("zip", true)] [InlineData("otf", true)]
    [InlineData("xlsx", false)] [InlineData("exe", false)] [InlineData("mp4", false)] [InlineData("txt", false)] [InlineData("", false)]
    public void Craft_file_types(string ext, bool craft) => Assert.Equal(craft, FileKinds.IsCraft(ext));

    [Fact]
    public void Sizes_read_like_before()
    {
        Assert.Equal("812 bytes", FileKinds.HumanSize(812));
        Assert.Equal("96 KB", FileKinds.HumanSize(96_000));
        Assert.Equal("2.4 MB", FileKinds.HumanSize(2_400_000));
        Assert.Equal("1.5 GB", FileKinds.HumanSize(1_500_000_000));
    }
}
