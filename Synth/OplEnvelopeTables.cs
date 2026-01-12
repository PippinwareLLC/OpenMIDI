namespace OpenMIDI.Synth;

public static class OplEnvelopeTables
{
    // Table data derived from ymfm (BSD 3-Clause, Aaron Giles) for OPL envelope increments.
    private static readonly uint[] AttenuationIncrementTable =
    {
        0x00000000, 0x00000000, 0x10101010, 0x10101010,
        0x10101010, 0x10101010, 0x11101110, 0x11101110,
        0x10101010, 0x10111010, 0x11101110, 0x11111110,
        0x10101010, 0x10111010, 0x11101110, 0x11111110,
        0x10101010, 0x10111010, 0x11101110, 0x11111110,
        0x10101010, 0x10111010, 0x11101110, 0x11111110,
        0x10101010, 0x10111010, 0x11101110, 0x11111110,
        0x10101010, 0x10111010, 0x11101110, 0x11111110,
        0x10101010, 0x10111010, 0x11101110, 0x11111110,
        0x10101010, 0x10111010, 0x11101110, 0x11111110,
        0x10101010, 0x10111010, 0x11101110, 0x11111110,
        0x10101010, 0x10111010, 0x11101110, 0x11111110,
        0x11111111, 0x21112111, 0x21212121, 0x22212221,
        0x22222222, 0x42224222, 0x42424242, 0x44424442,
        0x44444444, 0x84448444, 0x84848484, 0x88848884,
        0x88888888, 0x88888888, 0x88888888, 0x88888888
    };

    public static readonly double[] MultipleTable =
    {
        0.5, 1.0, 2.0, 3.0,
        4.0, 5.0, 6.0, 7.0,
        8.0, 9.0, 10.0, 10.0,
        12.0, 12.0, 15.0, 15.0
    };

    public static uint GetAttenuationIncrement(int rate, int index)
    {
        if (rate < 0 || rate >= AttenuationIncrementTable.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }

        if (index < 0 || index > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        uint packed = AttenuationIncrementTable[rate];
        return (packed >> (index * 4)) & 0x0F;
    }
}
