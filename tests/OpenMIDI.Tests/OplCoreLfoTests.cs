using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplCoreLfoTests
{
    [Fact]
    public void Render_WithTremoloProducesDifferentOutput()
    {
        float[] noTremolo = RenderBuffer(tremoloEnabled: false);
        float[] tremolo = RenderBuffer(tremoloEnabled: true);

        float delta = 0f;
        for (int i = 0; i < noTremolo.Length; i++)
        {
            delta += Math.Abs(noTremolo[i] - tremolo[i]);
        }

        Assert.True(delta > 0.1f);
    }

    private static float[] RenderBuffer(bool tremoloEnabled)
    {
        OplCore core = new OplCore(OplChipType.Opl2);

        byte tremoloFlag = tremoloEnabled ? (byte)0x80 : (byte)0x00;
        core.WriteRegister(0x20, (byte)(0x01 | tremoloFlag));
        core.WriteRegister(0x23, (byte)(0x01 | tremoloFlag));
        core.WriteRegister(0x40, 0x00);
        core.WriteRegister(0x43, 0x00);
        core.WriteRegister(0x60, 0xF0);
        core.WriteRegister(0x63, 0xF0);
        core.WriteRegister(0x80, 0x00);
        core.WriteRegister(0x83, 0x00);
        core.WriteRegister(0xE0, 0x00);
        core.WriteRegister(0xE3, 0x00);

        core.WriteRegister(0xBD, 0x80);
        core.WriteRegister(0xA0, 0x90);
        core.WriteRegister(0xB0, 0x32);

        float[] buffer = new float[1024 * 2];
        core.Render(buffer, 0, 1024, 44100);
        return buffer;
    }
}
