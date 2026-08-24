using VendoredZSTD.Unsafe;

namespace VendoredZSTD;

public class ZstdException : Exception
{
    public ZstdException(ZstdErrorCode code, string message) : base(message)
    {
        Code = code;
    }

    public ZstdErrorCode Code { get; }
}