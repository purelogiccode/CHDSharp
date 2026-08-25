using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ZstdmtCCtxS
{
    public void* factory;
    public ZstdmtJobDescription* jobs;
    public ZstdmtBufferPoolS* bufPool;
    public ZstdmtCCtxPool* cctxPool;
    public ZstdmtBufferPoolS* seqPool;
    public ZstdCCtxParamsS @params;
    public nuint targetSectionSize;
    public nuint targetPrefixSize;

    /* 1 => one job is already prepared, but pool has shortage of workers. Don't create a new job. */
    public int jobReady;
    public InBuffT inBuff;
    public RoundBuffT roundBuff;
    public SerialStateT serial;
    public RsyncStateT rsync;
    public uint jobIDMask;
    public uint doneJobID;
    public uint nextJobID;
    public uint frameEnded;
    public uint allJobsCompleted;
    public ulong frameContentSize;
    public ulong consumed;
    public ulong produced;
    public ZstdCustomMem cMem;
    public ZstdCDictS* cdictLocal;
    public ZstdCDictS* cdict;
    public uint providedFactory;
}