using OpenMIDI.Midi;
using OpenMIDI.Playback;
using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class MidiPlayerTests
{
    [Fact]
    public void MidiPlayer_SchedulesEventsByTempo()
    {
        byte[] data = CreateMinimalMidi();
        using MemoryStream stream = new MemoryStream(data);
        MidiFile midi = MidiFile.Load(stream);

        TestSynth synth = new TestSynth();
        MidiPlayer player = new MidiPlayer(synth);
        player.Load(midi);

        float[] buffer = new float[400 * 2];
        player.Render(buffer, 0, 200, 1000);

        Assert.Contains(MidiEventKind.NoteOn, synth.Events);
        Assert.DoesNotContain(MidiEventKind.NoteOff, synth.Events);

        player.Render(buffer, 0, 400, 1000);

        Assert.Contains(MidiEventKind.NoteOff, synth.Events);
    }

    private static byte[] CreateMinimalMidi()
    {
        return new byte[]
        {
            0x4D, 0x54, 0x68, 0x64,
            0x00, 0x00, 0x00, 0x06,
            0x00, 0x00,
            0x00, 0x01,
            0x00, 0x60,
            0x4D, 0x54, 0x72, 0x6B,
            0x00, 0x00, 0x00, 0x16,
            0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20,
            0x00, 0xC0, 0x00,
            0x00, 0x90, 0x3C, 0x64,
            0x60, 0x80, 0x3C, 0x40,
            0x00, 0xFF, 0x2F, 0x00
        };
    }

    private sealed class TestSynth : IMidiSynth
    {
        public List<MidiEventKind> Events { get; } = new();

        public void Reset()
        {
            Events.Clear();
        }

        public void NoteOn(int channel, int note, int velocity)
        {
            Events.Add(MidiEventKind.NoteOn);
        }

        public void NoteOff(int channel, int note, int velocity)
        {
            Events.Add(MidiEventKind.NoteOff);
        }

        public void PolyAftertouch(int channel, int note, int pressure)
        {
        }

        public void ChannelAftertouch(int channel, int pressure)
        {
        }

        public void ControlChange(int channel, int controller, int value)
        {
        }

        public void ProgramChange(int channel, int program)
        {
        }

        public void PitchBend(int channel, int value)
        {
        }

        public void SysEx(ReadOnlySpan<byte> data)
        {
        }

        public void Render(float[] interleaved, int offset, int frames, int sampleRate)
        {
            if (frames <= 0)
            {
                return;
            }

            Array.Clear(interleaved, offset, frames * 2);
        }
    }
}
