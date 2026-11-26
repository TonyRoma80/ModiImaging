using ModiImaging.Drawing;
using System.Drawing.Printing;
using System.Globalization;

namespace MdiViewer.UI;

public sealed class MainForm : Form
{
    private const string AppName = "MdiViewer";

    private readonly PageView _view = new() { Dock = DockStyle.Fill };
    private readonly MenuStrip _menu = new();
    private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
    private readonly StatusStrip _status = new();

    private readonly ToolStripStatusLabel _stFile = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _stWarn = new()
    {
        BorderSides = ToolStripStatusLabelBorderSides.Left, ForeColor = Color.FromArgb(0xB0, 0x50, 0x00), Visible = false,
    };
    private readonly ToolStripStatusLabel _stSize = new() { BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ToolStripStatusLabel _stPage = new() { BorderSides = ToolStripStatusLabelBorderSides.Left };
    private readonly ToolStripStatusLabel _stZoom = new() { BorderSides = ToolStripStatusLabelBorderSides.Left };

    private readonly ToolStripLabel _tbPage = new("Pagina 0 di 0");
    private readonly ToolStripComboBox _tbZoom = new() { AutoSize = false, Width = 150 };

    private readonly List<ToolStripItem> _needDoc = new();
    private readonly List<ToolStripItem> _needMultiPage = new();

    private Document? _doc;
    private int _pageIndex;
    private bool _busy;

    public MainForm(string? initialFile)
    {
        Text = AppName;
        Width = 1100;
        Height = 850;
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;
        KeyPreview = true;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        BuildMenu();
        BuildToolbar();
        _status.Items.AddRange(new ToolStripItem[] { _stFile, _stWarn, _stSize, _stPage, _stZoom });

        Controls.Add(_view);
        Controls.Add(_tools);
        Controls.Add(_menu);
        Controls.Add(_status);
        MainMenuStrip = _menu;

        _view.AllowDrop = true;
        _view.DragEnter += OnDragEnter;
        _view.DragDrop += OnDragDrop;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
        _view.ZoomChanged += (_, _) => UpdateZoomUi();
        _view.PageScrollRequested += (_, dir) => GoToPage(_pageIndex + dir, fromWheel: dir);

        UpdateUi();
        Shown += (_, _) =>
        {
            _view.Focus();
            if (!string.IsNullOrEmpty(initialFile)) OpenFile(initialFile);
        };
    }

    // ---------------------------------------------------------------- costruzione interfaccia

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(Item("&Apri...", OpenDialog, Keys.Control | Keys.O));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Doc(Item("&Stampa...", PrintDialogShow, Keys.Control | Keys.P)));
        file.DropDownItems.Add(Doc(Item("&Anteprima di stampa...", PrintPreviewShow, Keys.Control | Keys.Shift | Keys.P)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Doc(Item("Esporta in &PDF (vettoriale)...", () => ExportPdf(false), Keys.Control | Keys.E)));
        file.DropDownItems.Add(Doc(Item("Esporta in PDF da &immagini (200 dpi)...", () => ExportPdf(true))));
        file.DropDownItems.Add(Doc(Item("Esporta in P&NG...", ExportPng)));
        file.DropDownItems.Add(Doc(Item("Esporta &testo...", ExportText)));
        file.DropDownItems.Add(Doc(Item("C&opia testo", CopyText, Keys.Control | Keys.Shift | Keys.C)));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Converti cartella in PDF...", ConvertFolder));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Doc(Item("C&hiudi", CloseDocument, Keys.Control | Keys.W)));
        file.DropDownItems.Add(Item("&Esci", Close, Keys.Alt | Keys.F4));

        var view = new ToolStripMenuItem("&Visualizza");
        view.DropDownItems.Add(Doc(Item("Zoom &avanti", _view.ZoomIn, Keys.Control | Keys.Oemplus, "Ctrl++")));
        view.DropDownItems.Add(Doc(Item("Zoom &indietro", _view.ZoomOut, Keys.Control | Keys.OemMinus, "Ctrl+-")));
        view.DropDownItems.Add(Doc(Item("Dimensioni &reali (100%)", () => _view.SetZoom(1f), Keys.Control | Keys.D1)));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(Doc(Item("Adatta alla &larghezza", () => _view.ZoomMode = ZoomMode.FitWidth, Keys.Control | Keys.D2)));
        view.DropDownItems.Add(Doc(Item("Adatta alla &pagina", () => _view.ZoomMode = ZoomMode.FitPage, Keys.Control | Keys.D0)));

        var page = new ToolStripMenuItem("&Pagina");
        page.DropDownItems.Add(Multi(Item("&Prima pagina", () => GoToPage(0), Keys.None, "Ctrl+Home")));
        page.DropDownItems.Add(Multi(Item("Pagina p&recedente", () => GoToPage(_pageIndex - 1), Keys.None, "PagSu")));
        page.DropDownItems.Add(Multi(Item("Pagina &successiva", () => GoToPage(_pageIndex + 1), Keys.None, "PagGiù")));
        page.DropDownItems.Add(Multi(Item("&Ultima pagina", () => GoToPage(int.MaxValue), Keys.None, "Ctrl+Fine")));
        page.DropDownItems.Add(new ToolStripSeparator());
        page.DropDownItems.Add(Multi(Item("&Vai alla pagina...", GoToPageDialog, Keys.Control | Keys.G)));

        var help = new ToolStripMenuItem("&?");
        help.DropDownItems.Add(Item("Apri i file .mdi con &MdiViewer...", () => FileAssociation.MakeDefault(this)));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Item("&Riga di comando...", ShowCommandLineHelp));
        help.DropDownItems.Add(Item("&Informazioni su MdiViewer...", ShowAbout));

        _menu.Items.AddRange(new ToolStripItem[] { file, view, page, help });
    }

    private void BuildToolbar()
    {
        _tools.Items.Add(Button("📂 Apri", "Apri un file (Ctrl+O)", OpenDialog));
        _tools.Items.Add(Doc(Button("🖨 Stampa", "Stampa (Ctrl+P)", PrintDialogShow)));
        _tools.Items.Add(Doc(Button("Anteprima", "Anteprima di stampa (Ctrl+Maiusc+P)", PrintPreviewShow)));
        _tools.Items.Add(Doc(Button("Esporta PDF", "Esporta in PDF vettoriale (Ctrl+E)", () => ExportPdf(false))));
        _tools.Items.Add(new ToolStripSeparator());
        _tools.Items.Add(Multi(Button("⏮", "Prima pagina (Ctrl+Home)", () => GoToPage(0))));
        _tools.Items.Add(Multi(Button("◀", "Pagina precedente (PagSu)", () => GoToPage(_pageIndex - 1))));
        _tools.Items.Add(_tbPage);
        _tools.Items.Add(Multi(Button("▶", "Pagina successiva (PagGiù)", () => GoToPage(_pageIndex + 1))));
        _tools.Items.Add(Multi(Button("⏭", "Ultima pagina (Ctrl+End)", () => GoToPage(int.MaxValue))));
        _tools.Items.Add(new ToolStripSeparator());
        _tools.Items.Add(Doc(Button("−", "Zoom indietro (Ctrl+-)", _view.ZoomOut)));
        _tools.Items.Add(Doc(_tbZoom));
        _tools.Items.Add(Doc(Button("+", "Zoom avanti (Ctrl++)", _view.ZoomIn)));
        _tools.Items.Add(Doc(Button("↔ Larghezza", "Adatta alla larghezza (Ctrl+2)", () => _view.ZoomMode = ZoomMode.FitWidth)));
        _tools.Items.Add(Doc(Button("▣ Pagina", "Adatta alla pagina (Ctrl+0)", () => _view.ZoomMode = ZoomMode.FitPage)));

        _tbZoom.Items.AddRange(new object[]
            { "25%", "50%", "75%", "100%", "125%", "150%", "200%", "300%", "400%", "Adatta alla larghezza", "Adatta alla pagina" });
        _tbZoom.ToolTipText = "Zoom (Ctrl+rotellina)";
        _tbZoom.SelectedIndexChanged += (_, _) => ApplyZoomText(_tbZoom.Text);
        _tbZoom.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { ApplyZoomText(_tbZoom.Text); _view.Focus(); e.SuppressKeyPress = true; }
        };
    }

    private ToolStripMenuItem Item(string text, Action action, Keys keys = Keys.None, string? keyText = null)
    {
        var item = new ToolStripMenuItem(text);
        item.Click += (_, _) => action();
        if (keys != Keys.None) item.ShortcutKeys = keys;
        if (keyText != null) item.ShortcutKeyDisplayString = keyText;
        return item;
    }

    private static ToolStripButton Button(string text, string tip, Action action)
    {
        var b = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = tip };
        b.Click += (_, _) => action();
        return b;
    }

    private T Doc<T>(T item) where T : ToolStripItem { _needDoc.Add(item); return item; }
    private T Multi<T>(T item) where T : ToolStripItem { _needMultiPage.Add(item); return item; }

    // ---------------------------------------------------------------- tastiera

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_tbZoom.Focused) return base.ProcessCmdKey(ref msg, keyData);
        switch (keyData)
        {
            case Keys.PageDown: GoToPage(_pageIndex + 1); return true;
            case Keys.PageUp: GoToPage(_pageIndex - 1); return true;
            case Keys.Control | Keys.Home: GoToPage(0); return true;
            case Keys.Control | Keys.End: GoToPage(int.MaxValue); return true;
            case Keys.Control | Keys.Add: _view.ZoomIn(); return true;
            case Keys.Control | Keys.Subtract: _view.ZoomOut(); return true;
            case Keys.Control | Keys.NumPad0: _view.ZoomMode = ZoomMode.FitPage; return true;
            case Keys.Down: _view.ScrollBy(0, 60); return true;
            case Keys.Up: _view.ScrollBy(0, -60); return true;
            case Keys.Right: _view.ScrollBy(60, 0); return true;
            case Keys.Left: _view.ScrollBy(-60, 0); return true;
            case Keys.Home: _view.ScrollBy(0, -1_000_000); return true;
            case Keys.End: _view.ScrollBy(0, 1_000_000); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    // ---------------------------------------------------------------- apertura file

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
        {
            // Rilascio asincrono: non blocca Esplora risorse durante il caricamento.
            BeginInvoke(() => { Activate(); OpenFile(files[0]); });
        }
    }

    private void OpenDialog()
    {
        using var dlg = new OpenFileDialog { Filter = Document.OpenFileFilter, Title = "Apri documento" };
        if (_doc != null) dlg.InitialDirectory = Path.GetDirectoryName(_doc.FilePath);
        if (dlg.ShowDialog(this) == DialogResult.OK) OpenFile(dlg.FileName);
    }

    public void OpenFile(string path)
    {
        if (_busy) return;
        Document newDoc;
        try
        {
            Cursor = Cursors.WaitCursor;
            newDoc = Document.Load(path);
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            MessageBox.Show(this, $"Impossibile aprire \"{Path.GetFileName(path)}\".\n\n{ex.Message}",
                AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally { Cursor = Cursors.Default; }

        var old = _doc;
        _doc = newDoc;
        _pageIndex = 0;
        _view.Page = _doc.Pages[0];
        old?.Dispose();
        UpdateUi();
    }

    private void CloseDocument()
    {
        if (_busy) return;
        var old = _doc;
        _doc = null;
        _view.Page = null;
        old?.Dispose();
        UpdateUi();
    }

    // ---------------------------------------------------------------- navigazione e zoom

    private void GoToPage(int index, int fromWheel = 0)
    {
        if (_doc == null) return;
        int target = Math.Clamp(index, 0, _doc.Pages.Count - 1);
        if (target == _pageIndex) return;
        _pageIndex = target;
        _view.Page = _doc.Pages[_pageIndex];
        if (fromWheel < 0) _view.ScrollBy(0, 1_000_000); // tornando indietro con la rotellina si parte dal fondo
        UpdateUi();
    }

    private void GoToPageDialog()
    {
        if (_doc == null) return;
        using var f = new Form
        {
            Text = "Vai alla pagina", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var label = new Label { Text = $"Pagina (1-{_doc.Pages.Count}):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) };
        var num = new NumericUpDown { Minimum = 1, Maximum = _doc.Pages.Count, Value = _pageIndex + 1, Width = 80 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Annulla", DialogResult = DialogResult.Cancel, AutoSize = true };
        panel.Controls.AddRange(new Control[] { label, num, ok, cancel });
        f.Controls.Add(panel);
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        if (f.ShowDialog(this) == DialogResult.OK) GoToPage((int)num.Value - 1);
    }

    private void ApplyZoomText(string text)
    {
        if (_doc == null) return;
        if (text.StartsWith("Adatta alla larghezza")) { _view.ZoomMode = ZoomMode.FitWidth; return; }
        if (text.StartsWith("Adatta alla pagina")) { _view.ZoomMode = ZoomMode.FitPage; return; }
        string t = text.Replace("%", "").Trim().Replace(',', '.');
        if (float.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out float pct) && pct > 0)
            _view.SetZoom(pct / 100f);
        else
            UpdateZoomUi();
    }

    // ---------------------------------------------------------------- stato interfaccia

    private void UpdateUi()
    {
        bool has = _doc != null;
        bool multi = has && _doc!.Pages.Count > 1;
        foreach (var i in _needDoc) i.Enabled = has && !_busy;
        foreach (var i in _needMultiPage) i.Enabled = multi;

        if (has)
        {
            var p = _doc!.Pages[_pageIndex];
            string pageText = $"Pagina {_pageIndex + 1} di {_doc.Pages.Count}";
            _tbPage.Text = pageText;
            _stPage.Text = pageText;
            _stFile.Text = $"{_doc.FilePath}  ·  {_doc.FormatName}";
            _stSize.Text = $"{p.SizeMm.Width:0.#} × {p.SizeMm.Height:0.#} mm" + (p.IsVector ? " · vettoriale" : "");
            Text = $"{Path.GetFileName(_doc.FilePath)} - {AppName}";
            _stWarn.Visible = p.UnsupportedImages > 0;
            _stWarn.Text = p.UnsupportedImages == 1
                ? "⚠ 1 immagine non visualizzabile"
                : $"⚠ {p.UnsupportedImages} immagini non visualizzabili";
            _stWarn.ToolTipText = "Immagini compresse con l'algoritmo proprietario di Microsoft Office Document Imaging:\n" +
                                  "sono mostrate come riquadri grigi. Testo e grafica vettoriale non sono interessati.";
        }
        else
        {
            _tbPage.Text = "Pagina 0 di 0";
            _stPage.Text = "";
            _stFile.Text = "Nessun documento aperto";
            _stSize.Text = "";
            _stWarn.Visible = false;
            Text = AppName;
        }
        UpdateZoomUi();
    }

    private void UpdateZoomUi()
    {
        string z = _doc == null ? "" : $"{_view.Zoom * 100:0}%";
        _stZoom.Text = z;
        if (!_tbZoom.Focused) _tbZoom.Text = z;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _busy = busy;
        UseWaitCursor = busy;
        if (message != null) _stFile.Text = message;
        if (!busy) UpdateUi();
        else foreach (var i in _needDoc) i.Enabled = false;
    }

    // ---------------------------------------------------------------- stampa

    private void PrintDialogShow()
    {
        if (_doc == null || _busy) return;
        try
        {
            using var pd = new DocumentPrinter(_doc);
            int n = _doc.Pages.Count;
            pd.PrinterSettings.MinimumPage = 1;
            pd.PrinterSettings.MaximumPage = n;
            pd.PrinterSettings.FromPage = 1;
            pd.PrinterSettings.ToPage = n;

            using var dlg = new PrintDialog
            {
                Document = pd, AllowSomePages = true, AllowCurrentPage = true, AllowSelection = false, UseEXDialog = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            var ps = pd.PrinterSettings;
            switch (ps.PrintRange)
            {
                case PrintRange.SomePages: pd.SetRange(ps.FromPage - 1, ps.ToPage - 1); break;
                case PrintRange.CurrentPage: pd.SetRange(_pageIndex, _pageIndex); break;
                default: pd.SetRange(0, n - 1); break;
            }
            // Con la stampante PDF si usa anche il formato carta dell'originale.
            pd.MatchPaperSize = string.Equals(ps.PrinterName, PdfExporter.PdfPrinterName, StringComparison.OrdinalIgnoreCase);
            pd.Print();
        }
        catch (InvalidPrinterException ex)
        {
            ShowError("Nessuna stampante disponibile.\n\n" + ex.Message);
        }
        catch (Exception ex)
        {
            ShowError("Errore durante la stampa.\n\n" + ex.Message);
        }
    }

    private void PrintPreviewShow()
    {
        if (_doc == null || _busy) return;
        try
        {
            using var pd = new DocumentPrinter(_doc);
            using var dlg = new PrintPreviewDialog
            {
                Document = pd, Width = Math.Max(900, Width), Height = Math.Max(700, Height),
                StartPosition = FormStartPosition.CenterParent, UseAntiAlias = true, Text = "Anteprima di stampa",
                ShowIcon = false,
            };
            dlg.PrintPreviewControl.Zoom = 1.0;
            dlg.PrintPreviewControl.AutoZoom = true;
            dlg.PrintPreviewControl.StartPage = _pageIndex;
            dlg.ShowDialog(this);
        }
        catch (InvalidPrinterException ex)
        {
            ShowError("L'anteprima richiede almeno una stampante installata.\n\n" + ex.Message);
        }
        catch (Exception ex)
        {
            ShowError("Errore durante l'anteprima di stampa.\n\n" + ex.Message);
        }
    }

    // ---------------------------------------------------------------- esportazione

    private string? AskSavePath(string filter, string ext, string title)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = filter, DefaultExt = ext, AddExtension = true, OverwritePrompt = true, Title = title,
            FileName = Path.GetFileNameWithoutExtension(_doc!.FilePath) + ext,
            InitialDirectory = Path.GetDirectoryName(_doc.FilePath),
        };
        return dlg.ShowDialog(this) == DialogResult.OK ? dlg.FileName : null;
    }

    private async void ExportPdf(bool raster)
    {
        if (_doc == null || _busy) return;
        if (!raster && !PdfExporter.IsPdfPrinterAvailable())
        {
            var answer = MessageBox.Show(this,
                $"La stampante \"{PdfExporter.PdfPrinterName}\" non è installata, quindi non è possibile creare un PDF vettoriale.\n\n" +
                "Vuoi creare un PDF da immagini (200 dpi)?\n\n" +
                "(Per installarla: Impostazioni > App > Funzionalità facoltative > Aggiungi funzionalità, oppure " +
                "\"Attiva o disattiva funzionalità di Windows\" > Microsoft Print to PDF.)",
                AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            raster = true;
        }

        string? output = AskSavePath("Documento PDF (*.pdf)|*.pdf", ".pdf",
            raster ? "Esporta in PDF da immagini" : "Esporta in PDF vettoriale");
        if (output == null) return;

        string input = _doc.FilePath;
        SetBusy(true, "Esportazione PDF in corso...");
        try
        {
            // Si lavora su una copia del documento in un thread separato: l'interfaccia resta reattiva.
            await RunStaAsync(() =>
            {
                using var d = Document.Load(input);
                if (raster) RasterPdfWriter.Export(d, output);
                else PdfExporter.ExportVector(d, output);
            });
            SetBusy(false);
            AskOpen(output, "Esportazione PDF completata.");
        }
        catch (Exception ex)
        {
            SetBusy(false);
            ShowError("Esportazione PDF non riuscita.\n\n" + ex.Message);
        }
    }

    private async void ExportPng()
    {
        if (_doc == null || _busy) return;
        string? output = AskSavePath("Immagine PNG (*.png)|*.png", ".png", "Esporta in PNG (una immagine per pagina)");
        if (output == null) return;

        string input = _doc.FilePath;
        SetBusy(true, "Esportazione PNG in corso...");
        try
        {
            var files = await RunStaAsync(() =>
            {
                using var d = Document.Load(input);
                return PngExporter.Export(d, output);
            });
            SetBusy(false);
            string msg = files.Count == 1
                ? "Immagine PNG salvata."
                : $"Salvate {files.Count} immagini PNG:\n{Path.GetFileName(files[0])} ... {Path.GetFileName(files[^1])}";
            AskOpen(files[0], msg);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            ShowError("Esportazione PNG non riuscita.\n\n" + ex.Message);
        }
    }

    private bool CheckHasText()
    {
        if (_doc!.HasText) return true;
        MessageBox.Show(this, "Questo documento è un'immagine e non contiene testo leggibile direttamente: " +
                              "per estrarlo serve l'OCR (pacchetto ModiImaging.Ocr).",
            AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void ExportText()
    {
        if (_doc == null || _busy || !CheckHasText()) return;
        string? output = AskSavePath("Testo (*.txt)|*.txt", ".txt", "Esporta testo");
        if (output == null) return;
        try
        {
            File.WriteAllText(output, _doc.GetText(), new System.Text.UTF8Encoding(true));
            AskOpen(output, "Testo esportato.");
        }
        catch (Exception ex) { ShowError("Esportazione del testo non riuscita.\n\n" + ex.Message); }
    }

    private void CopyText()
    {
        if (_doc == null || !CheckHasText()) return;
        string text = _doc.Pages[_pageIndex].Source?.GetText() ?? "";
        if (text.Length == 0) { _stFile.Text = "La pagina non contiene testo."; return; }
        Clipboard.SetText(text);
        _stFile.Text = $"Testo della pagina {_pageIndex + 1} copiato negli appunti.";
    }

    private async void ConvertFolder()
    {
        if (_busy) return;
        using var dlg = new FolderBrowserDialog
        {
            Description = "Scegli la cartella con i file .mdi da convertire in PDF (i PDF verranno salvati accanto agli originali)",
            UseDescriptionForTitle = true,
        };
        if (_doc != null) dlg.InitialDirectory = Path.GetDirectoryName(_doc.FilePath) ?? "";
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        List<string> inputs;
        try { inputs = Converter.CollectInputs(dlg.SelectedPath, recursive: false); }
        catch (Exception ex) { ShowError(ex.Message); return; }

        if (inputs.Count == 0)
        {
            MessageBox.Show(this, "Nella cartella scelta non ci sono file .mdi.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        bool raster = false;
        if (!PdfExporter.IsPdfPrinterAvailable())
        {
            if (MessageBox.Show(this,
                    $"La stampante \"{PdfExporter.PdfPrinterName}\" non è installata: verranno creati PDF da immagini (200 dpi). Continuare?",
                    AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
            raster = true;
        }
        else if (MessageBox.Show(this,
                     $"Verranno convertiti {inputs.Count} file .mdi in PDF vettoriale.\nI PDF esistenti con lo stesso nome saranno sovrascritti.\n\nContinuare?",
                     AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

        SetBusy(true, $"Conversione di {inputs.Count} file...");
        try
        {
            var results = await RunStaAsync(() => Converter.ConvertBatch(inputs, raster, (i, n, r) =>
                BeginInvoke(() => _stFile.Text = $"Conversione {i}/{n}: {Path.GetFileName(r.Input)}")));
            SetBusy(false);

            var errors = results.Where(r => !r.Success).ToList();
            string summary = $"Conversione completata: {results.Count - errors.Count} di {results.Count} file convertiti.";
            if (errors.Count > 0)
            {
                summary += "\n\nErrori:\n" + string.Join("\n",
                    errors.Take(10).Select(r => $"• {Path.GetFileName(r.Input)}: {Truncate(r.Message, 160)}"));
                if (errors.Count > 10) summary += $"\n... e altri {errors.Count - 10}.";
            }
            MessageBox.Show(this, summary, AppName, MessageBoxButtons.OK,
                errors.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            SetBusy(false);
            ShowError("Conversione non riuscita.\n\n" + ex.Message);
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    /// <summary>Esegue il lavoro in un thread STA dedicato (la stampa GDI+ preferisce STA).</summary>
    private static Task<T> RunStaAsync<T>(Func<T> work)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var t = new Thread(() =>
        {
            try { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }) { IsBackground = true };
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        return tcs.Task;
    }

    private static Task RunStaAsync(Action work) => RunStaAsync(() => { work(); return true; });

    private void AskOpen(string file, string message)
    {
        if (MessageBox.Show(this, $"{message}\n\n{file}\n\nVuoi aprire il file?", AppName,
                MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(file) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex.Message); }
        }
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);

    // ---------------------------------------------------------------- informazioni

    private void ShowCommandLineHelp()
    {
        MessageBox.Show(this,
            "MdiViewer.exe [file]\n    apre il file nel visualizzatore\n\n" +
            "MdiViewer.exe file.mdi file.pdf [--raster]\n    converte un file in PDF\n\n" +
            "MdiViewer.exe --convert <file o cartella> [--raster] [--recursive]\n    converte in blocco tutti i .mdi, salvando ogni PDF accanto all'originale\n\n" +
            "--raster: PDF da immagini a 200 dpi invece del PDF vettoriale.",
            "Riga di comando", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        var v = typeof(MainForm).Assembly.GetName().Version;
        MessageBox.Show(this,
            $"MdiViewer {v?.ToString(3)}\n\nVisualizzatore e convertitore di file Microsoft Document Imaging (.mdi).\n" +
            "Apre anche EMF, WMF, TIFF multipagina, PNG, JPG e BMP.\n\n" +
            "Non richiede Microsoft Office.",
            "Informazioni su MdiViewer", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy)
        {
            MessageBox.Show(this, "Attendi il termine dell'operazione in corso.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
            return;
        }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _doc?.Dispose();
        base.Dispose(disposing);
    }
}
