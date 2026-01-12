using OpenMIDI.Midi;

namespace OpenMIDI.Tests;

public sealed class MidiFileTests
{
    [Fact]
    public void MidiFile_LoadsHeaderAndEvents()
    {
        byte[] data = CreateMinimalMidi();
        using MemoryStream stream = new MemoryStream(data);
        MidiFile midi = MidiFile.Load(stream);

        Assert.Equal(0, midi.Format);
        Assert.Single(midi.Tracks);
        Assert.Equal(96, midi.TicksPerQuarterNote);
        Assert.Equal(5, midi.Events.Count);

        MidiEvent tempo = midi.Events[0];
        Assert.True(tempo.IsTempo);
        Assert.Equal(500000, tempo.TempoMicrosecondsPerQuarter);

        MidiEvent noteOn = midi.Events.First(e => e.Kind == MidiEventKind.NoteOn);
        Assert.Equal(0, noteOn.AbsoluteTicks);
        Assert.Equal(60, noteOn.Data1);
        Assert.Equal(100, noteOn.Data2);

        MidiEvent noteOff = midi.Events.First(e => e.Kind == MidiEventKind.NoteOff);
        Assert.Equal(96, noteOff.AbsoluteTicks);
    }

    private static byte[] CreateMinimalMidi()
    {
        return new byte[]
        {
            0x4D, 0x54, 0x68, 0x64, // MThd
            0x00, 0x00, 0x00, 0x06, // header length
            0x00, 0x00, // format 0
            0x00, 0x01, // track count
            0x00, 0x60, // division 96
            0x4D, 0x54, 0x72, 0x6B, // MTrk
            0x00, 0x00, 0x00, 0x16, // track length 22 bytes
            0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20, // tempo
            0x00, 0xC0, 0x00, // program change
            0x00, 0x90, 0x3C, 0x64, // note on
            0x60, 0x80, 0x3C, 0x40, // note off
            0x00, 0xFF, 0x2F, 0x00 // end of track
        };
    }
}
