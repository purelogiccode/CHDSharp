using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* Context for block-level external matchfinder API */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZSTD_externalMatchCtx
{
    public void* mState;
    public void* mFinder;
    public ZSTD_Sequence* seqBuffer;
    public nuint seqBufferCapacity;
}