using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplRegisterMapTests
{
    [Fact]
    public void Write_TimerRegisters_UpdateValues()
    {
        OplRegisterMap map = new OplRegisterMap(OplChipType.Opl2);

        map.Write(0x02, 0x12);
        map.Write(0x03, 0x34);

        Assert.Equal(0x12, map.TimerAValue);
        Assert.Equal(0x34, map.TimerBValue);
        Assert.Equal(0x12, map.Read(0x02));
        Assert.Equal(0x34, map.Read(0x03));
    }

    [Fact]
    public void Write_ControlRegister_ParsesFlagsAndClearsStatus()
    {
        OplRegisterMap map = new OplRegisterMap(OplChipType.Opl2);
        map.SetTimerOverflow(OplTimer.A, true);
        map.SetTimerOverflow(OplTimer.B, true);

        map.Write(0x04, 0xE3);

        Assert.True(map.TimerAEnabled);
        Assert.True(map.TimerBEnabled);
        Assert.True(map.TimerAMasked);
        Assert.True(map.TimerBMasked);
        Assert.False(map.TimerAOverflow);
        Assert.False(map.TimerBOverflow);
    }

    [Fact]
    public void Write_RhythmRegister_UpdatesFlags()
    {
        OplRegisterMap map = new OplRegisterMap(OplChipType.Opl2);

        map.Write(0xBD, 0xE5);

        Assert.True(map.TremoloDepth);
        Assert.True(map.VibratoDepth);
        Assert.True(map.RhythmEnabled);
        Assert.Equal(0x05, map.RhythmFlags);
    }

    [Fact]
    public void Write_Opl3Mode_UpdatesState()
    {
        OplRegisterMap map = new OplRegisterMap(OplChipType.Opl3);

        map.Write(0x105, 0x01);
        map.Write(0x104, 0x3F);

        Assert.True(map.Opl3Enabled);
        Assert.Equal(0x3F, map.FourOperatorEnableMask);
    }

    [Fact]
    public void Write_InvalidAddress_Throws()
    {
        OplRegisterMap map = new OplRegisterMap(OplChipType.Opl2);

        Assert.Throws<ArgumentOutOfRangeException>(() => map.Write(0x105, 0x01));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Read(-1));
    }
}
