namespace VendoredZSTD.Unsafe;

public enum ZstdFormatE
{
    /* zstd frame format, specified in zstd_compression_format.md (default) */
    ZstdFZstd1 = 0,
    /* Variant of zstd frame format, without initial 4-bytes magic number.
     * Useful to save 4 bytes per generated frame.
     * Decoder cannot recognise automatically this format, requiring this instruction. */
    ZstdFZstd1Magicless = 1
}