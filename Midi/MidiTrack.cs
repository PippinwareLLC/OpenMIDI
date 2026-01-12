namespace OpenMIDI.Midi;

public sealed class MidiTrack
{
    internal MidiTrack(int index, List<MidiEvent> events, string? name)
    {
        Index = index;
        Events = events;
        Name = name;
    }

    public int Index { get; }
    public IReadOnlyList<MidiEvent> Events { get; }
    public string? Name { get; }
}
