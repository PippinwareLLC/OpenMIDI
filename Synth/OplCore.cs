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

    private const byte RhythmFlagHh = 0x01;
    private const byte RhythmFlagTc = 0x02;
    private const byte RhythmFlagTom = 0x04;
    private const byte RhythmFlagSd = 0x08;
    private const byte RhythmFlagBd = 0x10;

    private const int RhythmOpBdMod = 12;
    private const int RhythmOpBdCar = 15;
    private const int RhythmOpHh = 13;
    private const int RhythmOpSd = 16;
    private const int RhythmOpTom = 14;
    private const int RhythmOpTc = 17;

    private const float DefaultModulationIndex = 1.0f;
    private const double TwoPi = Math.PI * 2.0;
    private static readonly int[] KeyScaleLevelTable = { 0, 24, 32, 37, 40, 43, 45, 47, 48, 50, 51, 52, 53, 54, 55, 56 };
    private static readonly int[] PmScale = { 8, 4, 0, -4, -8, -4, 0, 4 };

    private double _timerARemainingSeconds;
    private double _timerBRemainingSeconds;
    private uint _envCounter;
    private double _chipSampleRemainder;
    private uint _noiseLfsr = 1;
    private ushort _lfoAmCounter;
    private ushort _lfoPmCounter;
    private byte _lfoAm;
    private int _lfoPm;
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
        _envCounter = 0;
        _chipSampleRemainder = 0;
        _noiseLfsr = 1;
        _lfoAmCounter = 0;
        _lfoPmCounter = 0;
        _lfoAm = 0;
        _lfoPm = 0;

        foreach (OplOperator op in _operators)
        {
            op.Reset();
        }

        foreach (OplChannel channel in _channels)
        {
            channel.Reset();
        }

        UpdateAllChannelKeyCodes();
    }

    public void WriteRegister(int address, byte value)
    {
        bool resetStatus = address == 0x04 && (value & 0x80) != 0;
        bool updateRhythm = address == 0xBD;
        bool prevRhythmEnabled = updateRhythm && Registers.RhythmEnabled;
        byte prevRhythmFlags = updateRhythm ? Registers.RhythmFlags : (byte)0;

        Registers.Write(address, value);
        ApplyRegisterWrite(address, value);

        if (updateRhythm)
        {
            UpdateRhythmState(prevRhythmEnabled, prevRhythmFlags);
        }

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

        StepTimers(seconds);

        double chipSamples = seconds * OplTiming.GetChipSampleRateHz(ChipType);
        StepChipSamples(chipSamples);
    }

    public void StepChipSamples(double chipSamples)
    {
        if (chipSamples <= 0)
        {
            return;
        }

        _chipSampleRemainder += chipSamples;
        int wholeSamples = (int)_chipSampleRemainder;
        if (wholeSamples <= 0)
        {
            return;
        }

        _chipSampleRemainder -= wholeSamples;

        for (int i = 0; i < wholeSamples; i++)
        {
            _envCounter += 4;
            _lfoPm = ClockLfo();

            if ((_envCounter & 0x03) == 0)
            {
                uint envCounter = _envCounter >> 2;
                foreach (OplOperator op in _operators)
                {
                    op.Envelope.Step(envCounter);
                }
            }
        }
    }

    public void StepOutputSamples(int outputSamples, int outputSampleRate)
    {
        if (outputSamples <= 0 || outputSampleRate <= 0)
        {
            return;
        }

        StepSeconds(outputSamples / (double)outputSampleRate);
    }

    public void Render(float[] interleaved, int offset, int frames, int outputSampleRate)
    {
        if (frames <= 0 || outputSampleRate <= 0)
        {
            return;
        }

        if (offset < 0 || interleaved.Length < offset + frames * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        Array.Clear(interleaved, offset, frames * 2);

        StepTimers(frames / (double)outputSampleRate);

        double chipSamplesPerOutput = OplTiming.ConvertOutputSamplesToChipSamples(1, outputSampleRate, ChipType);

        for (int i = 0; i < frames; i++)
        {
            StepChipSamples(chipSamplesPerOutput);

            float left = 0f;
            float right = 0f;
            float noiseSample = (_noiseLfsr & 0x01) != 0 ? 1f : -1f;

            for (int channelIndex = 0; channelIndex < _channels.Length; channelIndex++)
            {
                OplChannel channel = _channels[channelIndex];
                if (IsRhythmChannel(channelIndex))
                {
                    float rhythmSample = RenderRhythmChannel(channelIndex, outputSampleRate, noiseSample);
                    bool rhythmLeftOn = channel.LeftEnable || (!channel.LeftEnable && !channel.RightEnable);
                    bool rhythmRightOn = channel.RightEnable || (!channel.LeftEnable && !channel.RightEnable);

                    if (rhythmLeftOn)
                    {
                        left += rhythmSample;
                    }

                    if (rhythmRightOn)
                    {
                        right += rhythmSample;
                    }

                    continue;
                }

                if (IsFourOpSecondaryChannel(channelIndex))
                {
                    continue;
                }

                if (TryGetFourOpPair(channelIndex, out int secondaryIndex))
                {
                    float fourOpSample = RenderFourOperatorChannel(channel, _channels[secondaryIndex], outputSampleRate);
                    bool fourOpLeftOn = channel.LeftEnable || (!channel.LeftEnable && !channel.RightEnable);
                    bool fourOpRightOn = channel.RightEnable || (!channel.LeftEnable && !channel.RightEnable);

                    if (fourOpLeftOn)
                    {
                        left += fourOpSample;
                    }

                    if (fourOpRightOn)
                    {
                        right += fourOpSample;
                    }

                    continue;
                }

                int modIndex = channel.ModulatorIndex;
                int carIndex = channel.CarrierIndex;
                if (modIndex < 0 || modIndex >= _operators.Length || carIndex < 0 || carIndex >= _operators.Length)
                {
                    continue;
                }

                OplOperator mod = _operators[modIndex];
                OplOperator car = _operators[carIndex];

                float modOutput = RenderOperator(mod, channel, outputSampleRate, 0f, channel.Feedback, applyFeedback: true);
                float carrierPhaseMod = channel.Additive ? 0f : modOutput * DefaultModulationIndex;
                float carOutput = RenderOperator(car, channel, outputSampleRate, carrierPhaseMod, 0, applyFeedback: false);
                float sample = channel.Additive ? modOutput + carOutput : carOutput;

                bool leftOn = channel.LeftEnable || (!channel.LeftEnable && !channel.RightEnable);
                bool rightOn = channel.RightEnable || (!channel.LeftEnable && !channel.RightEnable);

                if (leftOn)
                {
                    left += sample;
                }

                if (rightOn)
                {
                    right += sample;
                }
            }

            interleaved[offset + i * 2] = Math.Clamp(left, -1f, 1f);
            interleaved[offset + i * 2 + 1] = Math.Clamp(right, -1f, 1f);
        }
    }

    private bool IsRhythmChannel(int channelIndex)
    {
        return Registers.RhythmEnabled && channelIndex >= 6 && channelIndex < 9;
    }

    private float RenderRhythmChannel(int channelIndex, int outputSampleRate, float noiseSample)
    {
        float sample = 0f;
        int local = channelIndex % 9;
        OplChannel channel = _channels[channelIndex];

        if (local == 6)
        {
            int modIndex = channel.ModulatorIndex;
            int carIndex = channel.CarrierIndex;
            if (modIndex >= 0 && modIndex < _operators.Length && carIndex >= 0 && carIndex < _operators.Length)
            {
                OplOperator mod = _operators[modIndex];
                OplOperator car = _operators[carIndex];

                float modOutput = RenderOperator(mod, channel, outputSampleRate, 0f, channel.Feedback, applyFeedback: true);
                float carrierPhaseMod = channel.Additive ? 0f : modOutput * DefaultModulationIndex;
                float carOutput = RenderOperator(car, channel, outputSampleRate, carrierPhaseMod, 0, applyFeedback: false);
                sample += channel.Additive ? modOutput + carOutput : carOutput;
            }
        }
        else if (local == 7)
        {
            sample += RenderNoiseOperator(RhythmOpHh, channel, noiseSample);
            sample += RenderNoiseOperator(RhythmOpSd, channel, noiseSample);
        }
        else if (local == 8)
        {
            sample += RenderSingleOperator(RhythmOpTom, channel, outputSampleRate);
            sample += RenderNoiseOperator(RhythmOpTc, channel, noiseSample);
        }

        return sample;
    }

    private float RenderFourOperatorChannel(OplChannel primary, OplChannel secondary, int outputSampleRate)
    {
        int op1Index = primary.ModulatorIndex;
        int op2Index = primary.CarrierIndex;
        int op3Index = secondary.ModulatorIndex;
        int op4Index = secondary.CarrierIndex;

        if (op1Index < 0 || op2Index < 0 || op3Index < 0 || op4Index < 0 ||
            op1Index >= _operators.Length || op2Index >= _operators.Length ||
            op3Index >= _operators.Length || op4Index >= _operators.Length)
        {
            return 0f;
        }

        OplOperator op1 = _operators[op1Index];
        OplOperator op2 = _operators[op2Index];
        OplOperator op3 = _operators[op3Index];
        OplOperator op4 = _operators[op4Index];

        int algorithm = ((primary.Additive ? 1 : 0) << 1) | (secondary.Additive ? 1 : 0);
        float op1Output = RenderOperator(op1, primary, outputSampleRate, 0f, primary.Feedback, applyFeedback: true);
        float sample = 0f;

        switch (algorithm)
        {
            case 0:
            {
                float op2Output = RenderOperator(op2, primary, outputSampleRate, op1Output * DefaultModulationIndex, 0, applyFeedback: false);
                float op3Output = RenderOperator(op3, primary, outputSampleRate, op2Output * DefaultModulationIndex, 0, applyFeedback: false);
                float op4Output = RenderOperator(op4, primary, outputSampleRate, op3Output * DefaultModulationIndex, 0, applyFeedback: false);
                sample = op4Output;
                break;
            }
            case 1:
            {
                float op2Output = RenderOperator(op2, primary, outputSampleRate, op1Output * DefaultModulationIndex, 0, applyFeedback: false);
                float op3Output = RenderOperator(op3, primary, outputSampleRate, 0f, 0, applyFeedback: false);
                float op4Output = RenderOperator(op4, primary, outputSampleRate, op3Output * DefaultModulationIndex, 0, applyFeedback: false);
                sample = op2Output + op4Output;
                break;
            }
            case 2:
            {
                float op2Output = RenderOperator(op2, primary, outputSampleRate, 0f, 0, applyFeedback: false);
                float op3Output = RenderOperator(op3, primary, outputSampleRate, op2Output * DefaultModulationIndex, 0, applyFeedback: false);
                float op4Output = RenderOperator(op4, primary, outputSampleRate, op3Output * DefaultModulationIndex, 0, applyFeedback: false);
                sample = op1Output + op4Output;
                break;
            }
            default:
            {
                float op2Output = RenderOperator(op2, primary, outputSampleRate, 0f, 0, applyFeedback: false);
                float op3Output = RenderOperator(op3, primary, outputSampleRate, op2Output * DefaultModulationIndex, 0, applyFeedback: false);
                float op4Output = RenderOperator(op4, primary, outputSampleRate, 0f, 0, applyFeedback: false);
                sample = op1Output + op3Output + op4Output;
                break;
            }
        }

        return sample;
    }

    private float RenderSingleOperator(int opIndex, OplChannel channel, int outputSampleRate)
    {
        if (opIndex < 0 || opIndex >= _operators.Length)
        {
            return 0f;
        }

        return RenderOperator(_operators[opIndex], channel, outputSampleRate, 0f, 0, applyFeedback: false);
    }

    private float RenderNoiseOperator(int opIndex, OplChannel channel, float noiseSample)
    {
        if (opIndex < 0 || opIndex >= _operators.Length)
        {
            return 0f;
        }

        OplOperator op = _operators[opIndex];
        int attenuation = ComputeOperatorAttenuation(op, channel);
        if (attenuation >= 0x3FF)
        {
            op.PreviousOutput = op.LastOutput;
            op.LastOutput = 0f;
            return 0f;
        }

        uint totalAtten = (uint)attenuation << 2;
        int volume = OplWaveformTables.AttenuationToVolume(totalAtten);
        float output = noiseSample * (volume / 8192f);
        op.PreviousOutput = op.LastOutput;
        op.LastOutput = output;
        return output;
    }

    private void StepTimers(double seconds)
    {
        StepTimer(OplTimer.A, seconds, ref _timerARemainingSeconds);
        StepTimer(OplTimer.B, seconds, ref _timerBRemainingSeconds);
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

        if (reg == 0x08)
        {
            UpdateAllChannelKeyCodes();
            return;
        }

        if (reg >= 0xA0 && reg <= 0xA8)
        {
            int channelIndex = GetChannelIndex(bank, reg);
            if (channelIndex >= 0)
            {
                OplChannel channel = _channels[channelIndex];
                channel.ApplyFrequencyLow(value);
                UpdateChannelKeyCode(channel);
            }
            return;
        }

        if (reg >= 0xB0 && reg <= 0xB8)
        {
            int channelIndex = GetChannelIndex(bank, reg);
            if (channelIndex >= 0)
            {
                OplChannel channel = _channels[channelIndex];
                bool isRhythmChannel = Registers.RhythmEnabled && bank == 0 && (channelIndex % 9) >= 6;
                bool updateKeyOn = !isRhythmChannel;
                bool wasKeyOn = channel.KeyOn;
                channel.ApplyBlockKeyOn(value, updateKeyOn);
                UpdateChannelKeyCode(channel);
                if (updateKeyOn && channel.KeyOn != wasKeyOn)
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

    private void UpdateAllChannelKeyCodes()
    {
        foreach (OplChannel channel in _channels)
        {
            UpdateChannelKeyCode(channel);
        }
    }

    private void UpdateChannelKeyCode(OplChannel channel)
    {
        channel.UpdateKeyCode(Registers.NoteSelectEnabled);
        if (IsFourOpSecondaryChannel(channel.Index))
        {
            return;
        }

        UpdateOperatorKeyCode(channel.ModulatorIndex, channel.KeyCode);
        UpdateOperatorKeyCode(channel.CarrierIndex, channel.KeyCode);

        if (TryGetFourOpPair(channel.Index, out int secondaryIndex))
        {
            OplChannel secondary = _channels[secondaryIndex];
            UpdateOperatorKeyCode(secondary.ModulatorIndex, channel.KeyCode);
            UpdateOperatorKeyCode(secondary.CarrierIndex, channel.KeyCode);
        }
    }

    private void UpdateOperatorKeyCode(int opIndex, int keyCode)
    {
        if (opIndex < 0 || opIndex >= _operators.Length)
        {
            return;
        }

        _operators[opIndex].UpdateKeyCode(keyCode);
    }

    private void SetChannelKeyOn(OplChannel channel, bool keyOn)
    {
        if (IsFourOpSecondaryChannel(channel.Index))
        {
            return;
        }

        if (channel.ModulatorIndex >= 0 && channel.ModulatorIndex < _operators.Length)
        {
            UpdateOperatorKey(_operators[channel.ModulatorIndex], keyOn);
        }

        if (channel.CarrierIndex >= 0 && channel.CarrierIndex < _operators.Length)
        {
            UpdateOperatorKey(_operators[channel.CarrierIndex], keyOn);
        }

        if (TryGetFourOpPair(channel.Index, out int secondaryIndex))
        {
            OplChannel secondary = _channels[secondaryIndex];
            if (secondary.ModulatorIndex >= 0 && secondary.ModulatorIndex < _operators.Length)
            {
                UpdateOperatorKey(_operators[secondary.ModulatorIndex], keyOn);
            }

            if (secondary.CarrierIndex >= 0 && secondary.CarrierIndex < _operators.Length)
            {
                UpdateOperatorKey(_operators[secondary.CarrierIndex], keyOn);
            }
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

    private void UpdateRhythmState(bool wasEnabled, byte previousFlags)
    {
        bool enabled = Registers.RhythmEnabled;
        byte flags = Registers.RhythmFlags;

        if (enabled)
        {
            if (!wasEnabled)
            {
                for (int local = 6; local <= 8; local++)
                {
                    int channelIndex = local;
                    if (channelIndex >= _channels.Length)
                    {
                        break;
                    }

                    OplChannel channel = _channels[channelIndex];
                    if (channel.KeyOn)
                    {
                        SetChannelKeyOn(channel, false);
                        channel.SetKeyOn(false);
                    }
                }
            }

            UpdateRhythmFlags(previousFlags, flags);
        }
        else if (wasEnabled)
        {
            UpdateRhythmFlags(previousFlags, 0);
            SyncRhythmChannelsFromRegisters();
        }
    }

    private void UpdateRhythmFlags(byte previousFlags, byte newFlags)
    {
        UpdateRhythmFlag(previousFlags, newFlags, RhythmFlagBd, RhythmOpBdMod, RhythmOpBdCar);
        UpdateRhythmFlag(previousFlags, newFlags, RhythmFlagSd, RhythmOpSd);
        UpdateRhythmFlag(previousFlags, newFlags, RhythmFlagTom, RhythmOpTom);
        UpdateRhythmFlag(previousFlags, newFlags, RhythmFlagTc, RhythmOpTc);
        UpdateRhythmFlag(previousFlags, newFlags, RhythmFlagHh, RhythmOpHh);
    }

    private void UpdateRhythmFlag(byte previousFlags, byte newFlags, byte mask, params int[] operatorIndices)
    {
        bool wasOn = (previousFlags & mask) != 0;
        bool isOn = (newFlags & mask) != 0;
        if (wasOn == isOn)
        {
            return;
        }

        foreach (int opIndex in operatorIndices)
        {
            if (opIndex < 0 || opIndex >= _operators.Length)
            {
                continue;
            }

            UpdateOperatorKey(_operators[opIndex], isOn);
        }
    }

    private void SyncRhythmChannelsFromRegisters()
    {
        for (int local = 6; local <= 8; local++)
        {
            int channelIndex = local;
            if (channelIndex >= _channels.Length)
            {
                break;
            }

            OplChannel channel = _channels[channelIndex];
            bool wasKeyOn = channel.KeyOn;
            int reg = 0xB0 + local;
            channel.ApplyBlockKeyOn(Registers.Read(reg), updateKeyOn: true);
            UpdateChannelKeyCode(channel);
            if (channel.KeyOn != wasKeyOn)
            {
                SetChannelKeyOn(channel, channel.KeyOn);
            }
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

    private bool TryGetFourOpPair(int channelIndex, out int secondaryIndex)
    {
        secondaryIndex = -1;
        if (ChipType != OplChipType.Opl3 || !Registers.Opl3Enabled)
        {
            return false;
        }

        int bank = channelIndex / 9;
        int local = channelIndex % 9;
        if (local < 0 || local >= 3)
        {
            return false;
        }

        int bitIndex = local + bank * 3;
        if ((Registers.FourOperatorEnableMask & (1 << bitIndex)) == 0)
        {
            return false;
        }

        secondaryIndex = channelIndex + 3;
        return secondaryIndex >= 0 && secondaryIndex < _channels.Length;
    }

    private bool IsFourOpSecondaryChannel(int channelIndex)
    {
        if (ChipType != OplChipType.Opl3 || !Registers.Opl3Enabled)
        {
            return false;
        }

        int bank = channelIndex / 9;
        int local = channelIndex % 9;
        if (local < 3 || local > 5)
        {
            return false;
        }

        int bitIndex = (local - 3) + bank * 3;
        return (Registers.FourOperatorEnableMask & (1 << bitIndex)) != 0;
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

    private float RenderOperator(OplOperator op, OplChannel channel, int outputSampleRate, float phaseModulation, byte feedback, bool applyFeedback)
    {
        if (outputSampleRate <= 0)
        {
            return 0f;
        }

        if (op.Envelope.Stage == OplEnvelopeStage.Off && op.Envelope.Attenuation >= 0x3FF)
        {
            op.PreviousOutput = op.LastOutput;
            op.LastOutput = 0f;
            return 0f;
        }

        double phaseStep = ComputePhaseStep(op, channel, _lfoPm, outputSampleRate);
        if (phaseStep == 0)
        {
            op.PreviousOutput = op.LastOutput;
            op.LastOutput = 0f;
            return 0f;
        }

        float feedbackPhase = 0f;
        if (applyFeedback && feedback != 0)
        {
            float scale = ComputeFeedbackScale(feedback);
            if (scale != 0f)
            {
                feedbackPhase = (op.LastOutput + op.PreviousOutput) * 0.5f * scale;
            }
        }

        double phase = op.Phase + phaseModulation + feedbackPhase;
        float output = ComputeWaveformOutput(op, channel, phase);

        op.PreviousOutput = op.LastOutput;
        op.LastOutput = output;
        op.Phase = WrapPhase(op.Phase + phaseStep);

        return output;
    }

    private int ComputeOperatorAttenuation(OplOperator op, OplChannel channel)
    {
        int totalLevel = op.TotalLevel << 3;
        if (op.KeyScaleLevel != 0)
        {
            int ksl = ComputeKeyScaleAttenuation(channel.Block, channel.FNum);
            totalLevel += ksl << op.KeyScaleLevel;
        }

        int attenuation = op.Envelope.Attenuation + totalLevel;
        if (op.Tremolo)
        {
            attenuation += _lfoAm;
        }

        return Math.Clamp(attenuation, 0, 0x3FF);
    }

    private static float ComputeFeedbackScale(byte feedback)
    {
        if (feedback == 0)
        {
            return 0f;
        }

        return feedback / 7f * 2f;
    }

    private double ComputePhaseStep(OplOperator op, OplChannel channel, int lfoPm, int outputSampleRate)
    {
        int fnum = channel.FNum;
        if (fnum <= 0)
        {
            return 0;
        }

        int fnum12 = fnum << 2;
        if (op.Vibrato && lfoPm != 0)
        {
            int fnumHigh = (fnum >> 7) & 0x07;
            fnum12 += (lfoPm * fnumHigh) >> 1;
        }

        fnum12 &= 0xFFF;
        int blockShift = Math.Clamp(channel.Block, 0, 7);
        double phaseStepBase = (fnum12 * (1 << blockShift)) / 4.0;
        int multipleIndex = Math.Clamp(op.Multiple, 0, OplEnvelopeTables.MultipleTable.Length - 1);
        double phaseStep = phaseStepBase * OplEnvelopeTables.MultipleTable[multipleIndex];

        double chipSampleRate = OplTiming.GetChipSampleRateHz(ChipType);
        double frequency = phaseStep * chipSampleRate / 1048576.0;
        return TwoPi * frequency / outputSampleRate;
    }

    private float ComputeWaveformOutput(OplOperator op, OplChannel channel, double phase)
    {
        int attenuation = ComputeOperatorAttenuation(op, channel);
        if (attenuation >= 0x3FF)
        {
            return 0f;
        }

        int phaseIndex = (int)(phase / TwoPi * OplWaveformTables.WaveformLength);
        ushort waveform = OplWaveformTables.GetWaveformSample(op.WaveformIndex, phaseIndex);
        int sign = (waveform & 0x8000) != 0 ? -1 : 1;
        uint sinAtten = (uint)(waveform & 0x7FFF);
        uint totalAtten = sinAtten + ((uint)attenuation << 2);
        int volume = OplWaveformTables.AttenuationToVolume(totalAtten);
        return sign * (volume / 8192f);
    }

    private static int ComputeKeyScaleAttenuation(int block, int fnum)
    {
        int fnum4msb = (fnum >> 6) & 0x0F;
        int adjusted = KeyScaleLevelTable[fnum4msb] - 8 * (block ^ 7);
        return Math.Max(0, adjusted);
    }

    private static double WrapPhase(double phase)
    {
        if (phase >= TwoPi || phase <= -TwoPi)
        {
            phase %= TwoPi;
        }

        if (phase < 0)
        {
            phase += TwoPi;
        }

        return phase;
    }

    private int ClockLfo()
    {
        _noiseLfsr <<= 1;
        _noiseLfsr |= ((_noiseLfsr >> 23) ^ (_noiseLfsr >> 9) ^ (_noiseLfsr >> 8) ^ (_noiseLfsr >> 1)) & 0x01;

        uint amCounter = _lfoAmCounter++;
        if (amCounter >= 210 * 64 - 1)
        {
            _lfoAmCounter = 0;
        }

        int shift = 9 - 2 * (Registers.TremoloDepth ? 1 : 0);
        uint amValue = amCounter < 105 * 64 ? amCounter : (uint)(210 * 64 + 63 - amCounter);
        _lfoAm = (byte)(amValue >> shift);

        uint pmCounter = _lfoPmCounter++;
        int index = (int)((pmCounter >> 10) & 0x07);
        int pm = PmScale[index];
        return pm >> ((Registers.VibratoDepth ? 1 : 0) ^ 1);
    }

    private void UpdateIrq()
    {
        IrqActive = (Registers.TimerAOverflow && !Registers.TimerAMasked) ||
                    (Registers.TimerBOverflow && !Registers.TimerBMasked);
    }
}
