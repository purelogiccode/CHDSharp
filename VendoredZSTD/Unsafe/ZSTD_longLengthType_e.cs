namespace VendoredZSTD.Unsafe;

/* Controls whether seqStore has a single "long" litLength or matchLength. See seqStore_t. */
public enum ZstdLongLengthTypeE
{
    /* no longLengthType */
    ZstdLltNone = 0,

    /* represents a long literal */
    ZstdLltLiteralLength = 1,

    /* represents a long match */
    ZstdLltMatchLength = 2
}