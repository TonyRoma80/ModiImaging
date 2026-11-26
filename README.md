# ModiImaging and MdiViewer

Tools for **Microsoft Document Imaging (.mdi)** files on Windows 10/11, **without Microsoft Office**:

| Project | Type | Contents |
|---------|------|----------|
| [`src/ModiImaging`](src/ModiImaging) | library / NuGet package, `net8.0` | reads MDI files and **extracts text** with coordinates; no dependencies, also works outside Windows |
| [`src/ModiImaging.Drawing`](src/ModiImaging.Drawing) | library / NuGet package, `net8.0-windows` | page rendering, printing, vector and image-based PDF, PNG; also opens EMF, WMF, TIFF, PNG, JPG, BMP |
| [`src/ModiImaging.Ocr`](src/ModiImaging.Ocr) | library / NuGet package, `net8.0-windows10.0.19041.0` | OCR with the engine built into Windows (offline) |
| [`src/MdiViewer`](src/MdiViewer) | WinForms application | viewer and converter (GUI and command line) |
| [`samples/MdiText`](samples/MdiText) | console sample | how to extract text, using OCR only where needed |

No third-party libraries: only .NET, `System.Drawing` and Windows APIs.

## Extracting text from C#

The pages of an `.mdi` file are EMF metafiles that store text **as text**, not as an image, so
extraction is exact and instant. OCR is only needed for pages that are scans.

```csharp
using ModiImaging;

var mdi = MdiFile.Load(path);                  // UnsupportedFormatException if unreadable
string text = mdi.GetText();                   // pages separated by '\f'

foreach (var page in mdi.Pages)
{
    string t = page.GetText();                 // text rebuilt line by line
    IReadOnlyList<TextRun> runs = page.TextRuns; // fragments with X/Y in mm, font, bold
    if (page.NeedsOcr) { /* page is mostly an image */ }
}
```

With OCR (package `ModiImaging.Ocr`; the consuming project needs a TFM such as `net8.0-windows10.0.19041.0`):

```csharp
using ModiImaging.Ocr;

string text = await PageOcr.GetTextAsync(mdi, TextSource.Auto, "en-US"); // OCR only where needed
```

To check whether a file is an MDI without loading it: `MdiFile.IsMdiFile(path)` (reads 4 bytes).

**Line reconstruction.** `GetText()` groups fragments by baseline and sorts them left to right.
Large horizontal gaps become tabs. On multi-column pages, side-by-side lines end up on the same
text line; if you need a more precise layout, work directly with `TextRuns`, which carry
coordinates and sizes.

## Installation

The libraries are NuGet packages:

```
dotnet add package ModiImaging
dotnet add package ModiImaging.Drawing   # rendering, printing, PDF, PNG (Windows)
dotnet add package ModiImaging.Ocr       # OCR (Windows 10/11)
```

The **MdiViewer** application can be downloaded ready to use from the
[Releases](https://github.com/TonyRoma80/ModiImaging/releases) page, in two forms (neither needs .NET).
Its user interface is in Italian.

- **`MdiViewer-<version>-x64.msi`** (recommended): during setup you choose
  - *just for me* (default): no administrator rights needed; the program goes to
    `%LOCALAPPDATA%\Programs\MdiViewer` and is only visible to the user who installs it;
  - *for all users*: requires administrator rights; the program goes to `C:\Program Files\MdiViewer`.

  Either way it adds a Start menu shortcut, associates `.mdi` files (double-click to open) and can be
  removed from *Settings → Apps*. Newer versions automatically replace the installed one.
- **`MdiViewer.exe`** (portable): a single file you can copy and run anywhere, no installation.

If another program already opens `.mdi` files on the PC, Windows does not allow any installer to
replace it: the user has to choose (right-click an `.mdi` file → *Open with* → *MdiViewer* →
*Always*).

Unattended installation (e.g. from a script):
`msiexec /i MdiViewer-<version>-x64.msi /qn MSIINSTALLPERUSER=1 ALLUSERS=2` (current user only) or
`msiexec /i MdiViewer-<version>-x64.msi /qn ALLUSERS=1` (all users, as administrator).

The program is not digitally signed: on first run Windows SmartScreen may show
"Windows protected your PC". To continue: *More info* → *Run anyway*.

## Building the packages

```
dotnet pack ModiImaging.sln -c Release
```

creates the `ModiImaging.*.nupkg` files in `artifacts\packages` (plus `.snupkg` files with debug symbols).
To try them in another project before publishing, add that folder as a NuGet source in a
`nuget.config` next to the consuming solution:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="ModiImaging-local" value="C:\path\to\ModiImaging\artifacts\packages" />
  </packageSources>
</configuration>
```

The version is set in [Directory.Build.props](Directory.Build.props): **bump it on every change**,
because NuGet caches packages by version number and will not reload a version it has already
downloaded (alternatively: `dotnet nuget locals global-packages --clear`).
Publishing: `dotnet nuget push artifacts\packages\*.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json`.

## MdiViewer

**Viewer**: page centred with a drop shadow on a grey background; zoom with the toolbar buttons, the
percentage box, `Ctrl`+mouse wheel or `Ctrl`+`+`/`-` (`Ctrl+1` 100%, `Ctrl+2` fit width, `Ctrl+0`
fit page); pan by dragging with the mouse; pages with `PgUp`/`PgDn`, `Ctrl+Home`/`Ctrl+End`,
`Ctrl+G`; open files by dropping them on the window or from the command line.

**Printing** (`Ctrl+P`, preview `Ctrl+Shift+P`): printer and page range selection (all, from–to,
current page), automatic orientation, page scaled to fit the sheet.

**Export** (*File* menu): vector PDF (`Ctrl+E`) through *Microsoft Print to PDF* using the original
paper size; image-based PDF at 200 dpi if that printer is missing; PNG, one per page; text
(UTF-8 `.txt`) and copy of the page text (`Ctrl+Shift+C`); whole-folder conversion.

### Command line

```
MdiViewer.exe [file]                                   opens the viewer
MdiViewer.exe file.mdi file.pdf [--raster]             converts to PDF
MdiViewer.exe --convert <file or folder> [--raster] [--recursive]
MdiViewer.exe --text <file> [file.txt]                 extracts the text (to the console or a file)
MdiViewer.exe --text <folder> [--recursive]            a .txt next to every .mdi
```

`--convert` saves each PDF next to the original (overwriting existing ones); `--raster` produces an
image-based PDF (used automatically when *Microsoft Print to PDF* is missing). Exit codes: `0` success,
`1` at least one error, `2` invalid arguments. MdiViewer is a GUI application: in batch scripts use
`start "" /wait MdiViewer.exe ...`, in PowerShell `Start-Process ... -Wait`.

## The MDI format

Microsoft never documented the format. What the library relies on:

- files start with the 4 bytes `EP*\0` (`45 50 2A 00`);
- they contain one or more zlib streams (in the files examined the first one starts at offset `0x24`);
- each decompressed stream is an **EMF** page: an `EMR_HEADER` record (type 1), the `" EMF"` signature
  at offset 40, the total size `nBytes` at offset 48;
- the page size is `rclFrame` (offsets 24–39, in hundredths of a millimetre).

[MdiFile.cs](src/ModiImaging/MdiFile.cs) scans the file for valid zlib headers (`0x78` + FLG with
`(CMF*256+FLG) % 31 == 0`), inflates 88 bytes to check the EMF header and then inflates the whole page.

**Images with proprietary compression.** MODI may insert a non-standard record into the EMF (type
123, size not a multiple of 4) that wraps a compressed original EMF record, usually an
`EMR_STRETCHDIBITS` (a bitmap, for example a bank logo on a printed web page):

| Offset | Contents |
|--------|----------|
| 8 | type of the original record (81 = `EMR_STRETCHDIBITS`) |
| 12 | compression type |
| 16 / 20 | offset / length of the uncompressed original header (80 bytes) |
| 24 / 28 | offset / length of the compressed data (starts with `PTC+MSHM`) |

GDI+ rejects the whole EMF when it meets this record. [EmfSanitizer.cs](src/ModiImaging/Emf/EmfSanitizer.cs)
rebuilds the original record if the data is zlib/deflate; otherwise it replaces the bitmap with a
**grey box** of the same size. Text is not affected. The viewer reports it in the status bar, the
command line with a warning, and the library through `EmfPage.UnsupportedImages`.

**Text.** [EmfTextExtractor.cs](src/ModiImaging/Emf/EmfTextExtractor.cs) reads `EMR_EXTTEXTOUTW`,
`EMR_EXTTEXTOUTA` and `EMR_SMALLTEXTOUT` and converts logical coordinates to millimetres by tracking
map mode, window/viewport, world transform, `SaveDC`/`RestoreDC`, font and text alignment. Text written
as glyph indices (`ETO_GLYPH_INDEX`) cannot be decoded without the font: it is counted in
`UndecodableTextRuns` and sets `NeedsOcr`.

`.mdi` files that contain no EMF at all (scanner output with proprietary compression only) are not
supported (`UnsupportedFormatException`).

Independent project, not affiliated with or endorsed by Microsoft. [MIT](LICENSE) license.

## Building

Requires the **.NET 8** SDK or later: <https://dotnet.microsoft.com/download/dotnet/8.0>.

```
git clone https://github.com/TonyRoma80/ModiImaging.git
cd ModiImaging
dotnet build ModiImaging.sln -c Release
```

### MdiViewer as a single .exe

Self-contained (recommended, about 63 MB, includes .NET, runs on any 64-bit Windows 10/11):

```
dotnet publish src\MdiViewer\MdiViewer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Only `publish\MdiViewer.exe` needs to be distributed (the `.pdb` and `.xml` files next to it are not needed).
Framework-dependent (about 250 KB, requires the *.NET Desktop Runtime 8*): same command with
`--self-contained false` and without `-p:EnableCompressionInSingleFile=true`. For Windows on ARM: `win-arm64`.

### MSI installer

```
dotnet build installer\MdiViewer.Installer.wixproj -c Release
```

Builds MdiViewer as a self-contained exe and packages it with [WiX Toolset](https://wixtoolset.org) 5
(downloaded automatically from NuGet during the build) into
`installer\bin\Release\MdiViewer-<version>-x64.msi`, with an Italian user interface.
The version comes from [Directory.Build.props](Directory.Build.props). Never change the `UpgradeCode` in
[Package.wxs](installer/Package.wxs): Windows uses it to recognise upgrades.

## Requirements on the target PC

- Windows 10 or 11. Microsoft Office is **not** required.
- Vector PDF: the *Microsoft Print to PDF* printer (built in; it can be reinstalled from *Turn Windows
  features on or off*). Without it, an image-based PDF is produced.
- Printing and preview: at least one installed printer (the PDF printer is fine).
- OCR: the desired language installed in Windows with the *Optical character recognition* component
  (`PageOcr.AvailableLanguages`).
