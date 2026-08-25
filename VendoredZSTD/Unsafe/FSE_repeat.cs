namespace VendoredZSTD.Unsafe;

public enum FseRepeat
{
    /*
     * < Cannot use the previous table
     */
    FseRepeatNone,

    /*
     * < Can use the previous table but it must be checked
     */
    FseRepeatCheck,

    /*
     * < Can use the previous table and it is assumed to be valid
     */
    FseRepeatValid
}