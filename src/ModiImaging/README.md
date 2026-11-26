# ModiImaging

Read **Microsoft Document Imaging (.mdi)** files and extract their text in .NET 8,
without Microsoft Office and without dependencies.

```csharp
using ModiImaging;

var mdi = MdiFile.Load(@"C:\Archive\document.mdi");

string text = mdi.GetText();               // all pages, separated by '\f'

foreach (EmfPage page in mdi.Pages)
{
    Console.WriteLine($"Page {page.Number}: {page.WidthMm} × {page.HeightMm} mm");
    Console.WriteLine(page.GetText());     // text rebuilt line by line

    foreach (TextRun run in page.TextRuns) // fragments with coordinates in mm
        Console.WriteLine($"{run.X:0.0};{run.Y:0.0} {run.FontName} {run.FontSizePt:0}pt: {run.Text}");

    if (page.NeedsOcr) { /* page is mostly an image: see ModiImaging.Ocr */ }
}
```

## What it does

- recognises MDI files (`MdiFile.IsMdiFile(path)`) and extracts their pages, which are zlib-compressed EMF metafiles;
- reads the text directly from the EMF records (`EMR_EXTTEXTOUTW`, `EMR_EXTTEXTOUTA`, `EMR_SMALLTEXTOUT`):
  the result is exact, not an approximation as with OCR;
- converts coordinates to millimetres on the page, handling map mode, window/viewport,
  world transform and text alignment, and rebuilds the lines;
- cleans up the proprietary MODI records (images compressed with `PTC+MSHM`) that stop GDI+
  from opening the page: `EmfPage.Emf` is ready for `new Metafile(...)`;
- tells you when OCR is needed (`EmfPage.NeedsOcr`): a page covered by images with little text,
  or text written as glyph indices.

It also works with standalone `.emf` files: `EmfPage.Load(path)`.

## Related packages

- **ModiImaging.Drawing** (Windows): page rendering, printing, vector and image-based PDF, PNG.
- **ModiImaging.Ocr** (Windows 10/11): page OCR with the engine built into Windows.

## Limitations

- The MDI format is undocumented: reading is based on the analysis of real files.
- `.mdi` files produced by scanners, which contain only images with Microsoft's proprietary
  compression, are not supported (`UnsupportedFormatException`). Individual images with that
  compression inside EMF pages are replaced by grey boxes (`EmfPage.UnsupportedImages`).
- On multi-column pages `GetText()` puts side-by-side text on the same line, separated by a tab;
  for a precise layout use `TextRuns`.
- Exception messages are in Italian.

## Notes

Independent project, not affiliated with or endorsed by Microsoft. "Microsoft Document Imaging"
is mentioned only to identify the file format the library reads.

Source code and documentation: <https://github.com/TonyRoma80/ModiImaging>. MIT license.
