namespace VendoredZSTD.Unsafe;

/*-*************************************
 *  Structures
 ***************************************/
public enum ZstdCwkspAllocPhaseE
{
    ZstdCwkspAllocObjects,
    ZstdCwkspAllocAlignedInitOnce,
    ZstdCwkspAllocAligned,
    ZstdCwkspAllocBuffers
}