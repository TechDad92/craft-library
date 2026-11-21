using System.Diagnostics;
using System.Net.Http;
using CraftLibrary.Core;

namespace CraftLibrary.App.Services;

/// <summary>
/// Asks GitHub whether a newer version is out, downloads its installer, checks it, and starts it.
/// Only ever runs when she has said yes (except the quiet look on opening, which she can switch off).
/// </summary>
public sealed class UpdateService(string dataDir)
{
    private static readonly HttpClient Ask = Make(TimeSpan.FromSeconds(20));
    // A download on a slow connection can take a while; she can stop it with the Stop button.
    private static readonly HttpClient Fetch = Make(Timeout.InfiniteTimeSpan);

    private static HttpClient Make(TimeSpan timeout)
    {
        var h = new HttpClient { Timeout = timeout };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("CraftLibrary/" + ViewModels.MainViewModel.AppVersionText);
        return h;
    }

    private string Folder => Path.Combine(dataDir, "updates");

    /// <summary>The newer version, or null when she already has the newest.</summary>
    /// <exception cref="HttpRequestException">No internet, or GitHub didn't answer properly.</exception>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, Updates.LatestReleaseApi);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var res = await Ask.SendAsync(req, ct);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return Updates.FindNewer(json, ViewModels.MainViewModel.AppVersion);
    }

    /// <summary>Downloads the installer into %LOCALAPPDATA%\Craft Library\updates and checks it is exactly the right file.</summary>
    /// <returns>Where the checked installer is, or null if the file didn't match (it is deleted again).</returns>
    public async Task<string?> DownloadAsync(UpdateInfo u, IProgress<double> progress, CancellationToken ct)
    {
        Directory.CreateDirectory(Folder);
        var dest = Path.Combine(Folder, u.FileName);
        var part = dest + ".part";
        try
        {
            using (var res = await Fetch.GetAsync(u.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                res.EnsureSuccessStatusCode();
                await using var from = await res.Content.ReadAsStreamAsync(ct);
                await using var to = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                var buf = new byte[1 << 16];
                long done = 0;
                int n;
                while ((n = await from.ReadAsync(buf, ct)) > 0)
                {
                    await to.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (done > u.Size) return null; // bigger than it should be: not the right file
                    progress.Report((double)done / u.Size);
                }
            }
            var ok = await Task.Run(() => Updates.FileMatches(part, u), ct);
            if (!ok) return null;
            File.Move(part, dest, overwrite: true); // only ever our own installer file in our own folder
            return dest;
        }
        finally
        {
            try { if (File.Exists(part)) File.Delete(part); } catch { }
        }
    }

    /// <summary>Starts the installer in its "update" mode: it waits for the app to close, installs, and opens the app again.</summary>
    public static void RunInstaller(string path) =>
        Process.Start(new ProcessStartInfo(path, "/update") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) })?.Dispose();

    /// <summary>Removes installers left from earlier updates (they're only needed while updating).</summary>
    public void CleanUp()
    {
        try
        {
            if (!Directory.Exists(Folder)) return;
            foreach (var f in Directory.EnumerateFiles(Folder))
                try { File.Delete(f); } catch { /* still in use: next time */ }
        }
        catch { }
    }
}
