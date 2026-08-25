using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* ------------------------------------------ */
/* =====   Multi-threaded compression   ===== */
/* ------------------------------------------ */
[StructLayout(LayoutKind.Sequential)]
public struct inBuff_t
{
    /* read-only non-owned prefix buffer */
    public range_t prefix;
    public buffer_s buffer;
    public nuint filled;
}
