using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

/* Struct containing info needed to make decision about ldm inclusion */
[StructLayout(LayoutKind.Sequential)]
public struct ZstdOptLdmT
{
    /* External match candidates store for this block */
    public RawSeqStoreT seqStore;
    /* Start position of the current match candidate */
    public uint startPosInBlock;
    /* End position of the current match candidate */
    public uint endPosInBlock;
    /* Offset of the match candidate */
    public uint offset;
}