using System.Drawing.Imaging;
using System.Text;
using ModiImaging.Emf;

namespace ModiImaging.Drawing;

/// <summary>
/// Documento aperto: un elenco di pagine disegnabili. Carica MDI, EMF, WMF, TIFF multipagina, PNG, JPG e BMP.
/// </summary>
public sealed class Document : IDisposable
{
    public string FilePath { get; }
    public IReadOnlyList<Page> Pages { get; }
    public string FormatName { get; }

    private Document(string path, List<Page> pages, string format)
    {
        FilePath = path;
        Pages = pages;
        FormatName = format;
    }

    public static readonly string[] SupportedExtensions =
        { ".mdi", ".emf", ".wmf", ".tif", ".tiff", ".png", ".jpg", ".jpeg", ".bmp" };

    public const string OpenFileFilter =
        "Tutti i documenti supportati|*.mdi;*.emf;*.wmf;*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp|" +
        "Microsoft Document Imaging (*.mdi)|*.mdi|" +
        "Metafile (*.emf;*.wmf)|*.emf;*.wmf|" +
        "Immagini TIFF (*.tif;*.tiff)|*.tif;*.tiff|" +
        "Immagini (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|" +
        "Tutti i file (*.*)|*.*";

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static Document Load(string path)
    {
        byte[] data;
        try
        {
            data = File.ReadAllBytes(path);
        }
        catch (Exception ex)
        {
            throw new IOException($"Impossibile leggere il file:\n{path}\n\n{ex.Message}", ex);
        }

        if (data.Length < 8)
            throw new UnsupportedFormatException("Il file è vuoto o troppo corto.");

        var pages = new List<Page>();
        string format;
        try
        {
            if (MdiFile.HasSignature(data))
            {
                format = "Microsoft Document Imaging";
                foreach (var emfPage in MdiFile.Load(data).Pages)
                    pages.Add(CreateEmfPage(emfPage));
            }
            else if (EmfInfo.IsValidHeader(data))
            {
                format = "Enhanced Metafile (EMF)";
                pages.Add(CreateEmfPage(EmfPage.FromEmf(data)));
            }
            else if (IsTiff(data))
            {
                format = "TIFF";
                LoadTiff(data, pages);
            }
            else if (IsWmf(data))
            {
                format = "Windows Metafile (WMF)";
                var ms = new MemoryStream(data, writable: false);
                pages.Add(new ImagePage(new Metafile(ms), ms));
            }
            else
            {
                var ms = new MemoryStream(data, writable: false);
                Image img;
                try { img = Image.FromStream(ms); }
                catch (ArgumentException ex)
                {
                    ms.Dispose();
                    throw new UnsupportedFormatException("Formato del file non riconosciuto o file danneggiato.", ex);
                }
                format = DescribeRaster(img);
                pages.Add(new ImagePage(img, ms));
            }
        }
        catch
        {
            foreach (var p in pages) p.Dispose();
            throw;
        }

        return new Document(path, pages, format);
    }

    /// <summary>Numero totale di immagini non visualizzabili (sostituite da riquadri grigi).</summary>
    public int UnsupportedImages => Pages.Sum(p => p.UnsupportedImages);

    /// <summary>Avviso da mostrare all'utente, oppure null.</summary>
    public string? Warning => UnsupportedImages switch
    {
        0 => null,
        1 => "1 immagine non visualizzabile (compressione proprietaria MODI): sostituita da un riquadro grigio",
        int n => $"{n} immagini non visualizzabili (compressione proprietaria MODI): sostituite da riquadri grigi",
    };

    /// <summary>True se il testo del documento si può estrarre direttamente (pagine EMF).</summary>
    public bool HasText => Pages.Any(p => p.Source != null);

    /// <summary>Testo di tutte le pagine EMF, separate da '\f'. Le pagine raster non hanno testo (serve l'OCR).</summary>
    public string GetText(string pageSeparator = "\f\n")
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Pages.Count; i++)
        {
            if (i > 0) sb.Append(pageSeparator);
            sb.Append(Pages[i].Source?.GetText());
        }
        return sb.ToString();
    }

    /// <summary>Crea una pagina disegnabile a partire da una pagina EMF di <see cref="ModiImaging"/>.</summary>
    public static Page CreateEmfPage(EmfPage emfPage)
    {
        var ms = new MemoryStream(emfPage.Emf, writable: false);
        try
        {
            var mf = new Metafile(ms);
            return new ImagePage(mf, ms, new SizeF((float)emfPage.WidthMm, (float)emfPage.HeightMm))
            {
                UnsupportedImages = emfPage.UnsupportedImages,
                Source = emfPage,
            };
        }
        catch (Exception ex)
        {
            ms.Dispose();
            throw new UnsupportedFormatException("La pagina EMF contenuta nel file è danneggiata e non può essere visualizzata.", ex);
        }
    }

    private static void LoadTiff(byte[] data, List<Page> pages)
    {
        var ms = new MemoryStream(data, writable: false);
        Image img;
        try { img = Image.FromStream(ms); }
        catch (ArgumentException ex)
        {
            ms.Dispose();
            throw new UnsupportedFormatException("Il file TIFF è danneggiato o usa una compressione non supportata.", ex);
        }
        var shared = new SharedImage(img, ms);
        shared.AddRef(); // riferimento temporaneo durante la costruzione
        try
        {
            int n = img.FrameDimensionsList.Contains(FrameDimension.Page.Guid) ? img.GetFrameCount(FrameDimension.Page) : 1;
            for (int i = 0; i < n; i++)
                pages.Add(new TiffFramePage(shared, i));
        }
        finally
        {
            shared.Release();
        }
    }

    private static bool IsTiff(byte[] d) =>
        (d[0] == 0x49 && d[1] == 0x49 && d[2] == 0x2A && d[3] == 0x00) ||
        (d[0] == 0x4D && d[1] == 0x4D && d[2] == 0x00 && d[3] == 0x2A);

    private static bool IsWmf(byte[] d) =>
        (d[0] == 0xD7 && d[1] == 0xCD && d[2] == 0xC6 && d[3] == 0x9A) || // placeable
        ((d[0] == 0x01 || d[0] == 0x02) && d[1] == 0x00 && d[2] == 0x09 && d[3] == 0x00);

    private static string DescribeRaster(Image img)
    {
        var f = img.RawFormat.Guid;
        if (f == ImageFormat.Png.Guid) return "PNG";
        if (f == ImageFormat.Jpeg.Guid) return "JPEG";
        if (f == ImageFormat.Bmp.Guid) return "BMP";
        if (f == ImageFormat.Gif.Guid) return "GIF";
        return "Immagine";
    }

    public void Dispose()
    {
        foreach (var p in Pages) p.Dispose();
    }
}
