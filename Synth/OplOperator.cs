namespace OpenMIDI.Synth;

public sealed class OplOperator
{
    public OplOperator(int index)
    {
        Index = index;
        Envelope = new OplEnvelope();
        Reset();
    }

    public int Index { get; }
    public byte AmVibEgtKsrMult { get; private set; }
    public byte KslTl { get; private set; }
    public byte ArDr { get; private set; }
    public byte SlRr { get; private set; }
    public byte Waveform { get; private set; }

    public bool Tremolo => (AmVibEgtKsrMult & 0x80) != 0;
    public bool Vibrato => (AmVibEgtKsrMult & 0x40) != 0;
    public bool Sustain => (AmVibEgtKsrMult & 0x20) != 0;
    public bool KeyScaleRate => (AmVibEgtKsrMult & 0x10) != 0;
    public int Multiple => AmVibEgtKsrMult & 0x0F;

    public int KeyScaleLevel => (KslTl >> 6) & 0x03;
    public int TotalLevel => KslTl & 0x3F;

    public int AttackRate => (ArDr >> 4) & 0x0F;
    public int DecayRate => ArDr & 0x0F;
    public int SustainLevel => (SlRr >> 4) & 0x0F;
    public int ReleaseRate => SlRr & 0x0F;

    public int WaveformIndex => Waveform & 0x07;

    public OplEnvelope Envelope { get; }

    public void Reset()
    {
        AmVibEgtKsrMult = 0;
        KslTl = 0;
        ArDr = 0;
        SlRr = 0;
        Waveform = 0;
        Envelope.Reset();
        Envelope.SetRates(AttackRate, DecayRate, SustainLevel, ReleaseRate);
    }

    public void ApplyRegister(int registerGroup, byte value)
    {
        switch (registerGroup)
        {
            case 0x20:
                AmVibEgtKsrMult = value;
                break;
            case 0x40:
                KslTl = value;
                break;
            case 0x60:
                ArDr = value;
                Envelope.SetRates(AttackRate, DecayRate, SustainLevel, ReleaseRate);
                break;
            case 0x80:
                SlRr = value;
                Envelope.SetRates(AttackRate, DecayRate, SustainLevel, ReleaseRate);
                break;
            case 0xE0:
                Waveform = (byte)(value & 0x07);
                break;
        }
    }
}
