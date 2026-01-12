namespace OpenMIDI.Synth;

public sealed class OplChannel
{
    public OplChannel(int index, int modulatorIndex, int carrierIndex)
    {
        Index = index;
        ModulatorIndex = modulatorIndex;
        CarrierIndex = carrierIndex;
    }

    public int Index { get; }
    public int ModulatorIndex { get; }
    public int CarrierIndex { get; }
    public int FNum { get; private set; }
    public int Block { get; private set; }
    public bool KeyOn { get; private set; }
    public byte Feedback { get; private set; }
    public bool Additive { get; private set; }
    public bool LeftEnable { get; private set; }
    public bool RightEnable { get; private set; }
    public byte RawFeedbackConnection { get; private set; }
    public int KeyCode { get; private set; }

    public void Reset()
    {
        FNum = 0;
        Block = 0;
        KeyOn = false;
        Feedback = 0;
        Additive = false;
        LeftEnable = false;
        RightEnable = false;
        RawFeedbackConnection = 0;
        KeyCode = 0;
    }

    public void ApplyFrequencyLow(byte value)
    {
        FNum = (FNum & 0x300) | value;
    }

    public void ApplyBlockKeyOn(byte value)
    {
        FNum = (FNum & 0xFF) | ((value & 0x03) << 8);
        Block = (value >> 2) & 0x07;
        KeyOn = (value & 0x20) != 0;
    }

    public void ApplyFeedbackConnection(byte value)
    {
        RawFeedbackConnection = value;
        Feedback = (byte)((value >> 1) & 0x07);
        Additive = (value & 0x01) != 0;
        LeftEnable = (value & 0x10) != 0;
        RightEnable = (value & 0x20) != 0;
    }

    public void UpdateKeyCode(bool noteSelectEnabled)
    {
        int shift = noteSelectEnabled ? 8 : 9;
        KeyCode = (Block << 1) | ((FNum >> shift) & 0x01);
    }
}
