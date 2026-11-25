using System.Runtime.InteropServices;
using ModiImaging.Drawing;
using MdiViewer.UI;

namespace MdiViewer;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
    private const int AttachParentProcess = -1;

    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool raster = args.Any(a => Is(a, "--raster"));
        bool recursive = args.Any(a => Is(a, "--recursive") || Is(a, "-r"));
        bool convert = args.Any(a => Is(a, "--convert"));
        bool text = args.Any(a => Is(a, "--text"));
        bool help = args.Any(a => Is(a, "--help") || Is(a, "-h") || Is(a, "/?"));
        var positional = args.Where(a => !a.StartsWith("--") && !Is(a, "-r") && !Is(a, "-h") && !Is(a, "/?")).ToList();

        if (help) return RunConsole(() => { PrintUsage(); return 0; });
        if (text) return RunConsole(() => ExtractText(positional, recursive));
        if (convert) return RunConsole(() => BatchConvert(positional, raster, recursive));
        if (positional.Count == 2) return RunConsole(() => SingleConvert(positional[0], positional[1], raster));
        if (positional.Count > 2) return RunConsole(() => { PrintUsage(); return 2; });

        Application.Run(new MainForm(positional.FirstOrDefault()));
        return 0;
    }

    private static bool Is(string a, string opt) => string.Equals(a, opt, StringComparison.OrdinalIgnoreCase);

    private static int RunConsole(Func<int> body)
    {
        // Applicazione WinForms: si aggancia alla console da cui è stata lanciata (se c'è).
        // La codepage della console non viene modificata: quella OEM italiana (850) gestisce già le lettere accentate.
        AttachConsole(AttachParentProcess);
        Console.WriteLine();
        int code;
        try { code = body(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Errore: " + ex.Message);
            code = 1;
        }
        Console.Out.Flush();
        return code;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            MdiViewer - visualizzatore e convertitore di file Microsoft Document Imaging

            Uso:
              MdiViewer.exe [file]                         apre il visualizzatore
              MdiViewer.exe <file> <file.pdf> [--raster]   converte un file in PDF
              MdiViewer.exe --convert <file o cartella> [--raster] [--recursive]
                                                           converte in blocco i .mdi, salvando
                                                           ogni PDF accanto all'originale
              MdiViewer.exe --text <file> [file.txt]       estrae il testo (a video o in un file)
              MdiViewer.exe --text <cartella> [--recursive]
                                                           estrae il testo di tutti i .mdi, salvando
                                                           un .txt accanto a ogni originale

            Opzioni:
              --raster      PDF da immagini a 200 dpi invece del PDF vettoriale
                            (usato automaticamente se "Microsoft Print to PDF" manca)
              --recursive   con --convert include anche le sottocartelle

            Codici di uscita: 0 = ok, 1 = almeno un errore, 2 = argomenti non validi.
            """);
    }

    private static int ExtractText(List<string> paths, bool recursive)
    {
        if (paths.Count == 0 || paths.Count > 2) { PrintUsage(); return 2; }
        var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        if (File.Exists(paths[0]))
        {
            using var doc = Document.Load(paths[0]);
            if (!doc.HasText)
            {
                Console.Error.WriteLine("Il file non contiene testo (è un'immagine): serve l'OCR.");
                return 1;
            }
            string txt = doc.GetText();
            if (paths.Count == 2)
            {
                File.WriteAllText(paths[1], txt, utf8);
                Console.WriteLine($"OK  {Path.GetFullPath(paths[1])}");
            }
            else Console.WriteLine(txt);
            return 0;
        }

        var inputs = Converter.CollectInputs(paths[0], recursive);
        int errors = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            string output = Path.ChangeExtension(inputs[i], ".txt");
            try
            {
                using var doc = Document.Load(inputs[i]);
                File.WriteAllText(output, doc.GetText(), utf8);
                Console.WriteLine($"[{i + 1}/{inputs.Count}] OK      {output}");
            }
            catch (Exception ex)
            {
                errors++;
                Console.WriteLine($"[{i + 1}/{inputs.Count}] ERRORE  {inputs[i]}: {ex.Message.Replace('\n', ' ')}");
            }
        }
        Console.WriteLine($"\nCompletato: {inputs.Count - errors} file, {errors} errori.");
        return errors == 0 ? 0 : 1;
    }

    private static int SingleConvert(string input, string output, bool raster)
    {
        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"File non trovato: {input}");
            return 1;
        }
        Console.WriteLine($"Conversione di {input}...");
        var r = Converter.ConvertToPdf(input, output, raster);
        if (r.Success) Console.WriteLine($"OK  {Path.GetFullPath(output)} ({r.Message})");
        else Console.Error.WriteLine($"ERRORE  {r.Message}");
        return r.Success ? 0 : 1;
    }

    private static int BatchConvert(List<string> paths, bool raster, bool recursive)
    {
        if (paths.Count == 0)
        {
            PrintUsage();
            return 2;
        }

        var inputs = new List<string>();
        foreach (var p in paths) inputs.AddRange(Converter.CollectInputs(p, recursive));
        if (inputs.Count == 0)
        {
            Console.WriteLine("Nessun file .mdi trovato.");
            return 0;
        }

        Console.WriteLine($"File da convertire: {inputs.Count}");
        var results = Converter.ConvertBatch(inputs, raster, (i, n, r) =>
        {
            if (r.Success) Console.WriteLine($"[{i}/{n}] OK      {r.Output} ({r.Message})");
            else Console.WriteLine($"[{i}/{n}] ERRORE  {r.Input}: {r.Message}");
        });

        int ok = results.Count(r => r.Success);
        Console.WriteLine();
        Console.WriteLine($"Completato: {ok} convertiti, {results.Count - ok} errori.");
        return ok == results.Count ? 0 : 1;
    }
}
