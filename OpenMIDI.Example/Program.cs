using System.Globalization;
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
    private const int DefaultChipCount = 1;

    public static int Main(string[] args)
    {
        if (!TryResolveOptions(args, out string midiPath, out int chipCount, out string bankPath, out float gain, out int exitCode))
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
        OplSynth synth = new OplSynth(OplSynthMode.Opl3, chipCount);
        synth.MasterGain = gain;
        if (!string.IsNullOrWhiteSpace(bankPath))
        {
            if (!File.Exists(bankPath))
            {
                Console.WriteLine($"Missing bank file: {bankPath}");
                return 1;
            }

            try
            {
                synth.LoadBank(WoplBankLoader.LoadFromFile(bankPath));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load bank {bankPath}: {ex.Message}");
                return 1;
            }
        }
        MidiChannelNoteTracker noteTracker = new MidiChannelNoteTracker();
        TrackingSynth trackingSynth = new TrackingSynth(synth, noteTracker);
        MidiPlayer player = new MidiPlayer(trackingSynth);
        player.Load(midi);

        PlaybackState state = new PlaybackState(player, trackingSynth, SampleRate, Channels);
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

            Console.WriteLine($"Playing {Path.GetFileName(midiPath)} at {obtained.freq} Hz (chips {synth.ChipCount}, gain {synth.MasterGain:0.00}).");
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

    private static bool TryResolveOptions(string[] args, out string midiPath, out int chipCount, out string bankPath, out float gain, out int exitCode)
    {
        midiPath = string.Empty;
        chipCount = DefaultChipCount;
        bankPath = string.Empty;
        gain = 1f;
        exitCode = 0;
        bool chipCountSet = false;
        bool bankPathSet = false;
        bool gainSet = false;

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

            if (arg is "--chips" or "--chip-count" or "--opl-chips")
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith('-'))
                {
                    Console.WriteLine($"Missing chip count after {arg}.");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                if (chipCountSet)
                {
                    Console.WriteLine("Multiple chip counts provided.");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                if (!int.TryParse(args[i + 1], out int parsed) || parsed <= 0)
                {
                    Console.WriteLine($"Invalid chip count: {args[i + 1]}");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                chipCount = parsed;
                chipCountSet = true;
                i++;
                continue;
            }

            if (arg is "--bank" or "--wopl" or "--bank-file")
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith('-'))
                {
                    Console.WriteLine($"Missing bank path after {arg}.");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                if (bankPathSet)
                {
                    Console.WriteLine("Multiple bank paths provided.");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                bankPath = args[++i];
                bankPathSet = true;
                continue;
            }

            if (arg is "--gain" or "--volume" or "--master-gain")
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith('-'))
                {
                    Console.WriteLine($"Missing gain value after {arg}.");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                if (gainSet)
                {
                    Console.WriteLine("Multiple gain values provided.");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                if (!float.TryParse(args[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed) || parsed <= 0f)
                {
                    Console.WriteLine($"Invalid gain value: {args[i + 1]}");
                    PrintUsage();
                    exitCode = 1;
                    return false;
                }

                gain = parsed;
                gainSet = true;
                i++;
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

        if (!gainSet)
        {
            gain = 1f / Math.Max(1, chipCount);
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
        Console.WriteLine("  --chips <count>      Number of OPL chips to emulate (default 1).");
        Console.WriteLine("  --bank <path>        Path to a WOPL instrument bank file.");
        Console.WriteLine("  --gain <value>       Master output gain (default 1.0 / chip count).");
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

    private sealed class TrackingSynth : IMidiSynth
    {
        public TrackingSynth(OplSynth synth, MidiChannelNoteTracker tracker)
        {
            Synth = synth ?? throw new ArgumentNullException(nameof(synth));
            Tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        }

        public OplSynth Synth { get; }
        public MidiChannelNoteTracker Tracker { get; }

        public void Reset()
        {
            Tracker.Reset();
            Synth.Reset();
        }

        public void NoteOn(int channel, int note, int velocity)
        {
            Tracker.NoteOn(channel, note, velocity);
            Synth.NoteOn(channel, note, velocity);
        }

        public void NoteOff(int channel, int note, int velocity)
        {
            Tracker.NoteOff(channel, note, velocity);
            Synth.NoteOff(channel, note, velocity);
        }

        public void ControlChange(int channel, int controller, int value)
        {
            Synth.ControlChange(channel, controller, value);
        }

        public void ProgramChange(int channel, int program)
        {
            Synth.ProgramChange(channel, program);
        }

        public void PitchBend(int channel, int value)
        {
            Synth.PitchBend(channel, value);
        }

        public void Render(float[] interleaved, int offset, int frames, int sampleRate)
        {
            Synth.Render(interleaved, offset, frames, sampleRate);
        }
    }

    private sealed class PlaybackState
    {
        public PlaybackState(MidiPlayer player, TrackingSynth synth, int sampleRate, int channels)
        {
            Player = player;
            TrackingSynth = synth;
            SampleRate = sampleRate;
            Channels = channels;
        }

        public MidiPlayer Player { get; }
        public TrackingSynth TrackingSynth { get; }
        public OplSynth Synth => TrackingSynth.Synth;
        public MidiChannelNoteTracker NoteTracker => TrackingSynth.Tracker;
        public int SampleRate { get; }
        public int Channels { get; }
        public float[] Buffer { get; set; } = Array.Empty<float>();
    }

    private sealed class ConsoleStatusRenderer
    {
        private const int ChannelCount = 16;
        private const int VuBarWidth = 24;
        private const int ChannelBarWidth = 16;
        private const int RollLowNote = 36;
        private const int MinRollNoteCount = 12;
        private const int MaxRollNoteCount = 60;
        private static readonly string[] NoteNames =
        {
            "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"
        };
        private readonly PlaybackState _state;
        private readonly string _midiPath;
        private readonly int[] _channelCounts = new int[ChannelCount];
        private readonly float[] _channelLevels = new float[ChannelCount];
        private readonly bool _interactive;
        private int _lineWidth;
        private int _rollNoteCount = 36;

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
            _rollNoteCount = CalculateRollNoteCount();
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
                $"Time {_state.Player.CurrentTimeSeconds:0.00}/{_state.Player.DurationSeconds:0.00}s  Voices {_state.Synth.ActiveVoiceCount}/{_state.Synth.VoiceCount}  Chips {_state.Synth.ChipCount}",
                _lineWidth));
            builder.AppendLine(PadLine(
                $"VU L {FormatBar(_state.Synth.LastPeakLeft, VuBarWidth)} {_state.Synth.LastPeakLeft:0.00}  R {FormatBar(_state.Synth.LastPeakRight, VuBarWidth)} {_state.Synth.LastPeakRight:0.00}",
                _lineWidth));
            builder.AppendLine(PadLine(BuildOplStatusLine(), _lineWidth));
            builder.AppendLine(PadLine("Channels:", _lineWidth));
            builder.AppendLine(PadLine(BuildRollHeader(), _lineWidth));

            for (int i = 0; i < ChannelCount; i++)
            {
                string roll = BuildPianoRollLine(i);
                string line = $" CH{i + 1:00} {FormatBar(_channelLevels[i], ChannelBarWidth)} {_channelCounts[i],2} voice(s) {roll}";
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

        private int CalculateRollNoteCount()
        {
            int available = _lineWidth - (ChannelBarWidth + 20);
            return Math.Clamp(available, MinRollNoteCount, MaxRollNoteCount);
        }

        private static string GetNoteLabel(int note)
        {
            int index = Math.Clamp(note, 0, 127);
            int octave = index / 12 - 1;
            string name = NoteNames[index % 12];
            return $"{name}{octave}";
        }

        private string BuildRollHeader()
        {
            int highNote = GetRollHighNote();
            return $"Roll {GetNoteLabel(RollLowNote)}..{GetNoteLabel(highNote)}";
        }

        private string BuildPianoRollLine(int channel)
        {
            int highNote = GetRollHighNote();
            int count = highNote - RollLowNote + 1;
            char[] buffer = new char[count];
            for (int i = 0; i < count; i++)
            {
                int note = RollLowNote + i;
                if (_state.NoteTracker.IsActive(channel, note))
                {
                    buffer[i] = '#';
                }
                else
                {
                    buffer[i] = note % 12 == 0 ? '|' : '.';
                }
            }

            return new string(buffer);
        }

        private int GetRollHighNote()
        {
            return Math.Min(127, RollLowNote + _rollNoteCount - 1);
        }

        private string BuildOplStatusLine()
        {
            OplCore core = _state.Synth.Core;
            OplRegisterMap regs = core.Registers;
            string irq = core.IrqActive ? "IRQ" : "irq";
            string ta = regs.TimerAEnabled ? "TA" : "ta";
            string tb = regs.TimerBEnabled ? "TB" : "tb";
            string taFlag = regs.TimerAOverflow ? "!" : "-";
            string tbFlag = regs.TimerBOverflow ? "!" : "-";

            return $"OPL {core.ChipType} {irq} {ta}:{regs.TimerAValue:X2}{taFlag} {tb}:{regs.TimerBValue:X2}{tbFlag} " +
                   $"Status 0x{core.Status:X2} CSM {(regs.CsmEnabled ? "on" : "off")} " +
                   $"Rhythm {(regs.RhythmEnabled ? "on" : "off")}";
        }
    }
}
