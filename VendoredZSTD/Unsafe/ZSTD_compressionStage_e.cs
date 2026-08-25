namespace VendoredZSTD.Unsafe;

/*-*************************************
 *  Context memory management
 ***************************************/
public enum ZstdCompressionStageE
{
    ZstDcsCreated = 0,
    ZstDcsInit,
    ZstDcsOngoing,
    ZstDcsEnding
}