using OpenMIDI.Midi;

namespace OpenMIDI.Playback;

internal readonly struct ScheduledMidiEvent
{
    public ScheduledMidiEvent(MidiEvent midiEvent, double timeSeconds)
    {
        MidiEvent = midiEvent;
        TimeSeconds = timeSeconds;
    }

    public MidiEvent MidiEvent { get; }
    public double TimeSeconds { get; }
}
