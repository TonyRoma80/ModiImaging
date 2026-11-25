using System.IO.Compression;
using System.Text;
using ModiImaging.Emf;

namespace ModiImaging;

/// <summary>Errore sollevato quando un file non può essere letto perché in un formato non supportato.</summary>
public sealed class UnsupportedFormatException : Exception
{
    public UnsupportedFormatException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Documento Microsoft Document Imaging (.mdi).
/// <para>
/// Il formato non è documentato: i file iniziano con "EP*\0" e contengono uno o più stream zlib,
/// ognuno dei quali, decompresso, è una pagina in formato EMF (Enhanced Metafile).
/// </para>
/// </summary>
/// <example>
/// <code>
/// var mdi = MdiFile.Load(@"C:\documenti\scansione.mdi");
/// string testo = mdi.GetText();
/// foreach (var page in mdi.Pages)
///     if (page.NeedsOcr) { /* usare ModiImaging.Ocr */ }
/// </code>
/// </example>
public sealed class MdiFile
{
    private static readonly byte[] Signature = { 0x45, 0x50, 0x2A, 0x00 }; // "EP*\0"
    private const int MaxEmfSize = 512 * 1024 * 1024;

    /// <summary>Pagine del documento, nell'ordine in cui compaiono nel file.</summary>
    public IReadOnlyList<EmfPage> Pages { get; }

    private MdiFile(List<EmfPage> pages) => Pages = pages;

    /// <summary>True se i dati iniziano con la firma dei file MDI ("EP*\0").</summary>
    public static bool HasSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 4 && data[..4].SequenceEqual(Signature);

    /// <summary>True se il file indicato inizia con la firma dei file MDI (legge solo 4 byte).</summary>
    public static bool IsMdiFile(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> head = stackalloc byte[4];
            return fs.ReadAtLeast(head, 4, throwOnEndOfStream: false) == 4 && HasSignature(head);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    public static MdiFile Load(string path) => Load(File.ReadAllBytes(path));

    public static MdiFile Load(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return Load(ms.ToArray());
    }

    /// <exception cref="UnsupportedFormatException">Il file non è un MDI o non contiene pagine EMF.</exception>
    public static MdiFile Load(byte[] data)
    {
        var pages = ExtractEmfStreams(data)
            .Select((emf, i) => EmfPage.FromEmf(emf, i + 1))
            .ToList();
        return new MdiFile(pages);
    }

    /// <summary>
    /// Testo di tutte le pagine. Le pagine sono separate da <paramref name="pageSeparator"/>
    /// (per default il carattere di salto pagina, '\f').
    /// </summary>
    public string GetText(string pageSeparator = "\f\n")
    {
        var sb = new StringBuilder();
        for (int i = 0; i < Pages.Count; i++)
        {
            if (i > 0) sb.Append(pageSeparator);
            sb.Append(Pages[i].GetText());
        }
        return sb.ToString();
    }

    /// <summary>True se almeno una pagina sembra richiedere l'OCR (vedi <see cref="EmfPage.NeedsOcr"/>).</summary>
    public bool NeedsOcr => Pages.Any(p => p.NeedsOcr);

    /// <summary>
    /// Estrae le pagine EMF (non ancora ripulite) contenute in un file MDI.
    /// Scorre il file cercando intestazioni zlib valide, decomprime solo i primi 88 byte per verificare
    /// l'intestazione EMF e, se è valida, decomprime l'intera pagina (nBytes).
    /// </summary>
    public static List<byte[]> ExtractEmfStreams(byte[] data)
    {
        if (!HasSignature(data))
            throw new UnsupportedFormatException("Il file non è un documento Microsoft Document Imaging valido (firma \"EP*\" assente).");

        var pages = new List<byte[]>();
        for (int i = 4; i < data.Length - 2; i++)
        {
            if (!IsZlibHeader(data[i], data[i + 1]))
                continue;

            byte[]? head = TryInflate(data, i, EmfInfo.MinHeaderSize);
            if (head == null || !EmfInfo.IsValidHeader(head))
                continue;

            int nBytes = EmfInfo.GetTotalSize(head);
            if (nBytes < EmfInfo.MinHeaderSize || nBytes > MaxEmfSize)
                continue;

            byte[]? emf = TryInflate(data, i, nBytes);
            if (emf != null && emf.Length == nBytes)
                pages.Add(emf);
        }

        if (pages.Count == 0)
            throw new UnsupportedFormatException(
                "Il file MDI non contiene pagine in formato EMF.\n\n" +
                "Probabilmente contiene immagini acquisite da scanner, compresse con l'algoritmo " +
                "proprietario Microsoft (MODI), che non è supportato.\n\n" +
                "Per convertirlo è necessario un computer con Microsoft Office 2003/2007 " +
                "(Microsoft Office Document Imaging), da cui salvarlo in formato TIFF.");
        return pages;
    }

    /// <summary>CMF = 0x78 (deflate, finestra 32K), FLG senza dizionario predefinito e checksum valido.</summary>
    private static bool IsZlibHeader(byte cmf, byte flg) =>
        cmf == 0x78 && (flg & 0x20) == 0 && ((cmf << 8) | flg) % 31 == 0;

    private static byte[]? TryInflate(byte[] data, int offset, int count)
    {
        try
        {
            using var input = new MemoryStream(data, offset, data.Length - offset, writable: false);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            var buffer = new byte[count];
            int read = z.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
            if (read == 0) return null;
            if (read < count) Array.Resize(ref buffer, read);
            return buffer;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
