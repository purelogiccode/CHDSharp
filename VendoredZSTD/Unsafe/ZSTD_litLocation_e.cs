namespace VendoredZSTD.Unsafe;

public enum ZstdLitLocationE
{
    /* Stored entirely within litExtraBuffer */
    ZstdNotInDst = 0,
    /* Stored entirely within dst (in memory after current output write) */
    ZstdInDst = 1,
    /* Split between litExtraBuffer and dst */
    ZstdSplit = 2
}