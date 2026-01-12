using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplCoreTests
{
    [Fact]
    public void TimerA_OverflowSetsFlagsAndIrq()
    {
        OplCore core = new OplCore(OplChipType.Opl2);
        core.WriteRegister(0x02, 0x10);
        core.WriteRegister(0x04, 0x40);

        double period = OplTiming.GetTimerAOverflowSeconds(0x10 << 2, OplChipType.Opl2);
        core.StepSeconds(period + 0.0001);

        Assert.True(core.Registers.TimerAOverflow);
        Assert.True(core.IrqActive);
        Assert.Equal(0xC0, core.Status & 0xC0);
    }

    [Fact]
    public void TimerA_MaskedDoesNotSetIrq()
    {
        OplCore core = new OplCore(OplChipType.Opl2);
        core.WriteRegister(0x02, 0x20);
        core.WriteRegister(0x04, 0x42);

        double period = OplTiming.GetTimerAOverflowSeconds(0x20 << 2, OplChipType.Opl2);
        core.StepSeconds(period + 0.0001);

        Assert.True(core.Registers.TimerAOverflow);
        Assert.False(core.IrqActive);
    }

    [Fact]
    public void ControlResetClearsOverflowAndIrq()
    {
        OplCore core = new OplCore(OplChipType.Opl2);
        core.WriteRegister(0x02, 0x30);
        core.WriteRegister(0x04, 0x40);

        double period = OplTiming.GetTimerAOverflowSeconds(0x30 << 2, OplChipType.Opl2);
        core.StepSeconds(period + 0.0001);

        Assert.True(core.Registers.TimerAOverflow);
        Assert.True(core.IrqActive);

        core.WriteRegister(0x04, 0x80);

        Assert.False(core.Registers.TimerAOverflow);
        Assert.False(core.IrqActive);
        Assert.Equal(0x00, core.Status);
    }

    [Fact]
    public void ReadStatus_ReturnsStatusRegister()
    {
        OplCore core = new OplCore(OplChipType.Opl2);
        core.WriteRegister(0x02, 0x10);
        core.WriteRegister(0x04, 0x40);

        double period = OplTiming.GetTimerAOverflowSeconds(0x10 << 2, OplChipType.Opl2);
        core.StepSeconds(period + 0.0001);

        Assert.Equal(core.Status, core.ReadStatus());
        Assert.Equal(core.Status, core.ReadRegister(0x00));
    }
}
