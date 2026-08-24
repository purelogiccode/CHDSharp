namespace VendoredZSTD.Unsafe;

public enum ZstdDictUsesE
{
    /* Use the dictionary indefinitely */
    ZstdUseIndefinitely = -1,
    /* Do not use the dictionary (if one exists free it) */
    ZstdDontUse = 0,
    /* Use the dictionary once and set to ZSTD_dont_use */
    ZstdUseOnce = 1
}