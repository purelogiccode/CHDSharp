namespace VendoredZSTD.Unsafe;

public enum FseRepeat
{
    /// <summary>Cannot use the previous table</summary>
    FseRepeatNone,

    /// <summary>Can use the previous table but it must be checked</summary>
    FseRepeatCheck,

    /// <summary>Can use the previous table and it is assumed to be valid</summary>
    FseRepeatValid
}