using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthGainTests
{
    [Fact]
    public void MasterGain_ScalesRenderedPeaks()
    {
        float high = RenderPeak(1f);
        float low = RenderPeak(0.25f);

        Assert.True(high > 0f);
        Assert.True(low > 0f);
        Assert.True(high > low);

        float ratio = low / high;
        Assert.InRange(ratio, 0.15f, 0.35f);
    }

    private static float RenderPeak(float gain)
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2)
        {
            MasterGain = gain
        };

        synth.NoteOn(0, 60, 100);

        const int frames = 4096;
        float[] buffer = new float[frames * 2];
        synth.Render(buffer, 0, frames, 44100);

        float peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(buffer[i]));
        }

        return peak;
    }
}
