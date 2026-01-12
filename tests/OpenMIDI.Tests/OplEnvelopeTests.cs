using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplEnvelopeTests
{
    [Fact]
    public void AttenuationIncrementTable_MatchesReferenceSamples()
    {
        Assert.Equal(0u, OplEnvelopeTables.GetAttenuationIncrement(0, 0));
        Assert.Equal(1u, OplEnvelopeTables.GetAttenuationIncrement(2, 1));
        Assert.Equal(0u, OplEnvelopeTables.GetAttenuationIncrement(6, 0));
        Assert.Equal(8u, OplEnvelopeTables.GetAttenuationIncrement(60, 3));
    }

    [Fact]
    public void Envelope_AttackUsesEffectiveRateTable()
    {
        OplEnvelope envelope = new OplEnvelope();
        envelope.Configure(4, 0, 0, 0, sustainEnabled: true, keyScaleRate: true, keyCode: 12);
        envelope.KeyOn();

        uint envCounter = 16;
        envelope.Step(envCounter);

        int rawRate = 4 * 4;
        int ksrValue = 12;
        int effectiveRate = Math.Min(rawRate + ksrValue, 63);
        int increment = (int)OplEnvelopeTables.GetAttenuationIncrement(effectiveRate, 1);
        int expected = 0x3FF + ((~0x3FF * increment) >> 4);
        if (expected < 0)
        {
            expected = 0;
        }

        Assert.Equal(expected, envelope.Attenuation);
        Assert.Equal(OplEnvelopeStage.Attack, envelope.Stage);
    }
}
