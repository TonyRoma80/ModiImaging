
namespace ModiImaging.Drawing;

public sealed record ConversionResult(string Input, string? Output, bool Success, string Message);

/// <summary>Conversione di file in PDF, usata sia dalla riga di comando sia dall'interfaccia.</summary>
public static class Converter
{
    /// <summary>Converte un file in PDF. Se la stampante PDF manca, usa automaticamente il PDF da immagini.</summary>
    public static ConversionResult ConvertToPdf(string input, string output, bool raster)
    {
        try
        {
            using var doc = Document.Load(input);
            bool useRaster = raster || !PdfExporter.IsPdfPrinterAvailable();
            if (useRaster)
                RasterPdfWriter.Export(doc, output);
            else
                PdfExporter.ExportVector(doc, output);

            string mode = useRaster
                ? (raster ? "PDF da immagini" : $"PDF da immagini: \"{PdfExporter.PdfPrinterName}\" non disponibile")
                : "PDF vettoriale";
            string pages = doc.Pages.Count == 1 ? "1 pagina" : $"{doc.Pages.Count} pagine";
            string warning = doc.Warning != null ? $"; ATTENZIONE: {doc.Warning}" : "";
            return new ConversionResult(input, output, true, $"{pages}, {mode}{warning}");
        }
        catch (Exception ex)
        {
            return new ConversionResult(input, null, false, ex.Message.Replace("\n\n", " ").Replace('\n', ' '));
        }
    }

    /// <summary>Elenca i file .mdi da convertire: un singolo file oppure tutti quelli di una cartella.</summary>
    public static List<string> CollectInputs(string path, bool recursive)
    {
        if (File.Exists(path)) return new List<string> { Path.GetFullPath(path) };
        if (Directory.Exists(path))
            return Directory.EnumerateFiles(path, "*.mdi",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".mdi", StringComparison.OrdinalIgnoreCase)) // esclude "*.mdi~" ecc.
                .OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        throw new FileNotFoundException($"File o cartella non trovati: {path}");
    }

    /// <summary>Converte in blocco, salvando ogni PDF accanto all'originale.</summary>
    public static List<ConversionResult> ConvertBatch(IEnumerable<string> inputs, bool raster,
        Action<int, int, ConversionResult>? onResult = null)
    {
        var list = inputs.ToList();
        var results = new List<ConversionResult>();
        for (int i = 0; i < list.Count; i++)
        {
            string output = Path.ChangeExtension(list[i], ".pdf");
            var r = ConvertToPdf(list[i], output, raster);
            results.Add(r);
            onResult?.Invoke(i + 1, list.Count, r);
        }
        return results;
    }
}
