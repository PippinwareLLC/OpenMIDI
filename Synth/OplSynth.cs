namespace OpenMIDI.Synth;

public enum OplSynthMode
{
    Opl2,
    Opl3
}

public sealed class OplSynth : IMidiSynth
{
    private enum VoiceAllocationKind
    {
        Free,
        ReleaseReuse,
        Steal
    }

    private sealed class OplVoice
    {
        public bool Active;
        public bool KeyOn;
        public bool Sustained;
        public int MidiChannel;
        public int Note;
        public int Velocity;
        public int OplChannel;
        public int Age;
    }

    private sealed class MidiChannelState
    {
        public int Program;
        public int PitchBend = 8192;
        public int Volume = 100;
        public int Expression = 127;
        public int Pan = 64;
        public bool SustainPedal;
    }

    private readonly OplVoice[] _voices;
    private readonly MidiChannelState[] _channels;
    private readonly OplSynthMode _mode;
    private int _ageCounter;
    private readonly int[] _channelActiveCounts;
    private readonly float[] _channelLevels;
    private int _activeVoiceCount;
    private int _peakActiveVoiceCount;
    private float _lastPeakLeft;
    private float _lastPeakRight;
    private int _noteOnCount;
    private int _sameNoteReuseCount;
    private int _releaseReuseCount;
    private int _voiceStealCount;

    private const byte DefaultFeedback = 2;
    private const byte ModAmVibEgtKsrMult = 0x21;
    private const byte ModKslTl = 0x20;
    private const byte ModArDr = 0xF3;
    private const byte ModSlRr = 0xF5;
    private const byte ModWaveform = 0x00;
    private const byte CarAmVibEgtKsrMult = 0x01;
    private const byte CarKslTl = 0x00;
    private const byte CarArDr = 0xF3;
    private const byte CarSlRr = 0xF5;
    private const byte CarWaveform = 0x00;
    private const float ReleaseReuseThreshold = 0.02f;

    private static readonly int[] OperatorIndexToOffset =
    {
        0, 1, 2, 3, 4, 5, 8, 9, 10, 11, 12, 13, 16, 17, 18, 19, 20, 21
    };

    public OplSynth(OplSynthMode mode = OplSynthMode.Opl3)
    {
        _mode = mode;
        int voiceCount = mode == OplSynthMode.Opl3 ? 18 : 9;
        _voices = new OplVoice[voiceCount];
        for (int i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new OplVoice();
        }

        _channels = new MidiChannelState[16];
        for (int i = 0; i < _channels.Length; i++)
        {
            _channels[i] = new MidiChannelState();
        }

        _channelActiveCounts = new int[16];
        _channelLevels = new float[16];
        Core = new OplCore(mode == OplSynthMode.Opl3 ? OplChipType.Opl3 : OplChipType.Opl2);
        Reset();
    }

    public float MasterGain { get; set; } = 0.2f;
    public float AttackPerSecond { get; set; } = 6f;
    public float ReleasePerSecond { get; set; } = 3f;
    public float PitchBendRangeSemitones { get; set; } = 2f;
    public int VoiceCount => _voices.Length;
    public int ActiveVoiceCount => _activeVoiceCount;
    public int PeakActiveVoiceCount => _peakActiveVoiceCount;
    public float LastPeakLeft => _lastPeakLeft;
    public float LastPeakRight => _lastPeakRight;
    public int NoteOnCount => _noteOnCount;
    public int SameNoteReuseCount => _sameNoteReuseCount;
    public int ReleaseReuseCount => _releaseReuseCount;
    public int VoiceStealCount => _voiceStealCount;
    public OplCore Core { get; }

    public void Reset()
    {
        foreach (OplVoice voice in _voices)
        {
            voice.Active = false;
            voice.KeyOn = false;
            voice.Sustained = false;
            voice.MidiChannel = 0;
            voice.Note = 0;
            voice.Velocity = 0;
            voice.OplChannel = 0;
            voice.Age = 0;
        }

        foreach (MidiChannelState channel in _channels)
        {
            channel.Program = 0;
            channel.PitchBend = 8192;
            channel.Volume = 100;
            channel.Expression = 127;
            channel.Pan = 64;
            channel.SustainPedal = false;
        }

        _ageCounter = 0;
        _activeVoiceCount = 0;
        _peakActiveVoiceCount = 0;
        _lastPeakLeft = 0f;
        _lastPeakRight = 0f;
        _noteOnCount = 0;
        _sameNoteReuseCount = 0;
        _releaseReuseCount = 0;
        _voiceStealCount = 0;
        Array.Clear(_channelActiveCounts, 0, _channelActiveCounts.Length);
        Array.Clear(_channelLevels, 0, _channelLevels.Length);
        Core.Reset();

        if (_mode == OplSynthMode.Opl3)
        {
            Core.WriteRegister(0x105, 0x01);
        }
    }

    public void NoteOn(int channel, int note, int velocity)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        if (velocity <= 0)
        {
            NoteOff(channel, note, velocity);
            return;
        }

        MidiChannelState channelState = _channels[channel];
        _noteOnCount++;

        int reuseIndex = FindSameNoteVoiceIndex(channel, note);
        if (reuseIndex >= 0)
        {
            _sameNoteReuseCount++;
            OplVoice reuseVoice = _voices[reuseIndex];
            KeyOffVoice(reuseVoice);
            reuseVoice.Active = true;
            reuseVoice.KeyOn = true;
            reuseVoice.Sustained = false;
            reuseVoice.MidiChannel = channel;
            reuseVoice.Note = note;
            reuseVoice.Velocity = velocity;
            reuseVoice.OplChannel = reuseIndex;
            reuseVoice.Age = _ageCounter++;
            ApplyChannelRegisters(reuseVoice, channelState);
            return;
        }

        int voiceIndex = AllocateVoiceIndex(out VoiceAllocationKind allocationKind);
        if (allocationKind == VoiceAllocationKind.ReleaseReuse)
        {
            _releaseReuseCount++;
        }
        else if (allocationKind == VoiceAllocationKind.Steal)
        {
            _voiceStealCount++;
        }

        OplVoice voice = _voices[voiceIndex];
        voice.Active = true;
        voice.KeyOn = true;
        voice.Sustained = false;
        voice.MidiChannel = channel;
        voice.Note = note;
        voice.Velocity = velocity;
        voice.OplChannel = voiceIndex;
        voice.Age = _ageCounter++;

        ApplyChannelRegisters(voice, channelState);
    }

    public void NoteOff(int channel, int note, int velocity)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        MidiChannelState channelState = _channels[channel];
        foreach (OplVoice voice in _voices)
        {
            if (voice.Active && voice.MidiChannel == channel && voice.Note == note)
            {
                if (channelState.SustainPedal)
                {
                    voice.Sustained = true;
                    continue;
                }

                KeyOffVoice(voice);
            }
        }
    }

    public void ControlChange(int channel, int controller, int value)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        MidiChannelState channelState = _channels[channel];
        switch (controller)
        {
            case 7:
                channelState.Volume = value;
                UpdateChannelGains(channel, channelState);
                break;
            case 10:
                channelState.Pan = value;
                UpdateChannelGains(channel, channelState);
                break;
            case 11:
                channelState.Expression = value;
                UpdateChannelGains(channel, channelState);
                break;
            case 64:
                UpdateSustainPedal(channel, channelState, value >= 64);
                break;
            case 120:
            case 123:
                AllNotesOff(channel);
                break;
        }
    }

    public void ProgramChange(int channel, int program)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        _channels[channel].Program = program;
    }

    public void PitchBend(int channel, int value)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        MidiChannelState channelState = _channels[channel];
        channelState.PitchBend = value;
        UpdateChannelFrequencies(channel, channelState);
    }

    public void Render(float[] interleaved, int offset, int frames, int sampleRate)
    {
        if (frames <= 0 || interleaved.Length < offset + frames * 2)
        {
            return;
        }

        Core.Render(interleaved, offset, frames, sampleRate);

        float peakLeft = 0f;
        float peakRight = 0f;
        if (Math.Abs(MasterGain - 1f) > 0.0001f)
        {
            for (int i = 0; i < frames; i++)
            {
                int baseIndex = offset + i * 2;
                interleaved[baseIndex] *= MasterGain;
                interleaved[baseIndex + 1] *= MasterGain;
            }
        }

        for (int i = 0; i < frames; i++)
        {
            int baseIndex = offset + i * 2;
            float left = interleaved[baseIndex];
            float right = interleaved[baseIndex + 1];
            peakLeft = Math.Max(peakLeft, Math.Abs(left));
            peakRight = Math.Max(peakRight, Math.Abs(right));
        }

        _lastPeakLeft = peakLeft;
        _lastPeakRight = peakRight;
        UpdateChannelMeters();
    }

    public void CopyChannelMeters(Span<int> counts, Span<float> levels)
    {
        int countLength = Math.Min(counts.Length, _channelActiveCounts.Length);
        _channelActiveCounts.AsSpan(0, countLength).CopyTo(counts);

        int levelLength = Math.Min(levels.Length, _channelLevels.Length);
        _channelLevels.AsSpan(0, levelLength).CopyTo(levels);
    }

    private int AllocateVoiceIndex(out VoiceAllocationKind allocationKind)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            if (!_voices[i].Active)
            {
                allocationKind = VoiceAllocationKind.Free;
                return i;
            }
        }

        int reuseIndex = FindReusableVoiceIndex();
        if (reuseIndex >= 0)
        {
            _voices[reuseIndex].Active = false;
            _voices[reuseIndex].KeyOn = false;
            _voices[reuseIndex].Sustained = false;
            allocationKind = VoiceAllocationKind.ReleaseReuse;
            return reuseIndex;
        }

        int stealIndex = SelectVoiceToSteal();
        KeyOffVoice(_voices[stealIndex]);
        _voices[stealIndex].Active = false;
        _voices[stealIndex].KeyOn = false;
        _voices[stealIndex].Sustained = false;
        allocationKind = VoiceAllocationKind.Steal;
        return stealIndex;
    }

    private int FindSameNoteVoiceIndex(int channel, int note)
    {
        int candidate = -1;
        for (int i = 0; i < _voices.Length; i++)
        {
            OplVoice voice = _voices[i];
            if (!voice.Active || voice.MidiChannel != channel || voice.Note != note)
            {
                continue;
            }

            if (voice.KeyOn)
            {
                return i;
            }

            if (candidate < 0)
            {
                candidate = i;
            }
        }

        return candidate;
    }

    private int FindReusableVoiceIndex()
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            OplVoice voice = _voices[i];
            if (!voice.Active || voice.KeyOn || voice.Sustained)
            {
                continue;
            }

            if (TryGetVoiceEnvelopeLevel(voice, out float level, out bool isOff) && (isOff || level <= ReleaseReuseThreshold))
            {
                return i;
            }
        }

        return -1;
    }

    private int SelectVoiceToSteal()
    {
        int bestIndex = 0;
        float bestLevel = float.MaxValue;
        bool bestIsRelease = false;
        bool bestIsSustained = true;

        for (int i = 0; i < _voices.Length; i++)
        {
            OplVoice voice = _voices[i];
            if (!voice.Active)
            {
                return i;
            }

            if (!TryGetVoiceEnvelopeLevel(voice, out float level, out bool isOff))
            {
                continue;
            }

            bool isRelease = !voice.KeyOn || isOff;
            bool isSustained = voice.Sustained;
            if (isSustained != bestIsSustained)
            {
                if (!isSustained)
                {
                    bestIsSustained = false;
                    bestIsRelease = isRelease;
                    bestLevel = level;
                    bestIndex = i;
                }

                continue;
            }

            if (isRelease && !bestIsRelease)
            {
                bestIsRelease = true;
                bestLevel = level;
                bestIndex = i;
                continue;
            }

            if (isRelease == bestIsRelease)
            {
                if (level < bestLevel || (Math.Abs(level - bestLevel) < 0.0001f && voice.Age < _voices[bestIndex].Age))
                {
                    bestLevel = level;
                    bestIndex = i;
                }
            }
        }

        return bestIndex;
    }

    private void AllNotesOff(int channel)
    {
        foreach (OplVoice voice in _voices)
        {
            if (voice.Active && voice.MidiChannel == channel)
            {
                KeyOffVoice(voice);
            }
        }
    }

    private void UpdateSustainPedal(int channel, MidiChannelState channelState, bool enabled)
    {
        if (channelState.SustainPedal == enabled)
        {
            return;
        }

        channelState.SustainPedal = enabled;
        if (!enabled)
        {
            ReleaseSustainedVoices(channel);
        }
    }

    private void ReleaseSustainedVoices(int channel)
    {
        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active || voice.MidiChannel != channel || !voice.Sustained)
            {
                continue;
            }

            KeyOffVoice(voice);
        }
    }

    private void UpdateChannelGains(int channel, MidiChannelState channelState)
    {
        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active || voice.MidiChannel != channel)
            {
                continue;
            }

            UpdateCarrierTotalLevel(voice, channelState);
            WriteChannelFeedback(voice.OplChannel, channelState);
        }
    }

    private void UpdateChannelFrequencies(int channel, MidiChannelState channelState)
    {
        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active || voice.MidiChannel != channel)
            {
                continue;
            }

            SetChannelFrequency(voice.OplChannel, voice.Note, channelState, keyOn: voice.KeyOn);
        }
    }

    private void UpdateChannelMeters()
    {
        Array.Clear(_channelActiveCounts, 0, _channelActiveCounts.Length);
        Array.Clear(_channelLevels, 0, _channelLevels.Length);
        _activeVoiceCount = 0;

        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active)
            {
                continue;
            }

            _activeVoiceCount++;
            if (voice.MidiChannel >= 0 && voice.MidiChannel < _channelActiveCounts.Length)
            {
                if (!TryGetVoiceEnvelopeLevel(voice, out float level, out bool isOff))
                {
                    continue;
                }

                if (isOff)
                {
                    voice.Active = false;
                    voice.KeyOn = false;
                    voice.Sustained = false;
                    continue;
                }

                _channelActiveCounts[voice.MidiChannel]++;
                _channelLevels[voice.MidiChannel] += level;
            }
        }

        if (_activeVoiceCount > _peakActiveVoiceCount)
        {
            _peakActiveVoiceCount = _activeVoiceCount;
        }
    }

    private bool TryGetVoiceEnvelopeLevel(OplVoice voice, out float level, out bool isOff)
    {
        if (voice.OplChannel < 0 || voice.OplChannel >= Core.Channels.Count)
        {
            level = 0f;
            isOff = true;
            return false;
        }

        OplChannel channel = Core.Channels[voice.OplChannel];
        if (channel.CarrierIndex < 0 || channel.CarrierIndex >= Core.Operators.Count)
        {
            level = 0f;
            isOff = true;
            return false;
        }

        OplEnvelope envelope = Core.Operators[channel.CarrierIndex].Envelope;
        level = envelope.Level;
        isOff = envelope.Stage == OplEnvelopeStage.Off;
        return true;
    }

    private void ApplyChannelRegisters(OplVoice voice, MidiChannelState channelState)
    {
        ConfigureChannelOperators(voice, channelState);
        SetChannelFrequency(voice.OplChannel, voice.Note, channelState, keyOn: true);
    }

    private void ConfigureChannelOperators(OplVoice voice, MidiChannelState channelState)
    {
        if (voice.OplChannel < 0 || voice.OplChannel >= Core.Channels.Count)
        {
            return;
        }

        OplChannel channel = Core.Channels[voice.OplChannel];
        WriteOperatorRegister(channel.ModulatorIndex, 0x20, ModAmVibEgtKsrMult);
        WriteOperatorRegister(channel.ModulatorIndex, 0x40, ModKslTl);
        WriteOperatorRegister(channel.ModulatorIndex, 0x60, ModArDr);
        WriteOperatorRegister(channel.ModulatorIndex, 0x80, ModSlRr);
        WriteOperatorRegister(channel.ModulatorIndex, 0xE0, ModWaveform);

        WriteOperatorRegister(channel.CarrierIndex, 0x20, CarAmVibEgtKsrMult);
        WriteOperatorRegister(channel.CarrierIndex, 0x40, ComputeCarrierTotalLevel(channelState, voice.Velocity));
        WriteOperatorRegister(channel.CarrierIndex, 0x60, CarArDr);
        WriteOperatorRegister(channel.CarrierIndex, 0x80, CarSlRr);
        WriteOperatorRegister(channel.CarrierIndex, 0xE0, CarWaveform);

        WriteChannelFeedback(voice.OplChannel, channelState);
    }

    private void UpdateCarrierTotalLevel(OplVoice voice, MidiChannelState channelState)
    {
        if (voice.OplChannel < 0 || voice.OplChannel >= Core.Channels.Count)
        {
            return;
        }

        OplChannel channel = Core.Channels[voice.OplChannel];
        WriteOperatorRegister(channel.CarrierIndex, 0x40, ComputeCarrierTotalLevel(channelState, voice.Velocity));
    }

    private byte ComputeCarrierTotalLevel(MidiChannelState channelState, int velocity)
    {
        float velocityGain = Math.Clamp(velocity / 127f, 0f, 1f);
        float gain = velocityGain * GetChannelGain(channelState);
        int attenuation = (int)Math.Round((1f - gain) * 63f);
        int baseTl = CarKslTl & 0x3F;
        int total = Math.Clamp(baseTl + attenuation, 0, 63);
        return (byte)((CarKslTl & 0xC0) | total);
    }

    private void SetChannelFrequency(int oplChannel, int note, MidiChannelState channelState, bool keyOn)
    {
        double frequency = GetFrequency(channelState, note);
        ComputeBlockAndFnum(frequency, out int block, out int fnum);
        WriteFrequency(oplChannel, fnum, block, keyOn);
    }

    private void ComputeBlockAndFnum(double frequency, out int block, out int fnum)
    {
        double chipRate = OplTiming.GetChipSampleRateHz(_mode == OplSynthMode.Opl3 ? OplChipType.Opl3 : OplChipType.Opl2);
        double baseValue = frequency * (1 << 20) / chipRate;
        block = 0;
        while (baseValue > 1023 && block < 7)
        {
            baseValue /= 2.0;
            block++;
        }

        fnum = (int)Math.Round(baseValue);
        fnum = Math.Clamp(fnum, 0, 1023);
    }

    private void WriteFrequency(int channelIndex, int fnum, int block, bool keyOn)
    {
        int baseA0 = GetChannelAddress(channelIndex, 0xA0);
        int baseB0 = GetChannelAddress(channelIndex, 0xB0);

        Core.WriteRegister(baseA0, (byte)(fnum & 0xFF));
        byte high = (byte)(((fnum >> 8) & 0x03) | ((block & 0x07) << 2) | (keyOn ? 0x20 : 0x00));
        Core.WriteRegister(baseB0, high);
    }

    private void WriteChannelFeedback(int channelIndex, MidiChannelState channelState)
    {
        byte feedback = (byte)(DefaultFeedback << 1);
        byte value = feedback;

        if (_mode == OplSynthMode.Opl3)
        {
            (bool left, bool right) = GetPanFlags(channelState);
            if (left)
            {
                value |= 0x10;
            }

            if (right)
            {
                value |= 0x20;
            }
        }

        Core.WriteRegister(GetChannelAddress(channelIndex, 0xC0), value);
    }

    private void WriteOperatorRegister(int opIndex, int groupBase, byte value)
    {
        if (opIndex < 0)
        {
            return;
        }

        int bank = opIndex / 18;
        int local = opIndex % 18;
        if (local < 0 || local >= OperatorIndexToOffset.Length)
        {
            return;
        }

        int offset = OperatorIndexToOffset[local];
        int address = groupBase + offset + bank * 0x100;
        Core.WriteRegister(address, value);
    }

    private int GetChannelAddress(int channelIndex, int baseAddress)
    {
        int bank = channelIndex / 9;
        int local = channelIndex % 9;
        return baseAddress + local + bank * 0x100;
    }

    private void KeyOffVoice(OplVoice voice)
    {
        if (!voice.Active)
        {
            return;
        }

        voice.KeyOn = false;
        voice.Sustained = false;

        int oplChannel = voice.OplChannel;
        if (oplChannel >= 0 && oplChannel < Core.Channels.Count)
        {
            OplChannel channel = Core.Channels[oplChannel];
            WriteFrequency(oplChannel, channel.FNum, channel.Block, keyOn: false);
        }
    }

    private double GetFrequency(MidiChannelState channelState, int note)
    {
        double bend = (channelState.PitchBend - 8192) / 8192.0;
        double bendSemitones = bend * PitchBendRangeSemitones;
        double noteValue = note + bendSemitones;
        return 440.0 * Math.Pow(2.0, (noteValue - 69.0) / 12.0);
    }

    private static float GetChannelGain(MidiChannelState channelState)
    {
        float volume = Math.Clamp(channelState.Volume / 127f, 0f, 1f);
        float expression = Math.Clamp(channelState.Expression / 127f, 0f, 1f);
        return volume * expression;
    }

    private static (bool left, bool right) GetPanFlags(MidiChannelState channelState)
    {
        int pan = Math.Clamp(channelState.Pan, 0, 127);
        if (pan <= 42)
        {
            return (true, false);
        }

        if (pan >= 85)
        {
            return (false, true);
        }

        return (true, true);
    }

    private static bool IsValidChannel(int channel)
    {
        return channel >= 0 && channel < 16;
    }
}
