using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplRhythmModeTests
{
    [Fact]
    public void RhythmMode_IgnoresChannelKeyOnForPercussionChannels()
    {
        OplCore core = new OplCore(OplChipType.Opl2);

        core.WriteRegister(0xBD, 0x20);
        core.WriteRegister(0xB6, 0x20);

        Assert.Equal(OplEnvelopeStage.Off, core.Operators[12].Envelope.Stage);
        Assert.Equal(OplEnvelopeStage.Off, core.Operators[15].Envelope.Stage);
    }

    [Fact]
    public void RhythmMode_BassDrumFlagKeysOperators()
    {
        OplCore core = new OplCore(OplChipType.Opl2);

        core.WriteRegister(0xBD, 0x20 | 0x10);

        Assert.Equal(OplEnvelopeStage.Attack, core.Operators[12].Envelope.Stage);
        Assert.Equal(OplEnvelopeStage.Attack, core.Operators[15].Envelope.Stage);

        core.WriteRegister(0xBD, 0x20);

        Assert.Equal(OplEnvelopeStage.Release, core.Operators[12].Envelope.Stage);
        Assert.Equal(OplEnvelopeStage.Release, core.Operators[15].Envelope.Stage);
    }

    [Fact]
    public void RhythmMode_DisableRestoresChannelKeyOn()
    {
        OplCore core = new OplCore(OplChipType.Opl2);

        core.WriteRegister(0xB6, 0x20);
        Assert.Equal(OplEnvelopeStage.Attack, core.Operators[12].Envelope.Stage);

        core.WriteRegister(0xBD, 0x20);
        Assert.Equal(OplEnvelopeStage.Release, core.Operators[12].Envelope.Stage);

        core.WriteRegister(0xBD, 0x00);
        Assert.Equal(OplEnvelopeStage.Attack, core.Operators[12].Envelope.Stage);
    }
}
