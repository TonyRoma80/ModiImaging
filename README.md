# ModiImaging e MdiViewer

Strumenti per i file **Microsoft Document Imaging (.mdi)** su Windows 10/11, **senza Microsoft Office**:

| Progetto | Tipo | Contenuto |
|----------|------|-----------|
| [`src/ModiImaging`](src/ModiImaging) | libreria / pacchetto NuGet, `net8.0` | lettura degli MDI ed **estrazione del testo** con coordinate; nessuna dipendenza, funziona anche fuori da Windows |
| [`src/ModiImaging.Drawing`](src/ModiImaging.Drawing) | libreria / pacchetto NuGet, `net8.0-windows` | disegno delle pagine, stampa, PDF vettoriale e da immagini, PNG; apre anche EMF, WMF, TIFF, PNG, JPG, BMP |
| [`src/ModiImaging.Ocr`](src/ModiImaging.Ocr) | libreria / pacchetto NuGet, `net8.0-windows10.0.19041.0` | OCR con il motore integrato in Windows (offline) |
| [`src/MdiViewer`](src/MdiViewer) | applicazione WinForms | visualizzatore e convertitore (finestra e riga di comando) |
| [`samples/MdiText`](samples/MdiText) | esempio console | come estrarre il testo, con OCR dove serve |

Nessuna libreria esterna: solo .NET, `System.Drawing` e le API di Windows.

## Estrarre il testo da un programma C#

Le pagine dei `.mdi` sono metafile EMF che contengono il testo **come testo**, non come immagine: la
lettura è esatta e istantanea e l'OCR serve solo per le pagine che sono scansioni.

```csharp
using ModiImaging;

var mdi = MdiFile.Load(path);                  // UnsupportedFormatException se non leggibile
string testo = mdi.GetText();                  // pagine separate da '\f'

foreach (var page in mdi.Pages)
{
    string t = page.GetText();                 // testo ricostruito per righe
    IReadOnlyList<TextRun> runs = page.TextRuns; // frammenti con X/Y in mm, font, grassetto
    if (page.NeedsOcr) { /* pagina in gran parte immagine */ }
}
```

Con l'OCR (pacchetto `ModiImaging.Ocr`, il progetto deve avere un TFM come `net8.0-windows10.0.19041.0`):

```csharp
using ModiImaging.Ocr;

string testo = await PageOcr.GetTextAsync(mdi, TextSource.Auto, "it-IT"); // OCR solo dove serve
```

Per capire se un file è un MDI senza caricarlo: `MdiFile.IsMdiFile(path)` (legge 4 byte).

**Ricostruzione delle righe.** `GetText()` raggruppa i frammenti per linea di base e li ordina da
sinistra a destra. Le distanze orizzontali grandi diventano tabulazioni. Nelle pagine a più colonne
le righe affiancate finiscono sulla stessa riga di testo: chi ha bisogno di un layout più preciso
può lavorare direttamente su `TextRuns`, che hanno coordinate e dimensioni.

## Installazione

Le librerie sono pacchetti NuGet:

```
dotnet add package ModiImaging
dotnet add package ModiImaging.Drawing   # rendering, stampa, PDF, PNG (Windows)
dotnet add package ModiImaging.Ocr       # OCR (Windows 10/11)
```

Il visualizzatore **MdiViewer** si scarica già compilato dalla pagina
[Releases](https://github.com/TonyRoma80/ModiImaging/releases), in due forme (nessuna richiede .NET):

- **`MdiViewer-<versione>-x64.msi`** (consigliato): durante l'installazione si sceglie
  - *solo per me* (predefinito): nessun permesso di amministratore, il programma va in
    `%LOCALAPPDATA%\Programs\MdiViewer` ed è visibile solo all'utente che lo installa;
  - *per tutti gli utenti*: richiede i permessi di amministratore, il programma va in
    `C:\Program Files\MdiViewer`.

  In entrambi i casi aggiunge il collegamento nel menu Start, associa i file `.mdi` (doppio clic)
  e si disinstalla da *Impostazioni → App*.
  Le versioni nuove sostituiscono automaticamente quella installata nello stesso modo.
- **`MdiViewer.exe`** portatile: un unico file da copiare ed eseguire dove si vuole, senza installazione.

Se sul PC un altro programma apre già i `.mdi`, Windows non permette a nessun installer di
sostituirlo: la scelta spetta all'utente (clic destro su un file `.mdi` → *Apri con* → *MdiViewer*
→ *Usa sempre*).

Installazione senza finestre (ad esempio da script):
`msiexec /i MdiViewer-<versione>-x64.msi /qn MSIINSTALLPERUSER=1 ALLUSERS=2` (solo per l'utente) oppure
`msiexec /i MdiViewer-<versione>-x64.msi /qn ALLUSERS=1` (per tutti, da amministratore).

Il programma non è firmato digitalmente: al primo avvio Windows SmartScreen può mostrare
"Windows ha protetto il PC". Per proseguire: *Ulteriori informazioni* → *Esegui comunque*.

## Creare i pacchetti

```
dotnet pack ModiImaging.sln -c Release
```

crea in `artifacts\packages` i file `ModiImaging.*.nupkg` (più i `.snupkg` con i simboli per il debug).
Per provarli in un altro progetto prima di pubblicarli, indicare quella cartella come origine NuGet
in un `nuget.config` accanto alla soluzione che li usa:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="ModiImaging-locale" value="C:\percorso\di\ModiImaging\artifacts\packages" />
  </packageSources>
</configuration>
```

La versione è in [Directory.Build.props](Directory.Build.props): **aumentarla a ogni modifica**,
perché NuGet tiene in cache i pacchetti per numero di versione e non rilegge un pacchetto con
una versione già scaricata (in alternativa: `dotnet nuget locals global-packages --clear`).
Pubblicazione: `dotnet nuget push artifacts\packages\*.nupkg --api-key <chiave> --source https://api.nuget.org/v3/index.json`.

## MdiViewer

**Visualizzatore**: pagina centrata con ombra su sfondo grigio; zoom con i pulsanti, la casella
della percentuale, `Ctrl`+rotellina o `Ctrl`+`+`/`-` (`Ctrl+1` 100%, `Ctrl+2` adatta alla larghezza,
`Ctrl+0` adatta alla pagina); spostamento trascinando con il mouse; pagine con `PagSu`/`PagGiù`,
`Ctrl+Home`/`Ctrl+Fine`, `Ctrl+G`; apertura trascinando il file sulla finestra o da riga di comando.

**Stampa** (`Ctrl+P`, anteprima `Ctrl+Maiusc+P`): scelta della stampante e dell'intervallo (tutte,
da-a, pagina corrente), orientamento automatico, pagina adattata al foglio.

**Esportazione** (menu File): PDF vettoriale (`Ctrl+E`) tramite *Microsoft Print to PDF* con il formato
carta dell'originale; PDF da immagini a 200 dpi se quella stampante manca; PNG una per pagina;
testo (`.txt` UTF-8) e copia del testo della pagina (`Ctrl+Maiusc+C`); conversione di una cartella.

### Riga di comando

```
MdiViewer.exe [file]                                   apre il visualizzatore
MdiViewer.exe file.mdi file.pdf [--raster]             converte in PDF
MdiViewer.exe --convert <file o cartella> [--raster] [--recursive]
MdiViewer.exe --text <file> [file.txt]                 estrae il testo (a video o in un file)
MdiViewer.exe --text <cartella> [--recursive]          un .txt accanto a ogni .mdi
```

`--convert` salva ogni PDF accanto all'originale (sovrascrivendo quelli esistenti); `--raster` produce
il PDF da immagini (automatico se *Microsoft Print to PDF* manca). Codici di uscita: `0` ok, `1`
almeno un errore, `2` argomenti non validi. MdiViewer è un'applicazione grafica: negli script usare
`start "" /wait MdiViewer.exe ...`, in PowerShell `Start-Process ... -Wait`.

## Formato MDI

Il formato non è documentato da Microsoft. Quello che la libreria assume:

- i file iniziano con i 4 byte `EP*\0` (`45 50 2A 00`);
- contengono uno o più stream zlib (nei file esaminati il primo parte all'offset `0x24`);
- ogni stream decompresso è una pagina **EMF**: record `EMR_HEADER` (tipo 1), firma `" EMF"`
  all'offset 40, dimensione totale `nBytes` all'offset 48;
- la dimensione della pagina è `rclFrame` (offset 24-39, centesimi di millimetro).

[MdiFile.cs](src/ModiImaging/MdiFile.cs) scorre il file cercando intestazioni zlib valide (`0x78` + FLG
con `(CMF*256+FLG) % 31 == 0`), decomprime 88 byte per verificare l'header EMF e poi l'intera pagina.

**Immagini con compressione proprietaria.** MODI può inserire nell'EMF un record non standard (tipo
123, dimensione non multipla di 4) che avvolge un record EMF originale compresso, di solito un
`EMR_STRETCHDIBITS` (una bitmap, ad esempio il logo di una banca in una pagina web stampata):

| Offset | Contenuto |
|--------|-----------|
| 8 | tipo del record originale (81 = `EMR_STRETCHDIBITS`) |
| 12 | tipo di compressione |
| 16 / 20 | offset / lunghezza dell'intestazione originale non compressa (80 byte) |
| 24 / 28 | offset / lunghezza dei dati compressi (iniziano con `PTC+MSHM`) |

GDI+ rifiuta l'intero EMF se trova questo record: [EmfSanitizer.cs](src/ModiImaging/Emf/EmfSanitizer.cs)
ricostruisce il record originale se i dati sono zlib/deflate, altrimenti sostituisce la bitmap con un
**riquadro grigio** delle stesse dimensioni. Il testo non è interessato. Il visualizzatore lo segnala
nella barra di stato, la riga di comando con "ATTENZIONE: ...", la libreria con `EmfPage.UnsupportedImages`.

**Testo.** [EmfTextExtractor.cs](src/ModiImaging/Emf/EmfTextExtractor.cs) legge `EMR_EXTTEXTOUTW`,
`EMR_EXTTEXTOUTA` e `EMR_SMALLTEXTOUT` e converte le coordinate logiche in millimetri ricostruendo
map mode, window/viewport, world transform, `SaveDC`/`RestoreDC`, font e allineamento. Il testo scritto
come indici di glifo (`ETO_GLYPH_INDEX`) non è decodificabile senza il font: viene contato in
`UndecodableTextRuns` e fa scattare `NeedsOcr`.

I `.mdi` che non contengono alcun EMF non sono supportati (`UnsupportedFormatException`).

Progetto indipendente, non affiliato né approvato da Microsoft. Licenza [MIT](LICENSE).

## Compilazione

Serve l'SDK di **.NET 8** o successivo: <https://dotnet.microsoft.com/download/dotnet/8.0>.

```
git clone https://github.com/TonyRoma80/ModiImaging.git
cd ModiImaging
dotnet build ModiImaging.sln -c Release
```

### MdiViewer in un singolo .exe

Autonomo (consigliato, circa 63 MB, include .NET, gira su qualsiasi Windows 10/11 a 64 bit):

```
dotnet publish src\MdiViewer\MdiViewer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Si distribuisce solo `publish\MdiViewer.exe` (i `.pdb` e `.xml` accanto non servono).

### Installer MSI

```
dotnet build installer\MdiViewer.Installer.wixproj -c Release
```

Compila MdiViewer come exe autonomo e lo impacchetta con [WiX Toolset](https://wixtoolset.org) 5
(scaricato automaticamente da NuGet durante la compilazione) in
`installer\bin\Release\MdiViewer-<versione>-x64.msi`, con interfaccia in italiano.
La versione è quella di [Directory.Build.props](Directory.Build.props). L'`UpgradeCode` in
[Package.wxs](installer/Package.wxs) non va mai cambiato: serve a Windows per riconoscere gli aggiornamenti.
Dipendente dal framework (circa 250 KB, richiede il *.NET Desktop Runtime 8*): stesso comando con
`--self-contained false` e senza `-p:EnableCompressionInSingleFile=true`. Per Windows su ARM: `win-arm64`.

## Requisiti sul PC di destinazione

- Windows 10 o 11. Microsoft Office **non** serve.
- PDF vettoriale: stampante *Microsoft Print to PDF* (di serie; si reinstalla da *Attiva o disattiva
  funzionalità di Windows*). Senza, si usa il PDF da immagini.
- Stampa e anteprima: almeno una stampante installata (va bene quella PDF).
- OCR: la lingua desiderata installata in Windows con il componente *Riconoscimento ottico dei
  caratteri* (`PageOcr.AvailableLanguages`); su Windows in italiano `it-IT` di solito c'è già.
