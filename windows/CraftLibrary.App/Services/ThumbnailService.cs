using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CraftLibrary.App.Native;
using CraftLibrary.Core;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace CraftLibrary.App.Services;

/// <summary>Something on screen that wants a thumbnail.</summary>
public interface IThumbTarget
{
    string Path { get; }
    long SizeBytes { get; }
    DateTime ModifiedUtc { get; }
    string Ext { get; }
    FileKind Kind { get; }
    /// <summary>Called on the screen's thread when the thumbnail is ready (null = no preview for this file).</summary>
    void ThumbReady(BitmapSource? image, int innerCount);
    /// <summary>The request was dropped (she moved on); ask again if it comes back on screen. Any thread.</summary>
    void ThumbCancelled();
}

public sealed record ThumbResult(BitmapSource? Image, int Inner);

/// <summary>
/// Makes thumbnails in the background, newest request first, so whatever is on screen right now comes first
/// even after a fast scroll. Finished thumbnails are kept on disk, so they appear straight away next time.
/// </summary>
public sealed class ThumbnailService : IDisposable
{
    public const int Side = 384;
    /// <summary>Raise this when the way thumbnails are made changes, so old saved ones are made again.</summary>
    private const int CacheVersion = 2;
    private const int MemoryLimit = 260;
    private const int QueueLimit = 400;

    private readonly string _dir;
    private readonly object _gate = new();
    private readonly LinkedList<IThumbTarget> _queue = new();
    private readonly Dictionary<IThumbTarget, LinkedListNode<IThumbTarget>> _queued = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, (LinkedListNode<string> Node, ThumbResult Result)> _memory = new();
    private readonly LinkedList<string> _memoryOrder = new();
    private readonly Dictionary<string, (char Kind, int Inner)> _index = new();
    private readonly StreamWriter? _indexWriter;
    private readonly List<Thread> _workers = [];
    private bool _stopping;

    public ThumbnailService(string cacheDir, int workers = 3)
    {
        _dir = cacheDir;
        Directory.CreateDirectory(_dir);
        LoadIndex();
        try { _indexWriter = new StreamWriter(new FileStream(System.IO.Path.Combine(_dir, "index.tsv"), FileMode.Append, FileAccess.Write, FileShare.Read)) { AutoFlush = false }; }
        catch (IOException) { /* a second copy of the app is open: thumbnails still work, they just aren't saved */ }
        for (var i = 0; i < workers; i++)
        {
            // Windows' thumbnail makers and the SVG drawer need this kind of thread.
            var t = new Thread(Work) { IsBackground = true, Name = "Thumbnails " + (i + 1), Priority = ThreadPriority.BelowNormal };
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
            _workers.Add(t);
        }
    }

    private static string KeyFor(IThumbTarget t) =>
        Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(CacheVersion + "|" + t.Path.ToLowerInvariant() + "|" + t.SizeBytes + "|" + t.ModifiedUtc.Ticks)))[..24];

    /// <summary>A thumbnail already in memory, if there is one (no waiting).</summary>
    public ThumbResult? Peek(IThumbTarget t)
    {
        var key = KeyFor(t);
        lock (_gate) return Touch(key);
    }

    public void Request(IThumbTarget t)
    {
        lock (_gate)
        {
            if (_queued.Remove(t, out var old)) _queue.Remove(old);
            _queued[t] = _queue.AddFirst(t);
            // Requests for tiles scrolled far away are dropped; they ask again if they come back on screen.
            while (_queue.Count > QueueLimit)
            {
                var last = _queue.Last!;
                _queue.RemoveLast();
                _queued.Remove(last.Value);
                last.Value.ThumbCancelled();
            }
            Monitor.Pulse(_gate);
        }
    }

    /// <summary>Drop everything waiting, e.g. when she opens another folder, so the new one's come first.</summary>
    public void ClearQueue()
    {
        lock (_gate)
        {
            foreach (var t in _queue) t.ThumbCancelled();
            _queue.Clear();
            _queued.Clear();
        }
    }


    private void Work()
    {
        while (true)
        {
            IThumbTarget t;
            lock (_gate)
            {
                while (_queue.Count == 0 && !_stopping) Monitor.Wait(_gate);
                if (_stopping) return;
                t = _queue.First!.Value;
                _queue.RemoveFirst();
                _queued.Remove(t);
            }
            ThumbResult result;
            try { result = Get(t); }
            catch { result = new ThumbResult(null, 0); }
            Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => t.ThumbReady(result.Image, result.Inner));
        }
    }

    private ThumbResult? Touch(string key)
    {
        if (!_memory.TryGetValue(key, out var hit)) return null;
        _memoryOrder.Remove(hit.Node);
        _memoryOrder.AddFirst(hit.Node);
        return hit.Result;
    }

    private void Remember(string key, ThumbResult r)
    {
        lock (_gate)
        {
            if (_memory.ContainsKey(key)) return;
            _memory[key] = (_memoryOrder.AddFirst(key), r);
            while (_memory.Count > MemoryLimit)
            {
                var last = _memoryOrder.Last!.Value;
                _memoryOrder.RemoveLast();
                _memory.Remove(last);
            }
        }
    }

    private ThumbResult Get(IThumbTarget t)
    {
        var key = KeyFor(t);
        lock (_gate) { var m = Touch(key); if (m != null) return m; }

        (char Kind, int Inner) known;
        bool isKnown;
        lock (_gate) isKnown = _index.TryGetValue(key, out known);
        if (isKnown)
        {
            if (known.Kind == 'n') return Keep(key, new ThumbResult(null, known.Inner));
            var cached = LoadFile(System.IO.Path.Combine(_dir, key + (known.Kind == 'p' ? ".png" : ".jpg")));
            if (cached != null) return Keep(key, new ThumbResult(cached, known.Inner));
        }

        var (img, inner) = Make(t, Side);
        Save(key, img, inner, rememberEmpty: img != null || t.Kind != FileKind.Pdf);
        return Keep(key, new ThumbResult(img, inner));
    }

    private ThumbResult Keep(string key, ThumbResult r) { Remember(key, r); return r; }

    /// <summary>Works out a picture for the file, trying the quickest way first.</summary>
    private static (BitmapSource? Image, int Inner) Make(IThumbTarget t, int side)
    {
        var ext = t.Ext.ToLowerInvariant();
        if (t.Kind == FileKind.Font) return (null, 0); // shown in the font itself instead
        if (ext == "docx") { var z = ZipPicture(t.Path, wordOnly: true, side); return (z.Image ?? ShellThumb(t.Path, side), 0); }
        if (ext == "zip") { var z = ZipPicture(t.Path, wordOnly: false, side); return (z.Image ?? ShellThumb(t.Path, side), z.Inner); }
        if (ext == "svg") return (ShellThumb(t.Path, side) ?? SvgPicture(t.Path, side), 0);
        if (ext == "psd") return (ShellThumb(t.Path, side) ?? PsdPicture(t.Path, side), 0);
        if (t.Kind == FileKind.Paper) return (ShellThumb(t.Path, side) ?? DecodeImage(t.Path, side), 0);
        if (t.Kind == FileKind.Pdf || ext == "ai") return (ShellThumb(t.Path, side) ?? PdfPicture(t.Path, side), 0);
        return (ShellThumb(t.Path, side), 0);
    }

    /// <summary>A bigger picture for the preview panel. Returns null when the thumbnail is as good as it gets.</summary>
    public static Task<BitmapSource?> LargeAsync(string path, string ext, FileKind kind, int side)
    {
        var tcs = new TaskCompletionSource<BitmapSource?>();
        var th = new Thread(() =>
        {
            try
            {
                var e = ext.ToLowerInvariant();
                BitmapSource? img = null;
                if (e == "svg") img = SvgPicture(path, side);
                else if (kind == FileKind.Paper && e != "psd") img = DecodeImage(path, side) ?? ShellThumb(path, side);
                else if (kind == FileKind.Pdf) img = PdfPicture(path, side) ?? ShellThumb(path, side);
                else if (e == "docx") img = ZipPicture(path, true, side).Image;
                tcs.SetResult(img);
            }
            catch { tcs.SetResult(null); }
        }) { IsBackground = true, Name = "Preview picture" };
        th.SetApartmentState(ApartmentState.STA);
        th.Start();
        return tcs.Task;
    }

    // ---------- ways of making a picture ----------

    /// <summary>Windows' own thumbnail (the same one File Explorer shows, from its cache when it has one).</summary>
    private static BitmapSource? ShellThumb(string path, int side)
    {
        var iid = NativeMethods.IID_IShellItemImageFactory;
        if (NativeMethods.SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out var factory) != 0 || factory == null) return null;
        try
        {
            var size = new NativeMethods.SIZE { cx = side, cy = side };
            if (factory.GetImage(size, NativeMethods.SIIGBF.ThumbnailOnly, out var hbm) != 0 || hbm == IntPtr.Zero) return null;
            try { return FromHBitmap(hbm); }
            finally { NativeMethods.DeleteObject(hbm); }
        }
        catch { return null; }
        finally { Marshal.ReleaseComObject(factory); }
    }

    private static BitmapSource? FromHBitmap(IntPtr hbm)
    {
        if (NativeMethods.GetObject(hbm, Marshal.SizeOf<NativeMethods.BITMAP>(), out var bm) == 0 || bm.bmWidth <= 0 || bm.bmHeight == 0) return null;
        int w = bm.bmWidth, h = Math.Abs(bm.bmHeight);
        var bmi = new NativeMethods.BITMAPINFOHEADER { biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(), biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32 };
        var bits = new byte[w * h * 4];
        var dc = NativeMethods.GetDC(IntPtr.Zero);
        try { if (NativeMethods.GetDIBits(dc, hbm, 0, (uint)h, bits, ref bmi, 0) == 0) return null; }
        finally { NativeMethods.ReleaseDC(IntPtr.Zero, dc); }
        // A completely empty picture means "no thumbnail" (some thumbnail makers do this instead of saying so).
        var anyPixel = false;
        for (var i = 0; i < bits.Length; i++) if (bits[i] != 0) { anyPixel = true; break; }
        if (!anyPixel) return null;
        // Photos come back with an empty see-through channel; treat those as solid.
        var anyAlpha = false;
        for (var i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
        var src = BitmapSource.Create(w, h, 96, 96, anyAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32, null, bits, w * 4);
        src.Freeze();
        return src;
    }

    /// <summary>Reads a picture file at a small size (only the pixels needed are decoded).</summary>
    private static BitmapSource? DecodeImage(string path, int side)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return DecodeStream(fs, side);
        }
        catch { return null; }
    }

    private static BitmapSource? DecodeStream(Stream s, int side)
    {
        try
        {
            var ms = s as MemoryStream ?? CopyToMemory(s);
            var frame = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None).Frames[0];
            int pw = frame.PixelWidth, ph = frame.PixelHeight;
            ms.Position = 0;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.StreamSource = ms;
            if (pw >= ph && pw > side) bi.DecodePixelWidth = side;
            else if (ph > pw && ph > side) bi.DecodePixelHeight = side;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private static MemoryStream CopyToMemory(Stream s)
    {
        var ms = new MemoryStream();
        s.CopyTo(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>The biggest picture inside a Word document or zip bundle (usually the paper itself, not a logo).</summary>
    private static (BitmapSource? Image, int Inner) ZipPicture(string path, bool wordOnly, int side)
    {
        try
        {
            using var zip = ZipFile.OpenRead(path);
            var entries = zip.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();
            var pics = entries.Where(e => IsPicture(e.FullName) && (!wordOnly || e.FullName.StartsWith("word/media/", StringComparison.OrdinalIgnoreCase)));
            var best = pics.OrderByDescending(e => e.Length).FirstOrDefault();
            BitmapSource? img = null;
            if (best != null && best.Length < 200_000_000)
            {
                using var es = best.Open();
                img = DecodeStream(CopyToMemory(es), side);
            }
            return (img, wordOnly ? 0 : entries.Count);
        }
        catch { return (null, 0); }
    }

    private static bool IsPicture(string name)
    {
        var e = System.IO.Path.GetExtension(name).ToLowerInvariant();
        return e is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff";
    }

    /// <summary>Photoshop files usually carry a small JPEG preview inside them (image resource 1036).</summary>
    private static BitmapSource? PsdPicture(string path, int side)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);
            var head = br.ReadBytes(30);
            if (head.Length < 30 || Encoding.ASCII.GetString(head, 0, 4) != "8BPS") return null;
            var colourModeLen = ReadBE32(head, 26);
            fs.Position = 30 + colourModeLen;
            var lenBuf = br.ReadBytes(4);
            var resLen = (int)Math.Min(ReadBE32(lenBuf, 0), 8 * 1024 * 1024);
            var res = br.ReadBytes(resLen);
            var i = 0;
            while (i + 12 < res.Length)
            {
                if (Encoding.ASCII.GetString(res, i, 4) != "8BIM") break;
                var id = (res[i + 4] << 8) | res[i + 5];
                var nameLen = res[i + 6];
                var off = i + 7 + nameLen;
                if ((nameLen + 1) % 2 != 0) off++;
                var size = (int)ReadBE32(res, off);
                var dataStart = off + 4;
                if ((id == 1036 || id == 1033) && dataStart + 28 < res.Length && size > 28)
                    return DecodeStream(new MemoryStream(res, dataStart + 28, Math.Min(size - 28, res.Length - dataStart - 28)), side);
                i = dataStart + size + (size % 2);
            }
        }
        catch { }
        return null;
    }

    private static uint ReadBE32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    /// <summary>Page 1 of a PDF, drawn by the PDF reader built into Windows.</summary>
    private static BitmapSource? PdfPicture(string path, int side)
    {
        try
        {
            return Task.Run(async () =>
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                var doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(file);
                if (doc.PageCount == 0) return null;
                using var page = doc.GetPage(0);
                var landscape = page.Size.Width >= page.Size.Height;
                var opts = new Windows.Data.Pdf.PdfPageRenderOptions { BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255) };
                if (landscape) opts.DestinationWidth = (uint)side; else opts.DestinationHeight = (uint)side;
                using var ras = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                await page.RenderToStreamAsync(ras, opts);
                using var s = ras.AsStreamForRead();
                return DecodeStream(CopyToMemory(s), side);
            }).GetAwaiter().GetResult();
        }
        catch { return null; }
    }

    /// <summary>Draws an SVG cut file (Windows has no SVG thumbnails of its own).</summary>
    private static BitmapSource? SvgPicture(string path, int side)
    {
        try
        {
            if (new FileInfo(path).Length > 30_000_000) return null;
            var settings = new WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = false, OptimizePath = true };
            using var reader = new FileSvgReader(settings);
            var drawing = reader.Read(path);
            if (drawing == null) return null;
            var b = drawing.Bounds;
            if (b.IsEmpty || b.Width <= 0 || b.Height <= 0) return null;
            var scale = side / Math.Max(b.Width, b.Height);
            int w = Math.Max(1, (int)Math.Ceiling(b.Width * scale)), h = Math.Max(1, (int)Math.Ceiling(b.Height * scale));
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(scale, scale));
                dc.PushTransform(new TranslateTransform(-b.X, -b.Y));
                dc.DrawDrawing(drawing);
            }
            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }
        catch { return null; }
    }

    // ---------- the disk cache ----------

    private static BitmapSource? LoadFile(string file)
    {
        try
        {
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = fs;
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    private void Save(string key, BitmapSource? img, int inner, bool rememberEmpty)
    {
        char kind;
        if (img == null)
        {
            if (!rememberEmpty) return;
            kind = 'n';
        }
        else
        {
            var alpha = img.Format == PixelFormats.Pbgra32 || img.Format == PixelFormats.Bgra32 || img.Format.Masks.Count == 4 || img.Format == PixelFormats.Indexed8;
            kind = alpha ? 'p' : 'j';
            try
            {
                BitmapEncoder enc = alpha ? new PngBitmapEncoder() : new JpegBitmapEncoder { QualityLevel = 85 };
                enc.Frames.Add(BitmapFrame.Create(img));
                var file = System.IO.Path.Combine(_dir, key + (alpha ? ".png" : ".jpg"));
                using (var fs = new FileStream(file + ".tmp", FileMode.Create, FileAccess.Write)) enc.Save(fs);
                File.Move(file + ".tmp", file, overwrite: true);
            }
            catch { return; }
        }
        lock (_gate)
        {
            _index[key] = (kind, inner);
            try { _indexWriter?.WriteLine(key + "\t" + kind + "\t" + inner); _indexWriter?.Flush(); } catch { }
        }
    }

    private void LoadIndex()
    {
        var file = System.IO.Path.Combine(_dir, "index.tsv");
        if (!File.Exists(file)) return;
        try
        {
            foreach (var line in File.ReadLines(file))
            {
                var p = line.Split('\t');
                if (p.Length == 3 && p[1].Length == 1 && int.TryParse(p[2], out var n)) _index[p[0]] = (p[1][0], n);
            }
        }
        catch (IOException) { }
    }

    public void Dispose()
    {
        lock (_gate) { _stopping = true; Monitor.PulseAll(_gate); }
        try { _indexWriter?.Dispose(); } catch { }
    }
}
