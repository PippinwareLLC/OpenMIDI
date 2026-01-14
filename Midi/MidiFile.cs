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
            SortTrackEvents(events);
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

            int trackCompare = left.TrackIndex.CompareTo(right.TrackIndex);
            if (trackCompare != 0)
            {
                return trackCompare;
            }

            return left.SequenceIndex.CompareTo(right.SequenceIndex);
        });

        return new MidiFile(format, division, tracks, allEvents);
    }

    private static void SortTrackEvents(List<MidiEvent> events)
    {
        if (events.Count == 0)
        {
            return;
        }

        bool[] noteStates = new bool[16 * 128];
        int sequenceIndex = 0;
        int index = 0;

        while (index < events.Count)
        {
            int tick = events[index].AbsoluteTicks;
            int start = index;
            while (index < events.Count && events[index].AbsoluteTicks == tick)
            {
                index++;
            }

            int count = index - start;
            if (count <= 1)
            {
                MidiEvent single = events[start];
                single.SequenceIndex = sequenceIndex++;
                UpdateNoteState(noteStates, single);
                continue;
            }

            List<MidiEvent> row = events.GetRange(start, count);
            Dictionary<MidiEvent, int> originalOrder = new Dictionary<MidiEvent, int>(count);
            for (int i = 0; i < row.Count; i++)
            {
                originalOrder[row[i]] = i;
            }

            row.Sort((left, right) =>
            {
                int priorityCompare = GetPriority(left).CompareTo(GetPriority(right));
                if (priorityCompare != 0)
                {
                    return priorityCompare;
                }

                return originalOrder[left].CompareTo(originalOrder[right]);
            });
            AdjustNoteOffOrdering(row, noteStates);

            for (int i = 0; i < count; i++)
            {
                MidiEvent midiEvent = row[i];
                midiEvent.SequenceIndex = sequenceIndex++;
                events[start + i] = midiEvent;
            }
        }
    }

    private static int GetPriority(MidiEvent midiEvent)
    {
        return midiEvent.Kind switch
        {
            MidiEventKind.SysEx => 0,
            MidiEventKind.NoteOff => 1,
            MidiEventKind.PolyAftertouch => 3,
            MidiEventKind.ControlChange => 3,
            MidiEventKind.ProgramChange => 3,
            MidiEventKind.ChannelAftertouch => 3,
            MidiEventKind.PitchBend => 3,
            MidiEventKind.NoteOn => 4,
            MidiEventKind.Meta when midiEvent.IsEndOfTrack => 20,
            _ => 10
        };
    }

    private static void AdjustNoteOffOrdering(List<MidiEvent> row, bool[] noteStates)
    {
        int maxSize = row.Count;
        if (row.Count > 1)
        {
            for (int i = row.Count - 1; i >= 0; i--)
            {
                MidiEvent midiEvent = row[i];
                if (midiEvent.Kind == MidiEventKind.NoteOff)
                {
                    break;
                }

                if (midiEvent.Kind != MidiEventKind.NoteOn)
                {
                    if (i == 0)
                    {
                        break;
                    }

                    continue;
                }

                int noteIndex = ((midiEvent.Channel & 0x0F) << 7) | (midiEvent.Data1 & 0x7F);
                bool wasOn = noteStates[noteIndex];
                int noteOffsOnSameNote = 0;

                for (int j = 0; j < maxSize;)
                {
                    MidiEvent other = row[j];
                    if (other.Kind == MidiEventKind.NoteOn)
                    {
                        break;
                    }

                    if (other.Kind != MidiEventKind.NoteOff)
                    {
                        j++;
                        continue;
                    }

                    int otherIndex = ((other.Channel & 0x0F) << 7) | (other.Data1 & 0x7F);
                    if (otherIndex == noteIndex)
                    {
                        if (!wasOn || noteOffsOnSameNote != 0)
                        {
                            if (j < row.Count - 1)
                            {
                                row.RemoveAt(j);
                                row.Add(other);
                                maxSize--;
                                if (j < i)
                                {
                                    i--;
                                }

                                continue;
                            }
                        }
                        else
                        {
                            noteOffsOnSameNote++;
                        }
                    }

                    j++;
                }

                if (i == 0)
                {
                    break;
                }
            }
        }

        foreach (MidiEvent midiEvent in row)
        {
            UpdateNoteState(noteStates, midiEvent);
        }
    }

    private static void UpdateNoteState(bool[] noteStates, MidiEvent midiEvent)
    {
        if (midiEvent.Kind != MidiEventKind.NoteOn && midiEvent.Kind != MidiEventKind.NoteOff)
        {
            return;
        }

        int index = ((midiEvent.Channel & 0x0F) << 7) | (midiEvent.Data1 & 0x7F);
        if (index < 0 || index >= noteStates.Length)
        {
            return;
        }

        noteStates[index] = midiEvent.Kind == MidiEventKind.NoteOn;
    }
}
