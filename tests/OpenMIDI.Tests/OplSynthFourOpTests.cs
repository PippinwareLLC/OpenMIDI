using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthFourOpTests
{
    [Fact]
    public void FourOpInstrument_ConfiguresPrimaryAndSecondaryOperators()
    {
        OplOperatorPatch carrier1 = new OplOperatorPatch(0x11, 0x21, 0x31, 0x41, 0x01);
        OplOperatorPatch modulator1 = new OplOperatorPatch(0x12, 0x22, 0x32, 0x42, 0x02);
        OplOperatorPatch carrier2 = new OplOperatorPatch(0x13, 0x23, 0x33, 0x43, 0x03);
        OplOperatorPatch modulator2 = new OplOperatorPatch(0x14, 0x24, 0x34, 0x44, 0x04);

        byte feedback1 = 0x01;
        byte feedback2 = 0x00;

        OplInstrument instrument = new OplInstrument(
            name: "FourOp",
            noteOffset1: 0,
            noteOffset2: 0,
            midiVelocityOffset: 0,
            secondVoiceDetune: 0,
            percussionKeyNumber: 0,
            flags: OplInstrumentFlags.FourOp,
            feedbackConnection1: feedback1,
            feedbackConnection2: feedback2,
            operators: new[] { carrier1, modulator1, carrier2, modulator2 },
            delayOnMs: 0,
            delayOffMs: 0);

        OplSynth synth = new OplSynth(OplSynthMode.Opl3);
        synth.LoadBank(CreateBank(instrument));
        synth.ControlChange(0, 7, 127);
        synth.ControlChange(0, 11, 127);
        synth.ProgramChange(0, 0);
        synth.NoteOn(0, 60, 127);

        OplCore core = synth.Core;
        Assert.Equal((byte)0x01, core.Registers.FourOperatorEnableMask);

        OplChannel primary = core.Channels[0];
        OplChannel secondary = core.Channels[3];

        OplOperator op1 = core.Operators[primary.ModulatorIndex];
        OplOperator op2 = core.Operators[primary.CarrierIndex];
        OplOperator op3 = core.Operators[secondary.ModulatorIndex];
        OplOperator op4 = core.Operators[secondary.CarrierIndex];

        Assert.Equal(modulator1.AmVibEgtKsrMult, op1.AmVibEgtKsrMult);
        Assert.Equal(modulator1.KslTl, op1.KslTl);
        Assert.Equal(modulator1.ArDr, op1.ArDr);
        Assert.Equal(modulator1.SlRr, op1.SlRr);
        Assert.Equal(modulator1.Waveform, op1.Waveform);

        Assert.Equal(carrier1.AmVibEgtKsrMult, op2.AmVibEgtKsrMult);
        Assert.Equal(carrier1.KslTl, op2.KslTl);
        Assert.Equal(carrier1.ArDr, op2.ArDr);
        Assert.Equal(carrier1.SlRr, op2.SlRr);
        Assert.Equal(carrier1.Waveform, op2.Waveform);

        Assert.Equal(modulator2.AmVibEgtKsrMult, op3.AmVibEgtKsrMult);
        Assert.Equal(modulator2.KslTl, op3.KslTl);
        Assert.Equal(modulator2.ArDr, op3.ArDr);
        Assert.Equal(modulator2.SlRr, op3.SlRr);
        Assert.Equal(modulator2.Waveform, op3.Waveform);

        Assert.Equal(carrier2.AmVibEgtKsrMult, op4.AmVibEgtKsrMult);
        Assert.Equal(carrier2.KslTl, op4.KslTl);
        Assert.Equal(carrier2.ArDr, op4.ArDr);
        Assert.Equal(carrier2.SlRr, op4.SlRr);
        Assert.Equal(carrier2.Waveform, op4.Waveform);

        Assert.Equal((feedback1 & 0x01) != 0, primary.Additive);
        Assert.Equal((feedback2 & 0x01) != 0, secondary.Additive);
    }

    private static OplInstrumentBankSet CreateBank(OplInstrument instrument)
    {
        OplInstrument[] melodic = new OplInstrument[128];
        OplInstrument[] percussion = new OplInstrument[128];
        for (int i = 0; i < 128; i++)
        {
            melodic[i] = instrument;
            percussion[i] = instrument;
        }

        OplBank melodicBank = new OplBank("FourOpMelodic", 0, 0, melodic);
        OplBank percussionBank = new OplBank("FourOpPercussion", 0, 0, percussion);
        return new OplInstrumentBankSet(new[] { melodicBank }, new[] { percussionBank },
            deepTremolo: false, deepVibrato: false, volumeModel: OplVolumeModel.Generic, mt32Defaults: false);
    }
}
