using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplCoreReferenceClipTests
{
    private const int ReferenceFrames = 64;
    private const int OutputSampleRate = 44100;

    [Fact]
    public void OplCore_ReferenceClip_Matches()
    {
        short[] actual = GenerateClip();
        string referencePath = GetReferencePath();

        if (Environment.GetEnvironmentVariable("OPENMIDI_UPDATE_REFERENCE") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(referencePath)!);
            File.WriteAllText(referencePath, string.Join(",", actual));
            return;
        }

        Assert.True(File.Exists(referencePath), $"Reference clip missing: {referencePath}");
        short[] expected = LoadClip(referencePath);
        Assert.Equal(expected.Length, actual.Length);

        for (int i = 0; i < expected.Length; i++)
        {
            int diff = Math.Abs(expected[i] - actual[i]);
            Assert.True(diff <= 2, $"Sample {i} differs: expected {expected[i]}, actual {actual[i]}");
        }
    }

    private static short[] GenerateClip()
    {
        OplCore core = new OplCore(OplChipType.Opl2);
        ConfigureBasicPatch(core);

        float[] interleaved = new float[ReferenceFrames * 2];
        core.Render(interleaved, 0, ReferenceFrames, OutputSampleRate);

        short[] result = new short[interleaved.Length];
        for (int i = 0; i < interleaved.Length; i++)
        {
            float scaled = interleaved[i] * 32767f;
            int quantized = (int)Math.Round(scaled);
            quantized = Math.Clamp(quantized, short.MinValue, short.MaxValue);
            result[i] = (short)quantized;
        }

        return result;
    }

    private static void ConfigureBasicPatch(OplCore core)
    {
        core.WriteRegister(0x20, 0x21);
        core.WriteRegister(0x40, 0x20);
        core.WriteRegister(0x60, 0xF3);
        core.WriteRegister(0x80, 0xF5);
        core.WriteRegister(0xE0, 0x00);

        core.WriteRegister(0x23, 0x01);
        core.WriteRegister(0x43, 0x00);
        core.WriteRegister(0x63, 0xF3);
        core.WriteRegister(0x83, 0xF5);
        core.WriteRegister(0xE3, 0x00);

        core.WriteRegister(0xC0, 0x00);
        core.WriteRegister(0xA0, 0x56);
        core.WriteRegister(0xB0, 0x31);
    }

    private static short[] LoadClip(string path)
    {
        string text = File.ReadAllText(path);
        string[] parts = text.Split(new[] { ',', '\r', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        short[] values = new short[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            values[i] = short.Parse(parts[i]);
        }

        return values;
    }

    private static string GetReferencePath()
    {
        string root = FindRepoRoot();
        return Path.Combine(root, "OpenMIDI", "tests", "OpenMIDI.Tests", "ReferenceClips", "opl2_basic.txt");
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
}
