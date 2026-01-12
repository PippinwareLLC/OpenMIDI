namespace OpenMIDI.Synth;

public sealed class OplCore
{
    private static readonly int[] ChannelModulatorIndex =
    {
        0, 1, 2, 6, 7, 8, 12, 13, 14
    };

    private static readonly int[] ChannelCarrierIndex =
    {
        3, 4, 5, 9, 10, 11, 15, 16, 17
    };

    private static readonly int[] OperatorOffsetToIndex =
    {
        0, 1, 2, 3, 4, 5, -1, -1,
        6, 7, 8, 9, 10, 11, -1, -1,
        12, 13, 14, 15, 16, 17, -1, -1,
        -1, -1, -1, -1, -1, -1, -1, -1
    };

    private double _timerARemainingSeconds;
    private double _timerBRemainingSeconds;
    private readonly OplOperator[] _operators;
    private readonly OplChannel[] _channels;

    public OplCore(OplChipType chipType)
    {
        ChipType = chipType;
        Registers = new OplRegisterMap(chipType);
        int channelCount = chipType == OplChipType.Opl3 ? 18 : 9;
        _channels = new OplChannel[channelCount];
        _operators = new OplOperator[channelCount * 2];

        for (int i = 0; i < _operators.Length; i++)
        {
            _operators[i] = new OplOperator(i);
        }

        for (int i = 0; i < _channels.Length; i++)
        {
            int bank = i / 9;
            int local = i % 9;
            int modIndex = ChannelModulatorIndex[local] + bank * 18;
            int carIndex = ChannelCarrierIndex[local] + bank * 18;
            _channels[i] = new OplChannel(i, modIndex, carIndex);
        }

        Reset();
    }

    public OplChipType ChipType { get; }
    public OplRegisterMap Registers { get; }
    public IReadOnlyList<OplOperator> Operators => _operators;
    public IReadOnlyList<OplChannel> Channels => _channels;
    public bool IrqActive { get; private set; }
    public double TimerARemainingSeconds => _timerARemainingSeconds;
    public double TimerBRemainingSeconds => _timerBRemainingSeconds;

    public byte Status => (byte)((IrqActive ? 0x80 : 0) |
                                 (Registers.TimerAOverflow ? 0x40 : 0) |
                                 (Registers.TimerBOverflow ? 0x20 : 0));

    public byte ReadStatus()
    {
        return Status;
    }

    public byte ReadRegister(int address)
    {
        if (address == 0x00)
        {
            return Status;
        }

        return Registers.Read(address);
    }

    public void Reset()
    {
        Registers.Reset();
        IrqActive = false;
        _timerARemainingSeconds = 0;
        _timerBRemainingSeconds = 0;

        foreach (OplOperator op in _operators)
        {
            op.Reset();
        }

        foreach (OplChannel channel in _channels)
        {
            channel.Reset();
        }
    }

    public void WriteRegister(int address, byte value)
    {
        bool resetStatus = address == 0x04 && (value & 0x80) != 0;

        Registers.Write(address, value);
        ApplyRegisterWrite(address, value);

        if (resetStatus)
        {
            IrqActive = false;
        }

        switch (address)
        {
            case 0x02:
                if (Registers.TimerAEnabled)
                {
                    _timerARemainingSeconds = GetTimerAPeriodSeconds();
                }
                break;
            case 0x03:
                if (Registers.TimerBEnabled)
                {
                    _timerBRemainingSeconds = GetTimerBPeriodSeconds();
                }
                break;
            case 0x04:
                UpdateTimerControl();
                break;
        }

        UpdateIrq();
    }

    public void StepSeconds(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        StepTimer(OplTimer.A, seconds, ref _timerARemainingSeconds);
        StepTimer(OplTimer.B, seconds, ref _timerBRemainingSeconds);

        foreach (OplOperator op in _operators)
        {
            op.Envelope.Step(seconds);
        }
    }

    public void StepChipSamples(double chipSamples)
    {
        if (chipSamples <= 0)
        {
            return;
        }

        double seconds = chipSamples / OplTiming.GetChipSampleRateHz(ChipType);
        StepSeconds(seconds);
    }

    public void StepOutputSamples(int outputSamples, int outputSampleRate)
    {
        if (outputSamples <= 0 || outputSampleRate <= 0)
        {
            return;
        }

        StepSeconds(outputSamples / (double)outputSampleRate);
    }

    private void StepTimer(OplTimer timer, double seconds, ref double remaining)
    {
        bool enabled = timer == OplTimer.A ? Registers.TimerAEnabled : Registers.TimerBEnabled;
        if (!enabled)
        {
            return;
        }

        double period = timer == OplTimer.A ? GetTimerAPeriodSeconds() : GetTimerBPeriodSeconds();
        if (period <= 0)
        {
            return;
        }

        if (remaining <= 0)
        {
            remaining = period;
        }

        remaining -= seconds;

        while (remaining <= 0)
        {
            Registers.SetTimerOverflow(timer, true);
            UpdateIrq();

            remaining += period;
        }
    }

    private void UpdateTimerControl()
    {
        if (!Registers.TimerAEnabled)
        {
            _timerARemainingSeconds = 0;
        }
        else if (_timerARemainingSeconds <= 0)
        {
            _timerARemainingSeconds = GetTimerAPeriodSeconds();
        }

        if (!Registers.TimerBEnabled)
        {
            _timerBRemainingSeconds = 0;
        }
        else if (_timerBRemainingSeconds <= 0)
        {
            _timerBRemainingSeconds = GetTimerBPeriodSeconds();
        }
    }

    private double GetTimerAPeriodSeconds()
    {
        int value = Registers.TimerAValue << 2;
        return OplTiming.GetTimerAOverflowSeconds(value, ChipType);
    }

    private double GetTimerBPeriodSeconds()
    {
        return OplTiming.GetTimerBOverflowSeconds(Registers.TimerBValue, ChipType);
    }

    private void ApplyRegisterWrite(int address, byte value)
    {
        int bank = (address & 0x100) != 0 ? 1 : 0;
        int reg = address & 0xFF;

        if (reg >= 0xA0 && reg <= 0xA8)
        {
            int channelIndex = GetChannelIndex(bank, reg);
            if (channelIndex >= 0)
            {
                _channels[channelIndex].ApplyFrequencyLow(value);
            }
            return;
        }

        if (reg >= 0xB0 && reg <= 0xB8)
        {
            int channelIndex = GetChannelIndex(bank, reg);
            if (channelIndex >= 0)
            {
                OplChannel channel = _channels[channelIndex];
                bool wasKeyOn = channel.KeyOn;
                channel.ApplyBlockKeyOn(value);
                if (channel.KeyOn != wasKeyOn)
                {
                    SetChannelKeyOn(channel, channel.KeyOn);
                }
            }
            return;
        }

        if (reg >= 0xC0 && reg <= 0xC8)
        {
            int channelIndex = GetChannelIndex(bank, reg);
            if (channelIndex >= 0)
            {
                _channels[channelIndex].ApplyFeedbackConnection(value);
            }
            return;
        }

        int group = reg & 0xE0;
        if (group is 0x20 or 0x40 or 0x60 or 0x80 or 0xE0)
        {
            int opIndex = GetOperatorIndex(bank, reg & 0x1F);
            if (opIndex >= 0 && opIndex < _operators.Length)
            {
                _operators[opIndex].ApplyRegister(group, value);
            }
        }
    }

    private void SetChannelKeyOn(OplChannel channel, bool keyOn)
    {
        if (channel.ModulatorIndex >= 0 && channel.ModulatorIndex < _operators.Length)
        {
            UpdateOperatorKey(_operators[channel.ModulatorIndex], keyOn);
        }

        if (channel.CarrierIndex >= 0 && channel.CarrierIndex < _operators.Length)
        {
            UpdateOperatorKey(_operators[channel.CarrierIndex], keyOn);
        }
    }

    private static void UpdateOperatorKey(OplOperator op, bool keyOn)
    {
        if (keyOn)
        {
            op.Envelope.KeyOn();
        }
        else
        {
            op.Envelope.KeyOff();
        }
    }

    private int GetChannelIndex(int bank, int reg)
    {
        int local = reg & 0x0F;
        if (local < 0 || local > 8)
        {
            return -1;
        }

        int index = local + bank * 9;
        if (index < 0 || index >= _channels.Length)
        {
            return -1;
        }

        return index;
    }

    private static int GetOperatorIndex(int bank, int offset)
    {
        if (offset < 0 || offset >= OperatorOffsetToIndex.Length)
        {
            return -1;
        }

        int index = OperatorOffsetToIndex[offset];
        if (index < 0)
        {
            return -1;
        }

        return index + bank * 18;
    }

    private void UpdateIrq()
    {
        IrqActive = (Registers.TimerAOverflow && !Registers.TimerAMasked) ||
                    (Registers.TimerBOverflow && !Registers.TimerBMasked);
    }
}
