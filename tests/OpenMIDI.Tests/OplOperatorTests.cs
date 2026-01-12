using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplOperatorTests
{
    [Fact]
    public void OperatorRegisters_UpdateOperatorFields()
    {
        OplCore core = new OplCore(OplChipType.Opl2);

        core.WriteRegister(0x20, 0xF1);
        core.WriteRegister(0x40, 0x3F);
        core.WriteRegister(0x60, 0xA4);
        core.WriteRegister(0x80, 0x57);
        core.WriteRegister(0xE0, 0x03);

        OplOperator op = core.Operators[0];

        Assert.True(op.Tremolo);
        Assert.True(op.Vibrato);
        Assert.True(op.Sustain);
        Assert.True(op.KeyScaleRate);
        Assert.Equal(1, op.Multiple);
        Assert.Equal(0, op.KeyScaleLevel);
        Assert.Equal(0x3F, op.TotalLevel);
        Assert.Equal(0x0A, op.AttackRate);
        Assert.Equal(0x04, op.DecayRate);
        Assert.Equal(0x05, op.SustainLevel);
        Assert.Equal(0x07, op.ReleaseRate);
        Assert.Equal(3, op.WaveformIndex);
    }

    [Fact]
    public void ChannelKeyOn_StartsEnvelopeAndRelease()
    {
        OplCore core = new OplCore(OplChipType.Opl2);

        core.WriteRegister(0x60, 0xF0);
        core.WriteRegister(0x80, 0x00);
        core.WriteRegister(0xA0, 0x00);
        core.WriteRegister(0xB0, 0x20);

        OplOperator mod = core.Operators[0];
        Assert.Equal(OplEnvelopeStage.Attack, mod.Envelope.Stage);

        core.StepSeconds(0.01);
        Assert.True(mod.Envelope.Level > 0f);

        float beforeRelease = mod.Envelope.Level;
        core.WriteRegister(0xB0, 0x00);
        Assert.Equal(OplEnvelopeStage.Release, mod.Envelope.Stage);

        core.StepSeconds(0.01);
        Assert.True(mod.Envelope.Level <= beforeRelease);
    }

    [Fact]
    public void ChannelRegisters_UpdateFrequencyAndBlock()
    {
        OplCore core = new OplCore(OplChipType.Opl2);

        core.WriteRegister(0xA0, 0x34);
        core.WriteRegister(0xB0, 0x27);

        OplChannel channel = core.Channels[0];

        Assert.Equal(0x334, channel.FNum);
        Assert.Equal(0x01, channel.Block);
        Assert.True(channel.KeyOn);
    }
}
