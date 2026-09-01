using VendoredZSTD.Unsafe;

namespace VendoredZSTD;

public class ZstdException : Exception
{
    public ZstdException(ZstdErrorCode code, string message)
        : base(message)
    {
        Code = code;
    }

    public ZstdException()
    {
    }

    public ZstdException(string? message) : base(message)
    {
    }

    public ZstdException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    public ZstdErrorCode Code { get; }
}