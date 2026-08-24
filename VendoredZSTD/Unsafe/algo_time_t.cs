using System.Runtime.InteropServices;

namespace VendoredZSTD.Unsafe;

[StructLayout(LayoutKind.Sequential)]
public struct AlgoTimeT
{
    public uint tableTime;
    public uint decode256Time;

    public AlgoTimeT(uint tableTime, uint decode256Time)
    {
        this.tableTime = tableTime;
        this.decode256Time = decode256Time;
    }
}