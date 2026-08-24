namespace VendoredZSTD.Unsafe;

public enum ZstdDictAttachPrefE
{
    /* Use the default heuristic. */
    ZstdDictDefaultAttach = 0,
    /* Never copy the dictionary. */
    ZstdDictForceAttach = 1,
    /* Always copy the dictionary. */
    ZstdDictForceCopy = 2,
    /* Always reload the dictionary */
    ZstdDictForceLoad = 3
}