using System.Text;

namespace ModiImaging;

/// <summary>
/// Un frammento di testo disegnato nella pagina. Le coordinate sono in millimetri dall'angolo
/// in alto a sinistra della pagina; <see cref="Y"/> è la linea di base del testo.
/// </summary>
/// <param name="Text">Testo del frammento.</param>
/// <param name="X">Bordo sinistro in mm.</param>
/// <param name="Y">Linea di base in mm.</param>
/// <param name="Width">Larghezza in mm (stimata se il file non indica le spaziature dei caratteri).</param>
/// <param name="FontSize">Altezza del carattere in mm.</param>
/// <param name="FontName">Nome del font, se noto.</param>
/// <param name="Bold">True se il font è grassetto.</param>
public readonly record struct TextRun(string Text, double X, double Y, double Width, double FontSize, string? FontName, bool Bold)
{
    /// <summary>Bordo destro in mm.</summary>
    public double Right => X + Width;

    /// <summary>Altezza del carattere in punti tipografici.</summary>
    public double FontSizePt => FontSize / 25.4 * 72;
}

/// <summary>Ricostruzione del testo leggibile a partire dai frammenti posizionati.</summary>
public static class TextLayout
{
    /// <summary>Raggruppa i frammenti in righe e li unisce in un testo semplice.</summary>
    public static string ToPlainText(IEnumerable<TextRun> runs)
    {
        var lines = GroupLines(runs);
        var sb = new StringBuilder();
        double? prevY = null, prevSize = null;
        foreach (var line in lines)
        {
            string text = JoinLine(line);
            if (text.Length == 0) continue;
            double y = line[0].Y, size = line.Max(r => r.FontSize);
            // Interlinea molto più grande del normale: separazione di paragrafo.
            if (prevY is double py && prevSize is double ps && y - py > 1.9 * Math.Max(ps, size))
                sb.Append('\n');
            sb.Append(text).Append('\n');
            prevY = y; prevSize = size;
        }
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Raggruppa i frammenti per riga (linea di base simile), ordinati dall'alto e da sinistra.</summary>
    public static List<List<TextRun>> GroupLines(IEnumerable<TextRun> runs)
    {
        var sorted = runs.Where(r => r.Text.Length > 0).OrderBy(r => r.Y).ThenBy(r => r.X).ToList();
        var lines = new List<List<TextRun>>();
        foreach (var run in sorted)
        {
            var last = lines.Count > 0 ? lines[^1] : null;
            if (last != null)
            {
                double baseY = last[0].Y;
                double tol = Math.Max(0.4, 0.35 * Math.Min(last.Max(r => r.FontSize), Math.Max(run.FontSize, 0.1)));
                if (Math.Abs(run.Y - baseY) <= tol) { last.Add(run); continue; }
            }
            lines.Add(new List<TextRun> { run });
        }
        foreach (var l in lines) l.Sort((a, b) => a.X.CompareTo(b.X));
        return lines;
    }

    private static string JoinLine(List<TextRun> line)
    {
        var sb = new StringBuilder();
        double? prevRight = null;
        foreach (var r in line)
        {
            if (prevRight is double pr && sb.Length > 0)
            {
                double gap = r.X - pr, size = Math.Max(r.FontSize, 0.5);
                if (gap > 4 * size) sb.Append('\t'); // colonne o campi distanti
                else if (gap > 0.2 * size && !char.IsWhiteSpace(sb[^1]) && !char.IsWhiteSpace(r.Text[0])) sb.Append(' ');
            }
            sb.Append(r.Text);
            prevRight = Math.Max(prevRight ?? double.MinValue, r.Right);
        }
        // Spazi multipli e caratteri di controllo ridotti a uno spazio; le tabulazioni restano (una sola).
        var clean = new StringBuilder(sb.Length);
        foreach (char c in sb.ToString())
        {
            char ch = c == '\t' ? '\t' : char.IsControl(c) || c == ' ' ? ' ' : c;
            if (ch == ' ' && clean.Length > 0 && (clean[^1] == ' ' || clean[^1] == '\t')) continue;
            if (ch == '\t' && clean.Length > 0 && clean[^1] == ' ') clean[^1] = '\t';
            else if (ch == '\t' && clean.Length > 0 && clean[^1] == '\t') continue;
            else clean.Append(ch);
        }
        return clean.ToString().Trim(' ', '\t');
    }
}
