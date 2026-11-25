using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

namespace ModiImaging.Drawing;

/// <summary>
/// Scrive un PDF in cui ogni pagina è un'immagine JPEG (filtro DCTDecode), senza librerie esterne.
/// </summary>
public static class RasterPdfWriter
{
    public const float DefaultDpi = 200f;

    public static void Export(Document doc, string outputPath, float dpi = DefaultDpi, long jpegQuality = 88,
                              IProgress<(int done, int total)>? progress = null)
    {
        string full = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string tmp = full + ".tmp";

        var jpegCodec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var encParams = new EncoderParameters(1);
        encParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, jpegQuality);

        int n = doc.Pages.Count;
        // Numerazione oggetti: 1 = Catalog, 2 = Pages, poi per ogni pagina: Page, Contents, Image.
        var offsets = new long[3 + 3 * n];

        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write))
        {
            var w = new PdfOut(fs);
            w.Write("%PDF-1.4\n%âãÏÓ\n");

            for (int i = 0; i < n; i++)
            {
                var page = doc.Pages[i];
                int pageObj = 3 + 3 * i, contObj = pageObj + 1, imgObj = pageObj + 2;
                float wPt = page.SizeInches.Width * 72f, hPt = page.SizeInches.Height * 72f;

                byte[] jpeg; int pxW, pxH;
                using (var bmp = page.Render(dpi))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, jpegCodec, encParams);
                    jpeg = ms.ToArray();
                    pxW = bmp.Width; pxH = bmp.Height;
                }

                offsets[pageObj] = fs.Position;
                w.Write($"{pageObj} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {F(wPt)} {F(hPt)}] " +
                        $"/Resources << /XObject << /Im0 {imgObj} 0 R >> /ProcSet [/PDF /ImageC] >> " +
                        $"/Contents {contObj} 0 R >>\nendobj\n");

                string content = $"q\n{F(wPt)} 0 0 {F(hPt)} 0 0 cm\n/Im0 Do\nQ\n";
                offsets[contObj] = fs.Position;
                w.Write($"{contObj} 0 obj\n<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream\nendobj\n");

                offsets[imgObj] = fs.Position;
                w.Write($"{imgObj} 0 obj\n<< /Type /XObject /Subtype /Image /Width {pxW} /Height {pxH} " +
                        $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n");
                fs.Write(jpeg);
                w.Write("\nendstream\nendobj\n");

                progress?.Report((i + 1, n));
            }

            offsets[1] = fs.Position;
            w.Write("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            offsets[2] = fs.Position;
            var kids = new StringBuilder();
            for (int i = 0; i < n; i++) kids.Append(3 + 3 * i).Append(" 0 R ");
            w.Write($"2 0 obj\n<< /Type /Pages /Kids [{kids.ToString().TrimEnd()}] /Count {n} >>\nendobj\n");

            int infoObj = offsets.Length;
            long infoOffset = fs.Position;
            string title = PdfString(Path.GetFileNameWithoutExtension(doc.FilePath));
            string date = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            w.Write($"{infoObj} 0 obj\n<< /Title {title} /Producer (MdiViewer) /CreationDate (D:{date}) >>\nendobj\n");

            long xref = fs.Position;
            int count = infoObj + 1;
            var sb = new StringBuilder();
            sb.Append($"xref\n0 {count}\n0000000000 65535 f \n");
            for (int i = 1; i < offsets.Length; i++) sb.Append($"{offsets[i]:D10} 00000 n \n");
            sb.Append($"{infoOffset:D10} 00000 n \n");
            sb.Append($"trailer\n<< /Size {count} /Root 1 0 R /Info {infoObj} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            w.Write(sb.ToString());
        }

        File.Move(tmp, full, overwrite: true);
    }

    private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Stringa PDF in UTF-16BE esadecimale, per i nomi con caratteri accentati.</summary>
    private static string PdfString(string s)
    {
        var sb = new StringBuilder("<FEFF");
        foreach (byte b in Encoding.BigEndianUnicode.GetBytes(s)) sb.Append(b.ToString("X2"));
        return sb.Append('>').ToString();
    }

    private sealed class PdfOut
    {
        private readonly Stream _s;
        public PdfOut(Stream s) => _s = s;
        public void Write(string text) => _s.Write(Encoding.Latin1.GetBytes(text));
    }
}
