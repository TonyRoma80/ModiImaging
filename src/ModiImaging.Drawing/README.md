# ModiImaging.Drawing

Rendering, printing and export of **MDI**, EMF, WMF, multi-page TIFF, PNG, JPG and BMP documents
on Windows. Built on `System.Drawing`, with no third-party libraries. Requires the `ModiImaging` package.

```csharp
using ModiImaging.Drawing;

using var doc = Document.Load(@"C:\Archive\document.mdi");

// Vector PDF through the Windows "Microsoft Print to PDF" printer
if (PdfExporter.IsPdfPrinterAvailable())
    PdfExporter.ExportVector(doc, @"C:\Archive\document.pdf");
else
    RasterPdfWriter.Export(doc, @"C:\Archive\document.pdf", dpi: 200);

// One PNG per page
PngExporter.Export(doc, @"C:\Archive\document.png", dpi: 300);

// Bitmap of a page (for example for an external OCR engine)
using Bitmap bmp = doc.Pages[0].Render(300);

// Text (EMF/MDI pages only)
string text = doc.GetText();
```

For printing with a dialog and preview, `DocumentPrinter` is a `PrintDocument` that handles the
page range, automatic orientation and scaling to the sheet.

`Converter.ConvertToPdf` and `Converter.ConvertBatch` convert one or more files to PDF, falling back
to an image-based PDF when *Microsoft Print to PDF* is not installed.

Source code and documentation: <https://github.com/TonyRoma80/ModiImaging>. MIT license.
