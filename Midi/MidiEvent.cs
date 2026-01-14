namespace OpenMIDI.Midi;

public sealed class MidiEvent
{
    internal MidiEvent(
        MidiEventKind kind,
        int deltaTicks,
        int absoluteTicks,
        int trackIndex,
        int channel,
        int data1,
        int data2,
        byte metaType,
        byte[]? metaData,
        int tempoMicrosecondsPerQuarter)
    {
        Kind = kind;
        DeltaTicks = deltaTicks;
        AbsoluteTicks = absoluteTicks;
        TrackIndex = trackIndex;
        Channel = channel;
        Data1 = data1;
        Data2 = data2;
        MetaType = metaType;
        MetaData = metaData;
        TempoMicrosecondsPerQuarter = tempoMicrosecondsPerQuarter;
    }

    public MidiEventKind Kind { get; }
    public int DeltaTicks { get; }
    public int AbsoluteTicks { get; }
    public int TrackIndex { get; }
    public int Channel { get; }
    public int Data1 { get; }
    public int Data2 { get; }
    public byte MetaType { get; }
    public byte[]? MetaData { get; }
    public int TempoMicrosecondsPerQuarter { get; }
    internal int SequenceIndex { get; set; }

    public bool IsTempo => Kind == MidiEventKind.Meta && MetaType == 0x51;
    public bool IsEndOfTrack => Kind == MidiEventKind.Meta && MetaType == 0x2F;
}
