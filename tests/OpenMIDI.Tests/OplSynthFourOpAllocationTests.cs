using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthFourOpAllocationTests
{
    [Fact]
    public void FourOpMask_ReservesSecondaryChannels()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl3);

        synth.Core.WriteRegister(0x104, 0x01);
        for (int i = 0; i < 18; i++)
        {
            synth.NoteOn(0, 60 + i, 100);
        }

        Assert.Equal(1, synth.VoiceStealCount);
    }
}
