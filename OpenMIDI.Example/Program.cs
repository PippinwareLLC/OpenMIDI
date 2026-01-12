using System.IO;
using System.Runtime.InteropServices;
using System.Text;
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

        PlaybackState state = new PlaybackState(player, synth, SampleRate, Channels);
        ConsoleStatusRenderer statusRenderer = new ConsoleStatusRenderer(state, midiPath);
        GCHandle handle = GCHandle.Alloc(state);
        statusRenderer.Initialize();

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

                statusRenderer.Render();
                SDL.SDL_Delay(100);
            }

            statusRenderer.Render();
            SDL.SDL_Delay(200);
            SDL.SDL_CloseAudioDevice(device);
        }
        finally
        {
            statusRenderer.Shutdown();
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
        int frames = sampleCount / state.Channels;
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
        public PlaybackState(MidiPlayer player, OplSynth synth, int sampleRate, int channels)
        {
            Player = player;
            Synth = synth;
            SampleRate = sampleRate;
            Channels = channels;
        }

        public MidiPlayer Player { get; }
        public OplSynth Synth { get; }
        public int SampleRate { get; }
        public int Channels { get; }
        public float[] Buffer { get; set; } = Array.Empty<float>();
    }

    private sealed class ConsoleStatusRenderer
    {
        private const int ChannelCount = 16;
        private const int VuBarWidth = 24;
        private const int ChannelBarWidth = 16;
        private readonly PlaybackState _state;
        private readonly string _midiPath;
        private readonly int[] _channelCounts = new int[ChannelCount];
        private readonly float[] _channelLevels = new float[ChannelCount];
        private readonly bool _interactive;
        private int _lineWidth;

        public ConsoleStatusRenderer(PlaybackState state, string midiPath)
        {
            _state = state;
            _midiPath = midiPath;
            _interactive = !Console.IsOutputRedirected;
        }

        public void Initialize()
        {
            if (!_interactive)
            {
                return;
            }

            _lineWidth = GetLineWidth();
            Console.Clear();
            Console.CursorVisible = false;
        }

        public void Render()
        {
            if (!_interactive)
            {
                return;
            }

            _state.Synth.CopyChannelMeters(_channelCounts, _channelLevels);

            StringBuilder builder = new StringBuilder();
            builder.AppendLine(PadLine($"OpenMIDI OPL Status - {Path.GetFileName(_midiPath)}", _lineWidth));
            builder.AppendLine(PadLine(
                $"Time {_state.Player.CurrentTimeSeconds:0.00}/{_state.Player.DurationSeconds:0.00}s  Voices {_state.Synth.ActiveVoiceCount}/{_state.Synth.VoiceCount}",
                _lineWidth));
            builder.AppendLine(PadLine(
                $"VU L {FormatBar(_state.Synth.LastPeakLeft, VuBarWidth)} {_state.Synth.LastPeakLeft:0.00}  R {FormatBar(_state.Synth.LastPeakRight, VuBarWidth)} {_state.Synth.LastPeakRight:0.00}",
                _lineWidth));
            builder.AppendLine(PadLine("Channels:", _lineWidth));

            for (int i = 0; i < ChannelCount; i++)
            {
                string line = $" CH{i + 1:00} {FormatBar(_channelLevels[i], ChannelBarWidth)} {_channelCounts[i],2} voice(s)";
                builder.AppendLine(PadLine(line, _lineWidth));
            }

            Console.SetCursorPosition(0, 0);
            Console.Write(builder.ToString());
        }

        public void Shutdown()
        {
            if (!_interactive)
            {
                return;
            }

            Console.CursorVisible = true;
        }

        private static string FormatBar(float value, int width)
        {
            float clamped = Math.Clamp(value, 0f, 1f);
            int filled = (int)Math.Round(clamped * width, MidpointRounding.AwayFromZero);
            filled = Math.Clamp(filled, 0, width);
            return $"[{new string('#', filled)}{new string('-', width - filled)}]";
        }

        private static string PadLine(string line, int width)
        {
            if (line.Length >= width)
            {
                return line;
            }

            return line + new string(' ', width - line.Length);
        }

        private static int GetLineWidth()
        {
            try
            {
                return Math.Max(60, Console.WindowWidth);
            }
            catch (IOException)
            {
                return 80;
            }
        }
    }
}
