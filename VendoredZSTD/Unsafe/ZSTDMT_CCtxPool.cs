using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* =====   CCtx Pool   ===== */
/* a single CCtx Pool can be invoked from multiple threads in parallel */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdmtCCtxPool
{
    public void* poolMutex;
    public int totalCCtx;
    public int availCCtx;
    public ZstdCustomMem cMem;

    /* variable size */
    public CctxEFixedBuffer cctx;

    [StructLayout(LayoutKind.Sequential)]
    public struct CctxEFixedBuffer
    {
        public ZstdCCtxS* e0;
    }
}