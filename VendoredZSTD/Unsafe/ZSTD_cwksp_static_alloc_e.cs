namespace VendoredZSTD.Unsafe;

/**
 * Used to describe whether the workspace is statically allocated (and will not
 * necessarily ever be freed), or if it's dynamically allocated and we can
 * expect a well-formed caller to free this.
 */
public enum ZstdCwkspStaticAllocE
{
    ZstdCwkspDynamicAlloc,
    ZstdCwkspStaticAlloc
}