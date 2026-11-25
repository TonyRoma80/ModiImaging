using System.Drawing.Printing;

namespace ModiImaging.Drawing;

/// <summary>Esportazione in PDF vettoriale tramite la stampante "Microsoft Print to PDF" di Windows.</summary>
public static class PdfExporter
{
    public const string PdfPrinterName = "Microsoft Print to PDF";

    public static bool IsPdfPrinterAvailable()
    {
        try
        {
            return PrinterSettings.InstalledPrinters.Cast<string>()
                .Any(p => string.Equals(p, PdfPrinterName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false; // spooler non disponibile
        }
    }

    public static void ExportVector(Document doc, string outputPath, TimeSpan? timeout = null)
    {
        if (!IsPdfPrinterAvailable())
            throw new InvalidOperationException($"La stampante \"{PdfPrinterName}\" non è installata.");

        string full = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (File.Exists(full)) File.Delete(full);

        DateTime started = DateTime.Now;
        using (var pd = new DocumentPrinter(doc) { MatchPaperSize = true })
        {
            pd.PrinterSettings.PrinterName = PdfPrinterName;
            if (!pd.PrinterSettings.IsValid)
                throw new InvalidOperationException($"La stampante \"{PdfPrinterName}\" non è utilizzabile.");
            pd.PrinterSettings.PrintToFile = true;
            pd.PrinterSettings.PrintFileName = full;
            pd.PrintController = new StandardPrintController(); // nessuna finestra di avanzamento
            pd.Print();
        }

        WaitForSpooler(full, timeout ?? TimeSpan.FromMinutes(3));
    }

    /// <summary>
    /// Il file viene scritto dallo spooler in modo asincrono: attende che esista, che termini con
    /// "%%EOF", che la dimensione sia stabile e che non sia più bloccato da altri processi.
    /// </summary>
    private static void WaitForSpooler(string path, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        long lastLen = -1;
        int stable = 0;
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(250);
            if (!File.Exists(path)) continue;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                long len = fs.Length;
                if (len < 64) { lastLen = len; stable = 0; continue; }
                if (len == lastLen && EndsWithEof(fs))
                {
                    if (++stable >= 2) return;
                }
                else stable = 0;
                lastLen = len;
            }
            catch (IOException)
            {
                stable = 0; // ancora in scrittura
            }
            catch (UnauthorizedAccessException)
            {
                stable = 0;
            }
        }
        throw new TimeoutException("Lo spooler di stampa non ha completato la scrittura del PDF entro il tempo previsto.");
    }

    private static bool EndsWithEof(FileStream fs)
    {
        int n = (int)Math.Min(64, fs.Length);
        var buf = new byte[n];
        fs.Seek(-n, SeekOrigin.End);
        fs.ReadExactly(buf);
        return System.Text.Encoding.ASCII.GetString(buf).Contains("%%EOF");
    }
}
