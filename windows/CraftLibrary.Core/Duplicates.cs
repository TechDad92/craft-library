using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace CraftLibrary.Core;

/// <summary>
/// Finds exact copies: only files of exactly the same size are compared, first by their start and end,
/// then (for files that still match) by their whole contents. Designs that only look alike are never matched.
/// </summary>
public sealed class Duplicates
{
    private const int QuickBytes = 64 * 1024;
    private readonly ConcurrentDictionary<string, string> _quick = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _full = new(StringComparer.Ordinal);

    private static string Key(FileEntry f) => f.Path + "|" + f.Size + "|" + f.ModifiedUtc.Ticks;

    /// <summary>Returns a short group key for every file that has at least one exact copy.</summary>
    public Dictionary<string, string> Find(IEnumerable<FileEntry> files, Func<string, bool> allowed, CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sameSize in files.Where(f => f.Size > 0).GroupBy(f => f.Size).Where(g => g.Count() > 1))
        {
            ct.ThrowIfCancellationRequested();
            var candidates = sameSize.Where(f => allowed(f.Path)).ToList();
            var byQuick = candidates
                .Select(f => (f, h: Hash(f, quick: true)))
                .Where(x => x.h != null).GroupBy(x => x.h!).Where(g => g.Count() > 1);
            foreach (var g in byQuick)
            {
                ct.ThrowIfCancellationRequested();
                // Small files were read completely by the quick check already.
                var groups = sameSize.Key <= 2 * QuickBytes
                    ? [g.Select(x => (x.f, h: (string?)x.h)).ToList()]
                    : g.Select(x => (x.f, h: Hash(x.f, quick: false))).Where(x => x.h != null).GroupBy(x => x.h!).Select(z => z.ToList()).ToList();
                foreach (var grp in groups.Where(z => z.Count > 1))
                    foreach (var (f, h) in grp) result[f.Path] = h![..12];
            }
        }
        return result;
    }

    private string? Hash(FileEntry f, bool quick)
    {
        var cache = quick ? _quick : _full;
        var key = Key(f);
        if (cache.TryGetValue(key, out var known)) return known;
        try
        {
            using var s = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16, FileOptions.SequentialScan);
            string h;
            if (quick)
            {
                var n = (int)Math.Min(f.Size, QuickBytes);
                var a = new byte[n];
                s.ReadExactly(a);
                var b = new byte[n];
                s.Seek(Math.Max(0, f.Size - n), SeekOrigin.Begin);
                s.ReadExactly(b);
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
                sha.AppendData(a); sha.AppendData(b);
                h = Convert.ToHexString(sha.GetHashAndReset());
            }
            else h = Convert.ToHexString(SHA1.HashData(s));
            cache[key] = h;
            return h;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
