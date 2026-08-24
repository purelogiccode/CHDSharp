namespace VendoredZSTD.Unsafe;

public enum ZstdDictContentTypeE
{
    /* dictionary is "full" when starting with ZSTD_MAGIC_DICTIONARY, otherwise it is "rawContent" */
    ZstdDctAuto = 0,
    /* ensures dictionary is always loaded as rawContent, even if it starts with ZSTD_MAGIC_DICTIONARY */
    ZstdDctRawContent = 1,
    /* refuses to load a dictionary if it does not respect Zstandard's specification, starting with ZSTD_MAGIC_DICTIONARY */
    ZstdDctFullDict = 2
}