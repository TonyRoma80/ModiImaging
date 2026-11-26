using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MdiViewer.UI;

/// <summary>
/// Programma predefinito per i file .mdi.
/// Da Windows 10 un programma non può impostarsi da solo come predefinito: lo sceglie l'utente.
/// MdiViewer verifica se lo è già e, se no, apre la pagina "App predefinite" delle Impostazioni,
/// dove l'installer lo ha registrato (chiave RegisteredApplications).
/// </summary>
internal static class FileAssociation
{
    private const string Extension = ".mdi";
    private const string RegisteredName = "MdiViewer";

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags, uint str, string pszAssoc, string? pszExtra,
                                               StringBuilder? pszOut, ref uint pcchOut);
    private const uint AssocStrExecutable = 2;
    private const uint AssocFInitIgnoreUnknown = 0x400;

    /// <summary>Programma che Windows usa oggi per aprire i .mdi, oppure null.</summary>
    public static string? CurrentHandler()
    {
        uint size = 0;
        AssocQueryString(AssocFInitIgnoreUnknown, AssocStrExecutable, Extension, null, null, ref size);
        if (size == 0) return null;
        var sb = new StringBuilder((int)size);
        return AssocQueryString(AssocFInitIgnoreUnknown, AssocStrExecutable, Extension, null, sb, ref size) == 0
            ? sb.ToString()
            : null;
    }

    public static bool IsDefault()
    {
        string? handler = CurrentHandler(), self = Environment.ProcessPath;
        return handler != null && self != null &&
               string.Equals(Path.GetFullPath(handler), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"user" o "machine" se MdiViewer è stato installato con il pacchetto MSI, altrimenti null (versione portatile).</summary>
    private static string? Registration()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(@"Software\RegisteredApplications"))
            if (k?.GetValue(RegisteredName) != null) return "User";
        using (var k = Registry.LocalMachine.OpenSubKey(@"Software\RegisteredApplications"))
            if (k?.GetValue(RegisteredName) != null) return "Machine";
        return null;
    }

    public static void MakeDefault(IWin32Window owner)
    {
        const string title = "Programma predefinito";
        if (IsDefault())
        {
            MessageBox.Show(owner, "MdiViewer è già il programma predefinito per i file .mdi.", title,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string? registration = Registration();
        if (registration == null)
        {
            MessageBox.Show(owner,
                "Questa copia di MdiViewer non è installata, quindi Windows non la elenca tra le app predefinite.\n\n" +
                "Per aprire i file .mdi con MdiViewer:\n" +
                "• installa MdiViewer con il pacchetto .msi, oppure\n" +
                "• fai clic destro su un file .mdi > Apri con > Scegli un'altra app, seleziona MdiViewer " +
                "e attiva \"Usa sempre questa app\".",
                title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string? current = CurrentHandler();
        string intro = current == null
            ? "Al momento nessun programma apre i file .mdi."
            : $"Al momento i file .mdi si aprono con:\n{current}";
        if (MessageBox.Show(owner,
                intro + "\n\nWindows non permette ai programmi di impostarsi da soli come predefiniti: " +
                "si aprirà la pagina \"App predefinite\" delle Impostazioni, dove puoi scegliere MdiViewer per i file .mdi.\n\nContinuare?",
                title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;

        // Windows 11 apre direttamente la scheda di MdiViewer; Windows 10 apre la pagina generale delle app predefinite.
        string uri = $"ms-settings:defaultapps?registeredApp{registration}={Uri.EscapeDataString(RegisteredName)}";
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:defaultapps") { UseShellExecute = true }); }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Impossibile aprire le Impostazioni di Windows.\n\n" + ex.Message, title,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
