namespace VendoredZSTD.Unsafe;

public enum ZstdDictLoadMethodE
{
    /**
     * < Copy dictionary content internally
     */
    ZstdDlmByCopy = 0,

    /**
     * < Reference dictionary content -- the dictionary buffer must outlive its users.
     */
    ZstdDlmByRef = 1
}