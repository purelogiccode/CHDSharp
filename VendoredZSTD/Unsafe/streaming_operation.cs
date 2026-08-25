namespace VendoredZSTD.Unsafe;

/* Streaming state is used to inform allocation of the literal buffer */
public enum StreamingOperation
{
    NotStreaming = 0,
    IsStreaming = 1
}