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
    public int KeyCode { get; private set; }
    public double Phase { get; set; }
    public float LastOutput { get; set; }
    public float PreviousOutput { get; set; }

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
        KeyCode = 0;
        Phase = 0;
        LastOutput = 0f;
        PreviousOutput = 0f;
        Envelope.Reset();
        UpdateEnvelope();
    }

    public void ApplyRegister(int registerGroup, byte value)
    {
        switch (registerGroup)
        {
            case 0x20:
                AmVibEgtKsrMult = value;
                UpdateEnvelope();
                break;
            case 0x40:
                KslTl = value;
                break;
            case 0x60:
                ArDr = value;
                UpdateEnvelope();
                break;
            case 0x80:
                SlRr = value;
                UpdateEnvelope();
                break;
            case 0xE0:
                Waveform = (byte)(value & 0x07);
                break;
        }
    }

    public void UpdateKeyCode(int keyCode)
    {
        int clamped = Math.Clamp(keyCode, 0, 15);
        if (KeyCode == clamped)
        {
            return;
        }

        KeyCode = clamped;
        UpdateEnvelope();
    }

    private void UpdateEnvelope()
    {
        Envelope.Configure(AttackRate, DecayRate, SustainLevel, ReleaseRate, Sustain, KeyScaleRate, KeyCode);
    }
}
