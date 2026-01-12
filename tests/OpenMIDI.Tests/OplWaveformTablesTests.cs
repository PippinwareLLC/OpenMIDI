using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplWaveformTablesTests
{
    [Fact]
    public void WaveformTable_ReturnsExpectedSample()
    {
        ushort sample = OplWaveformTables.GetWaveformSample(0, 0);
        Assert.Equal((ushort)0x0859, sample);
    }

    [Fact]
    public void AttenuationToVolume_ZeroAttenuationMatchesReference()
    {
        int volume = OplWaveformTables.AttenuationToVolume(0);
        Assert.Equal(0x1FE8, volume);
    }
}
