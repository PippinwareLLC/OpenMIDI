using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthSustainTests
{
    [Fact]
    public void SustainPedal_KeepsKeyOnUntilReleased()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);

        synth.NoteOn(0, 60, 100);
        synth.ControlChange(0, 64, 127);
        synth.NoteOff(0, 60, 0);

        byte b0 = synth.Core.ReadRegister(0xB0);
        Assert.True((b0 & 0x20) != 0);

        synth.ControlChange(0, 64, 0);
        byte b0Off = synth.Core.ReadRegister(0xB0);
        Assert.True((b0Off & 0x20) == 0);
    }

    [Fact]
    public void SustainPedal_ProtectsSustainedVoiceFromSteal()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);

        for (int i = 0; i < 9; i++)
        {
            synth.NoteOn(0, 60 + i, 100);
        }

        synth.ControlChange(0, 64, 127);
        synth.NoteOff(0, 60, 0);

        int a0Before = synth.Core.ReadRegister(0xA0);
        int b0Before = synth.Core.ReadRegister(0xB0);
        int fnumBefore = a0Before | ((b0Before & 0x03) << 8);
        int blockBefore = (b0Before >> 2) & 0x07;

        synth.NoteOn(1, 84, 100);

        int a0After = synth.Core.ReadRegister(0xA0);
        int b0After = synth.Core.ReadRegister(0xB0);
        int fnumAfter = a0After | ((b0After & 0x03) << 8);
        int blockAfter = (b0After >> 2) & 0x07;

        Assert.Equal(fnumBefore, fnumAfter);
        Assert.Equal(blockBefore, blockAfter);
    }
}
