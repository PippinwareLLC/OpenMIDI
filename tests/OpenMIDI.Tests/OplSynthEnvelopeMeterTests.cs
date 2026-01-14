using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthEnvelopeMeterTests
{
    [Fact]
    public void NoteOff_KeepsReleaseLevelWhileCountsTrackKeyOn()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);
        synth.LoadBank(CreateSlowReleaseBank());

        synth.NoteOn(0, 60, 100);
        RenderFrames(synth, 32);

        int[] counts = new int[16];
        int[] releaseCounts = new int[16];
        float[] levels = new float[16];
        synth.CopyChannelMeters(counts, releaseCounts, levels);

        Assert.Equal(1, counts[0]);
        Assert.Equal(0, releaseCounts[0]);
        Assert.True(levels[0] > 0f);

        synth.NoteOff(0, 60, 0);
        RenderFrames(synth, 32);
        synth.CopyChannelMeters(counts, releaseCounts, levels);

        Assert.Equal(0, counts[0]);
        Assert.Equal(1, releaseCounts[0]);
        Assert.True(levels[0] > 0f);
    }

    private static void RenderFrames(OplSynth synth, int frames)
    {
        float[] buffer = new float[frames * 2];
        synth.Render(buffer, 0, frames, 44100);
    }

    private static OplInstrumentBankSet CreateSlowReleaseBank()
    {
        OplOperatorPatch modulator = new OplOperatorPatch(
            amVibEgtKsrMult: 0x21,
            kslTl: 0x20,
            arDr: 0xF3,
            slRr: 0xF1,
            waveform: 0x00);

        OplOperatorPatch carrier = new OplOperatorPatch(
            amVibEgtKsrMult: 0x01,
            kslTl: 0x00,
            arDr: 0xF3,
            slRr: 0xF1,
            waveform: 0x00);

        OplInstrument instrument = new OplInstrument(
            name: "SlowRelease",
            noteOffset1: 0,
            noteOffset2: 0,
            midiVelocityOffset: 0,
            secondVoiceDetune: 0,
            percussionKeyNumber: 0,
            flags: OplInstrumentFlags.None,
            feedbackConnection1: 0x04,
            feedbackConnection2: 0x00,
            operators: new[] { carrier, modulator, carrier, modulator },
            delayOnMs: 0,
            delayOffMs: 0);

        OplInstrument[] melodic = new OplInstrument[128];
        OplInstrument[] percussion = new OplInstrument[128];
        for (int i = 0; i < 128; i++)
        {
            melodic[i] = instrument;
            percussion[i] = instrument;
        }

        OplBank melodicBank = new OplBank("TestMelodic", 0, 0, melodic);
        OplBank percussionBank = new OplBank("TestPercussion", 0, 0, percussion);
        return new OplInstrumentBankSet(new[] { melodicBank }, new[] { percussionBank },
            deepTremolo: false, deepVibrato: false, volumeModel: OplVolumeModel.Generic, mt32Defaults: false);
    }
}
