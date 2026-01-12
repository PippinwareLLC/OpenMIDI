using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplCoreKslTests
{
    [Fact]
    public void Render_WithKeyScaleLevelReducesOutput()
    {
        float peakBase = RenderWithCarrierKsl(0);
        float peakScaled = RenderWithCarrierKsl(3);

        Assert.True(peakScaled < peakBase);
    }

    private static float RenderWithCarrierKsl(int ksl)
    {
        OplCore core = new OplCore(OplChipType.Opl2);

        byte kslBits = (byte)((ksl & 0x03) << 6);
        core.WriteRegister(0x20, 0x01);
        core.WriteRegister(0x23, 0x01);
        core.WriteRegister(0x40, 0x00);
        core.WriteRegister(0x43, kslBits);
        core.WriteRegister(0x60, 0xF0);
        core.WriteRegister(0x63, 0xF0);
        core.WriteRegister(0x80, 0x00);
        core.WriteRegister(0x83, 0x00);
        core.WriteRegister(0xE0, 0x00);
        core.WriteRegister(0xE3, 0x00);

        core.WriteRegister(0xA0, 0xFF);
        core.WriteRegister(0xB0, 0x3F);

        float[] buffer = new float[512 * 2];
        core.Render(buffer, 0, 512, 44100);

        float peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(buffer[i]));
        }

        return peak;
    }
}
