namespace VendoredZSTD.Unsafe;

/*
 * Indicates whether this compression proceeds directly from user-provided
 * source buffer to user-provided destination buffer (ZSTDb_not_buffered), or
 * whether the context needs to buffer the input/output (ZSTDb_buffered).
 */
public enum ZstdBufferedPolicyE
{
    ZstDbNotBuffered,
    ZstDbBuffered
}