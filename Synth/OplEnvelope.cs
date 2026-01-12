namespace OpenMIDI.Synth;

public enum OplEnvelopeStage
{
    Off,
    Attack,
    Decay,
    Sustain,
    Release
}

public sealed class OplEnvelope
{
    private const int MaxAttenuation = 0x3FF;

    private int _sustainAttenuation;
    private int _effectiveAttackRate;
    private int _effectiveDecayRate;
    private int _effectiveSustainRate;
    private int _effectiveReleaseRate;
    private bool _sustainEnabled;
    private bool _keyScaleRate;
    private int _keyCode;

    public OplEnvelopeStage Stage { get; private set; } = OplEnvelopeStage.Off;
    public float Level { get; private set; }
    public int Attenuation { get; private set; } = MaxAttenuation;
    public int AttackRate { get; private set; }
    public int DecayRate { get; private set; }
    public int SustainLevel { get; private set; }
    public int ReleaseRate { get; private set; }

    public void Reset()
    {
        Stage = OplEnvelopeStage.Off;
        Attenuation = MaxAttenuation;
        Level = 0f;
        Configure(0, 0, 0, 0, sustainEnabled: false, keyScaleRate: false, keyCode: 0);
    }

    public void Configure(int attackRate, int decayRate, int sustainLevel, int releaseRate, bool sustainEnabled, bool keyScaleRate, int keyCode)
    {
        AttackRate = ClampRate(attackRate);
        DecayRate = ClampRate(decayRate);
        SustainLevel = ClampRate(sustainLevel);
        ReleaseRate = ClampRate(releaseRate);
        _sustainEnabled = sustainEnabled;
        _keyScaleRate = keyScaleRate;
        _keyCode = Math.Clamp(keyCode, 0, 15);
        UpdateEffectiveRates();
    }

    public void KeyOn()
    {
        bool wasSilent = Stage is OplEnvelopeStage.Off or OplEnvelopeStage.Release;
        Stage = OplEnvelopeStage.Attack;
        if (wasSilent)
        {
            Attenuation = MaxAttenuation;
        }

        if (_effectiveAttackRate >= 62)
        {
            Attenuation = 0;
        }

        UpdateLevel();
    }

    public void KeyOff()
    {
        if (Stage == OplEnvelopeStage.Off)
        {
            return;
        }

        Stage = OplEnvelopeStage.Release;
    }

    public void Step(uint envCounter)
    {
        if (Stage == OplEnvelopeStage.Off)
        {
            Attenuation = MaxAttenuation;
            Level = 0f;
            return;
        }

        if (Stage == OplEnvelopeStage.Attack && Attenuation <= 0)
        {
            Stage = OplEnvelopeStage.Decay;
        }

        if (Stage == OplEnvelopeStage.Decay && Attenuation >= _sustainAttenuation)
        {
            Stage = OplEnvelopeStage.Sustain;
        }

        int rate = Stage switch
        {
            OplEnvelopeStage.Attack => _effectiveAttackRate,
            OplEnvelopeStage.Decay => _effectiveDecayRate,
            OplEnvelopeStage.Sustain => _effectiveSustainRate,
            OplEnvelopeStage.Release => _effectiveReleaseRate,
            _ => 0
        };

        int rateShift = rate >> 2;
        uint shiftedCounter = envCounter << rateShift;
        if ((shiftedCounter & 0x7FF) != 0)
        {
            UpdateLevel();
            return;
        }

        int relevantBits = (int)((shiftedCounter >> (rateShift <= 11 ? 11 : rateShift)) & 0x07);
        int increment = (int)OplEnvelopeTables.GetAttenuationIncrement(rate, relevantBits);

        if (Stage == OplEnvelopeStage.Attack)
        {
            if (rate < 62)
            {
                Attenuation += (~Attenuation * increment) >> 4;
                if (Attenuation < 0)
                {
                    Attenuation = 0;
                }
            }
        }
        else
        {
            Attenuation += increment;
            if (Attenuation >= 0x400)
            {
                Attenuation = MaxAttenuation;
            }
        }

        if (Stage == OplEnvelopeStage.Release && Attenuation >= MaxAttenuation)
        {
            Stage = OplEnvelopeStage.Off;
        }

        UpdateLevel();
    }

    private static int ClampRate(int rate)
    {
        return Math.Clamp(rate, 0, 15);
    }

    private void UpdateEffectiveRates()
    {
        int shift = _keyScaleRate ? 0 : 2;
        int ksrValue = _keyCode >> shift;
        _effectiveAttackRate = EffectiveRate(AttackRate * 4, ksrValue);
        _effectiveDecayRate = EffectiveRate(DecayRate * 4, ksrValue);
        _effectiveSustainRate = _sustainEnabled ? 0 : EffectiveRate(ReleaseRate * 4, ksrValue);
        _effectiveReleaseRate = EffectiveRate(ReleaseRate * 4, ksrValue);

        int sustain = SustainLevel;
        sustain |= (sustain + 1) & 0x10;
        _sustainAttenuation = sustain << 5;
    }

    private static int EffectiveRate(int rawRate, int ksrValue)
    {
        if (rawRate == 0)
        {
            return 0;
        }

        return Math.Min(rawRate + ksrValue, 63);
    }

    private void UpdateLevel()
    {
        int clamped = Math.Clamp(Attenuation, 0, MaxAttenuation);
        float level = 1f - clamped / (float)MaxAttenuation;
        Level = Math.Clamp(level, 0f, 1f);
    }
}
