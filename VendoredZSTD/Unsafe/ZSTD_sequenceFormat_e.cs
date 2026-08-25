namespace VendoredZSTD.Unsafe;

public enum ZstdSequenceFormatE
{
    /* Representation of ZSTD_Sequence has no block delimiters, sequences only */
    ZstdSfNoBlockDelimiters = 0,

    /* Representation of ZSTD_Sequence contains explicit block delimiters */
    ZstdSfExplicitBlockDelimiters = 1
}