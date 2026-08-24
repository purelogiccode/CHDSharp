using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct SerialState
{
    /* All variables in the struct are protected by mutex. */
    public void* mutex;
    public void* cond;
    public ZstdCCtxParamsS @params;
    public LdmStateT ldmState;
    public Xxh64StateS xxhState;

    public uint nextJobID;

    /* Protects ldmWindow.
     * Must be acquired after the main mutex when acquiring both.
     */
    public void* ldmWindowMutex;

    /* Signaled when ldmWindow is updated */
    public void* ldmWindowCond;

    /* A thread-safe copy of ldmState.window */
    public ZstdWindowT ldmWindow;
}