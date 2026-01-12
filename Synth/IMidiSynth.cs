namespace OpenMIDI.Synth;

public interface IMidiSynth
{
    void Reset();
    void NoteOn(int channel, int note, int velocity);
    void NoteOff(int channel, int note, int velocity);
    void ControlChange(int channel, int controller, int value);
    void ProgramChange(int channel, int program);
    void PitchBend(int channel, int value);
    void Render(float[] interleaved, int offset, int frames, int sampleRate);
}
