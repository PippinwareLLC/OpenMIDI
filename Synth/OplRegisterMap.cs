namespace OpenMIDI.Synth;

public enum OplTimer
{
    A,
    B
}

public sealed class OplRegisterMap
{
    private readonly byte[] _registers;

    public OplRegisterMap(OplChipType chipType)
    {
        ChipType = chipType;
        _registers = new byte[chipType == OplChipType.Opl3 ? 0x200 : 0x100];
    }

    public OplChipType ChipType { get; }
    public int RegisterCount => _registers.Length;

    public byte TimerAValue { get; private set; }
    public byte TimerBValue { get; private set; }
    public byte TimerControl { get; private set; }
    public bool TimerAEnabled { get; private set; }
    public bool TimerBEnabled { get; private set; }
    public bool TimerAMasked { get; private set; }
    public bool TimerBMasked { get; private set; }
    public bool TimerAOverflow { get; private set; }
    public bool TimerBOverflow { get; private set; }
    public bool CsmEnabled { get; private set; }
    public bool NoteSelectEnabled { get; private set; }
    public bool TremoloDepth { get; private set; }
    public bool VibratoDepth { get; private set; }
    public bool RhythmEnabled { get; private set; }
    public byte RhythmFlags { get; private set; }
    public bool Opl3Enabled { get; private set; }
    public byte FourOperatorEnableMask { get; private set; }

    public byte Read(int address)
    {
        ValidateAddress(address);
        return _registers[address];
    }

    public void Write(int address, byte value)
    {
        ValidateAddress(address);
        _registers[address] = value;
        ApplySideEffects(address, value);
    }

    public void Reset()
    {
        Array.Clear(_registers, 0, _registers.Length);
        TimerAValue = 0;
        TimerBValue = 0;
        TimerControl = 0;
        TimerAEnabled = false;
        TimerBEnabled = false;
        TimerAMasked = false;
        TimerBMasked = false;
        TimerAOverflow = false;
        TimerBOverflow = false;
        CsmEnabled = false;
        NoteSelectEnabled = false;
        TremoloDepth = false;
        VibratoDepth = false;
        RhythmEnabled = false;
        RhythmFlags = 0;
        Opl3Enabled = false;
        FourOperatorEnableMask = 0;
    }

    public void SetTimerOverflow(OplTimer timer, bool value)
    {
        switch (timer)
        {
            case OplTimer.A:
                TimerAOverflow = value;
                break;
            case OplTimer.B:
                TimerBOverflow = value;
                break;
        }
    }

    public void ClearStatusFlags()
    {
        TimerAOverflow = false;
        TimerBOverflow = false;
    }

    private void ApplySideEffects(int address, byte value)
    {
        switch (address)
        {
            case 0x02:
                TimerAValue = value;
                break;
            case 0x03:
                TimerBValue = value;
                break;
            case 0x04:
                TimerControl = value;
                TimerAEnabled = (value & 0x40) != 0;
                TimerBEnabled = (value & 0x20) != 0;
                TimerAMasked = (value & 0x02) != 0;
                TimerBMasked = (value & 0x01) != 0;
                if ((value & 0x80) != 0)
                {
                    ClearStatusFlags();
                }
                break;
            case 0x08:
                CsmEnabled = (value & 0x80) != 0;
                NoteSelectEnabled = (value & 0x40) != 0;
                break;
            case 0xBD:
                TremoloDepth = (value & 0x80) != 0;
                VibratoDepth = (value & 0x40) != 0;
                RhythmEnabled = (value & 0x20) != 0;
                RhythmFlags = (byte)(value & 0x1F);
                break;
            case 0x104:
                if (ChipType == OplChipType.Opl3)
                {
                    FourOperatorEnableMask = value;
                }
                break;
            case 0x105:
                if (ChipType == OplChipType.Opl3)
                {
                    Opl3Enabled = (value & 0x01) != 0;
                }
                break;
        }
    }

    private void ValidateAddress(int address)
    {
        if (address < 0 || address >= _registers.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(address));
        }
    }
}
