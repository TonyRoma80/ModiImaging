using System.Drawing.Imaging;

namespace ModiImaging.Drawing;

public static class PngExporter
{
    /// <summary>
    /// Salva una PNG per pagina. Con una sola pagina usa il nome indicato, altrimenti
    /// aggiunge il suffisso "_p001", "_p002", ... Restituisce i file creati.
    /// </summary>
    public static List<string> Export(Document doc, string outputPath, float dpi = 200f,
                                      IProgress<(int done, int total)>? progress = null)
    {
        string full = Path.GetFullPath(outputPath);
        string dir = Path.GetDirectoryName(full)!;
        string baseName = Path.GetFileNameWithoutExtension(full);
        Directory.CreateDirectory(dir);

        int n = doc.Pages.Count;
        int digits = Math.Max(3, n.ToString().Length);
        var files = new List<string>();
        for (int i = 0; i < n; i++)
        {
            string file = n == 1 ? full : Path.Combine(dir, $"{baseName}_p{(i + 1).ToString().PadLeft(digits, '0')}.png");
            using var bmp = doc.Pages[i].Render(dpi);
            bmp.Save(file, ImageFormat.Png);
            files.Add(file);
            progress?.Report((i + 1, n));
        }
        return files;
    }
}
