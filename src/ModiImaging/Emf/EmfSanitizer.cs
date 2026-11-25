using System.Buffers.Binary;
using System.IO.Compression;

namespace ModiImaging.Emf;

/// <summary>
/// Rende leggibile da GDI+ un EMF prodotto da Microsoft Office Document Imaging.
///
/// MODI può inserire record non standard (tipo 123, dimensione non multipla di 4) che
/// "avvolgono" un record EMF originale compresso, tipicamente un EMR_STRETCHDIBITS (bitmap).
/// Struttura osservata del record involucro (offset dall'inizio del record):
///   0  tipo (123)            4  dimensione
///   8  tipo originale       12  tipo di compressione
///  16  offset intestazione  20  lunghezza intestazione (record originale non compresso, es. 80 byte)
///  24  offset dati          28  lunghezza dati compressi
///  32  copia dell'intestazione originale (tipo, dimensione, campi...)
/// I dati compressi con l'algoritmo proprietario iniziano con "PTC+MSHM" e non sono decodificabili:
/// in quel caso la bitmap viene sostituita da un riquadro grigio delle stesse dimensioni, così il
/// resto della pagina (testo e grafica vettoriale) resta visibile.
/// </summary>
public static class EmfSanitizer
{
    public sealed record Result(byte[] Emf, int ReplacedImages, int DroppedRecords)
    {
        public bool Changed => ReplacedImages > 0 || DroppedRecords > 0;
    }

    private const uint EmrHeader = 1;
    private const uint EmrEof = 14;
    private const uint EmrStretchDiBits = 81;
    private const uint MaxStandardType = 122; // EMR_CREATECOLORSPACEW
    private const int StretchDiBitsHeaderSize = 80;

    public static Result Sanitize(byte[] emf)
    {
        if (IsClean(emf)) return new Result(emf, 0, 0);

        using var output = new MemoryStream(emf.Length);
        int replaced = 0, dropped = 0, records = 0;
        bool eofWritten = false;
        int i = 0;
        while (i + 8 <= emf.Length)
        {
            uint type = U32(emf, i);
            int size = (int)Math.Min(int.MaxValue, U32(emf, i + 4));
            if (size < 8 || i + size > emf.Length) break; // struttura interrotta: si chiude qui

            var rec = new ReadOnlySpan<byte>(emf, i, size);
            if (IsStandard(type, size) && (records > 0 || type == EmrHeader))
            {
                output.Write(rec);
                records++;
                if (type == EmrEof) { eofWritten = true; break; }
            }
            else if (records > 0 && TryUnwrap(rec, out byte[]? fixedRecord, out bool isPlaceholder))
            {
                output.Write(fixedRecord);
                records++;
                if (isPlaceholder) replaced++;
            }
            else
            {
                dropped++;
            }
            i += size;
        }

        if (records == 0) return new Result(emf, 0, 0); // nessun header valido: lascia decidere a GDI+

        if (!eofWritten)
        {
            var eof = new byte[20];
            BinaryPrimitives.WriteUInt32LittleEndian(eof, EmrEof);
            BinaryPrimitives.WriteUInt32LittleEndian(eof.AsSpan(4), 20);
            BinaryPrimitives.WriteUInt32LittleEndian(eof.AsSpan(12), 16); // offPalEntries
            BinaryPrimitives.WriteUInt32LittleEndian(eof.AsSpan(16), 20); // nSizeLast
            output.Write(eof);
            records++;
        }

        byte[] result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(48), (uint)result.Length); // nBytes
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(52), (uint)records);       // nRecords
        return new Result(result, replaced, dropped);
    }

    private static bool IsStandard(uint type, int size) => type >= 1 && type <= MaxStandardType && size % 4 == 0;

    /// <summary>Controllo veloce: tutti i record sono standard e la catena è integra.</summary>
    private static bool IsClean(byte[] emf)
    {
        int i = 0;
        while (i + 8 <= emf.Length)
        {
            uint type = U32(emf, i);
            uint size = U32(emf, i + 4);
            if (size < 8 || i + size > emf.Length || !IsStandard(type, (int)size)) return false;
            if (type == EmrEof) return true;
            i += (int)size;
        }
        return false;
    }

    /// <summary>
    /// Prova a ricostruire il record originale contenuto in un involucro MODI. Se i dati non sono
    /// decodificabili e il record è una bitmap, restituisce un segnaposto grigio.
    /// </summary>
    private static bool TryUnwrap(ReadOnlySpan<byte> rec, out byte[]? result, out bool isPlaceholder)
    {
        result = null;
        isPlaceholder = false;
        if (rec.Length < 40) return false;

        uint origType = BinaryPrimitives.ReadUInt32LittleEndian(rec[8..]);
        int offH = (int)BinaryPrimitives.ReadUInt32LittleEndian(rec[16..]);
        int lenH = (int)BinaryPrimitives.ReadUInt32LittleEndian(rec[20..]);
        int offD = (int)BinaryPrimitives.ReadUInt32LittleEndian(rec[24..]);
        int lenD = (int)BinaryPrimitives.ReadUInt32LittleEndian(rec[28..]);

        if (origType < 1 || origType > MaxStandardType) return false;
        if (offH < 32 || lenH < 8 || offH + lenH > rec.Length || offH + lenH < 0) return false;
        if (offD < 0 || lenD < 0 || offD + lenD > rec.Length || offD + lenD < 0) return false;

        var header = rec.Slice(offH, lenH);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != origType) return false;
        int origSize = (int)Math.Min(int.MaxValue, BinaryPrimitives.ReadUInt32LittleEndian(header[4..]));

        // 1) dati compressi con zlib/deflate: si ricostruisce il record originale esatto.
        if (origSize > lenH && origSize % 4 == 0)
        {
            byte[]? body = TryInflate(rec.Slice(offD, lenD).ToArray(), origSize - lenH);
            if (body != null)
            {
                result = new byte[origSize];
                header.CopyTo(result);
                body.CopyTo(result.AsSpan(lenH));
                return true;
            }
        }

        // 2) compressione proprietaria: segnaposto per le bitmap.
        if (origType == EmrStretchDiBits && lenH >= StretchDiBitsHeaderSize)
        {
            result = BuildPlaceholder(header[..StretchDiBitsHeaderSize]);
            isPlaceholder = true;
            return true;
        }
        return false;
    }

    /// <summary>EMR_STRETCHDIBITS con una bitmap 1×1 grigio chiaro stirata sulla stessa area di destinazione.</summary>
    private static byte[] BuildPlaceholder(ReadOnlySpan<byte> origHeader)
    {
        const int size = StretchDiBitsHeaderSize + 40 + 4;
        var r = new byte[size];
        origHeader.CopyTo(r);
        var s = r.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], size);
        BinaryPrimitives.WriteInt32LittleEndian(s[32..], 0);   // xSrc
        BinaryPrimitives.WriteInt32LittleEndian(s[36..], 0);   // ySrc
        BinaryPrimitives.WriteInt32LittleEndian(s[40..], 1);   // cxSrc
        BinaryPrimitives.WriteInt32LittleEndian(s[44..], 1);   // cySrc
        BinaryPrimitives.WriteUInt32LittleEndian(s[48..], 80); // offBmiSrc
        BinaryPrimitives.WriteUInt32LittleEndian(s[52..], 40); // cbBmiSrc
        BinaryPrimitives.WriteUInt32LittleEndian(s[56..], 120);// offBitsSrc
        BinaryPrimitives.WriteUInt32LittleEndian(s[60..], 4);  // cbBitsSrc
        BinaryPrimitives.WriteUInt32LittleEndian(s[64..], 0);  // DIB_RGB_COLORS
        BinaryPrimitives.WriteUInt32LittleEndian(s[68..], 0x00CC0020); // SRCCOPY
        // cxDest / cyDest (offset 72, 76) restano quelli originali.

        // BITMAPINFOHEADER
        BinaryPrimitives.WriteUInt32LittleEndian(s[80..], 40);
        BinaryPrimitives.WriteInt32LittleEndian(s[84..], 1);   // biWidth
        BinaryPrimitives.WriteInt32LittleEndian(s[88..], 1);   // biHeight
        BinaryPrimitives.WriteUInt16LittleEndian(s[92..], 1);  // biPlanes
        BinaryPrimitives.WriteUInt16LittleEndian(s[94..], 24); // biBitCount
        BinaryPrimitives.WriteUInt32LittleEndian(s[100..], 4); // biSizeImage
        // pixel BGR
        s[120] = 0xDC; s[121] = 0xDC; s[122] = 0xDC;
        return r;
    }

    private static byte[]? TryInflate(byte[] data, int expected)
    {
        foreach (bool zlib in new[] { true, false })
        {
            try
            {
                using var input = new MemoryStream(data);
                using Stream z = zlib ? new ZLibStream(input, CompressionMode.Decompress)
                                      : new DeflateStream(input, CompressionMode.Decompress);
                var buf = new byte[expected];
                if (z.ReadAtLeast(buf, expected, throwOnEndOfStream: false) == expected) return buf;
            }
            catch (InvalidDataException) { }
        }
        return null;
    }

    private static uint U32(byte[] b, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(offset));
}
