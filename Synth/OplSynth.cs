namespace OpenMIDI.Synth;

public enum OplSynthMode
{
    Opl2,
    Opl3
}

public sealed class OplSynth : IMidiSynth
{
    [Flags]
    private enum SynthMode
    {
        Gm = 0,
        Gs = 1,
        Xg = 2,
        Gm2 = 4
    }

    private enum VoiceAllocationKind
    {
        Free,
        ReleaseReuse,
        Steal
    }

    private enum ChannelAllocMode
    {
        OffDelay,
        SameInstrument,
        AnyReleased
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
        public bool Sostenuto;
        public bool FourOp;
        public int MidiChannel;
        public int Note;
        public int OplNote;
        public int Velocity;
        public int NoteAftertouch;
        public int MulOffset;
        public int OperatorOffset;
        public bool GlideActive;
        public double GlideCurrentNote;
        public double GlideTargetNote;
        public double GlideRate;
        public int Program;
        public byte BankMsb;
        public byte BankLsb;
        public OplInstrument Instrument = OplInstrumentDefaults.DefaultInstrument;
        public int OplChannel;
        public int SecondaryOplChannel;
        public int Age;
        public long KeyOnRemainingUs;
        public long KeyOffRemainingUs;
        public long VibDelayUs;
    }

    private sealed class MidiChannelState
    {
        public int Program;
        public byte BankMsb;
        public byte BankLsb;
        public int PitchBend = 8192;
        public float PitchBendRangeSemitones = 2f;
        public int Volume = 100;
        public int Expression = 127;
        public int Pan = 64;
        public int Brightness = 127;
        public bool SustainPedal;
        public bool SostenutoPedal;
        public bool SoftPedal;
        public int ModWheel;
        public int Aftertouch;
        public byte[] NoteAftertouch = new byte[128];
        public bool IsXgPercussion;
        public int RpnMsb = 127;
        public int RpnLsb = 127;
        public bool RpnIsNrpn;
        public int RpnDataMsb;
        public int RpnDataLsb;
        public int PortamentoValue;
        public bool PortamentoEnabled;
        public int PortamentoSourceNote = -1;
        public double PortamentoRate = double.PositiveInfinity;
        public double ModulationPhase;
        public double VibratoSpeedRad;
        public double VibratoDepthSemitonesPerUnit;
        public long VibratoDelayUs;
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
    private const byte ManufacturerUniversalNonRealtime = 0x7E;
    private const byte ManufacturerUniversalRealtime = 0x7F;
    private const byte ManufacturerRoland = 0x41;
    private const byte ManufacturerYamaha = 0x43;
    private const byte RolandModelGs = 0x42;
    private const byte RolandModeSend = 0x12;
    private const byte YamahaModelXg = 0x4C;
    private const byte SysExDeviceAll = 0x7F;
    private const byte MasterVolumeDefault = 127;
    private const double VibratoDepthSemitonesPerUnit = 0.5 / 127.0;
    private static readonly double ModulationSpeedRad = 2.0 * Math.PI * 5.0;
    private static readonly double ModulationTwoPi = Math.PI * 2.0;
    private readonly OplCore[] _cores;
    private readonly int _channelsPerChip;
    private readonly OplVoice[] _voices;
    private readonly MidiChannelState[] _channels;
    private readonly OplSynthMode _mode;
    private int _ageCounter;
    private readonly int[] _channelActiveCounts;
    private readonly int[] _channelReleaseCounts;
    private readonly float[] _channelLevels;
    private int _activeVoiceCount;
    private int _releaseVoiceCount;
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
    private readonly OplInstrument?[] _rhythmInstruments = new OplInstrument?[RhythmInstrumentCount];
    private readonly int[] _rhythmVelocities = new int[RhythmInstrumentCount];
    private OplInstrumentBankSet _bankSet = OplInstrumentBankSet.CreateDefault();
    private SynthMode _synthMode = SynthMode.Xg;
    private byte _masterVolume = MasterVolumeDefault;
    private byte _sysExDeviceId;
    private bool _deepTremolo;
    private bool _deepVibrato;
    private static readonly int[] OperatorIndexToOffset =
    {
        0, 1, 2, 3, 4, 5, 8, 9, 10, 11, 12, 13, 16, 17, 18, 19, 20, 21
    };
    private static readonly (bool DoMod, bool DoCar)[] DoOps =
    {
        (false, true),
        (true, true),
        (false, false),
        (true, false),
        (false, true),
        (true, false),
        (false, true),
        (false, true),
        (false, true),
        (true, true)
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
        _channelReleaseCounts = new int[16];
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
    public int ReleaseVoiceCount => _releaseVoiceCount;
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
        _synthMode = SynthMode.Xg;
        _masterVolume = MasterVolumeDefault;
        _sysExDeviceId = 0;
        int defaultVolume = GetDefaultVolume();
        float defaultBendRange = GetDefaultPitchBendRange();

        foreach (OplVoice voice in _voices)
        {
            voice.Active = false;
            voice.KeyOn = false;
            voice.Sustained = false;
            voice.Sostenuto = false;
            voice.FourOp = false;
            voice.MidiChannel = 0;
            voice.Note = 0;
            voice.OplNote = 0;
            voice.Velocity = 0;
            voice.NoteAftertouch = 0;
            voice.MulOffset = 0;
            voice.OperatorOffset = 0;
            voice.GlideActive = false;
            voice.GlideCurrentNote = 0;
            voice.GlideTargetNote = 0;
            voice.GlideRate = double.PositiveInfinity;
            voice.Program = 0;
            voice.BankMsb = 0;
            voice.BankLsb = 0;
            voice.Instrument = _bankSet.DefaultInstrument;
            voice.OplChannel = 0;
            voice.SecondaryOplChannel = -1;
            voice.Age = 0;
            voice.KeyOnRemainingUs = 0;
            voice.KeyOffRemainingUs = 0;
            voice.VibDelayUs = 0;
        }

        foreach (MidiChannelState channel in _channels)
        {
            channel.Program = 0;
            channel.BankMsb = 0;
            channel.BankLsb = 0;
            channel.PitchBend = 8192;
            channel.PitchBendRangeSemitones = defaultBendRange;
            channel.Volume = defaultVolume;
            channel.Expression = 127;
            channel.Pan = 64;
            channel.Brightness = 127;
            channel.SustainPedal = false;
            channel.SostenutoPedal = false;
            channel.SoftPedal = false;
            channel.ModWheel = 0;
            channel.Aftertouch = 0;
            Array.Clear(channel.NoteAftertouch, 0, channel.NoteAftertouch.Length);
            channel.IsXgPercussion = false;
            channel.RpnMsb = 127;
            channel.RpnLsb = 127;
            channel.RpnIsNrpn = false;
            channel.RpnDataMsb = 0;
            channel.RpnDataLsb = 0;
            channel.PortamentoValue = 0;
            channel.PortamentoEnabled = false;
            channel.PortamentoSourceNote = -1;
            channel.PortamentoRate = double.PositiveInfinity;
            channel.ModulationPhase = 0;
            channel.VibratoSpeedRad = ModulationSpeedRad;
            channel.VibratoDepthSemitonesPerUnit = VibratoDepthSemitonesPerUnit;
            channel.VibratoDelayUs = 0;
        }

        _ageCounter = 0;
        _activeVoiceCount = 0;
        _releaseVoiceCount = 0;
        _peakActiveVoiceCount = 0;
        _lastPeakLeft = 0f;
        _lastPeakRight = 0f;
        _noteOnCount = 0;
        _sameNoteReuseCount = 0;
        _releaseReuseCount = 0;
        _voiceStealCount = 0;
        Array.Clear(_channelActiveCounts, 0, _channelActiveCounts.Length);
        Array.Clear(_channelReleaseCounts, 0, _channelReleaseCounts.Length);
        Array.Clear(_channelLevels, 0, _channelLevels.Length);
        Array.Clear(_rhythmCounts, 0, _rhythmCounts.Length);
        Array.Clear(_rhythmNoteCounts, 0, _rhythmNoteCounts.Length);
        Array.Clear(_rhythmNoteMap, 0, _rhythmNoteMap.Length);
        Array.Clear(_rhythmInstruments, 0, _rhythmInstruments.Length);
        Array.Clear(_rhythmVelocities, 0, _rhythmVelocities.Length);
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

    private int GetDefaultVolume()
    {
        return _bankSet.Mt32Defaults ? 127 : 100;
    }

    private float GetDefaultPitchBendRange()
    {
        return _bankSet.Mt32Defaults ? 12f : PitchBendRangeSemitones;
    }

    private void ConfigureVoiceForNote(OplVoice voice, int voiceIndex, int channel, int note, int oplNote,
        int velocity, MidiChannelState channelState, OplInstrument instrument, bool fourOp, int secondaryOplChannel,
        int operatorOffset, bool portamentoEnabled, int portamentoSourceNote)
    {
        voice.Active = true;
        voice.KeyOn = true;
        voice.Sustained = false;
        voice.Sostenuto = false;
        voice.FourOp = fourOp;
        voice.MidiChannel = channel;
        voice.Note = note;
        voice.OplNote = oplNote;
        voice.Velocity = velocity;
        voice.NoteAftertouch = 0;
        voice.MulOffset = 0;
        voice.OperatorOffset = operatorOffset;
        voice.GlideActive = false;
        voice.GlideCurrentNote = oplNote;
        voice.GlideTargetNote = oplNote;
        voice.GlideRate = double.PositiveInfinity;
        voice.Program = channelState.Program;
        voice.BankMsb = channelState.BankMsb;
        voice.BankLsb = channelState.BankLsb;
        voice.Instrument = instrument;
        voice.OplChannel = voiceIndex;
        voice.SecondaryOplChannel = secondaryOplChannel;
        voice.Age = _ageCounter++;
        InitializeVoiceTimers(voice, instrument);

        int noteIndex = Math.Clamp(note, 0, 127);
        voice.NoteAftertouch = channelState.NoteAftertouch[noteIndex];

        if (portamentoEnabled && portamentoSourceNote >= 0)
        {
            voice.GlideCurrentNote = portamentoSourceNote;
            voice.GlideTargetNote = oplNote;
            voice.GlideRate = channelState.PortamentoRate;
            voice.GlideActive = voice.GlideRate > 0 && !double.IsInfinity(voice.GlideRate);
        }
    }

    private static long GetInstrumentDelayUs(ushort delayMs)
    {
        if (delayMs <= 0)
        {
            return 0;
        }

        return delayMs * 1000L;
    }

    private void InitializeVoiceTimers(OplVoice voice, OplInstrument instrument)
    {
        OplInstrument source = instrument ?? _bankSet.DefaultInstrument;
        voice.KeyOnRemainingUs = GetInstrumentDelayUs(source.DelayOnMs);
        voice.KeyOffRemainingUs = 0;
        voice.VibDelayUs = 0;
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

            if (channelState.SoftPedal)
            {
                adjustedVelocity = (int)Math.Floor(adjustedVelocity * 0.8f);
            }

            HandlePercussionNoteOn(note, adjustedVelocity, instrument, oplNote, rhythmInstrument, channelState);
            return;
        }

        if (!TryResolveMelodicInstrument(channelState, note, velocity, out OplInstrument melodicInstrument,
                out int melodicNote, out int melodicVelocity))
        {
            return;
        }

        int portamentoSourceNote = channelState.PortamentoSourceNote;
        bool portamentoEnabled = channelState.PortamentoEnabled && channelState.PortamentoRate < double.PositiveInfinity;
        channelState.PortamentoSourceNote = note;

        if (channelState.SoftPedal)
        {
            melodicVelocity = (int)Math.Floor(melodicVelocity * 0.8f);
        }

        bool wantsFourOp = IsFourOpInstrument(melodicInstrument) && _mode == OplSynthMode.Opl3;
        bool wantsPseudoFourOp = !wantsFourOp && IsPseudoFourOpInstrument(melodicInstrument);

        if (wantsFourOp)
        {
            int reuseIndex = FindSameNoteVoiceIndex(channel, note, channelState.Program, channelState.BankMsb,
                channelState.BankLsb, operatorOffset: 0);
            if (reuseIndex >= 0 && !_voices[reuseIndex].FourOp)
            {
                reuseIndex = -1;
            }

            if (reuseIndex >= 0)
            {
                _sameNoteReuseCount++;
                KeyOffDuplicateVoices(channel, note, reuseIndex);
                OplVoice reuseVoice = _voices[reuseIndex];
                ReclaimVoice(reuseVoice);
                int secondaryChannel = -1;
                bool useFourOp = true;

                secondaryChannel = reuseVoice.SecondaryOplChannel;
                if (secondaryChannel < 0 && !TryGetFourOpSecondaryChannel(reuseVoice.OplChannel, out secondaryChannel))
                {
                    useFourOp = false;
                }

                if (useFourOp)
                {
                    EnableFourOpPair(reuseVoice.OplChannel);
                }

                ConfigureVoiceForNote(reuseVoice, reuseIndex, channel, note, melodicNote, melodicVelocity, channelState,
                    melodicInstrument, useFourOp, secondaryChannel, operatorOffset: 0, portamentoEnabled, portamentoSourceNote);
                ApplyChannelRegisters(reuseVoice, channelState);
                return;
            }

            KeyOffDuplicateVoices(channel, note, -1);
            int voiceIndex = 0;
            int secondaryOplChannel = -1;
            VoiceAllocationKind allocationKind = VoiceAllocationKind.Free;
            bool useFourOpVoice =
                TryAllocateFourOpVoiceIndex(channelState.Program, channelState.BankMsb, channelState.BankLsb,
                    out voiceIndex, out secondaryOplChannel, out allocationKind);

            if (!useFourOpVoice)
            {
                voiceIndex = AllocateVoiceIndex(channel, channelState.Program, channelState.BankMsb, channelState.BankLsb,
                    out allocationKind);
            }

            if (allocationKind == VoiceAllocationKind.ReleaseReuse)
            {
                _releaseReuseCount++;
            }
            else if (allocationKind == VoiceAllocationKind.Steal)
            {
                _voiceStealCount++;
            }

            if (useFourOpVoice)
            {
                EnableFourOpPair(voiceIndex);
            }

            OplVoice voice = _voices[voiceIndex];
            ConfigureVoiceForNote(voice, voiceIndex, channel, note, melodicNote, melodicVelocity, channelState,
                melodicInstrument, useFourOpVoice, secondaryOplChannel, operatorOffset: 0, portamentoEnabled, portamentoSourceNote);
            ApplyChannelRegisters(voice, channelState);
            return;
        }

        int reusePrimary = FindSameNoteVoiceIndex(channel, note, channelState.Program, channelState.BankMsb,
            channelState.BankLsb, operatorOffset: 0);
        if (reusePrimary >= 0 && _voices[reusePrimary].FourOp)
        {
            reusePrimary = -1;
        }

        if (reusePrimary >= 0)
        {
            _sameNoteReuseCount++;
            KeyOffDuplicateVoices(channel, note, reusePrimary);
            OplVoice reuseVoice = _voices[reusePrimary];
            ReclaimVoice(reuseVoice);
            ConfigureVoiceForNote(reuseVoice, reusePrimary, channel, note, melodicNote, melodicVelocity, channelState,
                melodicInstrument, fourOp: false, secondaryOplChannel: -1, operatorOffset: 0, portamentoEnabled, portamentoSourceNote);
            ApplyChannelRegisters(reuseVoice, channelState);

            if (wantsPseudoFourOp)
            {
                TryStartPseudoSecondVoice(channelState, channel, note, melodicNote, melodicVelocity, melodicInstrument,
                    portamentoEnabled, portamentoSourceNote, reusePrimary);
            }
            return;
        }

        KeyOffDuplicateVoices(channel, note, -1);
        int primaryIndex = AllocateVoiceIndex(channel, channelState.Program, channelState.BankMsb, channelState.BankLsb,
            out VoiceAllocationKind primaryKind);

        if (primaryKind == VoiceAllocationKind.ReleaseReuse)
        {
            _releaseReuseCount++;
        }
        else if (primaryKind == VoiceAllocationKind.Steal)
        {
            _voiceStealCount++;
        }

        OplVoice primaryVoice = _voices[primaryIndex];
        ConfigureVoiceForNote(primaryVoice, primaryIndex, channel, note, melodicNote, melodicVelocity, channelState,
            melodicInstrument, fourOp: false, secondaryOplChannel: -1, operatorOffset: 0, portamentoEnabled, portamentoSourceNote);
        ApplyChannelRegisters(primaryVoice, channelState);

        if (wantsPseudoFourOp)
        {
            TryStartPseudoSecondVoice(channelState, channel, note, melodicNote, melodicVelocity, melodicInstrument,
                portamentoEnabled, portamentoSourceNote, primaryIndex);
        }
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

                if (voice.Sostenuto)
                {
                    continue;
                }

                KeyOffVoice(voice);
            }
        }
    }

    public void PolyAftertouch(int channel, int note, int pressure)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        MidiChannelState channelState = _channels[channel];
        int value = Math.Clamp(pressure, 0, 127);
        int noteIndex = Math.Clamp(note, 0, 127);
        channelState.NoteAftertouch[noteIndex] = (byte)value;

        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active || voice.MidiChannel != channel || voice.Note != note)
            {
                continue;
            }

            voice.NoteAftertouch = value;
        }

        UpdateChannelModulation(channel, channelState);
    }

    public void ChannelAftertouch(int channel, int pressure)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        MidiChannelState channelState = _channels[channel];
        channelState.Aftertouch = Math.Clamp(pressure, 0, 127);
        UpdateChannelModulation(channel, channelState);
    }

    public void ControlChange(int channel, int controller, int value)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        MidiChannelState channelState = _channels[channel];
        int clamped = Math.Clamp(value, 0, 127);
        switch (controller)
        {
            case 1:
                channelState.ModWheel = clamped;
                UpdateChannelModulation(channel, channelState);
                break;
            case 0:
                channelState.BankMsb = (byte)clamped;
                if ((_synthMode & SynthMode.Gs) == 0)
                {
                    channelState.IsXgPercussion = IsXgPercussionBank(channelState);
                }
                break;
            case 32:
                channelState.BankLsb = (byte)clamped;
                if ((_synthMode & SynthMode.Gs) == 0)
                {
                    channelState.IsXgPercussion = IsXgPercussionBank(channelState);
                }
                break;
            case 5:
                channelState.PortamentoValue = (channelState.PortamentoValue & 0x007F) | (clamped << 7);
                UpdatePortamentoRate(channelState);
                break;
            case 37:
                channelState.PortamentoValue = (channelState.PortamentoValue & 0x3F80) | clamped;
                UpdatePortamentoRate(channelState);
                break;
            case 65:
                channelState.PortamentoEnabled = clamped >= 64;
                UpdatePortamentoRate(channelState);
                break;
            case 7:
                channelState.Volume = clamped;
                UpdateChannelGains(channel, channelState);
                break;
            case 10:
                channelState.Pan = clamped;
                UpdateChannelGains(channel, channelState);
                break;
            case 11:
                channelState.Expression = clamped;
                UpdateChannelGains(channel, channelState);
                break;
            case 64:
                UpdateSustainPedal(channel, channelState, clamped >= 64);
                break;
            case 66:
                UpdateSostenutoPedal(channel, channelState, clamped >= 64);
                break;
            case 67:
                channelState.SoftPedal = clamped >= 64;
                break;
            case 74:
                channelState.Brightness = clamped;
                UpdateChannelGains(channel, channelState);
                break;
            case 98:
                channelState.RpnLsb = clamped;
                channelState.RpnIsNrpn = true;
                break;
            case 99:
                channelState.RpnMsb = clamped;
                channelState.RpnIsNrpn = true;
                break;
            case 100:
                channelState.RpnLsb = clamped;
                channelState.RpnIsNrpn = false;
                break;
            case 101:
                channelState.RpnMsb = clamped;
                channelState.RpnIsNrpn = false;
                break;
            case 6:
                channelState.RpnDataMsb = clamped;
                ApplyRpnData(channel, channelState, dataIsMsb: true);
                break;
            case 38:
                channelState.RpnDataLsb = clamped;
                ApplyRpnData(channel, channelState, dataIsMsb: false);
                break;
            case 121:
                ResetChannelControllers(channel, channelState);
                break;
            case 120:
                AllSoundsOff(channel);
                break;
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

    public void SysEx(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xF0 || data[^1] != 0xF7)
        {
            return;
        }

        byte manufacturer = data[1];
        byte dev = data[2];
        ReadOnlySpan<byte> payload = data.Slice(3, data.Length - 4);

        switch (manufacturer)
        {
            case ManufacturerUniversalNonRealtime:
                ProcessUniversalSysEx(dev, realtime: false, payload);
                break;
            case ManufacturerUniversalRealtime:
                ProcessUniversalSysEx(dev, realtime: true, payload);
                break;
            case ManufacturerRoland:
                ProcessRolandSysEx(dev, payload);
                break;
            case ManufacturerYamaha:
                ProcessYamahaSysEx(dev, payload);
                break;
        }
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
        AdvanceVoiceTiming(frames, sampleRate);
        double deltaSeconds = sampleRate > 0 ? frames / (double)sampleRate : 0.0;
        UpdateGlide(deltaSeconds);
        UpdateModulation(deltaSeconds);
        UpdateChannelMeters();
    }

    public void CopyChannelMeters(Span<int> counts, Span<float> levels)
    {
        CopyChannelMeters(counts, Span<int>.Empty, levels);
    }

    public void CopyChannelMeters(Span<int> counts, Span<int> releaseCounts, Span<float> levels)
    {
        int countLength = Math.Min(counts.Length, _channelActiveCounts.Length);
        _channelActiveCounts.AsSpan(0, countLength).CopyTo(counts);

        if (!releaseCounts.IsEmpty)
        {
            int releaseLength = Math.Min(releaseCounts.Length, _channelReleaseCounts.Length);
            _channelReleaseCounts.AsSpan(0, releaseLength).CopyTo(releaseCounts);
        }

        int levelLength = Math.Min(levels.Length, _channelLevels.Length);
        _channelLevels.AsSpan(0, levelLength).CopyTo(levels);
    }

    private int AllocateVoiceIndex(int midiChannel, int program, byte bankMsb, byte bankLsb, out VoiceAllocationKind allocationKind)
    {
        _ = midiChannel;
        ChannelAllocMode allocMode = ResolveAllocMode();
        long bestScore = long.MinValue;
        int bestIndex = -1;

        for (int i = 0; i < _voices.Length; i++)
        {
            if (IsChannelUnavailableForAllocation(i))
            {
                continue;
            }

            OplVoice voice = _voices[i];
            long score = CalculateVoiceGoodness(voice, program, bankMsb, bankLsb, allocMode);

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            allocationKind = VoiceAllocationKind.Free;
            return 0;
        }

        OplVoice selected = _voices[bestIndex];
        if (!selected.Active)
        {
            allocationKind = VoiceAllocationKind.Free;
            return bestIndex;
        }

        allocationKind = selected.KeyOn ? VoiceAllocationKind.Steal : VoiceAllocationKind.ReleaseReuse;
        ReclaimVoice(selected);
        return bestIndex;
    }

    private int AllocateVoiceIndexExcluding(int midiChannel, int program, byte bankMsb, byte bankLsb, int excludeIndex,
        out VoiceAllocationKind allocationKind)
    {
        _ = midiChannel;
        ChannelAllocMode allocMode = ResolveAllocMode();
        long bestScore = long.MinValue;
        int bestIndex = -1;

        for (int i = 0; i < _voices.Length; i++)
        {
            if (i == excludeIndex)
            {
                continue;
            }

            if (IsChannelUnavailableForAllocation(i))
            {
                continue;
            }

            OplVoice voice = _voices[i];
            long score = CalculateVoiceGoodness(voice, program, bankMsb, bankLsb, allocMode);

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (bestIndex < 0)
        {
            allocationKind = VoiceAllocationKind.Free;
            return -1;
        }

        OplVoice selected = _voices[bestIndex];
        if (!selected.Active)
        {
            allocationKind = VoiceAllocationKind.Free;
            return bestIndex;
        }

        allocationKind = selected.KeyOn ? VoiceAllocationKind.Steal : VoiceAllocationKind.ReleaseReuse;
        ReclaimVoice(selected);
        return bestIndex;
    }

    private bool TryAllocateFourOpVoiceIndex(int program, byte bankMsb, byte bankLsb, out int primaryIndex,
        out int secondaryIndex, out VoiceAllocationKind allocationKind)
    {
        primaryIndex = -1;
        secondaryIndex = -1;
        allocationKind = VoiceAllocationKind.Free;

        ChannelAllocMode allocMode = ResolveAllocMode();
        long bestScore = long.MinValue;
        int bestPrimary = -1;
        int bestSecondary = -1;
        VoiceAllocationKind bestKind = VoiceAllocationKind.Free;

        for (int i = 0; i < _voices.Length; i++)
        {
            if (!TryGetFourOpPairInfo(i, out _, out _, out int candidateSecondary, out _))
            {
                continue;
            }

            OplVoice primaryVoice = _voices[i];
            OplVoice secondaryVoice = _voices[candidateSecondary];
            long primaryScore = CalculateVoiceGoodness(primaryVoice, program, bankMsb, bankLsb, allocMode);
            long secondaryScore = CalculateVoiceGoodness(secondaryVoice, program, bankMsb, bankLsb, allocMode);
            long score = Math.Min(primaryScore, secondaryScore);
            VoiceAllocationKind kind = GetPairAllocationKind(primaryVoice, secondaryVoice);

            if (score > bestScore)
            {
                bestScore = score;
                bestPrimary = i;
                bestSecondary = candidateSecondary;
                bestKind = kind;
            }
        }

        if (bestPrimary < 0 || bestSecondary < 0)
        {
            return false;
        }

        OplVoice selectedPrimary = _voices[bestPrimary];
        OplVoice selectedSecondary = _voices[bestSecondary];

        if (selectedPrimary.Active)
        {
            ReclaimVoice(selectedPrimary);
        }

        if (selectedSecondary.Active)
        {
            ReclaimVoice(selectedSecondary);
        }

        primaryIndex = bestPrimary;
        secondaryIndex = bestSecondary;
        allocationKind = bestKind;
        return true;
    }

    private void TryStartPseudoSecondVoice(MidiChannelState channelState, int channel, int note, int oplNote, int velocity,
        OplInstrument instrument, bool portamentoEnabled, int portamentoSourceNote, int primaryIndex)
    {
        int secondaryIndex = AllocateVoiceIndexExcluding(channel, channelState.Program, channelState.BankMsb, channelState.BankLsb,
            primaryIndex, out VoiceAllocationKind allocationKind);

        if (secondaryIndex < 0)
        {
            return;
        }

        if (allocationKind == VoiceAllocationKind.ReleaseReuse)
        {
            _releaseReuseCount++;
        }
        else if (allocationKind == VoiceAllocationKind.Steal)
        {
            _voiceStealCount++;
        }

        OplVoice secondaryVoice = _voices[secondaryIndex];
        ConfigureVoiceForNote(secondaryVoice, secondaryIndex, channel, note, oplNote, velocity, channelState,
            instrument, fourOp: false, secondaryOplChannel: -1, operatorOffset: 2, portamentoEnabled, portamentoSourceNote);
        ApplyChannelRegisters(secondaryVoice, channelState);
    }

    private static VoiceAllocationKind GetPairAllocationKind(OplVoice primary, OplVoice secondary)
    {
        bool primaryActive = primary.Active;
        bool secondaryActive = secondary.Active;
        if (!primaryActive && !secondaryActive)
        {
            return VoiceAllocationKind.Free;
        }

        bool anyKeyOn = (primaryActive && primary.KeyOn) || (secondaryActive && secondary.KeyOn);
        return anyKeyOn ? VoiceAllocationKind.Steal : VoiceAllocationKind.ReleaseReuse;
    }

    private int FindSameNoteVoiceIndex(int channel, int note, int program, byte bankMsb, byte bankLsb, int operatorOffset)
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

            if (voice.OperatorOffset != operatorOffset)
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

    private void AllNotesOff(int channel)
    {
        if (IsPercussionChannel(channel))
        {
            ClearRhythmNotes();
            return;
        }

        MidiChannelState channelState = _channels[channel];
        foreach (OplVoice voice in _voices)
        {
            if (voice.Active && voice.MidiChannel == channel)
            {
                if (channelState.SustainPedal)
                {
                    voice.Sustained = true;
                    continue;
                }

                if (voice.Sostenuto)
                {
                    continue;
                }

                KeyOffVoice(voice);
            }
        }
    }

    private void AllSoundsOff(int channel)
    {
        if (IsPercussionChannel(channel))
        {
            ClearRhythmNotes();
            return;
        }

        MidiChannelState channelState = _channels[channel];
        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active || voice.MidiChannel != channel)
            {
                continue;
            }

            if (channelState.SustainPedal)
            {
                voice.Sustained = true;
                continue;
            }

            if (voice.Sostenuto)
            {
                continue;
            }

            MuteVoice(voice);
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

    private void UpdateSostenutoPedal(int channel, MidiChannelState channelState, bool enabled)
    {
        if (channelState.SostenutoPedal == enabled)
        {
            return;
        }

        channelState.SostenutoPedal = enabled;
        if (enabled)
        {
            foreach (OplVoice voice in _voices)
            {
                if (!voice.Active || voice.MidiChannel != channel || !voice.KeyOn || voice.Sustained)
                {
                    continue;
                }

                voice.Sostenuto = true;
            }
        }
        else
        {
            ReleaseSostenutoVoices(channel);
        }
    }

    private void ResetChannelControllers(int channel, MidiChannelState channelState)
    {
        channelState.PitchBend = 8192;
        channelState.PitchBendRangeSemitones = GetDefaultPitchBendRange();
        channelState.Expression = 127;
        channelState.SustainPedal = false;
        channelState.SostenutoPedal = false;
        channelState.SoftPedal = false;
        channelState.ModWheel = 0;
        channelState.Aftertouch = 0;
        Array.Clear(channelState.NoteAftertouch, 0, channelState.NoteAftertouch.Length);
        channelState.RpnMsb = 127;
        channelState.RpnLsb = 127;
        channelState.RpnIsNrpn = false;
        channelState.RpnDataMsb = 0;
        channelState.RpnDataLsb = 0;
        channelState.PortamentoValue = 0;
        channelState.PortamentoEnabled = false;
        channelState.PortamentoSourceNote = -1;
        channelState.PortamentoRate = double.PositiveInfinity;
        channelState.ModulationPhase = 0;
        channelState.VibratoSpeedRad = ModulationSpeedRad;
        channelState.VibratoDepthSemitonesPerUnit = VibratoDepthSemitonesPerUnit;
        channelState.VibratoDelayUs = 0;

        ReleaseSustainedVoices(channel);
        ReleaseSostenutoVoices(channel);
        UpdateChannelGains(channel, channelState);
        UpdateChannelFrequencies(channel, channelState);
        UpdateChannelModulation(channel, channelState);
    }

    private void ApplyRpnData(int channel, MidiChannelState channelState, bool dataIsMsb)
    {
        if (channelState.RpnIsNrpn)
        {
            if ((_synthMode & SynthMode.Xg) == 0 || !dataIsMsb)
            {
                return;
            }

            int address = (channelState.RpnMsb << 8) | channelState.RpnLsb;
            int value = channelState.RpnDataMsb;
            switch (address)
            {
                case 0x0108:
                    if (value == 64)
                    {
                        channelState.VibratoSpeedRad = ModulationSpeedRad;
                    }
                    else if (value < 100)
                    {
                        int divisor = value == 0 ? 1 : value;
                        channelState.VibratoSpeedRad = (1.0 / (1.6e-2 * divisor)) * ModulationSpeedRad;
                    }
                    else
                    {
                        channelState.VibratoSpeedRad = (1.0 / (0.051153846 * value - 3.4965385)) * ModulationSpeedRad;
                    }
                    break;
                case 0x0109:
                    channelState.VibratoDepthSemitonesPerUnit = ((value - 64) * 0.15) * 0.01;
                    break;
                case 0x010A:
                    channelState.VibratoDelayUs = value == 0
                        ? 0
                        : (long)Math.Round(209.2 * Math.Exp(0.0795 * value));
                    break;
            }

            return;
        }

        if (channelState.RpnMsb == 0 && channelState.RpnLsb == 0)
        {
            float semitones = channelState.RpnDataMsb + channelState.RpnDataLsb / 128f;
            channelState.PitchBendRangeSemitones = Math.Clamp(semitones, 0f, 96f);
            UpdateChannelFrequencies(channel, channelState);
        }
    }

    private void UpdatePortamentoRate(MidiChannelState channelState)
    {
        double rate = double.PositiveInfinity;
        if (channelState.PortamentoEnabled && channelState.PortamentoValue > 0)
        {
            rate = 350.0 * Math.Pow(2.0, -0.062 * (1.0 / 128.0) * channelState.PortamentoValue);
        }

        channelState.PortamentoRate = rate;
    }

    private void ResetState()
    {
        _masterVolume = MasterVolumeDefault;
        int defaultVolume = GetDefaultVolume();
        float defaultBendRange = GetDefaultPitchBendRange();

        for (int i = 0; i < _channels.Length; i++)
        {
            MidiChannelState channelState = _channels[i];
            channelState.PitchBend = 8192;
            channelState.PitchBendRangeSemitones = defaultBendRange;
            channelState.Volume = defaultVolume;
            channelState.Expression = 127;
            channelState.Pan = 64;
            channelState.Brightness = 127;
            channelState.SustainPedal = false;
            channelState.SostenutoPedal = false;
            channelState.SoftPedal = false;
            channelState.ModWheel = 0;
            channelState.Aftertouch = 0;
            Array.Clear(channelState.NoteAftertouch, 0, channelState.NoteAftertouch.Length);
            channelState.RpnMsb = 127;
            channelState.RpnLsb = 127;
            channelState.RpnIsNrpn = false;
            channelState.RpnDataMsb = 0;
            channelState.RpnDataLsb = 0;
            channelState.PortamentoValue = 0;
            channelState.PortamentoEnabled = false;
            channelState.PortamentoSourceNote = -1;
            channelState.PortamentoRate = double.PositiveInfinity;
            channelState.ModulationPhase = 0;
            channelState.VibratoSpeedRad = ModulationSpeedRad;
            channelState.VibratoDepthSemitonesPerUnit = VibratoDepthSemitonesPerUnit;
            channelState.VibratoDelayUs = 0;

            if ((_synthMode & SynthMode.Gs) != 0)
            {
                channelState.IsXgPercussion = false;
            }

            UpdateChannelGains(i, channelState);
            UpdateChannelFrequencies(i, channelState);
            UpdateChannelModulation(i, channelState);
            AllNotesOff(i);
        }
    }

    private bool ProcessUniversalSysEx(byte dev, bool realtime, ReadOnlySpan<byte> data)
    {
        bool deviceMatch = dev == SysExDeviceAll || dev == _sysExDeviceId;
        if (data.Length < 2 || !deviceMatch)
        {
            return false;
        }

        int address = ((data[0] & 0x7F) << 8) | (data[1] & 0x7F);
        ReadOnlySpan<byte> payload = data.Slice(2);

        if (!realtime && address == 0x0901)
        {
            _synthMode = SynthMode.Gm;
            ResetState();
            return true;
        }

        if (!realtime && address == 0x0902)
        {
            _synthMode = SynthMode.Xg;
            ResetState();
            return true;
        }

        if (realtime && address == 0x0401 && payload.Length == 2)
        {
            int volume = (payload[0] & 0x7F) | ((payload[1] & 0x7F) << 7);
            _masterVolume = (byte)(volume >> 7);
            for (int i = 0; i < _channels.Length; i++)
            {
                UpdateChannelGains(i, _channels[i]);
            }
            return true;
        }

        return false;
    }

    private bool ProcessRolandSysEx(byte dev, ReadOnlySpan<byte> data)
    {
        bool deviceMatch = dev == SysExDeviceAll || (dev & 0x0F) == _sysExDeviceId;
        if (data.Length < 6 || !deviceMatch)
        {
            return false;
        }

        byte model = (byte)(data[0] & 0x7F);
        byte mode = (byte)(data[1] & 0x7F);
        byte checksum = (byte)(data[^1] & 0x7F);
        ReadOnlySpan<byte> payload = data.Slice(2, data.Length - 3);

        int checkValue = 0;
        for (int i = 0; i < payload.Length; i++)
        {
            checkValue += payload[i] & 0x7F;
        }
        checkValue = (128 - (checkValue & 127)) & 127;
        if (checkValue != checksum)
        {
            return false;
        }

        int address = ((payload[0] & 0x7F) << 16) | ((payload[1] & 0x7F) << 8) | (payload[2] & 0x7F);
        int targetChannel = 0;
        if ((address & 0xFFF0FF) == 0x401015)
        {
            address = 0x401015;
            targetChannel = payload[1] & 0x0F;
        }

        ReadOnlySpan<byte> commandData = payload.Slice(3);
        if (mode != RolandModeSend)
        {
            return false;
        }

        switch ((model << 24) | address)
        {
            case (RolandModelGs << 24) | 0x00007F:
            {
                if (commandData.Length != 1 || (dev & 0xF0) != 0x10)
                {
                    break;
                }

                _synthMode = SynthMode.Gs;
                ResetState();
                return true;
            }
            case (RolandModelGs << 24) | 0x40007F:
            {
                if (commandData.Length != 1 || (dev & 0xF0) != 0x10)
                {
                    break;
                }

                _synthMode = SynthMode.Gs;
                ResetState();
                return true;
            }
            case (RolandModelGs << 24) | 0x401015:
            {
                if (commandData.Length != 1 || (dev & 0xF0) != 0x10)
                {
                    break;
                }

                if (_channels.Length < 16)
                {
                    break;
                }

                byte value = (byte)(commandData[0] & 0x7F);
                int[] channelsMap =
                {
                    9, 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15
                };
                int midiChannel = channelsMap[Math.Clamp(targetChannel, 0, channelsMap.Length - 1)];
                _channels[midiChannel].IsXgPercussion = value is 0x01 or 0x02;
                return true;
            }
        }

        return false;
    }

    private bool ProcessYamahaSysEx(byte dev, ReadOnlySpan<byte> data)
    {
        bool deviceMatch = dev == SysExDeviceAll || (dev & 0x0F) == _sysExDeviceId;
        if (data.Length < 1 || !deviceMatch)
        {
            return false;
        }

        byte model = (byte)(data[0] & 0x7F);
        ReadOnlySpan<byte> payload = data.Slice(1);

        switch ((model << 8) | (dev & 0xF0))
        {
            case (YamahaModelXg << 8) | 0x10:
            {
                if (payload.Length < 3)
                {
                    break;
                }

                int address = ((payload[0] & 0x7F) << 16) | ((payload[1] & 0x7F) << 8) | (payload[2] & 0x7F);
                ReadOnlySpan<byte> commandData = payload.Slice(3);

                if (address == 0x00007E && commandData.Length == 1)
                {
                    _synthMode = SynthMode.Xg;
                    ResetState();
                    return true;
                }
                break;
            }
        }

        return false;
    }

    private void ReleaseSustainedVoices(int channel)
    {
        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active || voice.MidiChannel != channel || !voice.Sustained)
            {
                continue;
            }

            if (voice.Sostenuto)
            {
                voice.Sustained = false;
                continue;
            }

            KeyOffVoice(voice);
        }
    }

    private void ReleaseSostenutoVoices(int channel)
    {
        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active || voice.MidiChannel != channel || !voice.Sostenuto)
            {
                continue;
            }

            if (voice.Sustained)
            {
                voice.Sostenuto = false;
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

            UpdateVoiceLevels(voice, channelState);
            UpdateVoiceFeedback(voice, channelState);
        }

        if (IsPercussionChannel(channel) && _cores.Length > 0 && _cores[0].Registers.RhythmEnabled)
        {
            OplCore core = _cores[0];
            for (int i = 0; i < _rhythmInstruments.Length; i++)
            {
                if (_rhythmCounts[i] <= 0)
                {
                    continue;
                }

                OplInstrument? instrument = _rhythmInstruments[i];
                if (instrument == null)
                {
                    continue;
                }

                ConfigureRhythmOperators(core, channelState, _rhythmVelocities[i], (RhythmInstrument)i, instrument);
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

            UpdateVoiceFrequency(voice, channelState);
        }
    }

    private void UpdateChannelModulation(int channel, MidiChannelState channelState)
    {
        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active || voice.MidiChannel != channel)
            {
                continue;
            }

            UpdateVoiceFrequency(voice, channelState);
        }
    }

    private void UpdateGlide(double deltaSeconds)
    {
        if (deltaSeconds <= 0)
        {
            return;
        }

        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active || !voice.GlideActive)
            {
                continue;
            }

            if (!IsValidChannel(voice.MidiChannel))
            {
                voice.GlideActive = false;
                continue;
            }

            double rate = voice.GlideRate;
            if (rate <= 0 || double.IsInfinity(rate))
            {
                voice.GlideActive = false;
                continue;
            }

            double previous = voice.GlideCurrentNote;
            double target = voice.GlideTargetNote;
            bool directionUp = previous < target;
            double next = previous + (directionUp ? rate : -rate) * deltaSeconds;
            bool finished = directionUp ? next >= target : next <= target;
            if (finished)
            {
                next = target;
                voice.GlideActive = false;
            }

            if (Math.Abs(next - previous) < 1e-6)
            {
                continue;
            }

            voice.GlideCurrentNote = next;
            MidiChannelState channelState = _channels[voice.MidiChannel];
            UpdateVoiceFrequency(voice, channelState);
        }
    }

    private void UpdateModulation(double deltaSeconds)
    {
        if (deltaSeconds <= 0)
        {
            return;
        }

        Span<bool> channelHasNotes = stackalloc bool[_channels.Length];
        Span<bool> noteAftertouch = stackalloc bool[_channels.Length];
        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active)
            {
                continue;
            }

            int channel = voice.MidiChannel;
            if (channel >= 0 && channel < noteAftertouch.Length)
            {
                channelHasNotes[channel] = true;
                if (voice.NoteAftertouch > 0)
                {
                    noteAftertouch[channel] = true;
                }
            }
        }

        bool anyModulation = false;
        Span<bool> channelHasModulation = stackalloc bool[_channels.Length];
        for (int i = 0; i < _channels.Length; i++)
        {
            MidiChannelState channelState = _channels[i];
            bool hasModulation = channelState.ModWheel > 0 || channelState.Aftertouch > 0 || noteAftertouch[i];
            channelHasModulation[i] = hasModulation;
            if (hasModulation && channelHasNotes[i])
            {
                double phaseStep = deltaSeconds * channelState.VibratoSpeedRad;
                channelState.ModulationPhase = AdvancePhase(channelState.ModulationPhase, phaseStep);
                anyModulation = true;
            }
            else
            {
                channelState.ModulationPhase = 0;
            }
        }

        if (!anyModulation)
        {
            return;
        }

        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active)
            {
                continue;
            }

            int channel = voice.MidiChannel;
            if (!IsValidChannel(channel))
            {
                continue;
            }

            if (!channelHasModulation[channel])
            {
                continue;
            }

            MidiChannelState channelState = _channels[channel];
            UpdateVoiceFrequency(voice, channelState);
        }
    }

    private void AdvanceVoiceTiming(int frames, int sampleRate)
    {
        if (frames <= 0 || sampleRate <= 0)
        {
            return;
        }

        long deltaUs = (long)Math.Round(frames * 1_000_000.0 / sampleRate);
        if (deltaUs <= 0)
        {
            return;
        }

        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active)
            {
                continue;
            }

            if (voice.KeyOn)
            {
                if (voice.KeyOnRemainingUs > 0)
                {
                    voice.KeyOnRemainingUs = Math.Max(0, voice.KeyOnRemainingUs - deltaUs);
                }
            }
            else if (voice.KeyOffRemainingUs > 0)
            {
                voice.KeyOffRemainingUs = Math.Max(0, voice.KeyOffRemainingUs - deltaUs);
            }

            if (voice.VibDelayUs <= long.MaxValue - deltaUs)
            {
                voice.VibDelayUs += deltaUs;
            }
            else
            {
                voice.VibDelayUs = long.MaxValue;
            }
        }
    }

    private void UpdateChannelMeters()
    {
        Array.Clear(_channelActiveCounts, 0, _channelActiveCounts.Length);
        Array.Clear(_channelReleaseCounts, 0, _channelReleaseCounts.Length);
        Array.Clear(_channelLevels, 0, _channelLevels.Length);
        _activeVoiceCount = 0;
        _releaseVoiceCount = 0;

        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active)
            {
                continue;
            }

            if (!TryGetVoiceEnvelopeInfo(voice, out float level, out OplEnvelopeStage stage))
            {
                continue;
            }

            if (stage == OplEnvelopeStage.Off)
            {
                DeactivateVoice(voice);
                continue;
            }

            bool channelValid = voice.MidiChannel >= 0 && voice.MidiChannel < _channelActiveCounts.Length;
            if (voice.KeyOn)
            {
                _activeVoiceCount++;
                if (channelValid)
                {
                    _channelActiveCounts[voice.MidiChannel]++;
                }
            }
            else
            {
                _releaseVoiceCount++;
                if (channelValid)
                {
                    _channelReleaseCounts[voice.MidiChannel]++;
                }
            }

            if (channelValid)
            {
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
        if (voice.FourOp)
        {
            return TryGetFourOpEnvelopeInfo(voice, out level, out stage);
        }

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

    private bool TryGetFourOpEnvelopeInfo(OplVoice voice, out float level, out OplEnvelopeStage stage)
    {
        level = 0f;
        stage = OplEnvelopeStage.Off;
        if (voice.SecondaryOplChannel < 0)
        {
            return false;
        }

        if (!TryGetChannel(voice.OplChannel, out OplCore core, out _, out OplChannel primary) ||
            !TryGetChannel(voice.SecondaryOplChannel, out _, out _, out OplChannel secondary))
        {
            return false;
        }

        int op1Index = primary.ModulatorIndex;
        int op2Index = primary.CarrierIndex;
        int op3Index = secondary.ModulatorIndex;
        int op4Index = secondary.CarrierIndex;
        int algorithm = ((primary.Additive ? 1 : 0) << 1) | (secondary.Additive ? 1 : 0);

        float maxLevel = 0f;
        OplEnvelopeStage maxStage = OplEnvelopeStage.Off;
        bool allOff = true;

        bool Accumulate(int opIndex)
        {
            if (!TryGetOperatorEnvelopeInfo(core, opIndex, out float opLevel, out OplEnvelopeStage opStage))
            {
                return false;
            }

            if (opLevel > maxLevel)
            {
                maxLevel = opLevel;
                maxStage = opStage;
            }

            if (opStage != OplEnvelopeStage.Off)
            {
                allOff = false;
            }

            return true;
        }

        switch (algorithm)
        {
            case 0:
                if (!Accumulate(op4Index))
                {
                    return false;
                }
                break;
            case 1:
                if (!Accumulate(op2Index) || !Accumulate(op4Index))
                {
                    return false;
                }
                break;
            case 2:
                if (!Accumulate(op1Index) || !Accumulate(op4Index))
                {
                    return false;
                }
                break;
            default:
                if (!Accumulate(op1Index) || !Accumulate(op3Index) || !Accumulate(op4Index))
                {
                    return false;
                }
                break;
        }

        level = maxLevel;
        stage = allOff ? OplEnvelopeStage.Off : maxStage;
        return true;
    }

    private static bool TryGetOperatorEnvelopeInfo(OplCore core, int operatorIndex, out float level,
        out OplEnvelopeStage stage)
    {
        level = 0f;
        stage = OplEnvelopeStage.Off;
        if (operatorIndex < 0 || operatorIndex >= core.Operators.Count)
        {
            return false;
        }

        OplEnvelope envelope = core.Operators[operatorIndex].Envelope;
        level = envelope.Level;
        stage = envelope.Stage;
        return true;
    }

    private void ApplyChannelRegisters(OplVoice voice, MidiChannelState channelState)
    {
        ConfigureChannelOperators(voice, channelState);
        UpdateVoiceLevels(voice, channelState);
        UpdateVoiceFrequency(voice, channelState);
    }

    private void ConfigureChannelOperators(OplVoice voice, MidiChannelState channelState)
    {
        if (!TryGetChannel(voice.OplChannel, out OplCore core, out int localChannel, out OplChannel channel))
        {
            return;
        }
        OplInstrument instrument = voice.Instrument ?? _bankSet.DefaultInstrument;
        if (voice.FourOp && voice.SecondaryOplChannel >= 0 &&
            TryGetChannel(voice.SecondaryOplChannel, out OplCore secondaryCore, out int secondaryLocalChannel, out OplChannel secondaryChannel))
        {
            if (!ReferenceEquals(core, secondaryCore))
            {
                return;
            }

            OplOperatorPatch carrier1 = GetOperatorPatch(instrument, 0, OplInstrumentDefaults.DefaultCarrier);
            OplOperatorPatch modulator1 = GetOperatorPatch(instrument, 1, OplInstrumentDefaults.DefaultModulator);
            OplOperatorPatch carrier2 = GetOperatorPatch(instrument, 2, OplInstrumentDefaults.DefaultCarrier);
            OplOperatorPatch modulator2 = GetOperatorPatch(instrument, 3, OplInstrumentDefaults.DefaultModulator);

            WriteOperatorRegister(core, channel.ModulatorIndex, 0x20, modulator1.AmVibEgtKsrMult);
            WriteOperatorRegister(core, channel.ModulatorIndex, 0x40, modulator1.KslTl);
            WriteOperatorRegister(core, channel.ModulatorIndex, 0x60, modulator1.ArDr);
            WriteOperatorRegister(core, channel.ModulatorIndex, 0x80, modulator1.SlRr);
            WriteOperatorRegister(core, channel.ModulatorIndex, 0xE0, modulator1.Waveform);

            WriteOperatorRegister(core, channel.CarrierIndex, 0x20, carrier1.AmVibEgtKsrMult);
            WriteOperatorRegister(core, channel.CarrierIndex, 0x40, carrier1.KslTl);
            WriteOperatorRegister(core, channel.CarrierIndex, 0x60, carrier1.ArDr);
            WriteOperatorRegister(core, channel.CarrierIndex, 0x80, carrier1.SlRr);
            WriteOperatorRegister(core, channel.CarrierIndex, 0xE0, carrier1.Waveform);

            WriteOperatorRegister(core, secondaryChannel.ModulatorIndex, 0x20, modulator2.AmVibEgtKsrMult);
            WriteOperatorRegister(core, secondaryChannel.ModulatorIndex, 0x40, modulator2.KslTl);
            WriteOperatorRegister(core, secondaryChannel.ModulatorIndex, 0x60, modulator2.ArDr);
            WriteOperatorRegister(core, secondaryChannel.ModulatorIndex, 0x80, modulator2.SlRr);
            WriteOperatorRegister(core, secondaryChannel.ModulatorIndex, 0xE0, modulator2.Waveform);

            WriteOperatorRegister(core, secondaryChannel.CarrierIndex, 0x20, carrier2.AmVibEgtKsrMult);
            WriteOperatorRegister(core, secondaryChannel.CarrierIndex, 0x40, carrier2.KslTl);
            WriteOperatorRegister(core, secondaryChannel.CarrierIndex, 0x60, carrier2.ArDr);
            WriteOperatorRegister(core, secondaryChannel.CarrierIndex, 0x80, carrier2.SlRr);
            WriteOperatorRegister(core, secondaryChannel.CarrierIndex, 0xE0, carrier2.Waveform);

            WriteChannelFeedback(core, localChannel, channelState, instrument.FeedbackConnection1);
            WriteChannelFeedback(core, secondaryLocalChannel, channelState, instrument.FeedbackConnection2);
            return;
        }

        int operatorOffset = voice.OperatorOffset;
        OplOperatorPatch carrier = GetOperatorPatch(instrument, operatorOffset, OplInstrumentDefaults.DefaultCarrier);
        OplOperatorPatch modulator = GetOperatorPatch(instrument, operatorOffset + 1, OplInstrumentDefaults.DefaultModulator);

        WriteOperatorRegister(core, channel.ModulatorIndex, 0x20, modulator.AmVibEgtKsrMult);
        WriteOperatorRegister(core, channel.ModulatorIndex, 0x40, modulator.KslTl);
        WriteOperatorRegister(core, channel.ModulatorIndex, 0x60, modulator.ArDr);
        WriteOperatorRegister(core, channel.ModulatorIndex, 0x80, modulator.SlRr);
        WriteOperatorRegister(core, channel.ModulatorIndex, 0xE0, modulator.Waveform);

        WriteOperatorRegister(core, channel.CarrierIndex, 0x20, carrier.AmVibEgtKsrMult);
        WriteOperatorRegister(core, channel.CarrierIndex, 0x40, carrier.KslTl);
        WriteOperatorRegister(core, channel.CarrierIndex, 0x60, carrier.ArDr);
        WriteOperatorRegister(core, channel.CarrierIndex, 0x80, carrier.SlRr);
        WriteOperatorRegister(core, channel.CarrierIndex, 0xE0, carrier.Waveform);

        byte feedbackConnection = operatorOffset == 2 ? instrument.FeedbackConnection2 : instrument.FeedbackConnection1;
        WriteChannelFeedback(core, localChannel, channelState, feedbackConnection);
    }

    private void UpdateVoiceLevels(OplVoice voice, MidiChannelState channelState)
    {
        if (!TryGetChannel(voice.OplChannel, out OplCore core, out _, out OplChannel channel))
        {
            return;
        }

        OplInstrument instrument = voice.Instrument ?? _bankSet.DefaultInstrument;
        bool isDrum = IsPercussionChannel(voice.MidiChannel);
        byte brightness = GetScaledBrightness(channelState.Brightness);
        byte velocity = (byte)Math.Clamp(voice.Velocity, 0, 127);
        byte channelVolume = (byte)Math.Clamp(channelState.Volume, 0, 127);
        byte channelExpression = (byte)Math.Clamp(channelState.Expression, 0, 127);

        if (voice.FourOp && voice.SecondaryOplChannel >= 0 &&
            TryGetChannel(voice.SecondaryOplChannel, out OplCore secondaryCore, out _, out OplChannel secondaryChannel) &&
            ReferenceEquals(core, secondaryCore))
        {
            int algoBits = (instrument.FeedbackConnection1 & 0x01) | ((instrument.FeedbackConnection2 & 0x01) << 1);
            OplVoiceMode primaryMode = (OplVoiceMode)((int)OplVoiceMode.FourOp1_2FmFm + algoBits);
            OplVoiceMode secondaryMode = (OplVoiceMode)((int)OplVoiceMode.FourOp3_4FmFm + algoBits);

            ApplyVolumeToOperators(core, channel.ModulatorIndex, channel.CarrierIndex,
                instrument, velocity, channelVolume, channelExpression, brightness, isDrum,
                instrument.FeedbackConnection1, primaryMode);

            ApplyVolumeToOperators(core, secondaryChannel.ModulatorIndex, secondaryChannel.CarrierIndex,
                instrument, velocity, channelVolume, channelExpression, brightness, isDrum,
                instrument.FeedbackConnection2, secondaryMode, operatorOffset: 2);
            return;
        }

        byte feedbackConnection = voice.OperatorOffset == 2 ? instrument.FeedbackConnection2 : instrument.FeedbackConnection1;
        OplVoiceMode mode = (OplVoiceMode)((int)OplVoiceMode.TwoOpFm + (feedbackConnection & 0x01));
        ApplyVolumeToOperators(core, channel.ModulatorIndex, channel.CarrierIndex,
            instrument, velocity, channelVolume, channelExpression, brightness, isDrum,
            feedbackConnection, mode, operatorOffset: voice.OperatorOffset);
    }

    private void ApplyVolumeToOperators(OplCore core, int modulatorIndex, int carrierIndex, OplInstrument instrument,
        byte velocity, byte channelVolume, byte channelExpression, byte brightness, bool isDrum,
        byte feedbackConnection, OplVoiceMode voiceMode, int operatorOffset = 0)
    {
        ComputeOperatorLevels(instrument, operatorOffset, feedbackConnection, voiceMode, velocity, channelVolume,
            channelExpression, brightness, isDrum, out byte modTl, out byte carTl);
        WriteOperatorRegister(core, modulatorIndex, 0x40, modTl);
        WriteOperatorRegister(core, carrierIndex, 0x40, carTl);
    }

    private void ComputeOperatorLevels(OplInstrument instrument, int operatorOffset, byte feedbackConnection, OplVoiceMode voiceMode,
        byte velocity, byte channelVolume, byte channelExpression, byte brightness, bool isDrum,
        out byte modTl, out byte carTl)
    {
        OplOperatorPatch carrier = GetOperatorPatch(instrument, operatorOffset, OplInstrumentDefaults.DefaultCarrier);
        OplOperatorPatch modulator = GetOperatorPatch(instrument, operatorOffset + 1, OplInstrumentDefaults.DefaultModulator);

        bool doMod = DoOps[(int)voiceMode].DoMod;
        bool doCar = DoOps[(int)voiceMode].DoCar;

        OplVolumeContext volume = new OplVolumeContext
        {
            Velocity = velocity,
            ChannelVolume = channelVolume,
            ChannelExpression = channelExpression,
            MasterVolume = _masterVolume,
            VoiceMode = voiceMode,
            FeedbackConnection = (byte)(feedbackConnection & 0x0F),
            TlMod = (byte)(modulator.KslTl & 0x3F),
            TlCar = (byte)(carrier.KslTl & 0x3F),
            DoMod = doMod,
            DoCar = doCar,
            IsDrum = isDrum
        };

        OplModels.ApplyVolumeModel(_bankSet.VolumeModel, ref volume);
        OplModels.ApplyBrightness(brightness, ref volume);

        modTl = (byte)((modulator.KslTl & 0xC0) | (volume.TlMod & 0x3F));
        carTl = (byte)((carrier.KslTl & 0xC0) | (volume.TlCar & 0x3F));
    }

    private void UpdateVoiceFeedback(OplVoice voice, MidiChannelState channelState)
    {
        if (!TryGetCore(voice.OplChannel, out OplCore core, out int localChannel))
        {
            return;
        }

        OplInstrument instrument = voice.Instrument ?? _bankSet.DefaultInstrument;
        if (voice.FourOp && voice.SecondaryOplChannel >= 0 &&
            TryGetCore(voice.SecondaryOplChannel, out OplCore secondaryCore, out int secondaryLocalChannel) &&
            ReferenceEquals(core, secondaryCore))
        {
            WriteChannelFeedback(core, localChannel, channelState, instrument.FeedbackConnection1);
            WriteChannelFeedback(core, secondaryLocalChannel, channelState, instrument.FeedbackConnection2);
            return;
        }

        byte feedbackConnection = voice.OperatorOffset == 2 ? instrument.FeedbackConnection2 : instrument.FeedbackConnection1;
        WriteChannelFeedback(core, localChannel, channelState, feedbackConnection);
    }

    private void UpdateVoiceFrequency(OplVoice voice, MidiChannelState channelState)
    {
        if (voice.Sustained || voice.Sostenuto)
        {
            return;
        }

        if (!TryGetCore(voice.OplChannel, out OplCore core, out int localChannel))
        {
            return;
        }

        OplInstrument instrument = voice.Instrument ?? _bankSet.DefaultInstrument;
        double baseNote = voice.GlideActive ? voice.GlideCurrentNote : voice.OplNote;
        double tone = baseNote + GetPitchBendOffset(channelState) + GetVibratoOffset(channelState, voice);

        double primaryTone = tone + GetNoteOffset(instrument, voice.OperatorOffset);
        if (voice.OperatorOffset == 2 && (instrument.Flags & OplInstrumentFlags.PseudoFourOp) != 0)
        {
            primaryTone += GetSecondVoiceDetune(instrument);
        }

        ushort primaryToneValue = OplModels.ComputeTone(_bankSet.VolumeModel, primaryTone, out int primaryMulOffset);
        DecodeTone(primaryToneValue, out int primaryFnum, out int primaryBlock);
        WriteFrequency(core, localChannel, primaryFnum, primaryBlock, voice.KeyOn);
        ApplyOperatorMultiples(core, voice.OplChannel, instrument, voice.OperatorOffset, primaryMulOffset);
        voice.MulOffset = primaryMulOffset;

        if (voice.FourOp && voice.SecondaryOplChannel >= 0 &&
            TryGetCore(voice.SecondaryOplChannel, out OplCore secondaryCore, out int secondaryLocalChannel))
        {
            double secondaryTone = tone + instrument.NoteOffset2;
            ushort secondaryToneValue = OplModels.ComputeTone(_bankSet.VolumeModel, secondaryTone, out int secondaryMulOffset);
            DecodeTone(secondaryToneValue, out int secondaryFnum, out int secondaryBlock);
            WriteFrequency(secondaryCore, secondaryLocalChannel, secondaryFnum, secondaryBlock, voice.KeyOn);
            ApplyOperatorMultiples(secondaryCore, voice.SecondaryOplChannel, instrument, operatorOffset: 2, secondaryMulOffset);
        }
    }

    private void WriteFrequency(OplCore core, int channelIndex, int fnum, int block, bool keyOn)
    {
        int baseA0 = GetChannelAddress(channelIndex, 0xA0);
        int baseB0 = GetChannelAddress(channelIndex, 0xB0);

        core.WriteRegister(baseA0, (byte)(fnum & 0xFF));
        byte high = (byte)(((fnum >> 8) & 0x03) | ((block & 0x07) << 2) | (keyOn ? 0x20 : 0x00));
        core.WriteRegister(baseB0, high);
    }

    private void ApplyOperatorMultiples(OplCore core, int oplChannel, OplInstrument instrument, int operatorOffset, int mulOffset)
    {
        if (!TryGetChannel(oplChannel, out _, out _, out OplChannel channel))
        {
            return;
        }

        OplOperatorPatch carrier = GetOperatorPatch(instrument, operatorOffset, OplInstrumentDefaults.DefaultCarrier);
        OplOperatorPatch modulator = GetOperatorPatch(instrument, operatorOffset + 1, OplInstrumentDefaults.DefaultModulator);
        int offset = mulOffset;

        ApplyOperatorMultiple(core, channel.ModulatorIndex, modulator.AmVibEgtKsrMult, ref offset);
        ApplyOperatorMultiple(core, channel.CarrierIndex, carrier.AmVibEgtKsrMult, ref offset);
    }

    private void ApplyOperatorMultiple(OplCore core, int opIndex, byte baseValue, ref int mulOffset)
    {
        if (opIndex < 0)
        {
            return;
        }

        int dt = baseValue & 0xF0;
        int mul = baseValue & 0x0F;
        if (mulOffset > 0)
        {
            if (mul + mulOffset > 0x0F)
            {
                mulOffset = 0;
                mul = 0x0F;
            }
            else
            {
                mul += mulOffset;
            }
        }

        WriteOperatorRegister(core, opIndex, 0x20, (byte)(dt | (mul & 0x0F)));
    }

    private static void DecodeTone(ushort toneValue, out int fnum, out int block)
    {
        fnum = toneValue & 0x03FF;
        block = (toneValue >> 10) & 0x07;
    }

    private void WriteChannelFeedback(OplCore core, int channelIndex, MidiChannelState channelState, byte feedbackConnection)
    {
        byte value = (byte)(feedbackConnection & 0x0F);

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

        if (voice.KeyOn)
        {
            BeginVoiceRelease(voice);
        }

        voice.KeyOn = false;
        voice.Sustained = false;

        if (TryGetChannel(voice.OplChannel, out OplCore core, out int localChannel, out OplChannel channel))
        {
            WriteFrequency(core, localChannel, channel.FNum, channel.Block, keyOn: false);
        }
    }

    private void MuteVoice(OplVoice voice)
    {
        if (!voice.Active)
        {
            return;
        }

        OplCore? primaryCore = null;
        if (TryGetChannel(voice.OplChannel, out OplCore core, out int localChannel, out OplChannel channel))
        {
            primaryCore = core;
            WriteFrequency(core, localChannel, channel.FNum, channel.Block, keyOn: false);
            WriteOperatorRegister(core, channel.ModulatorIndex, 0x40, 0x3F);
            WriteOperatorRegister(core, channel.CarrierIndex, 0x40, 0x3F);
        }

        if (voice.FourOp && voice.SecondaryOplChannel >= 0 &&
            primaryCore != null &&
            TryGetChannel(voice.SecondaryOplChannel, out OplCore secondaryCore, out _, out OplChannel secondaryChannel) &&
            ReferenceEquals(primaryCore, secondaryCore))
        {
            WriteOperatorRegister(primaryCore, secondaryChannel.ModulatorIndex, 0x40, 0x3F);
            WriteOperatorRegister(primaryCore, secondaryChannel.CarrierIndex, 0x40, 0x3F);
        }

        DeactivateVoice(voice);
    }

    private void BeginVoiceRelease(OplVoice voice)
    {
        OplInstrument source = voice.Instrument ?? _bankSet.DefaultInstrument;
        voice.KeyOnRemainingUs = 0;
        voice.KeyOffRemainingUs = GetInstrumentDelayUs(source.DelayOffMs);
        voice.VibDelayUs = 0;
    }

    private void ReclaimVoice(OplVoice voice)
    {
        if (!voice.Active)
        {
            return;
        }

        if (voice.KeyOn)
        {
            KeyOffVoice(voice);
        }

        DeactivateVoice(voice);
    }

    private void DeactivateVoice(OplVoice voice)
    {
        bool wasFourOp = voice.FourOp;
        int primaryChannel = voice.OplChannel;
        int secondaryChannel = voice.SecondaryOplChannel;

        voice.Active = false;
        voice.KeyOn = false;
        voice.Sustained = false;
        voice.Sostenuto = false;
        voice.FourOp = false;
        voice.SecondaryOplChannel = -1;
        voice.KeyOnRemainingUs = 0;
        voice.KeyOffRemainingUs = 0;
        voice.VibDelayUs = 0;
        voice.NoteAftertouch = 0;
        voice.MulOffset = 0;
        voice.OperatorOffset = 0;
        voice.GlideActive = false;
        voice.GlideCurrentNote = 0;
        voice.GlideTargetNote = 0;
        voice.GlideRate = double.PositiveInfinity;

        if (wasFourOp)
        {
            ReleaseFourOpPairIfUnused(primaryChannel, secondaryChannel);
        }
    }

    private bool IsPercussionChannel(int channel)
    {
        if (channel == PercussionChannel)
        {
            return true;
        }

        if (!IsValidChannel(channel))
        {
            return false;
        }

        return _channels[channel].IsXgPercussion;
    }

    private static bool IsFourOpInstrument(OplInstrument instrument)
    {
        return (instrument.Flags & OplInstrumentFlags.FourOp) != 0;
    }

    private static bool IsPseudoFourOpInstrument(OplInstrument instrument)
    {
        return (instrument.Flags & OplInstrumentFlags.PseudoFourOp) != 0;
    }

    private bool TryResolveMelodicInstrument(MidiChannelState channelState, int note, int velocity,
        out OplInstrument instrument, out int oplNote, out int adjustedVelocity)
    {
        byte bankMsb = channelState.BankMsb;
        byte bankLsb = channelState.BankLsb;
        int program = channelState.Program;

        instrument = _bankSet.GetMelodic(bankMsb, bankLsb, program);
        instrument ??= _bankSet.DefaultInstrument;

        if (instrument.IsBlank && bankLsb != 0)
        {
            instrument = _bankSet.GetMelodic(bankMsb, 0, program);
            instrument ??= _bankSet.DefaultInstrument;
        }

        if (instrument.IsBlank && (bankMsb != 0 || bankLsb != 0))
        {
            instrument = _bankSet.GetMelodic(0, 0, program);
            instrument ??= _bankSet.DefaultInstrument;
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

        byte fallbackLsb = (byte)(bankLsb & 0x80);
        if (instrument.IsBlank && fallbackLsb != bankLsb)
        {
            instrument = _bankSet.GetPercussion(bankMsb, fallbackLsb, note);
            instrument ??= _bankSet.DefaultInstrument;
        }

        if (instrument.IsBlank && (bankMsb != 0 || bankLsb != 0))
        {
            instrument = _bankSet.GetPercussion(0, 0, note);
            instrument ??= _bankSet.DefaultInstrument;
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
        int bank = channelState.Program;
        if ((_synthMode & SynthMode.Xg) != 0 && channelState.BankMsb == 0x7E)
        {
            bank += 128;
        }

        bankMsb = (byte)((bank >> 8) & 0xFF);
        bankLsb = (byte)(bank & 0xFF);
    }

    private static int AdjustNote(int note, OplInstrument instrument, bool isPercussion)
    {
        int tone = note;
        if ((isPercussion || instrument.IsFixedNote) && instrument.PercussionKeyNumber != 0)
        {
            tone = instrument.PercussionKeyNumber;
            if (tone >= 128)
            {
                tone -= 128;
            }
        }
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

        _rhythmInstruments[instrumentIndex] = instrument;
        _rhythmVelocities[instrumentIndex] = velocity;

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
            _rhythmInstruments[instrumentIndex] = null;
            _rhythmVelocities[instrumentIndex] = 0;
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
        Array.Clear(_rhythmInstruments, 0, _rhythmInstruments.Length);
        Array.Clear(_rhythmVelocities, 0, _rhythmVelocities.Length);
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
        byte channelVolume = (byte)Math.Clamp(channelState.Volume, 0, 127);
        byte channelExpression = (byte)Math.Clamp(channelState.Expression, 0, 127);
        byte brightness = GetScaledBrightness(channelState.Brightness);
        byte velocityValue = (byte)Math.Clamp(velocity, 0, 127);
        byte feedbackConnection = patch.FeedbackConnection1;
        OplVoiceMode voiceMode = (OplVoiceMode)((int)OplVoiceMode.TwoOpFm + (feedbackConnection & 0x01));
        if (instrument != RhythmInstrument.BassDrum)
        {
            voiceMode = OplVoiceMode.TwoOpAm;
        }

        ComputeOperatorLevels(patch, operatorOffset: 0, feedbackConnection, voiceMode, velocityValue, channelVolume,
            channelExpression, brightness, isDrum: true, out byte modTl, out byte carTl);

        switch (instrument)
        {
            case RhythmInstrument.BassDrum:
                WriteOperatorRegister(core, RhythmOpBdMod, 0x20, modulator.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpBdMod, 0x40, modTl);
                WriteOperatorRegister(core, RhythmOpBdMod, 0x60, modulator.ArDr);
                WriteOperatorRegister(core, RhythmOpBdMod, 0x80, modulator.SlRr);
                WriteOperatorRegister(core, RhythmOpBdMod, 0xE0, modulator.Waveform);

                WriteOperatorRegister(core, RhythmOpBdCar, 0x20, carrier.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpBdCar, 0x40, carTl);
                WriteOperatorRegister(core, RhythmOpBdCar, 0x60, carrier.ArDr);
                WriteOperatorRegister(core, RhythmOpBdCar, 0x80, carrier.SlRr);
                WriteOperatorRegister(core, RhythmOpBdCar, 0xE0, carrier.Waveform);
                break;
            case RhythmInstrument.Snare:
                WriteOperatorRegister(core, RhythmOpSd, 0x20, carrier.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpSd, 0x40, carTl);
                WriteOperatorRegister(core, RhythmOpSd, 0x60, carrier.ArDr);
                WriteOperatorRegister(core, RhythmOpSd, 0x80, carrier.SlRr);
                WriteOperatorRegister(core, RhythmOpSd, 0xE0, carrier.Waveform);
                break;
            case RhythmInstrument.Tom:
                WriteOperatorRegister(core, RhythmOpTom, 0x20, carrier.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpTom, 0x40, carTl);
                WriteOperatorRegister(core, RhythmOpTom, 0x60, carrier.ArDr);
                WriteOperatorRegister(core, RhythmOpTom, 0x80, carrier.SlRr);
                WriteOperatorRegister(core, RhythmOpTom, 0xE0, carrier.Waveform);
                break;
            case RhythmInstrument.Cymbal:
                WriteOperatorRegister(core, RhythmOpTc, 0x20, carrier.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpTc, 0x40, carTl);
                WriteOperatorRegister(core, RhythmOpTc, 0x60, carrier.ArDr);
                WriteOperatorRegister(core, RhythmOpTc, 0x80, carrier.SlRr);
                WriteOperatorRegister(core, RhythmOpTc, 0xE0, carrier.Waveform);
                break;
            case RhythmInstrument.HiHat:
                WriteOperatorRegister(core, RhythmOpHh, 0x20, carrier.AmVibEgtKsrMult);
                WriteOperatorRegister(core, RhythmOpHh, 0x40, carTl);
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

        WriteChannelFeedback(core, channelIndex, channelState, patch.FeedbackConnection1);
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

        double tone = note + GetPitchBendOffset(channelState);
        ushort toneValue = OplModels.ComputeTone(_bankSet.VolumeModel, tone, out _);
        DecodeTone(toneValue, out int fnum, out int block);
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
            DeactivateVoice(voice);
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

    private ChannelAllocMode ResolveAllocMode()
    {
        return _bankSet.VolumeModel switch
        {
            OplVolumeModel.Hmi => ChannelAllocMode.AnyReleased,
            OplVolumeModel.HmiOld => ChannelAllocMode.AnyReleased,
            OplVolumeModel.MsAdlib => ChannelAllocMode.SameInstrument,
            OplVolumeModel.ImfCreator => ChannelAllocMode.SameInstrument,
            _ => ChannelAllocMode.OffDelay
        };
    }

    private static bool IsSameInstrument(OplVoice voice, int program, byte bankMsb, byte bankLsb)
    {
        return voice.Program == program && voice.BankMsb == bankMsb && voice.BankLsb == bankLsb;
    }

    private long CalculateVoiceGoodness(OplVoice voice, int program, byte bankMsb, byte bankLsb, ChannelAllocMode allocMode)
    {
        bool sameInstrument = IsSameInstrument(voice, program, bankMsb, bankLsb);

        if (!voice.KeyOn)
        {
            long koffMs = voice.KeyOffRemainingUs / 1000;
            long score = -koffMs;

            if (score < 0)
            {
                score -= 40000;

                switch (allocMode)
                {
                    case ChannelAllocMode.SameInstrument:
                        if (sameInstrument)
                        {
                            score = 0;
                        }
                        break;
                    case ChannelAllocMode.AnyReleased:
                        score = 0;
                        break;
                    default:
                        if (sameInstrument)
                        {
                            score = -koffMs;
                        }
                        break;
                }
            }
            else
            {
                score = 0;
            }

            return score;
        }

        long konMs = voice.KeyOnRemainingUs / 1000;
        long scoreActive = voice.Sustained
            ? -(500000 + (konMs / 2))
            : -(4000000 + konMs);

        if (sameInstrument)
        {
            scoreActive += 300;
            if (voice.VibDelayUs < 70000 || voice.KeyOnRemainingUs > 20000000)
            {
                scoreActive += 10;
            }
        }

        if (IsPercussionChannel(voice.MidiChannel))
        {
            scoreActive += 50;
        }

        scoreActive += CountEvacuationStations(voice) * 4;
        return scoreActive;
    }

    private int CountEvacuationStations(OplVoice target)
    {
        int count = 0;
        for (int i = 0; i < _voices.Length; i++)
        {
            OplVoice voice = _voices[i];
            if (!voice.Active || !voice.KeyOn)
            {
                continue;
            }

            if (ReferenceEquals(voice, target))
            {
                continue;
            }

            if (voice.Sustained)
            {
                continue;
            }

            if (voice.VibDelayUs >= 200000)
            {
                continue;
            }

            if (!IsSameInstrument(voice, target.Program, target.BankMsb, target.BankLsb))
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private bool IsChannelUnavailableForAllocation(int oplChannel)
    {
        return IsChannelReservedForRhythm(oplChannel) || IsFourOperatorSecondaryChannel(oplChannel);
    }

    private bool TryGetFourOpPairInfo(int oplChannel, out OplCore core, out int localChannel,
        out int secondaryChannel, out int pairBit)
    {
        core = null!;
        localChannel = 0;
        secondaryChannel = -1;
        pairBit = -1;

        if (_mode != OplSynthMode.Opl3)
        {
            return false;
        }

        if (!TryGetCore(oplChannel, out core, out localChannel))
        {
            return false;
        }

        if (!core.Registers.Opl3Enabled)
        {
            return false;
        }

        int local = localChannel;
        if (local >= 0 && local <= 2)
        {
            pairBit = local;
            secondaryChannel = oplChannel + 3;
        }
        else if (local >= 9 && local <= 11)
        {
            pairBit = (local - 9) + 3;
            secondaryChannel = oplChannel + 3;
        }
        else
        {
            return false;
        }

        return secondaryChannel >= 0 && secondaryChannel < _voices.Length;
    }

    private bool TryGetFourOpSecondaryChannel(int oplChannel, out int secondaryChannel)
    {
        if (TryGetFourOpPairInfo(oplChannel, out _, out _, out secondaryChannel, out _))
        {
            return true;
        }

        secondaryChannel = -1;
        return false;
    }

    private void EnableFourOpPair(int oplChannel)
    {
        if (!TryGetFourOpPairInfo(oplChannel, out OplCore core, out _, out _, out int pairBit))
        {
            return;
        }

        byte mask = core.Registers.FourOperatorEnableMask;
        byte updated = (byte)(mask | (1 << pairBit));
        if (updated != mask)
        {
            core.WriteRegister(0x104, updated);
        }
    }

    private void ReleaseFourOpPairIfUnused(int primaryChannel, int secondaryChannel)
    {
        if (!TryGetFourOpPairInfo(primaryChannel, out OplCore core, out _, out int expectedSecondary, out int pairBit))
        {
            return;
        }

        if (secondaryChannel >= 0 && secondaryChannel != expectedSecondary)
        {
            return;
        }

        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active || !voice.FourOp)
            {
                continue;
            }

            if (!TryGetFourOpPairInfo(voice.OplChannel, out OplCore voiceCore, out _, out _, out int voicePairBit))
            {
                continue;
            }

            if (ReferenceEquals(core, voiceCore) && voicePairBit == pairBit)
            {
                return;
            }
        }

        byte mask = core.Registers.FourOperatorEnableMask;
        byte updated = (byte)(mask & ~(1 << pairBit));
        if (updated != mask)
        {
            core.WriteRegister(0x104, updated);
        }
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

    private static double AdvancePhase(double phase, double delta)
    {
        double next = phase + delta;
        if (next >= ModulationTwoPi || next <= -ModulationTwoPi)
        {
            next %= ModulationTwoPi;
        }

        if (next < 0)
        {
            next += ModulationTwoPi;
        }

        return next;
    }

    private int GetModulationValue(MidiChannelState channelState, OplVoice voice)
    {
        int value = channelState.ModWheel;
        if (channelState.Aftertouch > value)
        {
            value = channelState.Aftertouch;
        }

        if (voice.NoteAftertouch > value)
        {
            value = voice.NoteAftertouch;
        }

        return value;
    }

    private double GetVibratoOffset(MidiChannelState channelState, OplVoice voice)
    {
        int modulation = GetModulationValue(channelState, voice);
        if (modulation <= 0 || voice.VibDelayUs < channelState.VibratoDelayUs)
        {
            return 0.0;
        }

        return modulation * channelState.VibratoDepthSemitonesPerUnit * Math.Sin(channelState.ModulationPhase);
    }

    private static double GetPitchBendOffset(MidiChannelState channelState)
    {
        double bend = (channelState.PitchBend - 8192) / 8192.0;
        return bend * channelState.PitchBendRangeSemitones;
    }

    private static double GetNoteOffset(OplInstrument instrument, int operatorOffset)
    {
        return operatorOffset == 2 ? instrument.NoteOffset2 : instrument.NoteOffset1;
    }

    private static double GetSecondVoiceDetune(OplInstrument instrument)
    {
        int detune = instrument.SecondVoiceDetune;
        if (detune == 0)
        {
            return 0.0;
        }

        int fine = ((detune + 128) >> 1) - 64;
        return fine / 32.0;
    }

    private static byte GetScaledBrightness(int brightness)
    {
        int clamped = Math.Clamp(brightness, 0, 127);
        if (clamped >= 64)
        {
            return 127;
        }

        return (byte)(clamped * 2);
    }

    private static (bool left, bool right) GetPanFlags(MidiChannelState channelState)
    {
        int pan = Math.Clamp(channelState.Pan, 0, 127);
        bool left = pan < 80;
        bool right = pan >= 48;
        return (left, right);
    }

    private static bool IsValidChannel(int channel)
    {
        return channel >= 0 && channel < 16;
    }

    private static bool IsXgPercussionBank(MidiChannelState channelState)
    {
        return channelState.BankMsb == 0x7E || channelState.BankMsb == 0x7F;
    }
}
