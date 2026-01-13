using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthMultiChipTests
{
    [Fact]
    public void Opl3WithTwoChips_UsesSecondChipChannels()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl3, chips: 2);

        for (int i = 0; i < 19; i++)
        {
            synth.NoteOn(0, 60 + i, 100);
        }

        Assert.Equal(2, synth.ChipCount);
        Assert.Equal(36, synth.VoiceCount);
        Assert.True(synth.Cores[1].Channels[0].KeyOn);
    }
}
