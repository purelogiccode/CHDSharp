using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdBlockSplitCtx
{
    public SeqStoreT fullSeqStoreChunk;
    public SeqStoreT firstHalfSeqStore;
    public SeqStoreT secondHalfSeqStore;
    public SeqStoreT currSeqStore;
    public SeqStoreT nextSeqStore;
    public fixed uint partitions[196];
    public ZstdEntropyCTablesMetadataT entropyMetadata;
}