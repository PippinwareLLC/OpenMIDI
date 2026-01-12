namespace OpenMIDI.Midi;

public sealed class MidiFile
{
    private MidiFile(ushort format, ushort division, List<MidiTrack> tracks, List<MidiEvent> events)
    {
        Format = format;
        Division = division;
        Tracks = tracks;
        Events = events;
        TicksPerQuarterNote = (division & 0x8000) == 0 ? division : 0;
    }

    public ushort Format { get; }
    public ushort Division { get; }
    public int TicksPerQuarterNote { get; }
    public IReadOnlyList<MidiTrack> Tracks { get; }
    public IReadOnlyList<MidiEvent> Events { get; }

    public static MidiFile Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Load(stream);
    }

    public static MidiFile Load(Stream stream)
    {
        MidiReader reader = new MidiReader(stream);
        string headerId = reader.ReadChunkId();
        if (!string.Equals(headerId, "MThd", StringComparison.Ordinal))
        {
            throw new FormatException("Missing MIDI header chunk.");
        }

        uint headerLength = reader.ReadUInt32BE();
        if (headerLength < 6)
        {
            throw new FormatException("Invalid MIDI header length.");
        }

        ushort format = reader.ReadUInt16BE();
        ushort trackCount = reader.ReadUInt16BE();
        ushort division = reader.ReadUInt16BE();
        if (headerLength > 6)
        {
            reader.Skip(headerLength - 6);
        }

        if ((division & 0x8000) != 0)
        {
            throw new NotSupportedException("SMPTE time division is not supported yet.");
        }

        List<MidiTrack> tracks = new List<MidiTrack>(trackCount);
        List<MidiEvent> allEvents = new List<MidiEvent>();

        for (int trackIndex = 0; trackIndex < trackCount; trackIndex++)
        {
            string trackId = reader.ReadChunkId();
            if (!string.Equals(trackId, "MTrk", StringComparison.Ordinal))
            {
                throw new FormatException($"Expected MTrk chunk, found {trackId}.");
            }

            uint trackLength = reader.ReadUInt32BE();
            long trackEnd = reader.Position + trackLength;

            List<MidiEvent> events = new List<MidiEvent>();
            int absoluteTicks = 0;
            byte runningStatus = 0;
            string? trackName = null;

            while (reader.Position < trackEnd)
            {
                int delta = reader.ReadVariableLength();
                absoluteTicks += delta;

                byte status = reader.ReadByte();
                bool running = status < 0x80;
                byte data1 = 0;

                if (running)
                {
                    if (runningStatus == 0)
                    {
                        throw new FormatException("Running status without a previous status byte.");
                    }

                    data1 = status;
                    status = runningStatus;
                }
                else if (status < 0xF0)
                {
                    runningStatus = status;
                }
                else
                {
                    runningStatus = 0;
                }

                if (status >= 0x80 && status <= 0xEF)
                {
                    int channel = status & 0x0F;
                    int type = status & 0xF0;
                    if (!running)
                    {
                        data1 = reader.ReadByte();
                    }

                    MidiEvent? midiEvent = null;
                    switch (type)
                    {
                        case 0x80:
                            midiEvent = new MidiEvent(MidiEventKind.NoteOff, delta, absoluteTicks, trackIndex, channel, data1, reader.ReadByte(), 0, null, 0);
                            break;
                        case 0x90:
                        {
                            byte velocity = reader.ReadByte();
                            MidiEventKind kind = velocity == 0 ? MidiEventKind.NoteOff : MidiEventKind.NoteOn;
                            midiEvent = new MidiEvent(kind, delta, absoluteTicks, trackIndex, channel, data1, velocity, 0, null, 0);
                            break;
                        }
                        case 0xA0:
                            midiEvent = new MidiEvent(MidiEventKind.PolyAftertouch, delta, absoluteTicks, trackIndex, channel, data1, reader.ReadByte(), 0, null, 0);
                            break;
                        case 0xB0:
                            midiEvent = new MidiEvent(MidiEventKind.ControlChange, delta, absoluteTicks, trackIndex, channel, data1, reader.ReadByte(), 0, null, 0);
                            break;
                        case 0xC0:
                            midiEvent = new MidiEvent(MidiEventKind.ProgramChange, delta, absoluteTicks, trackIndex, channel, data1, 0, 0, null, 0);
                            break;
                        case 0xD0:
                            midiEvent = new MidiEvent(MidiEventKind.ChannelAftertouch, delta, absoluteTicks, trackIndex, channel, data1, 0, 0, null, 0);
                            break;
                        case 0xE0:
                            midiEvent = new MidiEvent(MidiEventKind.PitchBend, delta, absoluteTicks, trackIndex, channel, data1, reader.ReadByte(), 0, null, 0);
                            break;
                    }

                    if (midiEvent != null)
                    {
                        events.Add(midiEvent);
                        continue;
                    }
                }

                if (status == 0xFF)
                {
                    byte metaType = reader.ReadByte();
                    int length = reader.ReadVariableLength();
                    byte[] data = reader.ReadBytes(length);
                    int tempo = 0;
                    if (metaType == 0x51 && length == 3)
                    {
                        tempo = (data[0] << 16) | (data[1] << 8) | data[2];
                    }
                    if (metaType == 0x03 && length > 0)
                    {
                        trackName = System.Text.Encoding.ASCII.GetString(data);
                    }

                    MidiEvent metaEvent = new MidiEvent(MidiEventKind.Meta, delta, absoluteTicks, trackIndex, 0, 0, 0, metaType, data, tempo);
                    events.Add(metaEvent);
                    continue;
                }

                if (status == 0xF0 || status == 0xF7)
                {
                    int length = reader.ReadVariableLength();
                    byte[] data = reader.ReadBytes(length);
                    MidiEvent sysEx = new MidiEvent(MidiEventKind.SysEx, delta, absoluteTicks, trackIndex, 0, 0, 0, 0, data, 0);
                    events.Add(sysEx);
                    continue;
                }

                throw new FormatException($"Unhandled MIDI status byte 0x{status:X2}.");
            }

            MidiTrack track = new MidiTrack(trackIndex, events, trackName);
            tracks.Add(track);
            allEvents.AddRange(events);
        }

        allEvents.Sort((left, right) =>
        {
            int tickCompare = left.AbsoluteTicks.CompareTo(right.AbsoluteTicks);
            if (tickCompare != 0)
            {
                return tickCompare;
            }

            return left.TrackIndex.CompareTo(right.TrackIndex);
        });

        return new MidiFile(format, division, tracks, allEvents);
    }
}
