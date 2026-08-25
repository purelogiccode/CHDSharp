namespace VendoredZSTD.Unsafe;

public enum ZstdNextInputTypeE
{
    ZstDnitFrameHeader,
    ZstDnitBlockHeader,
    ZstDnitBlock,
    ZstDnitLastBlock,
    ZstDnitChecksum,
    ZstDnitSkippableFrame
}