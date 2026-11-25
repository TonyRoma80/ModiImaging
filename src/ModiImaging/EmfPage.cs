using ModiImaging.Emf;

namespace ModiImaging;

/// <summary>
/// Una pagina in formato EMF: una pagina di un file MDI oppure un file .emf autonomo.
/// </summary>
public sealed class EmfPage
{
    private readonly Lazy<EmfTextExtractor.Result> _text;

    /// <summary>Numero della pagina (da 1).</summary>
    public int Number { get; }

    /// <summary>EMF originale, così come era nel file.</summary>
    public byte[] OriginalEmf { get; }

    /// <summary>
    /// EMF ripulito dai record proprietari MODI, pronto per essere disegnato con GDI/GDI+
    /// (ad esempio <c>new Metafile(new MemoryStream(page.Emf))</c>).
    /// </summary>
    public byte[] Emf { get; }

    /// <summary>Larghezza della pagina in millimetri.</summary>
    public double WidthMm { get; }

    /// <summary>Altezza della pagina in millimetri.</summary>
    public double HeightMm { get; }

    /// <summary>
    /// Immagini compresse con l'algoritmo proprietario MODI ("PTC+MSHM"), non decodificabili:
    /// in <see cref="Emf"/> sono sostituite da riquadri grigi.
    /// </summary>
    public int UnsupportedImages { get; }

    private EmfPage(byte[] original, int number)
    {
        if (!EmfInfo.IsValidHeader(original))
            throw new UnsupportedFormatException("I dati non sono un Enhanced Metafile (EMF) valido.");
        Number = number;
        OriginalEmf = original;
        var sanitized = EmfSanitizer.Sanitize(original);
        Emf = sanitized.Emf;
        UnsupportedImages = sanitized.ReplacedImages;
        (WidthMm, HeightMm) = EmfInfo.GetPageSizeMm(original);
        _text = new Lazy<EmfTextExtractor.Result>(() => EmfTextExtractor.Extract(Emf));
    }

    /// <summary>Crea una pagina da un EMF (ad esempio il contenuto di un file .emf).</summary>
    public static EmfPage FromEmf(byte[] emf, int number = 1) => new(emf, number);

    /// <summary>Carica un file .emf.</summary>
    public static EmfPage Load(string path) => FromEmf(File.ReadAllBytes(path));

    /// <summary>Frammenti di testo della pagina, nell'ordine in cui sono disegnati, con le coordinate in mm.</summary>
    public IReadOnlyList<TextRun> TextRuns => _text.Value.Runs;

    /// <summary>
    /// Frammenti di testo che non si possono decodificare perché scritti come indici di glifo
    /// (ETO_GLYPH_INDEX): per leggerli serve l'OCR.
    /// </summary>
    public int UndecodableTextRuns => _text.Value.GlyphIndexRuns;

    /// <summary>Numero di immagini bitmap presenti nella pagina (incluse quelle non supportate).</summary>
    public int ImageCount => _text.Value.ImageCount;

    /// <summary>Frazione dell'area della pagina coperta da immagini bitmap (0-1, approssimata).</summary>
    public double ImageCoverage => _text.Value.ImageCoverage;

    /// <summary>Testo della pagina ricostruito per righe, dall'alto in basso e da sinistra a destra.</summary>
    public string GetText() => TextLayout.ToPlainText(TextRuns);

    /// <summary>
    /// True se il testo leggibile direttamente è scarso e la pagina è in gran parte un'immagine
    /// (tipicamente una scansione), oppure se contiene testo non decodificabile.
    /// In questi casi conviene passare la pagina all'OCR.
    /// </summary>
    public bool NeedsOcr
    {
        get
        {
            if (UndecodableTextRuns > 0) return true;
            int chars = TextRuns.Sum(r => r.Text.Count(c => !char.IsWhiteSpace(c)));
            return ImageCoverage >= 0.25 && chars < 200;
        }
    }

    public override string ToString() => $"Pagina {Number} ({WidthMm:0.#} × {HeightMm:0.#} mm, {TextRuns.Count} frammenti di testo)";
}
