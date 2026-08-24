using System.Runtime.InteropServices;
namespace ZstdSharp.Unsafe
{
    /* ------------------------------------------ */
    /* =====   Multi-threaded compression   ===== */
    /* ------------------------------------------ */
    [StructLayout(LayoutKind.Sequential)]
    public struct InBuff_t
    {
        /* read-only non-owned prefix buffer */
        public Range prefix;
        public buffer_s buffer;
        public nuint filled;
    }
}
