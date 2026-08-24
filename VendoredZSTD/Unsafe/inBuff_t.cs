using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* ------------------------------------------ */
/* =====   Multi-threaded compression   ===== */
/* ------------------------------------------ */
[StructLayout(LayoutKind.Sequential)]
public struct InBuffT
{
    /* read-only non-owned prefix buffer */
    public Range prefix;
    public BufferS buffer;
    public nuint filled;
}