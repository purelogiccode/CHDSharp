using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct ZdictParamsT
{
    /// <summary>optimize for a specific zstd compression level; 0 means default</summary>
    public int compressionLevel;

    /// <summary>Write log to stderr; 0 = none (default); 1 = errors; 2 = progression; 3 = details; 4 = debug;</summary>
    public uint notificationLevel;

    /// <summary>
    /// force dictID value; 0 means auto mode (32-bits random value)
    /// NOTE: The zstd format reserves some dictionary IDs for future use.
    /// You may use them in private settings, but be warned that they
    /// may be used by zstd in a public dictionary registry in the future.
    /// These dictionary IDs are:
    ///   - low range  : &lt;= 32767
    ///   - high range : &gt;= (2^31)
    /// </summary>
    public uint dictID;
}