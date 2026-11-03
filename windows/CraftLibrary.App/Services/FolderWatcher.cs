using CraftLibrary.Core;

namespace CraftLibrary.App.Services;

/// <summary>
/// Notices when files change in her folders, waits until things settle, then asks for one rescan.
/// Portable and network drives are not watched, so Windows can still safely remove them; the app checks
/// those again whenever its window comes back to the front.
/// </summary>
public sealed class FolderWatcher(IPlatform platform) : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _timer;
    public event Action? Changed;

    public void Watch(IEnumerable<string> roots)
    {
        Stop();
        foreach (var r in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(r) || platform.DriveKindOf(r) is DriveKind.Removable or DriveKind.Network) continue;
            try
            {
                var w = new FileSystemWatcher(r)
                {
                    IncludeSubdirectories = true,
                    InternalBufferSize = 64 * 1024,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size | NotifyFilters.LastWrite,
                };
                w.Created += OnEvent; w.Deleted += OnEvent; w.Changed += OnEvent; w.Renamed += OnEvent;
                w.Error += (_, _) => Ping(); // too many changes at once: just look again
                w.EnableRaisingEvents = true;
                _watchers.Add(w);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException) { /* drive unplugged */ }
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        // Browsers write half-finished downloads as temporary files; those changes are ignored.
        if (Scanner.IsTemporaryDownload(e.Name ?? "")) return;
        Ping();
    }

    private void Ping()
    {
        _timer?.Dispose();
        _timer = new Timer(_ => Changed?.Invoke(), null, 2500, Timeout.Infinite);
    }

    public void Stop()
    {
        foreach (var w in _watchers) { try { w.Dispose(); } catch { } }
        _watchers.Clear();
    }

    public void Dispose() { Stop(); _timer?.Dispose(); }
}
