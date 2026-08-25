namespace VendoredZSTD.Unsafe;

public enum HufRepeat
{
    /**
     * < Cannot use the previous table
     */
    HufRepeatNone,

    /**
     * < Can use the previous table but it must be checked. Note : The previous table must have been constructed by
     *     HUF_compress{1, 4} X_repeat
     */
    HufRepeatCheck,

    /**
     * < Can use the previous table and it is assumed to be valid
     */
    HufRepeatValid
}