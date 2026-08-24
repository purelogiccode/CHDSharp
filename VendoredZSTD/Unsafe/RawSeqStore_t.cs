using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct RawSeqStoreT
{
    /* The start of the sequences */
    public RawSeq* seq;
    /* The index in seq where reading stopped. pos <= size. */
    public nuint pos;
    /* The position within the sequence at seq[pos] where reading
    stopped. posInSequence <= seq[pos].litLength + seq[pos].matchLength */
    public nuint posInSequence;
    /* The number of sequences. <= capacity. */
    public nuint size;
    /* The capacity starting from `seq` pointer */
    public nuint capacity;
    public RawSeqStoreT(RawSeq* seq, nuint pos, nuint posInSequence, nuint size, nuint capacity)
    {
        this.seq = seq;
        this.pos = pos;
        this.posInSequence = posInSequence;
        this.size = size;
        this.capacity = capacity;
    }
}