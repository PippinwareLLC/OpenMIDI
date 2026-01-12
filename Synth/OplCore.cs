namespace OpenMIDI.Synth;

public sealed class OplCore
{
    private double _timerARemainingSeconds;
    private double _timerBRemainingSeconds;

    public OplCore(OplChipType chipType)
    {
        ChipType = chipType;
        Registers = new OplRegisterMap(chipType);
        Reset();
    }

    public OplChipType ChipType { get; }
    public OplRegisterMap Registers { get; }
    public bool IrqActive { get; private set; }
    public double TimerARemainingSeconds => _timerARemainingSeconds;
    public double TimerBRemainingSeconds => _timerBRemainingSeconds;

    public byte Status => (byte)((IrqActive ? 0x80 : 0) |
                                 (Registers.TimerAOverflow ? 0x40 : 0) |
                                 (Registers.TimerBOverflow ? 0x20 : 0));

    public void Reset()
    {
        Registers.Reset();
        IrqActive = false;
        _timerARemainingSeconds = 0;
        _timerBRemainingSeconds = 0;
    }

    public void WriteRegister(int address, byte value)
    {
        bool resetStatus = address == 0x04 && (value & 0x80) != 0;

        Registers.Write(address, value);

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
    }

    public void StepSeconds(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        StepTimer(OplTimer.A, seconds, ref _timerARemainingSeconds);
        StepTimer(OplTimer.B, seconds, ref _timerBRemainingSeconds);
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
            bool masked = timer == OplTimer.A ? Registers.TimerAMasked : Registers.TimerBMasked;
            if (!masked)
            {
                IrqActive = true;
            }

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
}
