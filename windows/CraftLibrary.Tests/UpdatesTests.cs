using System.Security.Cryptography;
using System.Text;
using CraftLibrary.Core;

namespace CraftLibrary.Tests;

public sealed class UpdatesTests
{
    private const string Sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static string Release(string tag = "v0.7.0", string? name = null, string? url = null, string? digest = "sha256:" + Sha,
        bool draft = false, bool pre = false, long size = 1234) =>
        $$"""
        {
          "tag_name": "{{tag}}", "draft": {{(draft ? "true" : "false")}}, "prerelease": {{(pre ? "true" : "false")}},
          "html_url": "https://github.com/{{Updates.Repo}}/releases/tag/{{tag}}",
          "assets": [
            { "name": "notes.txt", "size": 5, "browser_download_url": "https://github.com/{{Updates.Repo}}/releases/download/{{tag}}/notes.txt" },
            { "name": "{{name ?? "Craft-Library-Setup-0.7.0.exe"}}", "size": {{size}},
              "browser_download_url": "{{url ?? $"https://github.com/{Updates.Repo}/releases/download/{tag}/Craft-Library-Setup-0.7.0.exe"}}"
              {{(digest == null ? "" : $", \"digest\": \"{digest}\"")}} }
          ]
        }
        """;

    [Fact]
    public void Finds_a_newer_version_with_its_installer()
    {
        var u = Updates.FindNewer(Release(), new Version(0, 6, 2, 0));
        Assert.NotNull(u);
        Assert.Equal(new Version(0, 7, 0), u.Version);
        Assert.Equal("Craft-Library-Setup-0.7.0.exe", u.FileName);
        Assert.Equal(1234, u.Size);
        Assert.Equal(Sha, u.Sha256);
        Assert.EndsWith("/releases/tag/v0.7.0", u.NotesUrl);
    }

    [Theory]
    [InlineData("0.7.0")]   // the same version
    [InlineData("0.7.0.0")]
    [InlineData("0.8.1")]   // newer than the release
    public void Nothing_when_already_up_to_date(string current) =>
        Assert.Null(Updates.FindNewer(Release(), Version.Parse(current)));

    [Fact]
    public void Ignores_drafts_and_test_versions()
    {
        Assert.Null(Updates.FindNewer(Release(draft: true), new Version(0, 6, 2)));
        Assert.Null(Updates.FindNewer(Release(pre: true), new Version(0, 6, 2)));
    }

    [Fact]
    public void Needs_the_installer_with_a_checksum_from_this_repository()
    {
        var old = new Version(0, 6, 2);
        Assert.Null(Updates.FindNewer(Release(name: "Something-else.exe"), old));
        Assert.Null(Updates.FindNewer(Release(digest: null), old));
        Assert.Null(Updates.FindNewer(Release(digest: "sha1:abc"), old));
        Assert.Null(Updates.FindNewer(Release(size: 0), old));
        Assert.Null(Updates.FindNewer(Release(url: "https://example.com/Craft-Library-Setup-0.7.0.exe"), old));
        Assert.Null(Updates.FindNewer(Release(url: "https://github.com/someone/else/releases/download/v0.7.0/Craft-Library-Setup-0.7.0.exe"), old));
    }

    [Fact]
    public void Odd_answers_are_ignored()
    {
        Assert.Null(Updates.FindNewer("not json", new Version(0, 6, 2)));
        Assert.Null(Updates.FindNewer("{}", new Version(0, 6, 2)));
        Assert.Null(Updates.FindNewer(Release(tag: "latest"), new Version(0, 6, 2)));
    }

    [Fact]
    public void Checks_the_downloaded_file()
    {
        var path = Path.GetTempFileName();
        try
        {
            var bytes = Encoding.UTF8.GetBytes("pretend installer");
            File.WriteAllBytes(path, bytes);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var good = new UpdateInfo(new Version(0, 7, 0), "", "", "x.exe", bytes.Length, sha);
            Assert.True(Updates.FileMatches(path, good));
            Assert.False(Updates.FileMatches(path, good with { Size = bytes.Length + 1 }));
            Assert.False(Updates.FileMatches(path, good with { Sha256 = Sha }));
            Assert.False(Updates.FileMatches(path + ".missing", good));
        }
        finally { File.Delete(path); }
    }
}
