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

    private enum RhythmInstrument
    {
        BassDrum,
        Snare,
        Tom,
        Cymbal,
        HiHat
    }

    private sealed class OplVoice
    {
        public bool Active;
        public bool KeyOn;
        public bool Sustained;
        public int MidiChannel;
        public int Note;
        public int OplNote;
        public int Velocity;
        public int Program;
        public byte BankMsb;
        public byte BankLsb;
        public OplInstrument Instrument = OplInstrumentDefaults.DefaultInstrument;
        public int OplChannel;
        public int Age;
    }

    private sealed class MidiChannelState
    {
        public int Program;
        public byte BankMsb;
        public byte BankLsb;
        public int PitchBend = 8192;
        public int Volume = 100;
        public int Expression = 127;
        public int Pan = 64;
        public bool SustainPedal;
    }

    private const int MaxChipCount = 8;
    private const int PercussionChannel = 9;
    private const int RhythmChannelStart = 6;
    private const int RhythmChannelEnd = 8;
    private const int RhythmInstrumentCount = 5;
    private const int RhythmOpBdMod = 12;
    private const int RhythmOpBdCar = 15;
    private const int RhythmOpHh = 13;
    private const int RhythmOpSd = 16;
    private const int RhythmOpTom = 14;
    private const int RhythmOpTc = 17;
    private readonly OplCore[] _cores;
    private readonly int _channelsPerChip;
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
    private float[] _mixBuffer = Array.Empty<float>();
    private readonly int[] _rhythmCounts = new int[RhythmInstrumentCount];
    private readonly RhythmInstrument[] _rhythmNoteMap = new RhythmInstrument[128];
    private readonly int[] _rhythmNoteCounts = new int[128];
    private OplInstrumentBankSet _bankSet = OplInstrumentBankSet.CreateDefault();
    private bool _deepTremolo;
    private bool _deepVibrato;
    private static readonly int[] ChannelPriority =
    {
        1, 1, 2, 3, 3, 3, 3, 3,
        3, 0, 3, 3, 3, 3, 3, 3
    };

    private static readonly int[] OperatorIndexToOffset =
    {
        0, 1, 2, 3, 4, 5, 8, 9, 10, 11, 12, 13, 16, 17, 18, 19, 20, 21
    };

    public OplSynth(OplSynthMode mode = OplSynthMode.Opl3, int chips = 1)
    {
        _mode = mode;
        int chipCount = Math.Clamp(chips, 1, MaxChipCount);
        OplChipType chipType = mode == OplSynthMode.Opl3 ? OplChipType.Opl3 : OplChipType.Opl2;
        _channelsPerChip = chipType == OplChipType.Opl3 ? 18 : 9;
        _cores = new OplCore[chipCount];
        for (int i = 0; i < _cores.Length; i++)
        {
            _cores[i] = new OplCore(chipType);
        }

        int voiceCount = _channelsPerChip * chipCount;
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
        _deepTremolo = _bankSet.DeepTremolo;
        _deepVibrato = _bankSet.DeepVibrato;
        Reset();
    }

    public float MasterGain { get; set; } = 0.2f;
    public float AttackPerSecond { get; set; } = 6f;
    public float ReleasePerSecond { get; set; } = 3f;
    public float PitchBendRangeSemitones { get; set; } = 2f;
    public int ChipCount => _cores.Length;
    public int VoiceCount => _voices.Length;
    public int ActiveVoiceCount => _activeVoiceCount;
    public int PeakActiveVoiceCount => _peakActiveVoiceCount;
    public float LastPeakLeft => _lastPeakLeft;
    public float LastPeakRight => _lastPeakRight;
    public int NoteOnCount => _noteOnCount;
    public int SameNoteReuseCount => _sameNoteReuseCount;
    public int ReleaseReuseCount => _releaseReuseCount;
    public int VoiceStealCount => _voiceStealCount;
    public OplCore Core => _cores[0];
    public IReadOnlyList<OplCore> Cores => _cores;
    public OplInstrumentBankSet BankSet => _bankSet;

    public void Reset()
    {
        foreach (OplVoice voice in _voices)
        {
            voice.Active = false;
            voice.KeyOn = false;
            voice.Sustained = false;
            voice.MidiChannel = 0;
            voice.Note = 0;
            voice.OplNote = 0;
            voice.Velocity = 0;
            voice.Program = 0;
            voice.BankMsb = 0;
            voice.BankLsb = 0;
            voice.Instrument = _bankSet.DefaultInstrument;
            voice.OplChannel = 0;
            voice.Age = 0;
        }

        foreach (MidiChannelState channel in _channels)
        {
            channel.Program = 0;
            channel.BankMsb = 0;
            channel.BankLsb = 0;
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
        Array.Clear(_rhythmCounts, 0, _rhythmCounts.Length);
        Array.Clear(_rhythmNoteCounts, 0, _rhythmNoteCounts.Length);
        Array.Clear(_rhythmNoteMap, 0, _rhythmNoteMap.Length);
        foreach (OplCore core in _cores)
        {
            core.Reset();
            if (_mode == OplSynthMode.Opl3)
            {
                core.WriteRegister(0x105, 0x01);
            }
        }

        ApplyLfoDepths();
    }

    public void LoadBank(OplInstrumentBankSet bankSet)
    {
        _bankSet = bankSet ?? OplInstrumentBankSet.CreateDefault();
        _deepTremolo = _bankSet.DeepTremolo;
        _deepVibrato = _bankSet.DeepVibrato;
        ApplyLfoDepths();
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

        if (IsPercussionChannel(channel))
        {
            if (!TryResolvePercussionInstrument(channelState, note, velocity, out OplInstrument instrument,
                    out int oplNote, out int adjustedVelocity, out RhythmInstrument rhythmInstrument))
            {
                return;
            }

            HandlePercussionNoteOn(note, adjustedVelocity, instrument, oplNote, rhythmInstrument, channelState);
            return;
        }

        if (!TryResolveMelodicInstrument(channelState, note, velocity, out OplInstrument melodicInstrument,
                out int melodicNote, out int melodicVelocity))
        {
            return;
        }

        int reuseIndex = FindSameNoteVoiceIndex(channel, note, channelState.Program, channelState.BankMsb, channelState.BankLsb);
        if (reuseIndex >= 0)
        {
            _sameNoteReuseCount++;
            KeyOffDuplicateVoices(channel, note, reuseIndex);
            OplVoice reuseVoice = _voices[reuseIndex];
            KeyOffVoice(reuseVoice);
            reuseVoice.Active = true;
            reuseVoice.KeyOn = true;
            reuseVoice.Sustained = false;
            reuseVoice.MidiChannel = channel;
            reuseVoice.Note = note;
            reuseVoice.OplNote = melodicNote;
            reuseVoice.Velocity = melodicVelocity;
            reuseVoice.Program = channelState.Program;
            reuseVoice.BankMsb = channelState.BankMsb;
            reuseVoice.BankLsb = channelState.BankLsb;
            reuseVoice.Instrument = melodicInstrument;
            reuseVoice.OplChannel = reuseIndex;
            reuseVoice.Age = _ageCounter++;
            ApplyChannelRegisters(reuseVoice, channelState);
            return;
        }

        KeyOffDuplicateVoices(channel, note, -1);
        int voiceIndex = AllocateVoiceIndex(channel, channelState.Program, channelState.BankMsb, channelState.BankLsb,
            out VoiceAllocationKind allocationKind);
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
        voice.OplNote = melodicNote;
        voice.Velocity = melodicVelocity;
        voice.Program = channelState.Program;
        voice.BankMsb = channelState.BankMsb;
        voice.BankLsb = channelState.BankLsb;
        voice.Instrument = melodicInstrument;
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

        if (IsPercussionChannel(channel))
        {
            HandlePercussionNoteOff(note);
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
            case 0:
                channelState.BankMsb = (byte)Math.Clamp(value, 0, 127);
                break;
            case 32:
                channelState.BankLsb = (byte)Math.Clamp(value, 0, 127);
                break;
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
                if (!IsPercussionChannel(channel))
                {
                    UpdateSustainPedal(channel, channelState, value >= 64);
                }
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

        _channels[channel].Program = Math.Clamp(program, 0, 127);
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

        if (_cores.Length == 1)
        {
            _cores[0].Render(interleaved, offset, frames, sampleRate);
        }
        else
        {
            int sampleCount = frames * 2;
            if (_mixBuffer.Length < sampleCount)
            {
                _mixBuffer = new float[sampleCount];
            }

            Array.Clear(interleaved, offset, sampleCount);
            foreach (OplCore core in _cores)
            {
                Array.Clear(_mixBuffer, 0, sampleCount);
                core.Render(_mixBuffer, 0, frames, sampleRate);
                for (int i = 0; i < sampleCount; i++)
                {
                    interleaved[offset + i] += _mixBuffer[i];
                }
            }
        }

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

    private int AllocateVoiceIndex(int midiChannel, int program, byte bankMsb, byte bankLsb, out VoiceAllocationKind allocationKind)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            if (IsChannelUnavailableForAllocation(i))
            {
                continue;
            }

            OplVoice voice = _voices[i];
            if (!voice.Active)
            {
                allocationKind = VoiceAllocationKind.Free;
                return i;
            }

            if (TryGetVoiceEnvelopeInfo(voice, out _, out OplEnvelopeStage stage) && stage == OplEnvelopeStage.Off)
            {
                voice.Active = false;
                voice.KeyOn = false;
                voice.Sustained = false;
                allocationKind = VoiceAllocationKind.ReleaseReuse;
                return i;
            }
        }

        int stealIndex = SelectVoiceToSteal(midiChannel, program, bankMsb, bankLsb);
        if (stealIndex < 0)
        {
            allocationKind = VoiceAllocationKind.Free;
            return 0;
        }

        KeyOffVoice(_voices[stealIndex]);
        _voices[stealIndex].Active = false;
        _voices[stealIndex].KeyOn = false;
        _voices[stealIndex].Sustained = false;
        allocationKind = VoiceAllocationKind.Steal;
        return stealIndex;
    }

    private int FindSameNoteVoiceIndex(int channel, int note, int program, byte bankMsb, byte bankLsb)
    {
        int candidate = -1;
        for (int i = 0; i < _voices.Length; i++)
        {
            OplVoice voice = _voices[i];
            if (!voice.Active || voice.MidiChannel != channel || voice.Note != note ||
                voice.Program != program || voice.BankMsb != bankMsb || voice.BankLsb != bankLsb)
            {
                continue;
            }

            if (IsChannelUnavailableForAllocation(voice.OplChannel))
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

    private void KeyOffDuplicateVoices(int channel, int note, int excludeIndex)
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            if (i == excludeIndex)
            {
                continue;
            }

            OplVoice voice = _voices[i];
            if (!voice.Active || voice.MidiChannel != channel || voice.Note != note)
            {
                continue;
            }

            KeyOffVoice(voice);
        }
    }

    private int SelectVoiceToSteal(int midiChannel, int program, byte bankMsb, byte bankLsb)
    {
        int bestIndex = -1;
        int bestCategory = int.MinValue;
        int bestPriority = int.MinValue;
        bool bestSameInstrument = false;
        int bestAge = int.MaxValue;

        for (int i = 0; i < _voices.Length; i++)
        {
            OplVoice voice = _voices[i];
            if (!voice.Active || IsChannelUnavailableForAllocation(i))
            {
                continue;
            }

            int category = GetVoiceStealCategory(voice);
            int priority = GetChannelPriority(voice.MidiChannel);
            bool sameInstrument = voice.Program == program && voice.BankMsb == bankMsb && voice.BankLsb == bankLsb;
            int age = voice.Age;

            if (IsBetterStealCandidate(category, priority, sameInstrument, age, i, bestCategory, bestPriority, bestSameInstrument, bestAge, bestIndex))
            {
                bestCategory = category;
                bestPriority = priority;
                bestSameInstrument = sameInstrument;
                bestAge = age;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private void AllNotesOff(int channel)
    {
        if (IsPercussionChannel(channel))
        {
            ClearRhythmNotes();
            return;
        }

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
            if (TryGetCore(voice.OplChannel, out OplCore core, out int localChannel))
            {
                WriteChannelFeedback(core, localChannel, channelState, voice.Instrument ?? _bankSet.DefaultInstrument);
            }
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

            SetChannelFrequency(voice.OplChannel, voice.OplNote, channelState, keyOn: voice.KeyOn);
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
                if (!TryGetVoiceEnvelopeInfo(voice, out float level, out OplEnvelopeStage stage))
                {
                    continue;
                }

                if (stage == OplEnvelopeStage.Off)
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

    private bool TryGetCore(int oplChannel, out OplCore core, out int localChannel)
    {
        core = null!;
        localChannel = 0;
        if (oplChannel < 0)
        {
            return false;
        }

        int chipIndex = oplChannel / _channelsPerChip;
        if (chipIndex < 0 || chipIndex >= _cores.Length)
        {
            return false;
        }

        core = _cores[chipIndex];
        localChannel = oplChannel - chipIndex * _channelsPerChip;
        return localChannel >= 0 && localChannel < core.Channels.Count;
    }

    private bool TryGetChannel(int oplChannel, out OplCore core, out int localChannel, out OplChannel channel)
    {
        channel = null!;
        if (!TryGetCore(oplChannel, out core, out localChannel))
        {
            return false;
        }

        channel = core.Channels[localChannel];
        return true;
    }

    private bool TryGetVoiceEnvelopeInfo(OplVoice voice, out float level, out OplEnvelopeStage stage)
    {
        if (!TryGetChannel(voice.OplChannel, out OplCore core, out _, out OplChannel channel))
        {
            level = 0f;
            stage = OplEnvelopeStage.Off;
            return false;
        }

        if (channel.CarrierIndex < 0 || channel.CarrierIndex >= core.Operators.Count)
        {
            level = 0f;
            stage = OplEnvelopeStage.Off;
            return false;
        }

        OplEnvelope envelope = core.Operators[channel.CarrierIndex].Envelope;
        level = envelope.Level;
        stage = envelope.Stage;
        return true;
    }

    private void ApplyChannelRegisters(OplVoice voice, MidiChannelState channelState)
    {
        ConfigureChannelOperators(voice, channelState);
        SetChannelFrequency(voice.OplChannel, voice.OplNote, channelState, keyOn: true);
    }

    private void ConfigureChannelOperators(OplVoice voice, MidiChannelState channelState)
    {
        if (!TryGetChannel(voice.OplChannel, out OplCore core, out int localChannel, out OplChannel channel))
        {
            return;
        }
        OplInstrument instrument = voice.Instrument ?? _bankSet.DefaultInstrument;
        OplOperatorPatch carrier = GetOperatorPatch(instrument, 0, OplInstrumentDefaults.DefaultCarrier);
        OplOperatorPatch modulator = GetOperatorPatch(instrument, 1, OplInstrumentDefaults.DefaultModulator);

        WriteOperatorRegister(core, channel.ModulatorIndex, 0x20, modulator.AmVibEgtKsrMult);
        WriteOperatorRegister(core, channel.ModulatorIndex, 0x40, modulator.KslTl);
        WriteOperatorRegister(core, channel.ModulatorIndex, 0x60, modulator.ArDr);
        WriteOperatorRegister(core, channel.ModulatorIndex, 0x80, modulator.SlRr);
        WriteOperatorRegister(core, channel.ModulatorIndex, 0xE0, modulator.Waveform);

        WriteOperatorRegister(core, channel.CarrierIndex, 0x20, carrier.AmVibEgtKsrMult);
        WriteOperatorRegister(core, channel.CarrierIndex, 0x40, ComputeCarrierTotalLevel(channelState, voice.Velocity, carrier.KslTl));
        WriteOperatorRegister(core, channel.CarrierIndex, 0x60, carrier.ArDr);
        WriteOperatorRegister(core, channel.CarrierIndex, 0x80, carrier.SlRr);
        WriteOperatorRegister(core, channel.CarrierIndex, 0xE0, carrier.Waveform);

        WriteChannelFeedback(core, localChannel, channelState, instrument);
    }

    private void UpdateCarrierTotalLevel(OplVoice voice, MidiChannelState channelState)
    {
        if (!TryGetChannel(voice.OplChannel, out OplCore core, out _, out OplChannel channel))
        {
            return;
        }

        OplOperatorPatch carrier = GetOperatorPatch(voice.Instrument, 0, OplInstrumentDefaults.DefaultCarrier);
        WriteOperatorRegister(core, channel.CarrierIndex, 0x40, ComputeCarrierTotalLevel(channelState, voice.Velocity, carrier.KslTl));
    }

    private byte ComputeCarrierTotalLevel(MidiChannelState channelState, int velocity, byte baseKslTl)
    {
        float velocityGain = Math.Clamp(velocity / 127f, 0f, 1f);
        float gain = velocityGain * GetChannelGain(channelState);
        int attenuation = (int)Math.Round((1f - gain) * 63f);
        int baseTl = baseKslTl & 0x3F;
        int total = Math.Clamp(baseTl + attenuation, 0, 63);
        return (byte)((baseKslTl & 0xC0) | total);
    }

    private void SetChannelFrequency(int oplChannel, int note, MidiChannelState channelState, bool keyOn)
    {
        if (!TryGetCore(oplChannel, out OplCore core, out int localChannel))
        {
            return;
        }

        double frequency = GetFrequency(channelState, note);
        ComputeBlockAndFnum(frequency, out int block, out int fnum);
        WriteFrequency(core, localChannel, fnum, block, keyOn);
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

    private void WriteFrequency(OplCore core, int channelIndex, int fnum, int block, bool keyOn)
    {
        int baseA0 = GetChannelAddress(channelIndex, 0xA0);
        int baseB0 = GetChannelAddress(channelIndex, 0xB0);

        core.WriteRegister(baseA0, (byte)(fnum & 0xFF));
        byte high = (byte)(((fnum >> 8) & 0x03) | ((block & 0x07) << 2) | (keyOn ? 0x20 : 0x00));
        core.WriteRegister(baseB0, high);
    }

    private void WriteChannelFeedback(OplCore core, int channelIndex, MidiChannelState channelState, OplInstrument instrument)
    {
        byte value = (byte)(instrument.FeedbackConnection1 & 0x0F);

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

        core.WriteRegister(GetChannelAddress(channelIndex, 0xC0), value);
    }

    private void WriteOperatorRegister(OplCore core, int opIndex, int groupBase, byte value)
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
        core.WriteRegister(address, value);
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

        if (TryGetChannel(voice.OplChannel, out OplCore core, out int localChannel, out OplChannel channel))
        {
            WriteFrequency(core, localChannel, channel.FNum, channel.Block, keyOn: false);
        }
    }

    private bool IsPercussionChannel(int channel)
    {
        return channel == PercussionChannel;
    }

    private bool TryResolveMelodicInstrument(MidiChannelState channelState, int note, int velocity,
        out OplInstrument instrument, out int oplNote, out int adjustedVelocity)
    {
        instrument = _bankSet.GetMelodic(channelState.BankMsb, channelState.BankLsb, channelState.Program);
        instrument ??= _bankSet.DefaultInstrument;

        if (instrument.IsBlank && channelState.BankLsb != 0)
        {
            instrument = _bankSet.GetMelodic(channelState.BankMsb, 0, channelState.Program);
        }

        if (instrument.IsBlank)
        {
            oplNote = note;
            adjustedVelocity = velocity;
            return false;
        }

        adjustedVelocity = AdjustVelocity(velocity, instrument);
        oplNote = AdjustNote(note, instrument, isPercussion: false);
        return true;
    }

    private bool TryResolvePercussionInstrument(MidiChannelState channelState, int note, int velocity,
        out OplInstrument instrument, out int oplNote, out int adjustedVelocity, out RhythmInstrument rhythmInstrument)
    {
        ResolvePercussionBank(channelState, out byte bankMsb, out byte bankLsb);
        instrument = _bankSet.GetPercussion(bankMsb, bankLsb, note);
        instrument ??= _bankSet.DefaultInstrument;

        if (instrument.IsBlank && bankLsb != 0)
        {
            instrument = _bankSet.GetPercussion(bankMsb, 0, note);
        }

        if (instrument.IsBlank)
        {
            oplNote = note;
            adjustedVelocity = velocity;
            rhythmInstrument = MapPercussionInstrument(note);
            return false;
        }

        adjustedVelocity = AdjustVelocity(velocity, instrument);
        oplNote = AdjustNote(note, instrument, isPercussion: true);

        if (instrument.RhythmMode != OplRhythmMode.None)
        {
            rhythmInstrument = MapRhythmInstrument(instrument.RhythmMode);
        }
        else
        {
            rhythmInstrument = MapPercussionInstrument(note);
        }

        return true;
    }

    private void ResolvePercussionBank(MidiChannelState channelState, out byte bankMsb, out byte bankLsb)
    {
        if (channelState.Program != 0)
        {
            bankMsb = 0;
            bankLsb = (byte)channelState.Program;
            return;
        }

        bankMsb = channelState.BankMsb;
        bankLsb = channelState.BankLsb;
    }

    private static int AdjustNote(int note, OplInstrument instrument, bool isPercussion)
    {
        int tone = note;
        if ((isPercussion || instrument.IsFixedNote) && instrument.PercussionKeyNumber != 0)
        {
            tone = instrument.PercussionKeyNumber;
        }

        tone += instrument.NoteOffset1;
        return tone;
    }

    private static int AdjustVelocity(int velocity, OplInstrument instrument)
    {
        int adjusted = velocity + instrument.MidiVelocityOffset;
        return Math.Clamp(adjusted, 1, 127);
    }

    private static RhythmInstrument MapRhythmInstrument(OplRhythmMode mode)
    {
        return mode switch
        {
            OplRhythmMode.BassDrum => RhythmInstrument.BassDrum,
            OplRhythmMode.Snare => RhythmInstrument.Snare,
            OplRhythmMode.Tom => RhythmInstrument.Tom,
            OplRhythmMode.Cymbal => RhythmInstrument.Cymbal,
            OplRhythmMode.HiHat => RhythmInstrument.HiHat,
            _ => RhythmInstrument.BassDrum
        };
    }

    private RhythmInstrument ResolveRhythmInstrument(int noteIndex, int note)
    {
        if (_rhythmNoteCounts[noteIndex] > 0)
        {
            return _rhythmNoteMap[noteIndex];
        }

        return MapPercussionInstrument(note);
    }

    private void HandlePercussionNoteOn(int note, int velocity, OplInstrument instrument, int oplNote,
        RhythmInstrument rhythmInstrument, MidiChannelState channelState)
    {
        if (_cores.Length == 0)
        {
            return;
        }

        if (velocity <= 0)
        {
            HandlePercussionNoteOff(note);
            return;
        }

        OplCore core = _cores[0];
        EnsureRhythmModeEnabled(core);
        PreemptRhythmChannels();

        int instrumentIndex = (int)rhythmInstrument;
        if (_rhythmCounts[instrumentIndex] < int.MaxValue)
        {
            _rhythmCounts[instrumentIndex]++;
        }

        int noteIndex = Math.Clamp(note, 0, _rhythmNoteMap.Length - 1);
        if (_rhythmNoteCounts[noteIndex] < int.MaxValue)
        {
            _rhythmNoteCounts[noteIndex]++;
        }

        _rhythmNoteMap[noteIndex] = rhythmInstrument;

        ConfigureRhythmOperators(core, channelState, velocity, rhythmInstrument, instrument);
        SetRhythmChannelFrequency(core, rhythmInstrument, oplNote, channelState);
        SetRhythmFlag(core, rhythmInstrument, enable: true);
    }

    private void HandlePercussionNoteOff(int note)
    {
        if (_cores.Length == 0)
        {
            return;
        }

        OplCore core = _cores[0];
        if (!core.Registers.RhythmEnabled)
        {
            return;
        }

        int noteIndex = Math.Clamp(note, 0, _rhythmNoteMap.Length - 1);
        RhythmInstrument instrument = ResolveRhythmInstrument(noteIndex, note);
        int instrumentIndex = (int)instrument;

        if (_rhythmNoteCounts[noteIndex] > 0)
        {
            _rhythmNoteCounts[noteIndex]--;
            if (_rhythmNoteCounts[noteIndex] == 0)
            {
                _rhythmNoteMap[noteIndex] = MapPercussionInstrument(note);
            }
        }

        if (_rhythmCounts[instrumentIndex] > 0)
        {
            _rhythmCounts[instrumentIndex]--;
        }

        if (_rhythmCounts[instrumentIndex] == 0)
        {
            SetRhythmFlag(core, instrument, enable: false);
        }

        if (!AnyRhythmNotesActive())
        {
            DisableRhythmMode(core);
        }
    }

    private void ClearRhythmNotes()
    {
        if (_cores.Length == 0)
        {
            return;
        }

        Array.Clear(_rhythmCounts, 0, _rhythmCounts.Length);
        Array.Clear(_rhythmNoteCounts, 0, _rhythmNoteCounts.Length);
        Array.Clear(_rhythmNoteMap, 0, _rhythmNoteMap.Length);
        DisableRhythmMode(_cores[0]);
    }

    private bool AnyRhythmNotesActive()
    {
        for (int i = 0; i < _rhythmCounts.Length; i++)
        {
            if (_rhythmCounts[i] > 0)
            {
                return true;
            }
        }

        return false;
    }

    private void EnsureRhythmModeEnabled(OplCore core)
    {
        if (core.Registers.RhythmEnabled)
        {
            return;
        }

        WriteRhythmRegister(core, flags: 0, enabled: true);
    }

    private void DisableRhythmMode(OplCore core)
    {
        WriteRhythmRegister(core, flags: 0, enabled: false);
    }

    private void SetRhythmFlag(OplCore core, RhythmInstrument instrument, bool enable)
    {
        byte mask = GetRhythmFlagMask(instrument);
        byte flags = core.Registers.RhythmFlags;

        if (enable)
        {
            if ((flags & mask) != 0)
            {
                WriteRhythmRegister(core, (byte)(flags & ~mask), enabled: true);
            }

            flags |= mask;
            WriteRhythmRegister(core, flags, enabled: true);
            return;
        }

        flags = (byte)(flags & ~mask);
        WriteRhythmRegister(core, flags, enabled: core.Registers.RhythmEnabled);
    }

    private void WriteRhythmRegister(OplCore core, byte flags, bool enabled)
    {
        byte value = 0;
        if (_deepTremolo)
        {
            value |= 0x80;
        }

        if (_deepVibrato)
        {
            value |= 0x40;
        }

        if (enabled)
        {
            value |= 0x20;
        }

        value |= (byte)(flags & 0x1F);
        core.WriteRegister(0xBD, value);
    }

    private void ApplyLfoDepths()
    {
        foreach (OplCore core in _cores)
        {
            WriteRhythmRegister(core, core.Registers.RhythmFlags, core.Registers.RhythmEnabled);
        }
    }

    private void ConfigureRhythmOperators(OplCore core, MidiChannelState channelState, int velocity, RhythmInstrument instrument, OplInstrument patch)
    {
        OplOperatorPatch carrier = GetOperatorPatch(patch, 0, OplInstrumentDefaults.DefaultCarrier);
        OplOperatorPatch modulator = GetOperatorPatch(patch, 1, OplInstrumentDefaults.DefaultModulator);
        byte carrierTl = ComputeCarrierTotalLevel(channelState, velocity, carrier.KslTl);

        switch (instrument)
        {
            case RhythmInstrument.BassDrum:
                WriteOperatorRegister(core, RhythmOpBdMod, 0x20, modulator.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpBdMod, 0x40, modulator.KslTl);
                WriteOperatorRegister(core, RhythmOpBdMod, 0x60, modulator.ArDr);
                WriteOperatorRegister(core, RhythmOpBdMod, 0x80, modulator.SlRr);
                WriteOperatorRegister(core, RhythmOpBdMod, 0xE0, modulator.Waveform);

                WriteOperatorRegister(core, RhythmOpBdCar, 0x20, carrier.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpBdCar, 0x40, carrierTl);
                WriteOperatorRegister(core, RhythmOpBdCar, 0x60, carrier.ArDr);
                WriteOperatorRegister(core, RhythmOpBdCar, 0x80, carrier.SlRr);
                WriteOperatorRegister(core, RhythmOpBdCar, 0xE0, carrier.Waveform);
                break;
            case RhythmInstrument.Snare:
                WriteOperatorRegister(core, RhythmOpSd, 0x20, carrier.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpSd, 0x40, carrierTl);
                WriteOperatorRegister(core, RhythmOpSd, 0x60, carrier.ArDr);
                WriteOperatorRegister(core, RhythmOpSd, 0x80, carrier.SlRr);
                WriteOperatorRegister(core, RhythmOpSd, 0xE0, carrier.Waveform);
                break;
            case RhythmInstrument.Tom:
                WriteOperatorRegister(core, RhythmOpTom, 0x20, carrier.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpTom, 0x40, carrierTl);
                WriteOperatorRegister(core, RhythmOpTom, 0x60, carrier.ArDr);
                WriteOperatorRegister(core, RhythmOpTom, 0x80, carrier.SlRr);
                WriteOperatorRegister(core, RhythmOpTom, 0xE0, carrier.Waveform);
                break;
            case RhythmInstrument.Cymbal:
                WriteOperatorRegister(core, RhythmOpTc, 0x20, carrier.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpTc, 0x40, carrierTl);
                WriteOperatorRegister(core, RhythmOpTc, 0x60, carrier.ArDr);
                WriteOperatorRegister(core, RhythmOpTc, 0x80, carrier.SlRr);
                WriteOperatorRegister(core, RhythmOpTc, 0xE0, carrier.Waveform);
                break;
            case RhythmInstrument.HiHat:
                WriteOperatorRegister(core, RhythmOpHh, 0x20, carrier.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpHh, 0x40, carrierTl);
                WriteOperatorRegister(core, RhythmOpHh, 0x60, carrier.ArDr);
                WriteOperatorRegister(core, RhythmOpHh, 0x80, carrier.SlRr);
                WriteOperatorRegister(core, RhythmOpHh, 0xE0, carrier.Waveform);
                break;
        }

        WriteRhythmChannelFeedback(core, channelState, instrument, patch);
    }

    private void WriteRhythmChannelFeedback(OplCore core, MidiChannelState channelState, RhythmInstrument instrument, OplInstrument patch)
    {
        int channelIndex = instrument switch
        {
            RhythmInstrument.BassDrum => RhythmChannelStart,
            RhythmInstrument.Snare => RhythmChannelStart + 1,
            RhythmInstrument.HiHat => RhythmChannelStart + 1,
            RhythmInstrument.Tom => RhythmChannelEnd,
            RhythmInstrument.Cymbal => RhythmChannelEnd,
            _ => RhythmChannelStart
        };

        WriteChannelFeedback(core, channelIndex, channelState, patch);
    }

    private void SetRhythmChannelFrequency(OplCore core, RhythmInstrument instrument, int note, MidiChannelState channelState)
    {
        int channelIndex = instrument switch
        {
            RhythmInstrument.BassDrum => 6,
            RhythmInstrument.Snare => 7,
            RhythmInstrument.HiHat => 7,
            RhythmInstrument.Tom => 8,
            RhythmInstrument.Cymbal => 8,
            _ => 6
        };

        double frequency = GetFrequency(channelState, note);
        ComputeBlockAndFnum(frequency, out int block, out int fnum);
        WriteFrequency(core, channelIndex, fnum, block, keyOn: false);
    }

    private void PreemptRhythmChannels()
    {
        for (int i = 0; i < _voices.Length; i++)
        {
            OplVoice voice = _voices[i];
            if (!voice.Active || !IsChannelReservedForRhythm(voice.OplChannel))
            {
                continue;
            }

            KeyOffVoice(voice);
            voice.Active = false;
            voice.KeyOn = false;
            voice.Sustained = false;
        }
    }

    private static RhythmInstrument MapPercussionInstrument(int note)
    {
        switch (note)
        {
            case 35:
            case 36:
                return RhythmInstrument.BassDrum;
            case 38:
            case 40:
                return RhythmInstrument.Snare;
            case 41:
            case 43:
            case 45:
            case 47:
            case 48:
            case 50:
                return RhythmInstrument.Tom;
            case 49:
            case 51:
            case 57:
            case 59:
                return RhythmInstrument.Cymbal;
            case 42:
            case 44:
            case 46:
                return RhythmInstrument.HiHat;
            default:
                if (note < 40)
                {
                    return RhythmInstrument.BassDrum;
                }

                if (note < 48)
                {
                    return RhythmInstrument.Snare;
                }

                if (note < 55)
                {
                    return RhythmInstrument.Tom;
                }

                if (note < 60)
                {
                    return RhythmInstrument.Cymbal;
                }

                return RhythmInstrument.HiHat;
        }
    }

    private static byte GetRhythmFlagMask(RhythmInstrument instrument)
    {
        return instrument switch
        {
            RhythmInstrument.BassDrum => 0x10,
            RhythmInstrument.Snare => 0x08,
            RhythmInstrument.Tom => 0x04,
            RhythmInstrument.Cymbal => 0x02,
            RhythmInstrument.HiHat => 0x01,
            _ => 0x00
        };
    }

    private int GetChannelPriority(int channel)
    {
        if (channel < 0 || channel >= ChannelPriority.Length)
        {
            return 3;
        }

        return ChannelPriority[channel];
    }

    private int GetVoiceStealCategory(OplVoice voice)
    {
        if (!voice.KeyOn)
        {
            return 2;
        }

        return voice.Sustained ? 1 : 0;
    }

    private bool IsBetterStealCandidate(int category, int priority, bool sameInstrument, int age, int index,
        int bestCategory, int bestPriority, bool bestSameInstrument, int bestAge, int bestIndex)
    {
        if (bestIndex < 0)
        {
            return true;
        }

        if (category != bestCategory)
        {
            return category > bestCategory;
        }

        if (priority != bestPriority)
        {
            return priority > bestPriority;
        }

        if (sameInstrument != bestSameInstrument)
        {
            return sameInstrument;
        }

        if (age != bestAge)
        {
            return age < bestAge;
        }

        return index < bestIndex;
    }

    private bool IsChannelUnavailableForAllocation(int oplChannel)
    {
        return IsChannelReservedForRhythm(oplChannel) || IsFourOperatorSecondaryChannel(oplChannel);
    }

    private bool IsChannelReservedForRhythm(int oplChannel)
    {
        if (_cores.Length == 0)
        {
            return false;
        }

        int chipIndex = oplChannel / _channelsPerChip;
        if (chipIndex != 0)
        {
            return false;
        }

        if (!_cores[0].Registers.RhythmEnabled)
        {
            return false;
        }

        int localChannel = oplChannel - chipIndex * _channelsPerChip;
        return localChannel >= RhythmChannelStart && localChannel <= RhythmChannelEnd;
    }

    private bool IsFourOperatorSecondaryChannel(int oplChannel)
    {
        if (_mode != OplSynthMode.Opl3)
        {
            return false;
        }

        if (!TryGetCore(oplChannel, out OplCore core, out int localChannel))
        {
            return false;
        }

        byte mask = core.Registers.FourOperatorEnableMask;
        return IsFourOperatorSecondaryChannel(localChannel, mask);
    }

    private static bool IsFourOperatorSecondaryChannel(int localChannel, byte mask)
    {
        return localChannel switch
        {
            3 => (mask & 0x01) != 0,
            4 => (mask & 0x02) != 0,
            5 => (mask & 0x04) != 0,
            12 => (mask & 0x08) != 0,
            13 => (mask & 0x10) != 0,
            14 => (mask & 0x20) != 0,
            _ => false
        };
    }

    private static OplOperatorPatch GetOperatorPatch(OplInstrument instrument, int index, OplOperatorPatch fallback)
    {
        if (instrument == null)
        {
            return fallback;
        }

        OplOperatorPatch[] operators = instrument.Operators;
        if (operators == null || index < 0 || index >= operators.Length || operators[index] == null)
        {
            return fallback;
        }

        return operators[index];
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
