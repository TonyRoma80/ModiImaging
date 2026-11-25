using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace ModiImaging.Drawing;

/// <summary>Una pagina del documento, disegnabile in qualunque rettangolo (schermo, stampante, bitmap).</summary>
public abstract class Page : IDisposable
{
    /// <summary>Dimensione fisica della pagina in millimetri.</summary>
    public abstract SizeF SizeMm { get; }

    /// <summary>True se la pagina è un'immagine vettoriale (EMF/WMF).</summary>
    public abstract bool IsVector { get; }

    public SizeF SizeInches => new(SizeMm.Width / 25.4f, SizeMm.Height / 25.4f);

    public bool IsLandscape => SizeMm.Width > SizeMm.Height;

    /// <summary>Immagini della pagina non decodificabili (compressione proprietaria MODI), sostituite da riquadri grigi.</summary>
    public int UnsupportedImages { get; init; }

    /// <summary>Pagina EMF di origine (MDI o .emf), da cui si può estrarre il testo; null per le immagini raster.</summary>
    public EmfPage? Source { get; init; }

    /// <summary>Disegna la pagina scalata nel rettangolo di destinazione.</summary>
    public abstract void Draw(Graphics g, RectangleF dest);

    /// <summary>Renderizza la pagina in una bitmap con sfondo bianco alla risoluzione indicata.</summary>
    public Bitmap Render(float dpi)
    {
        int w = Math.Max(1, (int)Math.Round(SizeInches.Width * dpi));
        int h = Math.Max(1, (int)Math.Round(SizeInches.Height * dpi));
        var bmp = RenderToSize(w, h);
        bmp.SetResolution(dpi, dpi);
        return bmp;
    }

    public Bitmap RenderToSize(int width, int height)
    {
        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        ApplyQuality(g);
        Draw(g, new RectangleF(0, 0, width, height));
        return bmp;
    }

    public static void ApplyQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
    }

    public virtual void Dispose() => GC.SuppressFinalize(this);
}

/// <summary>Pagina basata su un'immagine GDI+ (Metafile EMF/WMF o bitmap).</summary>
public sealed class ImagePage : Page
{
    private readonly Image _image;
    private readonly Stream? _stream;
    private readonly SizeF _sizeMm;

    public ImagePage(Image image, Stream? backingStream, SizeF? sizeMm = null)
    {
        _image = image;
        _stream = backingStream;
        _sizeMm = sizeMm ?? ComputeSizeMm(image);
    }

    public override SizeF SizeMm => _sizeMm;
    public override bool IsVector => _image is Metafile;

    public override void Draw(Graphics g, RectangleF dest) => g.DrawImage(_image, dest);

    internal static SizeF ComputeSizeMm(Image image)
    {
        if (image is Metafile mf)
        {
            var h = mf.GetMetafileHeader();
            float dx = h.DpiX > 0 ? h.DpiX : 96, dy = h.DpiY > 0 ? h.DpiY : 96;
            return new SizeF(h.Bounds.Width / dx * 25.4f, h.Bounds.Height / dy * 25.4f);
        }
        float rx = image.HorizontalResolution > 1 ? image.HorizontalResolution : 96;
        float ry = image.VerticalResolution > 1 ? image.VerticalResolution : 96;
        return new SizeF(image.Width / rx * 25.4f, image.Height / ry * 25.4f);
    }

    public override void Dispose()
    {
        _image.Dispose();
        _stream?.Dispose();
        base.Dispose();
    }
}

/// <summary>Una pagina di un TIFF multipagina: condivide l'immagine e seleziona il frame al momento del disegno.</summary>
public sealed class TiffFramePage : Page
{
    private readonly SharedImage _shared;
    private readonly int _frame;
    private readonly SizeF _sizeMm;

    internal TiffFramePage(SharedImage shared, int frame)
    {
        _shared = shared;
        _frame = frame;
        shared.AddRef();
        lock (shared.Image)
        {
            shared.Image.SelectActiveFrame(FrameDimension.Page, frame);
            _sizeMm = ImagePage.ComputeSizeMm(shared.Image);
        }
    }

    public override SizeF SizeMm => _sizeMm;
    public override bool IsVector => false;

    public override void Draw(Graphics g, RectangleF dest)
    {
        lock (_shared.Image)
        {
            _shared.Image.SelectActiveFrame(FrameDimension.Page, _frame);
            g.DrawImage(_shared.Image, dest);
        }
    }

    public override void Dispose()
    {
        _shared.Release();
        base.Dispose();
    }
}

internal sealed class SharedImage
{
    private int _refs;
    public Image Image { get; }
    private readonly Stream _stream;

    public SharedImage(Image image, Stream stream) { Image = image; _stream = stream; }
    public void AddRef() => Interlocked.Increment(ref _refs);
    public void Release()
    {
        if (Interlocked.Decrement(ref _refs) == 0)
        {
            Image.Dispose();
            _stream.Dispose();
        }
    }
}
