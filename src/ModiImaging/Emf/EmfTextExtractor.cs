using System.Buffers.Binary;
using System.Text;

namespace ModiImaging.Emf;

/// <summary>
/// Estrae il testo da un EMF leggendo i record di testo (EMR_EXTTEXTOUTW, EMR_EXTTEXTOUTA,
/// EMR_SMALLTEXTOUT) e ricostruendo lo stato del contesto grafico (map mode, window/viewport,
/// world transform, SaveDC/RestoreDC, font selezionato, allineamento del testo) per convertire
/// le coordinate logiche in millimetri sulla pagina.
/// </summary>
public static class EmfTextExtractor
{
    public sealed record Result(IReadOnlyList<TextRun> Runs, int GlyphIndexRuns, int ImageCount, double ImageCoverage);

    // Tipi di record
    private const uint SetWindowExtEx = 9, SetWindowOrgEx = 10, SetViewportExtEx = 11, SetViewportOrgEx = 12;
    private const uint Eof = 14, SetMapMode = 17, SetTextAlign = 22, MoveToEx = 27;
    private const uint ScaleViewportExtEx = 31, ScaleWindowExtEx = 32, SaveDc = 33, RestoreDc = 34;
    private const uint SetWorldTransform = 35, ModifyWorldTransform = 36, SelectObject = 37, DeleteObject = 40;
    private const uint BitBlt = 76, StretchBlt = 77, SetDiBitsToDevice = 80, StretchDiBits = 81;
    private const uint ExtCreateFontIndirectW = 82, ExtTextOutA = 83, ExtTextOutW = 84;
    private const uint SmallTextOut = 108, AlphaBlend = 114, TransparentBlt = 116;

    // Opzioni del testo
    private const uint EtoGlyphIndex = 0x10, EtoNoRect = 0x100, EtoSmallChars = 0x200, EtoPdy = 0x2000;

    private sealed record FontInfo(int Height, int Weight, byte CharSet, string Face);

    private struct Matrix
    {
        public double M11, M12, M21, M22, Dx, Dy;
        public static Matrix Identity => new() { M11 = 1, M22 = 1 };
        public static Matrix Multiply(Matrix a, Matrix b) => new()
        {
            M11 = a.M11 * b.M11 + a.M12 * b.M21,
            M12 = a.M11 * b.M12 + a.M12 * b.M22,
            M21 = a.M21 * b.M11 + a.M22 * b.M21,
            M22 = a.M21 * b.M12 + a.M22 * b.M22,
            Dx = a.Dx * b.M11 + a.Dy * b.M21 + b.Dx,
            Dy = a.Dx * b.M12 + a.Dy * b.M22 + b.Dy,
        };
        public (double X, double Y) Apply(double x, double y) => (x * M11 + y * M21 + Dx, x * M12 + y * M22 + Dy);
        public (double X, double Y) ApplyVector(double x, double y) => (x * M11 + y * M21, x * M12 + y * M22);
    }

    private sealed class DcState
    {
        public int MapMode = 1;
        public double WinOrgX, WinOrgY, WinExtX = 1, WinExtY = 1;
        public double VpOrgX, VpOrgY, VpExtX = 1, VpExtY = 1;
        public Matrix World = Matrix.Identity;
        public uint TextAlign;
        public FontInfo? Font;
        public double CurX, CurY;
        public DcState Clone() => (DcState)MemberwiseClone();
    }

    static EmfTextExtractor()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Result Extract(byte[] emf)
    {
        var runs = new List<TextRun>();
        if (!EmfInfo.IsValidHeader(emf)) return new Result(runs, 0, 0, 0);

        var (mmPerDevX, mmPerDevY) = EmfInfo.GetMmPerDeviceUnit(emf);
        var frame = EmfInfo.GetFrame(emf);
        double originX = frame.Left / 100.0, originY = frame.Top / 100.0;
        var (pageW, pageH) = EmfInfo.GetPageSizeMm(emf);
        double pageArea = Math.Max(1, pageW * pageH);

        var fonts = new Dictionary<uint, FontInfo>();
        var stack = new List<DcState>();
        var dc = new DcState();
        int glyphRuns = 0, images = 0;
        double imageArea = 0;

        int i = 0;
        while (i + 8 <= emf.Length)
        {
            uint type = U32(emf, i);
            int size = (int)Math.Min(int.MaxValue, U32(emf, i + 4));
            if (size < 8 || i + size > emf.Length) break;
            var r = new ReadOnlySpan<byte>(emf, i, size);

            switch (type)
            {
                case SetMapMode when size >= 12: dc.MapMode = I32(r, 8); break;
                case SetWindowExtEx when size >= 16: dc.WinExtX = I32(r, 8); dc.WinExtY = I32(r, 12); break;
                case SetWindowOrgEx when size >= 16: dc.WinOrgX = I32(r, 8); dc.WinOrgY = I32(r, 12); break;
                case SetViewportExtEx when size >= 16: dc.VpExtX = I32(r, 8); dc.VpExtY = I32(r, 12); break;
                case SetViewportOrgEx when size >= 16: dc.VpOrgX = I32(r, 8); dc.VpOrgY = I32(r, 12); break;
                case ScaleViewportExtEx when size >= 24:
                    dc.VpExtX = Scale(dc.VpExtX, I32(r, 8), I32(r, 12));
                    dc.VpExtY = Scale(dc.VpExtY, I32(r, 16), I32(r, 20));
                    break;
                case ScaleWindowExtEx when size >= 24:
                    dc.WinExtX = Scale(dc.WinExtX, I32(r, 8), I32(r, 12));
                    dc.WinExtY = Scale(dc.WinExtY, I32(r, 16), I32(r, 20));
                    break;
                case SetTextAlign when size >= 12: dc.TextAlign = U32(r, 8); break;
                case MoveToEx when size >= 16: dc.CurX = I32(r, 8); dc.CurY = I32(r, 12); break;
                case SaveDc: stack.Add(dc.Clone()); break;
                case RestoreDc when size >= 12:
                {
                    int rel = I32(r, 8);
                    int idx = rel < 0 ? stack.Count + rel : rel - 1;
                    if (idx >= 0 && idx < stack.Count)
                    {
                        dc = stack[idx];
                        stack.RemoveRange(idx, stack.Count - idx);
                    }
                    break;
                }
                case SetWorldTransform when size >= 32: dc.World = ReadMatrix(r, 8); break;
                case ModifyWorldTransform when size >= 36:
                {
                    var m = ReadMatrix(r, 8);
                    switch (U32(r, 32))
                    {
                        case 1: dc.World = Matrix.Identity; break;                     // MWT_IDENTITY
                        case 2: dc.World = Matrix.Multiply(m, dc.World); break;        // MWT_LEFTMULTIPLY
                        case 3: dc.World = Matrix.Multiply(dc.World, m); break;        // MWT_RIGHTMULTIPLY
                        case 4: dc.World = m; break;                                   // MWT_SET
                    }
                    break;
                }
                case ExtCreateFontIndirectW when size >= 12 + 92:
                    fonts[U32(r, 8)] = new FontInfo(I32(r, 12), I32(r, 28), r[12 + 23],
                        Encoding.Unicode.GetString(r.Slice(12 + 28, 64)).Split('\0')[0]);
                    break;
                case SelectObject when size >= 12:
                {
                    uint h = U32(r, 8);
                    if ((h & 0x80000000) != 0)
                    {
                        uint stock = h & 0x7FFFFFFF;
                        if (stock is >= 10 and <= 17 and not 15) dc.Font = new FontInfo(0, 400, 0, "System");
                    }
                    else if (fonts.TryGetValue(h, out var f)) dc.Font = f;
                    break;
                }
                case DeleteObject when size >= 12: fonts.Remove(U32(r, 8)); break;

                case BitBlt or StretchBlt or AlphaBlend or TransparentBlt when size >= 92 && U32(r, 88) > 0:
                case SetDiBitsToDevice or StretchDiBits when size >= 56 && U32(r, 52) > 0:
                {
                    images++;
                    double w = (I32(r, 16) - I32(r, 8) + 1) * mmPerDevX, h = (I32(r, 20) - I32(r, 12) + 1) * mmPerDevY;
                    if (w > 0 && h > 0) imageArea += Math.Min(w, pageW) * Math.Min(h, pageH);
                    break;
                }

                case ExtTextOutW or ExtTextOutA when size >= 76:
                {
                    uint opts = U32(r, 52);
                    if ((opts & EtoGlyphIndex) != 0) { glyphRuns++; break; }
                    int n = I32(r, 44), off = I32(r, 48), offDx = I32(r, 72);
                    int bytes = type == ExtTextOutW ? n * 2 : n;
                    if (n <= 0 || off < 0 || off + bytes > size) break;
                    string text = type == ExtTextOutW
                        ? Encoding.Unicode.GetString(r.Slice(off, bytes))
                        : DecodeAnsi(r.Slice(off, bytes), dc.Font);
                    double? advance = SumDx(r, offDx, n, (opts & EtoPdy) != 0);
                    AddRun(text, I32(r, 36), I32(r, 40), advance);
                    break;
                }
                case SmallTextOut when size >= 36:
                {
                    uint opts = U32(r, 20);
                    if ((opts & EtoGlyphIndex) != 0) { glyphRuns++; break; }
                    int n = I32(r, 16);
                    int off = 36 + ((opts & EtoNoRect) != 0 ? 0 : 16);
                    bool small = (opts & EtoSmallChars) != 0;
                    int bytes = small ? n : n * 2;
                    if (n <= 0 || off + bytes > size) break;
                    string text = small ? Encoding.Latin1.GetString(r.Slice(off, bytes)) : Encoding.Unicode.GetString(r.Slice(off, bytes));
                    AddRun(text, I32(r, 8), I32(r, 12), null);
                    break;
                }
                case Eof:
                    i = emf.Length;
                    continue;
            }
            i += size;
        }

        return new Result(runs, glyphRuns, images, Math.Min(1, imageArea / pageArea));

        // ---- funzioni locali -------------------------------------------------------------

        void AddRun(string text, int x, int y, double? advanceLogical)
        {
            text = CleanText(text);
            if (text.Length == 0) return;

            bool updateCp = (dc.TextAlign & 1) != 0;
            double lx = updateCp ? dc.CurX : x, ly = updateCp ? dc.CurY : y;

            var (px, py) = ToMm(lx, ly);
            double fontLogical = Math.Abs(dc.Font?.Height ?? 0);
            double fontMm = fontLogical > 0 ? VectorLengthMm(0, fontLogical) : 3.5;
            double widthMm = advanceLogical is double adv && adv > 0
                ? VectorLengthMm(adv, 0)
                : 0.5 * fontMm * text.Length;

            if (updateCp && advanceLogical is double a) dc.CurX += a;

            switch (dc.TextAlign & 6) // orizzontale
            {
                case 2: px -= widthMm; break;      // TA_RIGHT
                case 6: px -= widthMm / 2; break;  // TA_CENTER
            }
            switch (dc.TextAlign & 24) // verticale: si riporta tutto alla linea di base
            {
                case 0: py += 0.8 * fontMm; break; // TA_TOP
                case 8: py -= 0.2 * fontMm; break; // TA_BOTTOM
            }

            runs.Add(new TextRun(text, px, py, widthMm, fontMm, dc.Font?.Face, (dc.Font?.Weight ?? 400) >= 600));
        }

        (double X, double Y) ToMm(double lx, double ly)
        {
            var (wx, wy) = dc.World.Apply(lx, ly);
            var (dx, dy) = PageToDevice(wx, wy);
            return (dx * mmPerDevX - originX, dy * mmPerDevY - originY);
        }

        double VectorLengthMm(double vx, double vy)
        {
            var (wx, wy) = dc.World.ApplyVector(vx, vy);
            var (sx, sy) = DeviceScale();
            double dx = wx * sx * mmPerDevX, dy = wy * sy * mmPerDevY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        (double X, double Y) PageToDevice(double x, double y)
        {
            var (sx, sy) = DeviceScale();
            return ((x - dc.WinOrgX) * sx + dc.VpOrgX, (y - dc.WinOrgY) * sy + dc.VpOrgY);
        }

        (double X, double Y) DeviceScale()
        {
            double unitMm = dc.MapMode switch
            {
                2 => 0.1,           // MM_LOMETRIC
                3 => 0.01,          // MM_HIMETRIC
                4 => 0.254,         // MM_LOENGLISH
                5 => 0.0254,        // MM_HIENGLISH
                6 => 25.4 / 1440,   // MM_TWIPS
                _ => 0,
            };
            if (unitMm > 0) return (unitMm / mmPerDevX, -unitMm / mmPerDevY); // asse Y verso l'alto
            if (dc.MapMode is 7 or 8 && dc.WinExtX != 0 && dc.WinExtY != 0)
                return (dc.VpExtX / dc.WinExtX, dc.VpExtY / dc.WinExtY);
            return (1, 1); // MM_TEXT
        }
    }

    private static double? SumDx(ReadOnlySpan<byte> r, int offDx, int n, bool pdy)
    {
        int count = pdy ? n * 2 : n;
        if (offDx <= 0 || offDx + count * 4 > r.Length) return null;
        double sum = 0;
        for (int k = 0; k < count; k += pdy ? 2 : 1) sum += I32(r, offDx + k * 4);
        return sum;
    }

    private static string DecodeAnsi(ReadOnlySpan<byte> bytes, FontInfo? font)
    {
        int codePage = font?.CharSet switch
        {
            161 => 1253, 162 => 1254, 163 => 1258, 177 => 1255, 178 => 1256, 186 => 1257,
            204 => 1251, 222 => 874, 238 => 1250, 128 => 932, 129 => 949, 134 => 936, 136 => 950,
            255 => 437, 77 => 10000,
            _ => 1252,
        };
        try { return Encoding.GetEncoding(codePage).GetString(bytes); }
        catch (Exception) { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>Elimina i caratteri di controllo e trasforma i caratteri dei font simbolici in un pallino.</summary>
    private static string CleanText(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c == '\0') continue;
            if (c >= '' && c <= '') { sb.Append('•'); continue; } // area privata (Wingdings, Symbol)
            sb.Append(char.IsControl(c) ? ' ' : c);
        }
        return sb.ToString();
    }

    private static double Scale(double ext, int num, int den) => den == 0 ? ext : ext * num / den;

    private static Matrix ReadMatrix(ReadOnlySpan<byte> r, int o) => new()
    {
        M11 = F32(r, o), M12 = F32(r, o + 4), M21 = F32(r, o + 8), M22 = F32(r, o + 12), Dx = F32(r, o + 16), Dy = F32(r, o + 20),
    };

    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static uint U32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b[o..]);
    private static int I32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadInt32LittleEndian(b[o..]);
    private static float F32(ReadOnlySpan<byte> b, int o) => BinaryPrimitives.ReadSingleLittleEndian(b[o..]);
}
