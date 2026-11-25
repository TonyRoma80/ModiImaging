using System.Buffers.Binary;

namespace ModiImaging.Emf;

/// <summary>Lettura dei campi principali dell'intestazione EMF (record EMR_HEADER).</summary>
public static class EmfInfo
{
    public const int MinHeaderSize = 88;
    private const uint EmrHeader = 1;
    private const uint EmfSignature = 0x464D4520; // " EMF"

    /// <summary>True se i dati iniziano con un record EMR_HEADER valido.</summary>
    public static bool IsValidHeader(ReadOnlySpan<byte> d) =>
        d.Length >= MinHeaderSize &&
        BinaryPrimitives.ReadUInt32LittleEndian(d) == EmrHeader &&
        BinaryPrimitives.ReadUInt32LittleEndian(d[40..]) == EmfSignature;

    /// <summary>nBytes: dimensione totale del metafile.</summary>
    public static int GetTotalSize(ReadOnlySpan<byte> d) =>
        (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(d[48..]));

    /// <summary>
    /// Dimensione fisica della pagina in mm, ricavata da rclFrame (centesimi di millimetro).
    /// Se rclFrame non è valido usa la dimensione del dispositivo di riferimento.
    /// </summary>
    public static (double Width, double Height) GetPageSizeMm(ReadOnlySpan<byte> d)
    {
        if (!IsValidHeader(d)) return (0, 0);
        var f = GetFrame(d);
        double w = Math.Abs(f.Right - f.Left) / 100.0, h = Math.Abs(f.Bottom - f.Top) / 100.0;
        if (w > 1 && h > 1) return (w, h);
        int mmX = BinaryPrimitives.ReadInt32LittleEndian(d[80..]), mmY = BinaryPrimitives.ReadInt32LittleEndian(d[84..]);
        return (mmX, mmY);
    }

    /// <summary>rclFrame in centesimi di millimetro.</summary>
    public static (int Left, int Top, int Right, int Bottom) GetFrame(ReadOnlySpan<byte> d) => (
        BinaryPrimitives.ReadInt32LittleEndian(d[24..]), BinaryPrimitives.ReadInt32LittleEndian(d[28..]),
        BinaryPrimitives.ReadInt32LittleEndian(d[32..]), BinaryPrimitives.ReadInt32LittleEndian(d[36..]));

    /// <summary>
    /// Millimetri per unità di dispositivo (assi X e Y), da szlMicrometers se presente,
    /// altrimenti da szlDevice / szlMillimeters.
    /// </summary>
    public static (double X, double Y) GetMmPerDeviceUnit(ReadOnlySpan<byte> d)
    {
        int headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(d[4..]);
        double devX = BinaryPrimitives.ReadInt32LittleEndian(d[72..]);
        double devY = BinaryPrimitives.ReadInt32LittleEndian(d[76..]);
        double mmX = BinaryPrimitives.ReadInt32LittleEndian(d[80..]);
        double mmY = BinaryPrimitives.ReadInt32LittleEndian(d[84..]);
        if (headerSize >= 108 && d.Length >= 108)
        {
            double umX = BinaryPrimitives.ReadInt32LittleEndian(d[100..]);
            double umY = BinaryPrimitives.ReadInt32LittleEndian(d[104..]);
            if (umX > 0 && umY > 0) { mmX = umX / 1000.0; mmY = umY / 1000.0; }
        }
        if (devX <= 0 || devY <= 0 || mmX <= 0 || mmY <= 0) return (25.4 / 96, 25.4 / 96);
        return (mmX / devX, mmY / devY);
    }
}
