using CraftLibrary.Core;

namespace CraftLibrary.Tests;

// Tests that change PathRules.IgnoreCase must not run alongside others.
[Collection("paths")]
public sealed class SafeFilesTests : IDisposable
{
    private readonly TestFolder t = new();
    private readonly SafeFiles safe = new();
    private readonly string papers, cuts, outside;

    public SafeFilesTests()
    {
        papers = t.Dir("craft", "papers");
        cuts = t.Dir("craft", "cuts");
        outside = t.Dir("not-chosen");
        safe.SetRoots([Path.Combine(t.Root, "craft")]);
        t.File("craft/papers/flower.png", "aa");
        t.File("craft/papers/leaf.png", "bb");
    }

    public void Dispose() { PathRules.IgnoreCase = OperatingSystem.IsWindows(); t.Dispose(); }

    [Fact]
    public void Only_chosen_folders_are_allowed()
    {
        Assert.True(safe.IsAllowed(Path.Combine(papers, "flower.png")));
        Assert.False(safe.IsAllowed(Path.Combine(outside, "x.png")));
        Assert.False(safe.IsAllowed(Path.Combine(papers, "..", "..", "not-chosen", "x.png")));
        Assert.False(safe.IsAllowed(t.Root + "craft-lookalike"));
        Assert.False(safe.IsAllowed(""));
    }

    [Fact]
    public void Move_puts_the_file_in_the_folder()
    {
        var dest = safe.Move(Path.Combine(papers, "flower.png"), cuts);
        Assert.Equal(Path.Combine(cuts, "flower.png"), dest);
        Assert.Equal(["leaf.png"], t.Names("craft", "papers"));
    }

    [Fact]
    public void Move_never_overwrites()
    {
        t.File("craft/cuts/flower.png", "existing");
        var dest = safe.Move(Path.Combine(papers, "flower.png"), cuts);
        Assert.Equal(Path.Combine(cuts, "flower (2).png"), dest);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(cuts, "flower.png")));
        Assert.Equal("aa", File.ReadAllText(dest));
    }

    [Fact]
    public void Move_with_new_name()
    {
        var dest = safe.Move(Path.Combine(papers, "flower.png"), cuts, "Rose");
        Assert.Equal(Path.Combine(cuts, "Rose.png"), dest);
    }

    [Theory]
    [InlineData("../../not-chosen/escaped")]
    [InlineData("..\\..\\escaped")]
    [InlineData("a:b")]
    [InlineData("ends with dot.")]
    [InlineData("   ")]
    [InlineData("CON")]
    public void Move_refuses_bad_names(string bad)
    {
        Assert.Throws<FriendlyException>(() => safe.Move(Path.Combine(papers, "flower.png"), cuts, bad));
        Assert.True(File.Exists(Path.Combine(papers, "flower.png")));
        Assert.Empty(Directory.GetFiles(outside));
    }

    [Fact]
    public void Move_outside_chosen_folders_is_refused()
    {
        Assert.Throws<FriendlyException>(() => safe.Move(Path.Combine(papers, "flower.png"), outside));
        Assert.Throws<FriendlyException>(() => safe.Move(t.File("not-chosen/a.png"), cuts));
        Assert.True(File.Exists(Path.Combine(papers, "flower.png")));
    }

    [Fact]
    public void Move_to_same_folder_does_nothing()
    {
        var src = Path.Combine(papers, "flower.png");
        Assert.Equal(src, safe.Move(src, papers));
    }

    [Fact]
    public void Move_of_a_missing_file_gives_a_friendly_message()
    {
        var ex = Assert.Throws<FriendlyException>(() => safe.Move(Path.Combine(papers, "gone.png"), cuts));
        Assert.Contains("isn’t there any more", ex.Message);
    }

    [Fact]
    public void Rename_keeps_the_extension()
    {
        var dest = safe.Rename(Path.Combine(papers, "flower.png"), "  Pink Blossom  ");
        Assert.Equal(Path.Combine(papers, "Pink Blossom.png"), dest);
    }

    [Fact]
    public void Rename_onto_an_existing_name_is_refused()
    {
        var ex = Assert.Throws<FriendlyException>(() => safe.Rename(Path.Combine(papers, "flower.png"), "leaf"));
        Assert.Contains("already a file called “leaf.png”", ex.Message);
        Assert.Equal(["flower.png", "leaf.png"], t.Names("craft", "papers"));
    }

    [Fact]
    public void Rename_capitals_only_sticks_like_on_Windows()
    {
        PathRules.IgnoreCase = true;
        var dest = safe.Rename(Path.Combine(papers, "flower.png"), "Flower");
        Assert.Equal(Path.Combine(papers, "Flower.png"), dest);
        Assert.Equal(["Flower.png", "leaf.png"], t.Names("craft", "papers"));
    }

    [Fact]
    public void Rename_to_same_name_does_nothing()
    {
        var src = Path.Combine(papers, "flower.png");
        Assert.Equal(src, safe.Rename(src, "flower"));
    }

    [Fact]
    public void Unique_path_counts_up()
    {
        t.File("craft/cuts/a.svg"); t.File("craft/cuts/a (2).svg");
        Assert.Equal(Path.Combine(cuts, "a (3).svg"), SafeFiles.UniquePath(cuts, "a", "svg"));
    }

    [Fact]
    public void Names_without_extension_are_handled()
    {
        Assert.Equal(("README", ""), PathRules.SplitName("README"));
        Assert.Equal((".hidden", ""), PathRules.SplitName(".hidden"));
        Assert.Equal(("a.b", "c"), PathRules.SplitName("/x/a.b.c"));
    }
}

[CollectionDefinition("paths", DisableParallelization = true)]
public sealed class PathsCollection;
