using System.Runtime.InteropServices;
using OpenMIDI.Midi;
using OpenMIDI.Playback;
using OpenMIDI.Synth;
using SDL2;

namespace OpenMIDI.Example;

public static class Program
{
    private const int SampleRate = 44100;
    private const int Channels = 2;
    private const int BufferFrames = 1024;

    public static int Main(string[] args)
    {
        if (!TryResolveMidiPath(args, out string midiPath, out int exitCode))
        {
            return exitCode;
        }

        if (!File.Exists(midiPath))
        {
            Console.WriteLine($"Missing MIDI file: {midiPath}");
            return 1;
        }

        if (SDL.SDL_Init(SDL.SDL_INIT_AUDIO) != 0)
        {
            Console.WriteLine($"SDL audio init failed: {SDL.SDL_GetError()}");
            return 1;
        }

        MidiFile midi = MidiFile.Load(midiPath);
        OplSynth synth = new OplSynth(OplSynthMode.Opl3);
        MidiPlayer player = new MidiPlayer(synth);
        player.Load(midi);

        PlaybackState state = new PlaybackState(player, SampleRate);
        GCHandle handle = GCHandle.Alloc(state);

        try
        {
            SDL.SDL_AudioSpec desired = new SDL.SDL_AudioSpec
            {
                freq = SampleRate,
                format = SDL.AUDIO_F32SYS,
                channels = Channels,
                samples = BufferFrames,
                callback = AudioCallback,
                userdata = GCHandle.ToIntPtr(handle)
            };

            uint device = SDL.SDL_OpenAudioDevice(null, 0, ref desired, out SDL.SDL_AudioSpec obtained, 0);
            if (device == 0)
            {
                Console.WriteLine($"SDL audio device open failed: {SDL.SDL_GetError()}");
                return 1;
            }

            Console.WriteLine($"Playing {Path.GetFileName(midiPath)} at {obtained.freq} Hz.");
            SDL.SDL_PauseAudioDevice(device, 0);

            while (SDL.SDL_GetAudioDeviceStatus(device) == SDL.SDL_AudioStatus.SDL_AUDIO_PLAYING)
            {
                if (player.IsFinished)
                {
                    break;
                }

                SDL.SDL_Delay(100);
            }

            SDL.SDL_Delay(200);
            SDL.SDL_CloseAudioDevice(device);
        }
        finally
        {
            if (handle.IsAllocated)
            {
                handle.Free();
            }

            SDL.SDL_Quit();
        }

        return 0;
    }

    private static bool TryResolveMidiPath(string[] args, out string midiPath, out int exitCode)
    {
        midiPath = string.Empty;
        exitCode = 0;

        if (args.Length == 0)
        {
            midiPath = DefaultMidiPath();
            return true;
        }

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg is "--help" or "-h" or "/?")
            {
                PrintUsage();
                return false;
            }

            if (arg is "--file" or "-f" or "--midi")
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith('-'))
                {
                    Console.WriteLine($"Missing MIDI path after {arg}.");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(midiPath))
                {
                    Console.WriteLine("Multiple MIDI paths provided.");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                midiPath = args[++i];
                continue;
            }

            if (!arg.StartsWith('-'))
            {
                if (!string.IsNullOrWhiteSpace(midiPath))
                {
                    Console.WriteLine("Multiple MIDI paths provided.");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                midiPath = arg;
                continue;
            }

            Console.WriteLine($"Unknown argument: {arg}");
            PrintUsage();
            exitCode = 1;
            return false;
        }

        if (string.IsNullOrWhiteSpace(midiPath))
        {
            midiPath = DefaultMidiPath();
        }

        return true;
    }

    private static string DefaultMidiPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Test1.mid");
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  OpenMIDI.Example [path]");
        Console.WriteLine("  OpenMIDI.Example --file <path>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -f, --file, --midi   Path to a .mid file.");
        Console.WriteLine("  -h, --help           Show this help.");
        Console.WriteLine();
        Console.WriteLine("If no path is provided, Test1.mid from the output folder is used.");
    }

    private static void AudioCallback(IntPtr userdata, IntPtr stream, int len)
    {
        GCHandle handle = GCHandle.FromIntPtr(userdata);
        if (handle.Target is not PlaybackState state)
        {
            return;
        }

        int sampleCount = len / sizeof(float);
        int frames = sampleCount / Channels;
        if (frames <= 0)
        {
            return;
        }

        if (state.Buffer.Length < sampleCount)
        {
            state.Buffer = new float[sampleCount];
        }

        if (state.Player.IsFinished)
        {
            Array.Clear(state.Buffer, 0, sampleCount);
        }
        else
        {
            state.Player.Render(state.Buffer, 0, frames, state.SampleRate);
        }

        Marshal.Copy(state.Buffer, 0, stream, sampleCount);
    }

    private sealed class PlaybackState
    {
        public PlaybackState(MidiPlayer player, int sampleRate)
        {
            Player = player;
            SampleRate = sampleRate;
        }

        public MidiPlayer Player { get; }
        public int SampleRate { get; }
        public float[] Buffer { get; set; } = Array.Empty<float>();
    }
}
