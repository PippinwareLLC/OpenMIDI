using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthVoiceReuseTests
{
    [Fact]
    public void NoteOn_ReusesExistingVoiceForSameNote()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);

        synth.NoteOn(0, 60, 100);
        synth.NoteOn(0, 60, 110);
        RenderOnce(synth);

        Assert.Equal(2, synth.NoteOnCount);
        Assert.Equal(1, synth.SameNoteReuseCount);
        Assert.Equal(1, synth.ActiveVoiceCount);
    }

    private static void RenderOnce(OplSynth synth)
    {
        float[] buffer = new float[2];
        synth.Render(buffer, 0, 1, 44100);
    }
}
