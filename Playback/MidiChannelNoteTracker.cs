namespace OpenMIDI.Playback;

public sealed class MidiChannelNoteTracker
{
    private const int ChannelCount = 16;
    private const int NoteCount = 128;

    private readonly ushort[,] _noteCounts = new ushort[ChannelCount, NoteCount];
    private readonly int[] _activeCounts = new int[ChannelCount];

    public void Reset()
    {
        Array.Clear(_noteCounts, 0, _noteCounts.Length);
        Array.Clear(_activeCounts, 0, _activeCounts.Length);
    }

    public void NoteOn(int channel, int note, int velocity)
    {
        if (!IsValidChannel(channel) || !IsValidNote(note))
        {
            return;
        }

        if (velocity <= 0)
        {
            NoteOff(channel, note, velocity);
            return;
        }

        ushort count = _noteCounts[channel, note];
        if (count == 0)
        {
            _activeCounts[channel]++;
        }

        if (count < ushort.MaxValue)
        {
            _noteCounts[channel, note] = (ushort)(count + 1);
        }
    }

    public void NoteOff(int channel, int note, int velocity)
    {
        if (!IsValidChannel(channel) || !IsValidNote(note))
        {
            return;
        }

        ushort count = _noteCounts[channel, note];
        if (count == 0)
        {
            return;
        }

        count--;
        _noteCounts[channel, note] = count;
        if (count == 0)
        {
            _activeCounts[channel] = Math.Max(0, _activeCounts[channel] - 1);
        }
    }

    public bool IsActive(int channel, int note)
    {
        if (!IsValidChannel(channel) || !IsValidNote(note))
        {
            return false;
        }

        return _noteCounts[channel, note] > 0;
    }

    public int GetActiveNoteCount(int channel)
    {
        if (!IsValidChannel(channel))
        {
            return 0;
        }

        return _activeCounts[channel];
    }

    private static bool IsValidChannel(int channel)
    {
        return channel >= 0 && channel < ChannelCount;
    }

    private static bool IsValidNote(int note)
    {
        return note >= 0 && note < NoteCount;
    }
}
