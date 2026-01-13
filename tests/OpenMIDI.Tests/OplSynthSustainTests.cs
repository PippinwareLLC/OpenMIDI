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
    public void SustainPedal_SustainedVoiceIsStealCandidate()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);

        synth.ControlChange(0, 64, 127);
        synth.NoteOn(0, 60, 100);
        synth.NoteOff(0, 60, 0);

        for (int i = 0; i < 8; i++)
        {
            synth.NoteOn(1, 70 + i, 100);
        }

        RenderOnce(synth);

        int[] counts = new int[16];
        float[] levels = new float[16];
        synth.CopyChannelMeters(counts, levels);
        Assert.Equal(1, counts[0]);

        synth.NoteOn(1, 80, 100);
        RenderOnce(synth);
        synth.CopyChannelMeters(counts, levels);
        Assert.Equal(0, counts[0]);
    }

    private static void RenderOnce(OplSynth synth)
    {
        float[] buffer = new float[2];
        synth.Render(buffer, 0, 1, 44100);
    }
}
