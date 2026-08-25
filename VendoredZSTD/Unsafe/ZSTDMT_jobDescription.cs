using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdmtJobDescription
{
    /* SHARED - set0 by mtctx, then modified by worker AND read by mtctx */
    public nuint consumed;

    /* SHARED - set0 by mtctx, then modified by worker AND read by mtctx, then set0 by mtctx */
    public nuint cSize;

    /* Thread-safe - used by mtctx and worker */
    public void* job_mutex;

    /* Thread-safe - used by mtctx and worker */
    public void* job_cond;

    /* Thread-safe - used by mtctx and (all) workers */
    public ZstdmtCCtxPool* cctxPool;

    /* Thread-safe - used by mtctx and (all) workers */
    public ZstdmtBufferPoolS* bufPool;

    /* Thread-safe - used by mtctx and (all) workers */
    public ZstdmtBufferPoolS* seqPool;

    /* Thread-safe - used by mtctx and (all) workers */
    public SerialStateT* serial;

    /* set by worker (or mtctx), then read by worker & mtctx, then modified by mtctx => no barrier */
    public BufferS dstBuff;

    /* set by mtctx, then read by worker & mtctx => no barrier */
    public RangeT prefix;

    /* set by mtctx, then read by worker & mtctx => no barrier */
    public RangeT src;

    /* set by mtctx, then read by worker => no barrier */
    public uint jobID;

    /* set by mtctx, then read by worker => no barrier */
    public uint firstJob;

    /* set by mtctx, then read by worker => no barrier */
    public uint lastJob;

    /* set by mtctx, then read by worker => no barrier */
    public ZstdCCtxParamsS @params;

    /* set by mtctx, then read by worker => no barrier */
    public ZstdCDictS* cdict;

    /* set by mtctx, then read by worker => no barrier */
    public ulong fullFrameSize;

    /* used only by mtctx */
    public nuint dstFlushed;

    /* used only by mtctx */
    public uint frameChecksumNeeded;
}