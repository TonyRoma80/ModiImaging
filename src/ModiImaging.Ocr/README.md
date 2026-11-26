# ModiImaging.Ocr

OCR for **MDI**, EMF, TIFF and image pages using the OCR engine built into **Windows 10/11**
(`Windows.Media.Ocr`): it works offline, with no third-party libraries and no online services.
Requires the `ModiImaging` and `ModiImaging.Drawing` packages.

```csharp
using ModiImaging;
using ModiImaging.Ocr;

var mdi = MdiFile.Load(@"C:\Archive\document.mdi");

// Native text where available, OCR only for pages that look like scans
string text = await PageOcr.GetTextAsync(mdi, TextSource.Auto, language: "en-US");

// Explicit OCR of a page, with lines and their coordinates in mm
OcrPageResult r = await PageOcr.RecognizeAsync(mdi.Pages[0], "en-US");
foreach (var line in r.Lines) Console.WriteLine($"{line.Y:0.0} mm: {line.Text}");
```

It also works with the pages of `ModiImaging.Drawing.Document` (TIFF, PNG, JPG...) and with any `Bitmap`.

The available languages are those installed in Windows with the *Optical character recognition*
component (`PageOcr.AvailableLanguages`). Pass `language: null` to use the user's profile languages.

The consuming project needs a versioned Windows TFM, for example `net8.0-windows10.0.19041.0`.

Source code and documentation: <https://github.com/TonyRoma80/ModiImaging>. MIT license.
