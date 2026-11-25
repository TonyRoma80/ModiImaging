using ModiImaging.Drawing;
using System.Drawing.Drawing2D;

namespace MdiViewer.UI;

public enum ZoomMode { Custom, FitWidth, FitPage }

/// <summary>
/// Controllo che mostra una pagina centrata, con ombra, su sfondo grigio.
/// Supporta zoom (Ctrl+rotellina), adattamento a larghezza/pagina e spostamento trascinando.
/// </summary>
public sealed class PageView : ScrollableControl
{
    public const float MinZoom = 0.05f;
    public const float MaxZoom = 8f;
    private const int PageMargin = 24;
    private const long MaxCachePixels = 48_000_000; // oltre questa soglia si disegna direttamente

    private Page? _page;
    private float _zoom = 1f;
    private ZoomMode _mode = ZoomMode.FitPage;

    private Bitmap? _cache;
    private Size _cacheSize;
    private Page? _cachePage;
    private readonly System.Windows.Forms.Timer _rebuildTimer = new() { Interval = 160 };

    private bool _dragging;
    private Point _dragStartMouse;
    private Point _dragStartScroll;

    public event EventHandler? ZoomChanged;
    /// <summary>Richiesta di passare alla pagina successiva (+1) o precedente (-1) scorrendo oltre i bordi.</summary>
    public event EventHandler<int>? PageScrollRequested;

    public PageView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Color.FromArgb(0x52, 0x56, 0x59);
        AutoScroll = true;
        TabStop = true;
        _rebuildTimer.Tick += (_, _) => { _rebuildTimer.Stop(); InvalidateCache(); Invalidate(); };
    }

    public Page? Page
    {
        get => _page;
        set
        {
            _page = value;
            InvalidateCache();
            UpdateLayout();
            AutoScrollPosition = Point.Empty;
            Invalidate();
        }
    }

    public float Zoom => _zoom;

    public ZoomMode ZoomMode
    {
        get => _mode;
        set
        {
            _mode = value;
            UpdateLayout();
            Invalidate();
            ZoomChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Imposta lo zoom (1 = 100%) mantenendo fermo il punto indicato (coordinate client).</summary>
    public void SetZoom(float zoom, Point? anchor = null)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (_page == null) { _zoom = zoom; _mode = ZoomMode.Custom; ZoomChanged?.Invoke(this, EventArgs.Empty); return; }

        Point a = anchor ?? new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        RectangleF old = GetPageRect();
        float fx = old.Width > 0 ? (a.X - old.X) / old.Width : 0.5f;
        float fy = old.Height > 0 ? (a.Y - old.Y) / old.Height : 0.5f;

        _mode = ZoomMode.Custom;
        _zoom = zoom;
        UpdateLayout();

        Size ps = PagePixelSize();
        int sx = (int)Math.Round(PageMargin + fx * ps.Width - a.X);
        int sy = (int)Math.Round(PageMargin + fy * ps.Height - a.Y);
        AutoScrollPosition = new Point(Math.Max(0, sx), Math.Max(0, sy));

        if (_cache != null) _rebuildTimer.Start(); // nel frattempo si mostra la cache scalata
        Invalidate();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ZoomIn() => SetZoom(NextStep(_zoom, +1));
    public void ZoomOut() => SetZoom(NextStep(_zoom, -1));

    private static readonly float[] Steps = { 0.1f, 0.25f, 0.33f, 0.5f, 0.67f, 0.75f, 0.9f, 1f, 1.25f, 1.5f, 2f, 3f, 4f, 6f, 8f };

    private static float NextStep(float z, int dir)
    {
        if (dir > 0) { foreach (var s in Steps) if (s > z + 0.001f) return s; return MaxZoom; }
        for (int i = Steps.Length - 1; i >= 0; i--) if (Steps[i] < z - 0.001f) return Steps[i];
        return MinZoom;
    }

    private float PixelsPerInch => DeviceDpi * _zoom;

    private Size PagePixelSize(float? zoom = null)
    {
        if (_page == null) return Size.Empty;
        float ppi = DeviceDpi * (zoom ?? _zoom);
        return new Size(Math.Max(1, (int)Math.Round(_page.SizeInches.Width * ppi)),
                        Math.Max(1, (int)Math.Round(_page.SizeInches.Height * ppi)));
    }

    private void UpdateLayout()
    {
        if (_page == null) { AutoScrollMinSize = Size.Empty; return; }

        if (_mode != ZoomMode.Custom)
        {
            float pw = _page.SizeInches.Width * DeviceDpi, ph = _page.SizeInches.Height * DeviceDpi;
            // 2 px di tolleranza: evitano che l'arrotondamento faccia comparire le barre di scorrimento.
            float availW = Width - 2 * PageMargin - 2, availH = Height - 2 * PageMargin - 2;
            float z;
            if (_mode == ZoomMode.FitPage)
                z = Math.Min(availW / pw, availH / ph);
            else
            {
                z = availW / pw;
                if (ph * z + 2 * PageMargin > Height) // servirà la barra verticale
                    z = (availW - SystemInformation.VerticalScrollBarWidth) / pw;
            }
            _zoom = Math.Clamp(z, MinZoom, MaxZoom);
        }

        Size ps = PagePixelSize();
        AutoScrollMinSize = new Size(ps.Width + 2 * PageMargin, ps.Height + 2 * PageMargin);
    }

    /// <summary>Rettangolo della pagina in coordinate client.</summary>
    private RectangleF GetPageRect()
    {
        Size ps = PagePixelSize();
        float x = ps.Width + 2 * PageMargin <= ClientSize.Width
            ? (ClientSize.Width - ps.Width) / 2f
            : PageMargin + AutoScrollPosition.X;
        float y = ps.Height + 2 * PageMargin <= ClientSize.Height
            ? (ClientSize.Height - ps.Height) / 2f
            : PageMargin + AutoScrollPosition.Y;
        return new RectangleF((int)x, (int)y, ps.Width, ps.Height);
    }

    private void InvalidateCache()
    {
        _cache?.Dispose();
        _cache = null;
        _cachePage = null;
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_mode != ZoomMode.Custom)
        {
            float before = _zoom;
            UpdateLayout();
            if (Math.Abs(before - _zoom) > 0.0001f)
            {
                if (_cache != null) _rebuildTimer.Start();
                ZoomChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        InvalidateCache();
        UpdateLayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_page == null)
        {
            TextRenderer.DrawText(e.Graphics,
                "Apri un file (Ctrl+O) oppure trascinalo qui.\n\nFormati: MDI, EMF, WMF, TIFF, PNG, JPG, BMP",
                Font, ClientRectangle, Color.FromArgb(220, 220, 220),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        var g = e.Graphics;
        RectangleF r = GetPageRect();

        // Ombra
        using (var s1 = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
        using (var s2 = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
        {
            g.FillRectangle(s1, r.X + 2, r.Y + 2, r.Width + 6, r.Height + 6);
            g.FillRectangle(s2, r.X + 3, r.Y + 3, r.Width + 2, r.Height + 2);
        }
        g.FillRectangle(Brushes.White, r);

        Size ps = PagePixelSize();
        bool cacheable = (long)ps.Width * ps.Height <= MaxCachePixels;

        if (_cache != null && _cachePage == _page && _cacheSize == ps)
        {
            g.DrawImageUnscaled(_cache, (int)r.X, (int)r.Y);
        }
        else if (_cache != null && _cachePage == _page && _rebuildTimer.Enabled)
        {
            // Zoom in corso: mostra la cache scalata, verrà rigenerata a breve.
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.DrawImage(_cache, r);
        }
        else if (cacheable)
        {
            InvalidateCache();
            try
            {
                _cache = _page.RenderToSize(ps.Width, ps.Height);
                _cachePage = _page;
                _cacheSize = ps;
                g.DrawImageUnscaled(_cache, (int)r.X, (int)r.Y);
            }
            catch (Exception ex) when (ex is OutOfMemoryException or ArgumentException)
            {
                InvalidateCache();
                DrawDirect(g, r);
            }
        }
        else
        {
            InvalidateCache();
            DrawDirect(g, r);
        }

        using var border = new Pen(Color.FromArgb(60, 0, 0, 0));
        g.DrawRectangle(border, r.X - 1, r.Y - 1, r.Width + 1, r.Height + 1);
    }

    private void DrawDirect(Graphics g, RectangleF r)
    {
        var state = g.Save();
        g.SetClip(RectangleF.Intersect(r, ClientRectangle));
        ModiImaging.Drawing.Page.ApplyQuality(g);
        try { _page!.Draw(g, r); }
        catch (Exception) { /* zoom estremo: GDI+ può rifiutare il disegno */ }
        g.Restore(state);
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if ((ModifierKeys & Keys.Control) != 0)
        {
            float factor = (float)Math.Pow(1.15, e.Delta / 120.0);
            SetZoom(_zoom * factor, e.Location);
            if (e is HandledMouseEventArgs h) h.Handled = true;
            return;
        }

        bool canScrollV = VerticalScroll.Visible;
        int before = -AutoScrollPosition.Y;
        int maxScroll = Math.Max(0, AutoScrollMinSize.Height - ClientSize.Height);
        if (!canScrollV || (e.Delta < 0 && before >= maxScroll) || (e.Delta > 0 && before <= 0))
        {
            // Già al bordo: passa alla pagina adiacente.
            PageScrollRequested?.Invoke(this, e.Delta < 0 ? +1 : -1);
            if (e is HandledMouseEventArgs h) h.Handled = true;
            return;
        }
        base.OnMouseWheel(e);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left && _page != null)
        {
            _dragging = true;
            _dragStartMouse = e.Location;
            _dragStartScroll = new Point(-AutoScrollPosition.X, -AutoScrollPosition.Y);
            Cursor = Cursors.SizeAll;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            AutoScrollPosition = new Point(
                _dragStartScroll.X - (e.X - _dragStartMouse.X),
                _dragStartScroll.Y - (e.Y - _dragStartMouse.Y));
            Invalidate();
        }
        else
        {
            Cursor = _page != null && (HorizontalScroll.Visible || VerticalScroll.Visible) ? Cursors.Hand : Cursors.Default;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            Cursor = Cursors.Hand;
        }
    }

    /// <summary>Scorre di una "schermata" (usato da frecce/spazio).</summary>
    public void ScrollBy(int dx, int dy)
    {
        AutoScrollPosition = new Point(-AutoScrollPosition.X + dx, -AutoScrollPosition.Y + dy);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _rebuildTimer.Dispose();
            InvalidateCache();
        }
        base.Dispose(disposing);
    }
}
