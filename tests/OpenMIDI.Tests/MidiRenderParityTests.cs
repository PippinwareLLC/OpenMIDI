using System.Text.Json;
using OpenMIDI.Midi;
using OpenMIDI.Playback;
using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class MidiRenderParityTests
{
    private const int RmsTolerance = 7000;
    private const double CorrelationThreshold = 0.94;
    private const int ChunkFrames = 1024;

    [Fact]
    public void FixtureRenders_MatchReferenceRms()
    {
        ReferenceRoot reference = LoadReference();
        ReferenceMeta meta = reference.Meta;

        Assert.True(meta.SampleRate > 0, "Reference sample rate missing.");
        Assert.True(meta.BlockFrames > 0, "Reference block size missing.");
        Assert.True(meta.Scale > 0, "Reference RMS scale missing.");

        string root = FindRepoRoot();
        string bankPath = Path.Combine(root, "OpenMIDI", meta.Bank);
        Assert.True(File.Exists(bankPath), $"Bank file missing: {bankPath}");

        foreach (ReferenceFixture fixture in reference.Fixtures)
        {
            Assert.Equal(meta.SampleRate, fixture.SampleRate);
            Assert.Equal(2, fixture.Channels);
            Assert.Equal(fixture.Blocks, fixture.RmsLeft.Length);
            Assert.Equal(fixture.Blocks, fixture.RmsRight.Length);

            string midiPath = Path.Combine(root, "OpenMIDI", fixture.Name);
            Assert.True(File.Exists(midiPath), $"Fixture missing: {midiPath}");

            RenderRms(midiPath, bankPath, meta.SampleRate, fixture.Blocks, meta.BlockFrames, meta.Scale,
                out int[] left, out int[] right);

            int maxLeftDiff = 0;
            int maxRightDiff = 0;
            for (int i = 0; i < fixture.Blocks; i++)
            {
                int leftDiff = Math.Abs(left[i] - fixture.RmsLeft[i]);
                int rightDiff = Math.Abs(right[i] - fixture.RmsRight[i]);
                if (leftDiff > maxLeftDiff)
                {
                    maxLeftDiff = leftDiff;
                }

                if (rightDiff > maxRightDiff)
                {
                    maxRightDiff = rightDiff;
                }
            }

            double leftCorrelation = ComputeCorrelation(left, fixture.RmsLeft);
            double rightCorrelation = ComputeCorrelation(right, fixture.RmsRight);

            Assert.True(maxLeftDiff <= RmsTolerance,
                $"Left RMS mismatch for {fixture.Name}: max diff {maxLeftDiff}");
            Assert.True(maxRightDiff <= RmsTolerance,
                $"Right RMS mismatch for {fixture.Name}: max diff {maxRightDiff}");
            Assert.True(leftCorrelation >= CorrelationThreshold,
                $"Left RMS correlation below threshold for {fixture.Name}: {leftCorrelation:0.000}");
            Assert.True(rightCorrelation >= CorrelationThreshold,
                $"Right RMS correlation below threshold for {fixture.Name}: {rightCorrelation:0.000}");
        }
    }

    private static void RenderRms(string midiPath, string bankPath, int sampleRate, int blocks, int blockFrames, int scale,
        out int[] left, out int[] right)
    {
        MidiFile midi = MidiFile.Load(midiPath);
        OplSynth synth = new OplSynth(OplSynthMode.Opl3)
        {
            MasterGain = 1.0f
        };
        synth.LoadBank(WoplBankLoader.LoadFromFile(bankPath));

        MidiPlayer player = new MidiPlayer(synth);
        player.Load(midi);

        left = new int[blocks];
        right = new int[blocks];
        double[] sumLeft = new double[blocks];
        double[] sumRight = new double[blocks];

        int totalFrames = blocks * blockFrames;
        int framesRendered = 0;
        double peak = 0.0;
        float[] buffer = Array.Empty<float>();

        while (framesRendered < totalFrames)
        {
            int frames = Math.Min(ChunkFrames, totalFrames - framesRendered);
            int sampleCount = frames * 2;
            if (buffer.Length < sampleCount)
            {
                buffer = new float[sampleCount];
            }

            player.Render(buffer, 0, frames, sampleRate);

            for (int frame = 0; frame < frames; frame++)
            {
                int absoluteFrame = framesRendered + frame;
                int blockIndex = absoluteFrame / blockFrames;
                int baseIndex = frame * 2;
                float leftSample = buffer[baseIndex];
                float rightSample = buffer[baseIndex + 1];
                sumLeft[blockIndex] += leftSample * leftSample;
                sumRight[blockIndex] += rightSample * rightSample;

                double absLeft = Math.Abs(leftSample);
                if (absLeft > peak)
                {
                    peak = absLeft;
                }

                double absRight = Math.Abs(rightSample);
                if (absRight > peak)
                {
                    peak = absRight;
                }
            }

            framesRendered += frames;
        }

        if (peak <= 0.0)
        {
            peak = 1.0;
        }

        for (int i = 0; i < blocks; i++)
        {
            double rmsLeft = Math.Sqrt(sumLeft[i] / blockFrames) / peak;
            double rmsRight = Math.Sqrt(sumRight[i] / blockFrames) / peak;
            left[i] = (int)Math.Round(rmsLeft * scale, MidpointRounding.ToEven);
            right[i] = (int)Math.Round(rmsRight * scale, MidpointRounding.ToEven);
        }
    }

    private static double ComputeCorrelation(int[] left, int[] right)
    {
        if (left.Length == 0 || right.Length == 0)
        {
            return 0.0;
        }

        int count = Math.Min(left.Length, right.Length);
        double sumXY = 0.0;
        double sumX2 = 0.0;
        double sumY2 = 0.0;

        for (int i = 0; i < count; i++)
        {
            double x = left[i];
            double y = right[i];
            sumXY += x * y;
            sumX2 += x * x;
            sumY2 += y * y;
        }

        if (sumX2 <= 0.0 || sumY2 <= 0.0)
        {
            return 0.0;
        }

        return sumXY / Math.Sqrt(sumX2 * sumY2);
    }

    private static ReferenceRoot LoadReference()
    {
        string root = FindRepoRoot();
        string path = Path.Combine(root, "OpenMIDI", "tests", "OpenMIDI.Tests", "ReferenceClips", "midi_reference.json");
        string json = File.ReadAllText(path);
        ReferenceRoot? reference = JsonSerializer.Deserialize<ReferenceRoot>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (reference == null)
        {
            throw new InvalidOperationException("Failed to parse midi_reference.json");
        }

        return reference;
    }

    private static string FindRepoRoot()
    {
        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && current != null; i++)
        {
            if (File.Exists(Path.Combine(current, "OpenQuest.sln")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }

    private sealed class ReferenceRoot
    {
        public ReferenceMeta Meta { get; set; } = new();
        public List<ReferenceFixture> Fixtures { get; set; } = new();
    }

    private sealed class ReferenceMeta
    {
        public string Bank { get; set; } = string.Empty;
        public int SampleRate { get; set; }
        public int BlockFrames { get; set; }
        public int Scale { get; set; }
    }

    private sealed class ReferenceFixture
    {
        public string Name { get; set; } = string.Empty;
        public int Frames { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int Blocks { get; set; }
        public int[] RmsLeft { get; set; } = Array.Empty<int>();
        public int[] RmsRight { get; set; } = Array.Empty<int>();
    }
}
