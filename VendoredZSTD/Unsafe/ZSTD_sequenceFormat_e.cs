namespace VendoredZSTD.Unsafe;

public enum ZstdSequenceFormatE
{
    /* ZSTD_Sequence[] has no block delimiters, just sequences */
    ZstdSfNoBlockDelimiters = 0,
    /* ZSTD_Sequence[] contains explicit block delimiters */
    ZstdSfExplicitBlockDelimiters = 1
}