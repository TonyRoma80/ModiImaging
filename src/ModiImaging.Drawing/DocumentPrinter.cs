using System.Drawing.Printing;

namespace ModiImaging.Drawing;

/// <summary>
/// PrintDocument che stampa un intervallo di pagine, con orientamento automatico e pagina
/// adattata al foglio. Se <see cref="MatchPaperSize"/> è attivo sceglie anche il formato carta
/// più vicino a quello originale (usato per "Microsoft Print to PDF").
/// </summary>
public sealed class DocumentPrinter : PrintDocument
{
    private readonly Document _doc;
    private int _from, _to, _current;
    private List<PaperSize>? _papers;

    public bool MatchPaperSize { get; set; }

    public DocumentPrinter(Document doc)
    {
        _doc = doc;
        _from = 0;
        _to = doc.Pages.Count - 1;
        DocumentName = Path.GetFileName(doc.FilePath);
        OriginAtMargins = false;
    }

    /// <summary>Intervallo di pagine (0-based, estremi inclusi).</summary>
    public void SetRange(int from, int to)
    {
        int last = _doc.Pages.Count - 1;
        _from = Math.Clamp(from, 0, last);
        _to = Math.Clamp(to, _from, last);
    }

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        _current = _from;
        _papers = null;
        if (MatchPaperSize)
        {
            _papers = PrinterSettings.PaperSizes.Cast<PaperSize>()
                .Where(p => p.Kind != PaperKind.Custom && p.Width > 0 && p.Height > 0).ToList();
        }
        // Le impostazioni iniziali devono già corrispondere alla prima pagina.
        ApplyPageSettings(DefaultPageSettings, _doc.Pages[_current]);
        base.OnBeginPrint(e);
    }

    protected override void OnQueryPageSettings(QueryPageSettingsEventArgs e)
    {
        if (_current <= _to)
            ApplyPageSettings(e.PageSettings, _doc.Pages[_current]);
        base.OnQueryPageSettings(e);
    }

    private void ApplyPageSettings(PageSettings s, Page page)
    {
        if (_papers is { Count: > 0 })
        {
            var paper = PaperMatcher.FindBest(_papers, page.SizeMm);
            if (paper != null) s.PaperSize = paper;
        }
        s.Landscape = page.IsLandscape;
        s.Margins = new Margins(0, 0, 0, 0);
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        var g = e.Graphics!;
        var page = _doc.Pages[_current];

        g.PageUnit = GraphicsUnit.Display; // 1/100 di pollice
        // Con OriginAtMargins=false l'origine è l'angolo dell'area stampabile:
        // VisibleClipBounds è l'area stampabile già orientata correttamente.
        RectangleF area = g.VisibleClipBounds;
        if (area.Width <= 0 || area.Height <= 0)
            area = new RectangleF(0, 0, e.PageBounds.Width, e.PageBounds.Height);

        float pw = page.SizeInches.Width * 100f, ph = page.SizeInches.Height * 100f;
        float scale = Math.Min(area.Width / pw, area.Height / ph);
        // Se la pagina entra quasi esattamente (differenze di arrotondamento), la stampa resta al 100%.
        if (scale > 1f && scale < 1.01f) scale = 1f;
        float w = pw * scale, h = ph * scale;
        var dest = new RectangleF(area.X + (area.Width - w) / 2f, area.Y + (area.Height - h) / 2f, w, h);

        Page.ApplyQuality(g);
        page.Draw(g, dest);

        _current++;
        e.HasMorePages = _current <= _to;
    }
}

public static class PaperMatcher
{
    /// <summary>
    /// Restituisce il formato carta più vicino (indipendentemente dall'orientamento):
    /// prima una corrispondenza entro 3 mm, poi il formato più piccolo che contiene la pagina,
    /// infine il più grande disponibile.
    /// </summary>
    public static PaperSize? FindBest(IReadOnlyList<PaperSize> papers, SizeF pageMm)
    {
        if (papers.Count == 0) return null;
        float a = Math.Min(pageMm.Width, pageMm.Height) / 25.4f * 100f;
        float b = Math.Max(pageMm.Width, pageMm.Height) / 25.4f * 100f;
        const float tol = 3f / 25.4f * 100f; // 3 mm in centesimi di pollice

        PaperSize? best = null; float bestDiff = float.MaxValue;
        foreach (var p in papers)
        {
            float pa = Math.Min(p.Width, p.Height), pb = Math.Max(p.Width, p.Height);
            float diff = Math.Abs(pa - a) + Math.Abs(pb - b);
            if (diff < bestDiff) { bestDiff = diff; best = p; }
        }
        if (best != null && bestDiff <= tol * 2) return best;

        var containing = papers
            .Where(p => Math.Min(p.Width, p.Height) >= a - tol && Math.Max(p.Width, p.Height) >= b - tol)
            .OrderBy(p => (long)p.Width * p.Height).FirstOrDefault();
        return containing ?? papers.OrderByDescending(p => (long)p.Width * p.Height).First();
    }
}
