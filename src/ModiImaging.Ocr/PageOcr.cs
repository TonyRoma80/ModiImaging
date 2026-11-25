using System.Drawing.Imaging;
using ModiImaging.Drawing;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using DrawingPage = ModiImaging.Drawing.Page;
using WinOcrLine = Windows.Media.Ocr.OcrLine;

namespace ModiImaging.Ocr;

/// <summary>Una riga riconosciuta dall'OCR, con il rettangolo in millimetri dall'angolo in alto a sinistra.</summary>
public sealed record OcrTextLine(string Text, double X, double Y, double Width, double Height);

/// <summary>Risultato dell'OCR di una pagina.</summary>
public sealed record OcrPageResult(string Text, IReadOnlyList<OcrTextLine> Lines, string Language);

/// <summary>Come ottenere il testo di una pagina EMF.</summary>
public enum TextSource
{
    /// <summary>Testo nativo se presente, OCR solo se <see cref="EmfPage.NeedsOcr"/> è vero.</summary>
    Auto,
    /// <summary>Solo il testo contenuto nel file (nessun OCR).</summary>
    NativeOnly,
    /// <summary>Sempre OCR della pagina renderizzata.</summary>
    OcrOnly,
}

/// <summary>
/// OCR con il motore integrato in Windows 10/11 (Windows.Media.Ocr): funziona offline e non richiede
/// librerie esterne. Le lingue disponibili sono quelle installate in Windows con il componente
/// "Riconoscimento ottico dei caratteri" (Impostazioni > Ora e lingua > Lingua e area geografica).
/// </summary>
public static class PageOcr
{
    /// <summary>Risoluzione predefinita per il rendering delle pagine da passare all'OCR.</summary>
    public const float DefaultDpi = 300f;

    /// <summary>Lingue OCR installate (es. "it-IT", "en-US").</summary>
    public static IReadOnlyList<string> AvailableLanguages =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    /// <summary>True se l'OCR è disponibile per la lingua indicata (o per le lingue del profilo utente se null).</summary>
    public static bool IsAvailable(string? language = null)
    {
        try { return CreateEngine(language, throwIfMissing: false) != null; }
        catch { return false; }
    }

    /// <summary>
    /// Testo di una pagina EMF/MDI: in modalità <see cref="TextSource.Auto"/> usa il testo contenuto
    /// nel file e ricorre all'OCR solo per le pagine che sembrano scansioni.
    /// </summary>
    public static async Task<string> GetTextAsync(EmfPage page, TextSource source = TextSource.Auto,
                                                  string? language = null, float dpi = DefaultDpi)
    {
        bool ocr = source == TextSource.OcrOnly || (source == TextSource.Auto && page.NeedsOcr);
        if (!ocr) return page.GetText();
        return (await RecognizeAsync(page, language, dpi)).Text;
    }

    /// <summary>Testo di tutte le pagine di un MDI, separate da '\f'.</summary>
    public static async Task<string> GetTextAsync(MdiFile mdi, TextSource source = TextSource.Auto,
                                                  string? language = null, float dpi = DefaultDpi)
    {
        var parts = new List<string>();
        foreach (var page in mdi.Pages) parts.Add(await GetTextAsync(page, source, language, dpi));
        return string.Join("\f\n", parts);
    }

    /// <summary>OCR di una pagina EMF/MDI.</summary>
    public static async Task<OcrPageResult> RecognizeAsync(EmfPage page, string? language = null, float dpi = DefaultDpi)
    {
        using var drawingPage = Document.CreateEmfPage(page);
        return await RecognizeAsync(drawingPage, language, dpi);
    }

    /// <summary>OCR di una pagina di un <see cref="Document"/> (MDI, EMF, TIFF, immagini...).</summary>
    public static async Task<OcrPageResult> RecognizeAsync(DrawingPage page, string? language = null, float dpi = DefaultDpi)
    {
        // Il motore accetta immagini fino a MaxImageDimension pixel per lato.
        float maxInches = Math.Max(page.SizeInches.Width, page.SizeInches.Height);
        if (maxInches > 0) dpi = Math.Min(dpi, (OcrEngine.MaxImageDimension - 1) / maxInches);

        using var bmp = page.Render(dpi);
        var result = await RecognizeAsync(bmp, language);
        double mmPerPx = 25.4 / dpi;
        var lines = result.Lines
            .Select(l => l with { X = l.X * mmPerPx, Y = l.Y * mmPerPx, Width = l.Width * mmPerPx, Height = l.Height * mmPerPx })
            .ToList();
        return result with { Lines = lines };
    }

    /// <summary>OCR di una bitmap. Le coordinate delle righe sono in pixel.</summary>
    public static async Task<OcrPageResult> RecognizeAsync(Bitmap bitmap, string? language = null)
    {
        var engine = CreateEngine(language, throwIfMissing: true)!;

        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        using var ras = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var softwareBitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

        var ocr = await engine.RecognizeAsync(softwareBitmap);
        var lines = ocr.Lines.Select(ToLine).ToList();
        string text = string.Join("\n", ocr.Lines.Select(l => l.Text));
        return new OcrPageResult(text, lines, engine.RecognizerLanguage.LanguageTag);
    }

    private static OcrTextLine ToLine(WinOcrLine line)
    {
        double l = double.MaxValue, t = double.MaxValue, r = double.MinValue, b = double.MinValue;
        foreach (var w in line.Words)
        {
            var rc = w.BoundingRect;
            l = Math.Min(l, rc.X); t = Math.Min(t, rc.Y);
            r = Math.Max(r, rc.X + rc.Width); b = Math.Max(b, rc.Y + rc.Height);
        }
        return line.Words.Count == 0 ? new OcrTextLine(line.Text, 0, 0, 0, 0) : new OcrTextLine(line.Text, l, t, r - l, b - t);
    }

    private static OcrEngine? CreateEngine(string? language, bool throwIfMissing)
    {
        OcrEngine? engine = language == null
            ? OcrEngine.TryCreateFromUserProfileLanguages()
            : OcrEngine.TryCreateFromLanguage(new Language(language));
        if (engine == null && throwIfMissing)
            throw new InvalidOperationException(
                $"OCR non disponibile per la lingua \"{language ?? "del profilo utente"}\". " +
                $"Lingue OCR installate: {string.Join(", ", AvailableLanguages.DefaultIfEmpty("nessuna"))}. " +
                "Per aggiungerne una: Impostazioni > Ora e lingua > Lingua e area geografica > aggiungi la lingua " +
                "con il componente \"Riconoscimento ottico dei caratteri\".");
        return engine;
    }
}
