using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthPercussionTests
{
    [Fact]
    public void PercussionNoteOn_SetsRhythmFlags()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);

        synth.NoteOn(9, 36, 100);

        Assert.True(synth.Core.Registers.RhythmEnabled);
        Assert.True((synth.Core.Registers.RhythmFlags & 0x10) != 0);
    }

    [Fact]
    public void PercussionNoteOff_ClearsRhythmFlags()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);

        synth.NoteOn(9, 38, 100);
        synth.NoteOff(9, 38, 0);

        Assert.False(synth.Core.Registers.RhythmEnabled);
        Assert.Equal(0, synth.Core.Registers.RhythmFlags);
    }
}
