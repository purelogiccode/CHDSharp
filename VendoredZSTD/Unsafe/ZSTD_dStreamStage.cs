namespace VendoredZSTD.Unsafe;

public enum ZstdDStreamStage
{
    ZdssInit = 0,
    ZdssLoadHeader,
    ZdssRead,
    ZdssLoad,
    ZdssFlush
}