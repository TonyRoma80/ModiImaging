# ModiImaging.Ocr

OCR delle pagine **MDI**, EMF, TIFF e immagini con il motore OCR integrato in **Windows 10/11**
(`Windows.Media.Ocr`): funziona offline, senza librerie esterne e senza servizi online.
Richiede i pacchetti `ModiImaging` e `ModiImaging.Drawing`.

```csharp
using ModiImaging;
using ModiImaging.Ocr;

var mdi = MdiFile.Load(@"C:\Archivio\documento.mdi");

// Testo nativo dove c'è, OCR solo per le pagine che sembrano scansioni
string testo = await PageOcr.GetTextAsync(mdi, TextSource.Auto, language: "it-IT");

// OCR esplicito di una pagina, con le righe e le loro coordinate in mm
OcrPageResult r = await PageOcr.RecognizeAsync(mdi.Pages[0], "it-IT");
foreach (var line in r.Lines) Console.WriteLine($"{line.Y:0.0} mm: {line.Text}");
```

Funziona anche con le pagine di `ModiImaging.Drawing.Document` (TIFF, PNG, JPG...) e con qualsiasi `Bitmap`.

Le lingue disponibili sono quelle installate in Windows con il componente *Riconoscimento ottico dei
caratteri* (`PageOcr.AvailableLanguages`). Su un Windows in italiano `it-IT` è normalmente già presente.

Il progetto che usa questo pacchetto deve avere un TFM Windows con versione, ad esempio
`net8.0-windows10.0.19041.0`.
