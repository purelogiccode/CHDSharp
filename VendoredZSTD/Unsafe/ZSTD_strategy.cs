namespace VendoredZSTD.Unsafe;

/* Compression strategies, listed from fastest to strongest */
public enum ZstdStrategy
{
    ZstdFast = 1,
    ZstdDfast = 2,
    ZstdGreedy = 3,
    ZstdLazy = 4,
    ZstdLazy2 = 5,
    ZstdBtlazy2 = 6,
    ZstdBtopt = 7,
    ZstdBtultra = 8,
    ZstdBtultra2 = 9
}