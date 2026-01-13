using System;
using System.IO;
using OpenMIDI.Midi;
using OpenMIDI.Playback;
using OpenMIDI.Synth;
using Xunit.Abstractions;

namespace OpenMIDI.Tests;

public sealed class Test7VoiceStealDiagnostics
{
    private readonly ITestOutputHelper _output;

    public Test7VoiceStealDiagnostics(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Test7_VoiceStealDiagnostic()
    {
        string midiPath = GetTest7Path();
        Assert.True(File.Exists(midiPath), $"Missing MIDI file: {midiPath}");

        MidiFile midi = MidiFile.Load(midiPath);
        OplSynth synth = new OplSynth(OplSynthMode.Opl3);
        MidiPlayer player = new MidiPlayer(synth);
        player.Load(midi);

        const int sampleRate = 44100;
        const int framesPerBuffer = 1024;
        float[] buffer = new float[framesPerBuffer * 2];

        int maxIterations = (int)Math.Ceiling(player.DurationSeconds * sampleRate / framesPerBuffer) + 4;
        for (int i = 0; i < maxIterations && !player.IsFinished; i++)
        {
            player.Render(buffer, 0, framesPerBuffer, sampleRate);
        }

        _output.WriteLine($"Test7 NoteOns: {synth.NoteOnCount}");
        _output.WriteLine($"Test7 SameNoteReuse: {synth.SameNoteReuseCount}");
        _output.WriteLine($"Test7 ReleaseReuse: {synth.ReleaseReuseCount}");
        _output.WriteLine($"Test7 VoiceSteals: {synth.VoiceStealCount}");
        _output.WriteLine($"Test7 PeakVoices: {synth.PeakActiveVoiceCount}/{synth.VoiceCount}");

        Assert.True(player.IsFinished);
        Assert.True(synth.NoteOnCount > 0);
        Assert.InRange(synth.VoiceStealCount, 0, synth.NoteOnCount);
        Assert.InRange(synth.PeakActiveVoiceCount, 1, synth.VoiceCount);
    }

    private static string GetTest7Path()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Test7.mid"));
    }
}
