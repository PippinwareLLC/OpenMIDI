using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthAllocatorGoodnessTests
{
    private const byte VolumeModelAuto = 0;
    private const byte VolumeModelHmi = 10;
    private const byte VolumeModelMsAdlib = 12;

    [Fact]
    public void Allocator_OffDelayPrefersFreeChannel()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);
        synth.LoadBank(CreateBank(volumeModel: VolumeModelAuto, delayOffMs: 200));

        synth.ProgramChange(0, 1);
        synth.NoteOn(0, 60, 100);
        synth.NoteOff(0, 60, 0);

        int reuseBefore = synth.ReleaseReuseCount;
        synth.ProgramChange(0, 2);
        synth.NoteOn(0, 62, 100);

        Assert.Equal(reuseBefore, synth.ReleaseReuseCount);
    }

    [Fact]
    public void Allocator_SameInstrumentModePrefersReleasedSameInstrument()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);
        synth.LoadBank(CreateBank(volumeModel: VolumeModelMsAdlib, delayOffMs: 200));

        synth.ProgramChange(0, 1);
        synth.NoteOn(0, 60, 100);
        synth.NoteOff(0, 60, 0);

        int reuseBefore = synth.ReleaseReuseCount;
        synth.NoteOn(0, 62, 100);

        Assert.Equal(reuseBefore + 1, synth.ReleaseReuseCount);
    }

    [Fact]
    public void Allocator_AnyReleasedModePrefersReleasedChannel()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);
        synth.LoadBank(CreateBank(volumeModel: VolumeModelHmi, delayOffMs: 200));

        synth.ProgramChange(0, 1);
        synth.NoteOn(0, 60, 100);
        synth.NoteOff(0, 60, 0);

        int reuseBefore = synth.ReleaseReuseCount;
        synth.ProgramChange(0, 2);
        synth.NoteOn(0, 62, 100);

        Assert.Equal(reuseBefore + 1, synth.ReleaseReuseCount);
    }

    private static OplInstrumentBankSet CreateBank(byte volumeModel, ushort delayOffMs)
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
            name: "AllocTest",
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
            delayOffMs: delayOffMs);

        OplInstrument[] melodic = new OplInstrument[128];
        OplInstrument[] percussion = new OplInstrument[128];
        for (int i = 0; i < 128; i++)
        {
            melodic[i] = instrument;
            percussion[i] = instrument;
        }

        OplBank melodicBank = new OplBank("AllocMelodic", 0, 0, melodic);
        OplBank percussionBank = new OplBank("AllocPercussion", 0, 0, percussion);
        return new OplInstrumentBankSet(new[] { melodicBank }, new[] { percussionBank },
            deepTremolo: false, deepVibrato: false, volumeModel: volumeModel);
    }
}
