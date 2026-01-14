namespace OpenMIDI.Synth;

public interface IMidiSynth
{
    void Reset();
    void SysEx(ReadOnlySpan<byte> data);
    void NoteOn(int channel, int note, int velocity);
    void NoteOff(int channel, int note, int velocity);
    void PolyAftertouch(int channel, int note, int pressure);
    void ChannelAftertouch(int channel, int pressure);
    void ControlChange(int channel, int controller, int value);
    void ProgramChange(int channel, int program);
    void PitchBend(int channel, int value);
    void Render(float[] interleaved, int offset, int frames, int sampleRate);
}
