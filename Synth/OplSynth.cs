namespace OpenMIDI.Synth;

public enum OplSynthMode
{
    Opl2,
    Opl3
}

public sealed class OplSynth : IMidiSynth
{
    private sealed class OplEnvelope
    {
        public float Level;
        public bool Gate;

        public void Reset()
        {
            Level = 0f;
            Gate = false;
        }

        public void Step(float attackPerSecond, float releasePerSecond, int sampleRate)
        {
            if (sampleRate <= 0)
            {
                return;
            }

            float delta = (Gate ? attackPerSecond : -releasePerSecond) / sampleRate;
            Level = Math.Clamp(Level + delta, 0f, 1f);
        }
    }

    private sealed class OplVoice
    {
        public bool Active;
        public int Channel;
        public int Note;
        public double Frequency;
        public double Phase;
        public float GainLeft;
        public float GainRight;
        public int Age;
        public readonly OplEnvelope Envelope = new();
    }

    private sealed class MidiChannelState
    {
        public int Program;
        public int PitchBend = 8192;
        public int Volume = 100;
        public int Expression = 127;
        public int Pan = 64;
    }

    private readonly OplVoice[] _voices;
    private readonly MidiChannelState[] _channels;
    private int _ageCounter;
    private readonly int[] _channelActiveCounts;
    private readonly float[] _channelLevels;
    private int _activeVoiceCount;
    private float _lastPeakLeft;
    private float _lastPeakRight;

    public OplSynth(OplSynthMode mode = OplSynthMode.Opl3)
    {
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
    }

    public float MasterGain { get; set; } = 0.2f;
    public float AttackPerSecond { get; set; } = 6f;
    public float ReleasePerSecond { get; set; } = 3f;
    public float PitchBendRangeSemitones { get; set; } = 2f;
    public int VoiceCount => _voices.Length;
    public int ActiveVoiceCount => _activeVoiceCount;
    public float LastPeakLeft => _lastPeakLeft;
    public float LastPeakRight => _lastPeakRight;
    public OplCore Core { get; }

    public void Reset()
    {
        foreach (OplVoice voice in _voices)
        {
            voice.Active = false;
            voice.Channel = 0;
            voice.Note = 0;
            voice.Frequency = 0.0;
            voice.Phase = 0.0;
            voice.GainLeft = 0f;
            voice.GainRight = 0f;
            voice.Age = 0;
            voice.Envelope.Reset();
        }

        foreach (MidiChannelState channel in _channels)
        {
            channel.Program = 0;
            channel.PitchBend = 8192;
            channel.Volume = 100;
            channel.Expression = 127;
            channel.Pan = 64;
        }

        _ageCounter = 0;
        _activeVoiceCount = 0;
        _lastPeakLeft = 0f;
        _lastPeakRight = 0f;
        Array.Clear(_channelActiveCounts, 0, _channelActiveCounts.Length);
        Array.Clear(_channelLevels, 0, _channelLevels.Length);
        Core.Reset();
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
        float velocityGain = Math.Clamp(velocity / 127f, 0f, 1f);
        float gain = velocityGain * GetChannelGain(channelState);
        float pan = GetChannelPan(channelState);

        OplVoice voice = AllocateVoice(channel, note);
        voice.Active = true;
        voice.Channel = channel;
        voice.Note = note;
        voice.Frequency = GetFrequency(channelState, note);
        voice.GainLeft = gain * (1f - pan);
        voice.GainRight = gain * pan;
        voice.Age = _ageCounter++;
        voice.Envelope.Gate = true;
    }

    public void NoteOff(int channel, int note, int velocity)
    {
        if (!IsValidChannel(channel))
        {
            return;
        }

        foreach (OplVoice voice in _voices)
        {
            if (voice.Active && voice.Channel == channel && voice.Note == note)
            {
                voice.Envelope.Gate = false;
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

        Array.Clear(interleaved, offset, frames * 2);

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
            if (voice.Channel >= 0 && voice.Channel < _channelActiveCounts.Length)
            {
                _channelActiveCounts[voice.Channel]++;
                _channelLevels[voice.Channel] += voice.Envelope.Level * (voice.GainLeft + voice.GainRight) * 0.5f;
            }
        }

        float peakLeft = 0f;
        float peakRight = 0f;

        for (int i = 0; i < frames; i++)
        {
            float left = 0f;
            float right = 0f;

            foreach (OplVoice voice in _voices)
            {
                if (!voice.Active)
                {
                    continue;
                }

                voice.Envelope.Step(AttackPerSecond, ReleasePerSecond, sampleRate);
                float env = voice.Envelope.Level;
                if (env <= 0f && !voice.Envelope.Gate)
                {
                    voice.Active = false;
                    continue;
                }

                double phaseStep = (Math.PI * 2.0 * voice.Frequency) / sampleRate;
                double sample = Math.Sin(voice.Phase) * env * MasterGain;
                voice.Phase += phaseStep;
                if (voice.Phase >= Math.PI * 2.0)
                {
                    voice.Phase -= Math.PI * 2.0;
                }

                left += (float)sample * voice.GainLeft;
                right += (float)sample * voice.GainRight;
            }

            float clampedLeft = Math.Clamp(left, -1f, 1f);
            float clampedRight = Math.Clamp(right, -1f, 1f);

            peakLeft = Math.Max(peakLeft, Math.Abs(clampedLeft));
            peakRight = Math.Max(peakRight, Math.Abs(clampedRight));

            interleaved[offset + i * 2] = clampedLeft;
            interleaved[offset + i * 2 + 1] = clampedRight;
        }

        _lastPeakLeft = peakLeft;
        _lastPeakRight = peakRight;
        Core.StepOutputSamples(frames, sampleRate);
    }

    public void CopyChannelMeters(Span<int> counts, Span<float> levels)
    {
        int countLength = Math.Min(counts.Length, _channelActiveCounts.Length);
        _channelActiveCounts.AsSpan(0, countLength).CopyTo(counts);

        int levelLength = Math.Min(levels.Length, _channelLevels.Length);
        _channelLevels.AsSpan(0, levelLength).CopyTo(levels);
    }

    private OplVoice AllocateVoice(int channel, int note)
    {
        foreach (OplVoice voice in _voices)
        {
            if (!voice.Active)
            {
                return voice;
            }
        }

        OplVoice oldest = _voices[0];
        foreach (OplVoice voice in _voices)
        {
            if (voice.Age < oldest.Age)
            {
                oldest = voice;
            }
        }

        return oldest;
    }

    private void AllNotesOff(int channel)
    {
        foreach (OplVoice voice in _voices)
        {
            if (voice.Active && voice.Channel == channel)
            {
                voice.Envelope.Gate = false;
            }
        }
    }

    private void UpdateChannelGains(int channel, MidiChannelState channelState)
    {
        float gain = GetChannelGain(channelState);
        float pan = GetChannelPan(channelState);
        foreach (OplVoice voice in _voices)
        {
            if (voice.Active && voice.Channel == channel)
            {
                voice.GainLeft = gain * (1f - pan);
                voice.GainRight = gain * pan;
            }
        }
    }

    private void UpdateChannelFrequencies(int channel, MidiChannelState channelState)
    {
        foreach (OplVoice voice in _voices)
        {
            if (voice.Active && voice.Channel == channel)
            {
                voice.Frequency = GetFrequency(channelState, voice.Note);
            }
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

    private static float GetChannelPan(MidiChannelState channelState)
    {
        return Math.Clamp(channelState.Pan / 127f, 0f, 1f);
    }

    private static bool IsValidChannel(int channel)
    {
        return channel >= 0 && channel < 16;
    }
}
