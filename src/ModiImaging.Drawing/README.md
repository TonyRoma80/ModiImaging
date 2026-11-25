# ModiImaging.Drawing

Disegno, stampa ed esportazione di documenti **MDI**, EMF, WMF, TIFF multipagina, PNG, JPG e BMP
su Windows. Si basa su `System.Drawing`, senza librerie esterne. Richiede il pacchetto `ModiImaging`.

```csharp
using ModiImaging.Drawing;

using var doc = Document.Load(@"C:\Archivio\documento.mdi");

// PDF vettoriale tramite la stampante "Microsoft Print to PDF" di Windows
if (PdfExporter.IsPdfPrinterAvailable())
    PdfExporter.ExportVector(doc, @"C:\Archivio\documento.pdf");
else
    RasterPdfWriter.Export(doc, @"C:\Archivio\documento.pdf", dpi: 200);

// Una PNG per pagina
PngExporter.Export(doc, @"C:\Archivio\documento.png", dpi: 300);

// Bitmap di una pagina (ad esempio per un OCR esterno)
using Bitmap bmp = doc.Pages[0].Render(300);

// Testo (solo pagine EMF/MDI)
string testo = doc.GetText();
```

Per la stampa con finestra di dialogo e anteprima, `DocumentPrinter` è un `PrintDocument` che
gestisce l'intervallo di pagine, l'orientamento automatico e l'adattamento al foglio.

`Converter.ConvertToPdf` e `Converter.ConvertBatch` convertono uno o più file in PDF, usando il
PDF da immagini se *Microsoft Print to PDF* non è installata.
