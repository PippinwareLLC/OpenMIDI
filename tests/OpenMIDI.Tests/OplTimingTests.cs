using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplTimingTests
{
    [Fact]
    public void ChipSampleRates_MatchExpectedClockDividers()
    {
        double opl1Rate = OplTiming.GetChipSampleRateHz(OplChipType.Opl1);
        double opl2Rate = OplTiming.GetChipSampleRateHz(OplChipType.Opl2);
        double opl3Rate = OplTiming.GetChipSampleRateHz(OplChipType.Opl3);

        Assert.InRange(opl1Rate, 49715.0, 49717.0);
        Assert.InRange(opl2Rate, 49715.0, 49717.0);
        Assert.InRange(opl3Rate, 49715.0, 49717.0);
    }

    [Fact]
    public void SampleConversion_UsesChipRateToScaleOutput()
    {
        double opl2Rate = OplTiming.GetChipSampleRateHz(OplChipType.Opl2);
        double outputSamples = OplTiming.ConvertChipSamplesToOutputSamples(opl2Rate, 44100, OplChipType.Opl2);

        Assert.InRange(outputSamples, 44099.0, 44101.0);
    }

    [Fact]
    public void TimerTicks_AreInExpectedRange()
    {
        double timerATick = OplTiming.GetTimerATickSeconds(OplChipType.Opl2);
        double timerBTick = OplTiming.GetTimerBTickSeconds(OplChipType.Opl2);

        Assert.InRange(timerATick, 0.000079, 0.000082);
        Assert.InRange(timerBTick, 0.000315, 0.00033);
    }
}
