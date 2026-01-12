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
    private const double MinAttackSeconds = 0.002;
    private const double MaxAttackSeconds = 4.0;
    private const double MinDecaySeconds = 0.01;
    private const double MaxDecaySeconds = 8.0;
    private const double MinReleaseSeconds = 0.01;
    private const double MaxReleaseSeconds = 8.0;

    private double _attackSeconds = MaxAttackSeconds;
    private double _decaySeconds = MaxDecaySeconds;
    private double _releaseSeconds = MaxReleaseSeconds;
    private float _sustainLevel = 1f;

    public OplEnvelopeStage Stage { get; private set; } = OplEnvelopeStage.Off;
    public float Level { get; private set; }
    public int AttackRate { get; private set; }
    public int DecayRate { get; private set; }
    public int SustainLevel { get; private set; }
    public int ReleaseRate { get; private set; }

    public void Reset()
    {
        Stage = OplEnvelopeStage.Off;
        Level = 0f;
        SetRates(0, 0, 0, 0);
    }

    public void SetRates(int attackRate, int decayRate, int sustainLevel, int releaseRate)
    {
        AttackRate = ClampRate(attackRate);
        DecayRate = ClampRate(decayRate);
        SustainLevel = ClampRate(sustainLevel);
        ReleaseRate = ClampRate(releaseRate);

        _attackSeconds = MapRateSeconds(AttackRate, MinAttackSeconds, MaxAttackSeconds);
        _decaySeconds = MapRateSeconds(DecayRate, MinDecaySeconds, MaxDecaySeconds);
        _releaseSeconds = MapRateSeconds(ReleaseRate, MinReleaseSeconds, MaxReleaseSeconds);
        _sustainLevel = 1f - SustainLevel / 15f;
    }

    public void KeyOn()
    {
        Stage = OplEnvelopeStage.Attack;
        Level = 0f;
    }

    public void KeyOff()
    {
        if (Stage == OplEnvelopeStage.Off)
        {
            return;
        }

        Stage = OplEnvelopeStage.Release;
    }

    public void Step(double seconds)
    {
        if (seconds <= 0)
        {
            return;
        }

        switch (Stage)
        {
            case OplEnvelopeStage.Off:
                Level = 0f;
                break;
            case OplEnvelopeStage.Attack:
                StepAttack(seconds);
                break;
            case OplEnvelopeStage.Decay:
                StepDecay(seconds);
                break;
            case OplEnvelopeStage.Sustain:
                Level = _sustainLevel;
                break;
            case OplEnvelopeStage.Release:
                StepRelease(seconds);
                break;
        }
    }

    private void StepAttack(double seconds)
    {
        if (_attackSeconds <= 0)
        {
            Level = 1f;
            Stage = OplEnvelopeStage.Decay;
            return;
        }

        Level += (float)(seconds / _attackSeconds);
        if (Level >= 1f)
        {
            Level = 1f;
            Stage = OplEnvelopeStage.Decay;
        }
    }

    private void StepDecay(double seconds)
    {
        if (_decaySeconds <= 0)
        {
            Level = _sustainLevel;
            Stage = OplEnvelopeStage.Sustain;
            return;
        }

        Level -= (float)(seconds / _decaySeconds);
        if (Level <= _sustainLevel)
        {
            Level = _sustainLevel;
            Stage = OplEnvelopeStage.Sustain;
        }
    }

    private void StepRelease(double seconds)
    {
        if (_releaseSeconds <= 0)
        {
            Level = 0f;
            Stage = OplEnvelopeStage.Off;
            return;
        }

        Level -= (float)(seconds / _releaseSeconds);
        if (Level <= 0f)
        {
            Level = 0f;
            Stage = OplEnvelopeStage.Off;
        }
    }

    private static int ClampRate(int rate)
    {
        return Math.Clamp(rate, 0, 15);
    }

    private static double MapRateSeconds(int rate, double min, double max)
    {
        if (rate <= 0)
        {
            return max;
        }

        if (rate >= 15)
        {
            return min;
        }

        double t = rate / 15.0;
        return max * Math.Pow(min / max, t);
    }
}
