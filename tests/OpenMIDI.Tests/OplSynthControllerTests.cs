using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplSynthControllerTests
{
    [Fact]
    public void RpnPitchBendRange_UpdatesFrequency()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);
        synth.ControlChange(0, 7, 127);
        synth.ControlChange(0, 11, 127);
        synth.NoteOn(0, 69, 100);

        synth.PitchBend(0, 16383);
        OplChannel channel = synth.Core.Channels[0];
        (int fnum, int block) = ComputeExpected(69, 2f, 16383, OplVolumeModel.Generic);
        Assert.Equal(fnum, channel.FNum);
        Assert.Equal(block, channel.Block);

        synth.ControlChange(0, 101, 0);
        synth.ControlChange(0, 100, 0);
        synth.ControlChange(0, 6, 12);
        synth.ControlChange(0, 38, 0);
        synth.PitchBend(0, 16383);

        (int wideFnum, int wideBlock) = ComputeExpected(69, 12f, 16383, OplVolumeModel.Generic);
        channel = synth.Core.Channels[0];
        Assert.Equal(wideFnum, channel.FNum);
        Assert.Equal(wideBlock, channel.Block);
    }

    [Fact]
    public void ModWheel_UpdatesVibrato()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);
        synth.ControlChange(0, 7, 127);
        synth.ControlChange(0, 11, 127);
        synth.NoteOn(0, 60, 100);

        OplCore core = synth.Core;
        OplChannel channel = core.Channels[0];
        int fnumBase = channel.FNum;

        synth.ControlChange(0, 1, 127);

        float[] buffer = new float[1000 * 2];
        synth.Render(buffer, 0, 1000, 8000);

        int fnumModulated = channel.FNum;
        Assert.NotEqual(fnumBase, fnumModulated);
    }

    [Fact]
    public void ChannelAftertouch_ModulatesPitch()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);
        synth.ControlChange(0, 7, 127);
        synth.ControlChange(0, 11, 127);
        synth.NoteOn(0, 60, 100);

        OplChannel channel = synth.Core.Channels[0];
        int fnumBase = channel.FNum;

        synth.ChannelAftertouch(0, 127);

        float[] buffer = new float[1000 * 2];
        synth.Render(buffer, 0, 1000, 8000);

        int fnumModulated = channel.FNum;
        Assert.NotEqual(fnumBase, fnumModulated);
    }

    [Fact]
    public void Portamento_GlidesBetweenNotes()
    {
        OplSynth synth = new OplSynth(OplSynthMode.Opl2);
        synth.ControlChange(0, 7, 127);
        synth.ControlChange(0, 11, 127);
        synth.ControlChange(0, 5, 64);
        synth.ControlChange(0, 37, 0);
        synth.ControlChange(0, 65, 127);

        synth.NoteOn(0, 60, 100);
        synth.NoteOn(0, 72, 100);

        OplChannel glideChannel = synth.Core.Channels[1];
        (int sourceFnum, int sourceBlock) = ComputeExpected(60, 2f, 8192, OplVolumeModel.Generic);
        Assert.Equal(sourceFnum, glideChannel.FNum);
        Assert.Equal(sourceBlock, glideChannel.Block);

        float[] buffer = new float[800 * 2];
        synth.Render(buffer, 0, 800, 8000);

        (int targetFnum, int targetBlock) = ComputeExpected(72, 2f, 8192, OplVolumeModel.Generic);
        int midFnum = glideChannel.FNum;
        Assert.NotEqual(sourceFnum, midFnum);
        Assert.NotEqual(targetFnum, midFnum);

        buffer = new float[8000 * 2];
        synth.Render(buffer, 0, 8000, 8000);

        Assert.Equal(targetFnum, glideChannel.FNum);
        Assert.Equal(targetBlock, glideChannel.Block);
    }

    private static (int fnum, int block) ComputeExpected(int note, float bendRange, int bendValue, OplVolumeModel model)
    {
        double bend = (bendValue - 8192) / 8192.0;
        double bendSemitones = bend * bendRange;
        double noteValue = note + bendSemitones;
        ushort tone = OplModels.ComputeTone(model, noteValue, out _);
        int fnum = tone & 0x03FF;
        int block = (tone >> 10) & 0x07;
        return (fnum, block);
    }
}
