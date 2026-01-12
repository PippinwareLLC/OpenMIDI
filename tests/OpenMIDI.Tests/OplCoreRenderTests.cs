using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplCoreRenderTests
{
    [Fact]
    public void Render_ProducesOutputForKeyOnChannel()
    {
        OplCore core = new OplCore(OplChipType.Opl2);

        core.WriteRegister(0x20, 0x01);
        core.WriteRegister(0x23, 0x01);
        core.WriteRegister(0x40, 0x00);
        core.WriteRegister(0x43, 0x00);
        core.WriteRegister(0x60, 0xF0);
        core.WriteRegister(0x63, 0xF0);
        core.WriteRegister(0x80, 0x00);
        core.WriteRegister(0x83, 0x00);
        core.WriteRegister(0xE0, 0x00);
        core.WriteRegister(0xE3, 0x00);

        core.WriteRegister(0xA0, 0xA0);
        core.WriteRegister(0xB0, 0x30);

        float[] buffer = new float[256 * 2];
        core.Render(buffer, 0, 256, 44100);

        bool anyNonZero = false;
        for (int i = 0; i < buffer.Length; i++)
        {
            if (Math.Abs(buffer[i]) > 0.0001f)
            {
                anyNonZero = true;
                break;
            }
        }

        Assert.True(anyNonZero);
    }
}
