using OpenMIDI.Midi;
using OpenMIDI.Synth;

namespace OpenMIDI.Playback;

public sealed class MidiPlayer
{
    private readonly IMidiSynth _synth;
    private readonly List<ScheduledMidiEvent> _schedule = new();
    private int _eventIndex;
    private double _currentTimeSeconds;
    private bool _loaded;

    public MidiPlayer(IMidiSynth synth)
    {
        _synth = synth ?? throw new ArgumentNullException(nameof(synth));
    }

    public double CurrentTimeSeconds => _currentTimeSeconds;
    public bool IsFinished => _loaded && _eventIndex >= _schedule.Count;
    public double DurationSeconds { get; private set; }

    public void Load(MidiFile file)
    {
        if (file == null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        if (file.TicksPerQuarterNote <= 0)
        {
            throw new InvalidOperationException("MIDI file uses an unsupported timing mode.");
        }

        _schedule.Clear();
        _eventIndex = 0;
        _currentTimeSeconds = 0;
        DurationSeconds = 0;
        _loaded = true;

        int tempoMicrosecondsPerQuarter = 500000;
        int lastTick = 0;
        double currentSeconds = 0;

        foreach (MidiEvent midiEvent in file.Events)
        {
            int deltaTicks = midiEvent.AbsoluteTicks - lastTick;
            if (deltaTicks < 0)
            {
                deltaTicks = 0;
            }

            currentSeconds += deltaTicks * (tempoMicrosecondsPerQuarter / 1_000_000.0) / file.TicksPerQuarterNote;
            lastTick = midiEvent.AbsoluteTicks;

            if (midiEvent.IsTempo)
            {
                if (midiEvent.TempoMicrosecondsPerQuarter > 0)
                {
                    tempoMicrosecondsPerQuarter = midiEvent.TempoMicrosecondsPerQuarter;
                }

                continue;
            }

            if (midiEvent.Kind == MidiEventKind.Meta || midiEvent.Kind == MidiEventKind.SysEx)
            {
                continue;
            }

            _schedule.Add(new ScheduledMidiEvent(midiEvent, currentSeconds));
            DurationSeconds = Math.Max(DurationSeconds, currentSeconds);
        }

        _synth.Reset();
    }

    public void Reset()
    {
        _eventIndex = 0;
        _currentTimeSeconds = 0;
        _synth.Reset();
    }

    public void Render(float[] interleaved, int offset, int frames, int sampleRate)
    {
        if (!_loaded || frames <= 0 || sampleRate <= 0)
        {
            return;
        }

        if (interleaved.Length < offset + frames * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(interleaved));
        }

        int framesRemaining = frames;
        int writeIndex = offset;

        while (framesRemaining > 0)
        {
            if (_eventIndex >= _schedule.Count)
            {
                _synth.Render(interleaved, writeIndex, framesRemaining, sampleRate);
                _currentTimeSeconds += framesRemaining / (double)sampleRate;
                return;
            }

            ScheduledMidiEvent nextEvent = _schedule[_eventIndex];
            double nextTime = nextEvent.TimeSeconds;
            double bufferEnd = _currentTimeSeconds + framesRemaining / (double)sampleRate;

            if (nextTime > bufferEnd)
            {
                _synth.Render(interleaved, writeIndex, framesRemaining, sampleRate);
                _currentTimeSeconds = bufferEnd;
                return;
            }

            int segmentFrames = (int)Math.Round((nextTime - _currentTimeSeconds) * sampleRate);
            segmentFrames = Math.Clamp(segmentFrames, 0, framesRemaining);

            if (segmentFrames > 0)
            {
                _synth.Render(interleaved, writeIndex, segmentFrames, sampleRate);
                _currentTimeSeconds += segmentFrames / (double)sampleRate;
                writeIndex += segmentFrames * 2;
                framesRemaining -= segmentFrames;
            }
            else
            {
                DispatchEvent(nextEvent.MidiEvent);
                _eventIndex++;
                while (_eventIndex < _schedule.Count && _schedule[_eventIndex].TimeSeconds <= _currentTimeSeconds + 1e-9)
                {
                    DispatchEvent(_schedule[_eventIndex].MidiEvent);
                    _eventIndex++;
                }
            }
        }
    }

    private void DispatchEvent(MidiEvent midiEvent)
    {
        switch (midiEvent.Kind)
        {
            case MidiEventKind.NoteOn:
                _synth.NoteOn(midiEvent.Channel, midiEvent.Data1, midiEvent.Data2);
                break;
            case MidiEventKind.NoteOff:
                _synth.NoteOff(midiEvent.Channel, midiEvent.Data1, midiEvent.Data2);
                break;
            case MidiEventKind.PolyAftertouch:
                _synth.PolyAftertouch(midiEvent.Channel, midiEvent.Data1, midiEvent.Data2);
                break;
            case MidiEventKind.ControlChange:
                _synth.ControlChange(midiEvent.Channel, midiEvent.Data1, midiEvent.Data2);
                break;
            case MidiEventKind.ProgramChange:
                _synth.ProgramChange(midiEvent.Channel, midiEvent.Data1);
                break;
            case MidiEventKind.ChannelAftertouch:
                _synth.ChannelAftertouch(midiEvent.Channel, midiEvent.Data1);
                break;
            case MidiEventKind.PitchBend:
            {
                int value = midiEvent.Data1 | (midiEvent.Data2 << 7);
                _synth.PitchBend(midiEvent.Channel, value);
                break;
            }
        }
    }
}
