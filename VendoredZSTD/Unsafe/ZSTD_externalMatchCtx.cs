using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* Context for block-level external matchfinder API */
[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdExternalMatchCtx
{
    public void* mState;
    public void* mFinder;
    public ZstdSequence* seqBuffer;
    public nuint seqBufferCapacity;
}