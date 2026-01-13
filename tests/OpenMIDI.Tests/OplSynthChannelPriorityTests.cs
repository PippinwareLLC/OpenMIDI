using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthChannelPriorityTests
{
    [Fact]
    public void VoiceSteal_PrefersLowestPriorityChannel()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);

        for (int i = 0; i < 4; i++)
        {
            synth.NoteOn(0, 60 + i, 100);
        }

        for (int i = 0; i < 4; i++)
        {
            synth.NoteOn(1, 70 + i, 100);
        }

        synth.NoteOn(15, 80, 100);
        RenderOnce(synth);

        int[] counts = new int[16];
        float[] levels = new float[16];
        synth.CopyChannelMeters(counts, levels);
        Assert.Equal(1, counts[15]);

        synth.NoteOn(0, 90, 100);
        RenderOnce(synth);
        synth.CopyChannelMeters(counts, levels);
        Assert.Equal(0, counts[15]);
    }

    private static void RenderOnce(OplSynth synth)
    {
        float[] buffer = new float[2];
        synth.Render(buffer, 0, 1, 44100);
    }
}
