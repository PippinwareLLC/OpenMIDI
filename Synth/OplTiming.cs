namespace OpenMIDI.Synth;

public enum OplChipType
{
    Opl1,
    Opl2,
    Opl3
}

public static class OplTiming
{
    public const double Opl1ClockHz = 3579545.0;
    public const double Opl2ClockHz = 3579545.0;
    public const double Opl3ClockHz = 14318180.0;

    public const int Opl1SampleDivider = 72;
    public const int Opl2SampleDivider = 72;
    public const int Opl3SampleDivider = 288;

    public const int TimerABits = 10;
    public const int TimerBBits = 8;
    public const int TimerADivider = 288;
    public const int TimerBDivider = 1152;

    public static double GetChipClockHz(OplChipType chip)
    {
        return chip switch
        {
            OplChipType.Opl1 => Opl1ClockHz,
            OplChipType.Opl2 => Opl2ClockHz,
            OplChipType.Opl3 => Opl3ClockHz,
            _ => Opl2ClockHz
        };
    }

    public static int GetSampleDivider(OplChipType chip)
    {
        return chip switch
        {
            OplChipType.Opl1 => Opl1SampleDivider,
            OplChipType.Opl2 => Opl2SampleDivider,
            OplChipType.Opl3 => Opl3SampleDivider,
            _ => Opl2SampleDivider
        };
    }

    public static double GetChipSampleRateHz(OplChipType chip)
    {
        return GetChipClockHz(chip) / GetSampleDivider(chip);
    }

    public static double GetTimerATickSeconds(OplChipType chip)
    {
        return TimerADivider / GetChipClockHz(chip);
    }

    public static double GetTimerBTickSeconds(OplChipType chip)
    {
        return TimerBDivider / GetChipClockHz(chip);
    }

    public static double GetTimerAOverflowSeconds(int value, OplChipType chip)
    {
        int max = 1 << TimerABits;
        int clamped = Math.Clamp(value, 0, max - 1);
        return (max - clamped) * GetTimerATickSeconds(chip);
    }

    public static double GetTimerBOverflowSeconds(int value, OplChipType chip)
    {
        int max = 1 << TimerBBits;
        int clamped = Math.Clamp(value, 0, max - 1);
        return (max - clamped) * GetTimerBTickSeconds(chip);
    }

    public static double ConvertChipSamplesToOutputSamples(double chipSamples, int outputSampleRate, OplChipType chip)
    {
        if (outputSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputSampleRate));
        }

        return chipSamples * outputSampleRate / GetChipSampleRateHz(chip);
    }

    public static double ConvertOutputSamplesToChipSamples(double outputSamples, int outputSampleRate, OplChipType chip)
    {
        if (outputSampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputSampleRate));
        }

        return outputSamples * GetChipSampleRateHz(chip) / outputSampleRate;
    }
}
