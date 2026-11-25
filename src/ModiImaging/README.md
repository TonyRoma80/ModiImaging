# ModiImaging

Lettura dei file **Microsoft Document Imaging (.mdi)** ed estrazione del testo in .NET 8,
senza Microsoft Office e senza dipendenze.

```csharp
using ModiImaging;

var mdi = MdiFile.Load(@"C:\Archivio\documento.mdi");

string testo = mdi.GetText();              // tutte le pagine, separate da '\f'

foreach (EmfPage page in mdi.Pages)
{
    Console.WriteLine($"Pagina {page.Number}: {page.WidthMm} × {page.HeightMm} mm");
    Console.WriteLine(page.GetText());     // testo ricostruito per righe

    foreach (TextRun run in page.TextRuns) // frammenti con coordinate in mm
        Console.WriteLine($"{run.X:0.0};{run.Y:0.0} {run.FontName} {run.FontSizePt:0}pt: {run.Text}");

    if (page.NeedsOcr) { /* pagina in gran parte immagine: vedi ModiImaging.Ocr */ }
}
```

## Cosa fa

- riconosce i file MDI (`MdiFile.IsMdiFile(path)`) ed estrae le pagine, che sono metafile EMF compressi con zlib;
- legge il testo direttamente dai record EMF (`EMR_EXTTEXTOUTW`, `EMR_EXTTEXTOUTA`, `EMR_SMALLTEXTOUT`):
  il risultato è esatto, non approssimato come con l'OCR;
- converte le coordinate in millimetri sulla pagina, gestendo map mode, window/viewport,
  world transform e allineamento del testo, e ricostruisce le righe;
- ripulisce i record proprietari MODI (immagini compresse con `PTC+MSHM`) che impediscono a GDI+
  di aprire la pagina: `EmfPage.Emf` è pronto per `new Metafile(...)`;
- indica quando serve l'OCR (`EmfPage.NeedsOcr`): pagina coperta da immagini con poco testo,
  oppure testo scritto come indici di glifo.

Funziona anche con i file `.emf` autonomi: `EmfPage.Load(path)`.

## Pacchetti correlati

- **ModiImaging.Drawing** (Windows): disegno delle pagine, stampa, PDF vettoriale e da immagini, PNG.
- **ModiImaging.Ocr** (Windows 10/11): OCR delle pagine con il motore integrato in Windows.

## Limiti

- Il formato MDI non è documentato: la lettura si basa sull'analisi di file reali.
- I `.mdi` creati da scanner, che contengono solo immagini con la compressione proprietaria
  Microsoft, non sono supportati (`UnsupportedFormatException`). Le singole immagini con quella
  compressione dentro pagine EMF sono sostituite da riquadri grigi (`EmfPage.UnsupportedImages`).
- Nelle pagine a più colonne `GetText()` mette sulla stessa riga i testi affiancati, separati da
  una tabulazione; per un'impaginazione precisa usare `TextRuns`.

## Note

Progetto indipendente, non affiliato né approvato da Microsoft. "Microsoft Document Imaging"
è citato solo per indicare il formato dei file che la libreria legge.

Licenza MIT.
