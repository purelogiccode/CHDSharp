namespace VendoredZSTD.Unsafe;

public enum ZstdDStage
{
    ZstDdsGetFrameHeaderSize,
    ZstDdsDecodeFrameHeader,
    ZstDdsDecodeBlockHeader,
    ZstDdsDecompressBlock,
    ZstDdsDecompressLastBlock,
    ZstDdsCheckChecksum,
    ZstDdsDecodeSkippableHeader,
    ZstDdsSkipFrame
}