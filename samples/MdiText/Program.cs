// Esempio d'uso di ModiImaging: estrazione del testo da file .mdi, con OCR dove serve.
//
//   MdiText <file.mdi | cartella> [--ocr auto|always|never] [--lang it-IT] [--runs]
//
//   --ocr auto     (predefinito) testo nativo, OCR solo per le pagine che sembrano scansioni
//   --ocr always   OCR di tutte le pagine (utile per confrontare)
//   --ocr never    solo testo nativo
//   --runs         elenca anche i frammenti di testo con coordinate e font

using System.Text;
using ModiImaging;
using ModiImaging.Ocr;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0)
{
    Console.WriteLine("Uso: MdiText <file.mdi | cartella> [--ocr auto|always|never] [--lang it-IT] [--runs]");
    return 2;
}

string target = args[0];
string ocrMode = Option("--ocr") ?? "auto";
string? lang = Option("--lang");
bool showRuns = args.Contains("--runs");

var source = ocrMode switch
{
    "always" => TextSource.OcrOnly,
    "never" => TextSource.NativeOnly,
    _ => TextSource.Auto,
};

var files = Directory.Exists(target)
    ? Directory.GetFiles(target, "*.mdi").Order().ToArray()
    : new[] { target };

int errors = 0;
foreach (var file in files)
{
    Console.WriteLine($"===== {Path.GetFileName(file)}");
    try
    {
        var mdi = MdiFile.Load(file);
        foreach (var page in mdi.Pages)
        {
            Console.WriteLine($"--- {page}" +
                              $"{(page.ImageCount > 0 ? $", {page.ImageCount} immagini ({page.ImageCoverage:P0} della pagina)" : "")}" +
                              $"{(page.UnsupportedImages > 0 ? $", {page.UnsupportedImages} non decodificabili" : "")}" +
                              $"{(page.NeedsOcr ? ", RICHIEDE OCR" : "")}");

            if (showRuns)
                foreach (var run in page.TextRuns)
                    Console.WriteLine($"    [{run.X,6:0.0};{run.Y,6:0.0} mm  {run.FontName} {run.FontSizePt:0.#}pt{(run.Bold ? " grassetto" : "")}] {run.Text}");

            bool useOcr = source == TextSource.OcrOnly || (source == TextSource.Auto && page.NeedsOcr);
            if (useOcr && !PageOcr.IsAvailable(lang))
            {
                Console.WriteLine($"(OCR non disponibile: lingue installate {string.Join(", ", PageOcr.AvailableLanguages)})");
                useOcr = false;
            }
            Console.WriteLine(useOcr ? "(testo da OCR)" : "(testo nativo)");
            Console.WriteLine(await PageOcr.GetTextAsync(page, useOcr ? TextSource.OcrOnly : TextSource.NativeOnly, lang));
        }
    }
    catch (UnsupportedFormatException ex)
    {
        errors++;
        Console.WriteLine($"Formato non supportato: {ex.Message}");
    }
    catch (Exception ex)
    {
        errors++;
        Console.WriteLine($"Errore: {ex.Message}");
    }
    Console.WriteLine();
}
return errors == 0 ? 0 : 1;

string? Option(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
