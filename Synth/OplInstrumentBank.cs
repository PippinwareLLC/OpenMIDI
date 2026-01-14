using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace OpenMIDI.Synth;

[Flags]
public enum OplInstrumentFlags : byte
{
    None = 0,
    FourOp = 0x01,
    PseudoFourOp = 0x02,
    Blank = 0x04,
    FixedNote = 0x40,
    RhythmMask = 0x38
}

public enum OplRhythmMode : byte
{
    None = 0x00,
    BassDrum = 0x08,
    Snare = 0x10,
    Tom = 0x18,
    Cymbal = 0x20,
    HiHat = 0x28
}

public sealed class OplOperatorPatch
{
    public OplOperatorPatch(byte amVibEgtKsrMult, byte kslTl, byte arDr, byte slRr, byte waveform)
    {
        AmVibEgtKsrMult = amVibEgtKsrMult;
        KslTl = kslTl;
        ArDr = arDr;
        SlRr = slRr;
        Waveform = waveform;
    }

    public byte AmVibEgtKsrMult { get; }
    public byte KslTl { get; }
    public byte ArDr { get; }
    public byte SlRr { get; }
    public byte Waveform { get; }
}

public sealed class OplInstrument
{
    public OplInstrument(string name,
        short noteOffset1,
        short noteOffset2,
        sbyte midiVelocityOffset,
        sbyte secondVoiceDetune,
        byte percussionKeyNumber,
        OplInstrumentFlags flags,
        byte feedbackConnection1,
        byte feedbackConnection2,
        OplOperatorPatch[] operators,
        ushort delayOnMs,
        ushort delayOffMs)
    {
        Name = name;
        NoteOffset1 = noteOffset1;
        NoteOffset2 = noteOffset2;
        MidiVelocityOffset = midiVelocityOffset;
        SecondVoiceDetune = secondVoiceDetune;
        PercussionKeyNumber = percussionKeyNumber;
        Flags = flags;
        FeedbackConnection1 = feedbackConnection1;
        FeedbackConnection2 = feedbackConnection2;
        Operators = operators;
        DelayOnMs = delayOnMs;
        DelayOffMs = delayOffMs;
    }

    public string Name { get; }
    public short NoteOffset1 { get; }
    public short NoteOffset2 { get; }
    public sbyte MidiVelocityOffset { get; }
    public sbyte SecondVoiceDetune { get; }
    public byte PercussionKeyNumber { get; }
    public OplInstrumentFlags Flags { get; }
    public byte FeedbackConnection1 { get; }
    public byte FeedbackConnection2 { get; }
    public OplOperatorPatch[] Operators { get; }
    public ushort DelayOnMs { get; }
    public ushort DelayOffMs { get; }

    public bool IsBlank => (Flags & OplInstrumentFlags.Blank) != 0;
    public bool IsFixedNote => (Flags & OplInstrumentFlags.FixedNote) != 0;

    public OplRhythmMode RhythmMode => (OplRhythmMode)((byte)Flags & (byte)OplInstrumentFlags.RhythmMask);
}

public sealed class OplBank
{
    public OplBank(string name, byte bankMsb, byte bankLsb, OplInstrument[] instruments)
    {
        Name = name;
        BankMsb = bankMsb;
        BankLsb = bankLsb;
        Instruments = instruments;
    }

    public string Name { get; }
    public byte BankMsb { get; }
    public byte BankLsb { get; }
    public OplInstrument[] Instruments { get; }
}

public sealed class OplInstrumentBankSet
{
    public OplInstrumentBankSet(IReadOnlyList<OplBank> melodicBanks, IReadOnlyList<OplBank> percussionBanks,
        bool deepTremolo, bool deepVibrato, OplVolumeModel volumeModel, bool mt32Defaults)
    {
        MelodicBanks = melodicBanks;
        PercussionBanks = percussionBanks;
        DeepTremolo = deepTremolo;
        DeepVibrato = deepVibrato;
        VolumeModel = volumeModel;
        Mt32Defaults = mt32Defaults;
    }

    public IReadOnlyList<OplBank> MelodicBanks { get; }
    public IReadOnlyList<OplBank> PercussionBanks { get; }
    public bool DeepTremolo { get; }
    public bool DeepVibrato { get; }
    public OplVolumeModel VolumeModel { get; }
    public bool Mt32Defaults { get; }
    public OplInstrument DefaultInstrument => MelodicBanks.Count > 0 ? MelodicBanks[0].Instruments[0] : OplInstrumentDefaults.DefaultInstrument;

    public static OplInstrumentBankSet CreateDefault()
    {
        OplInstrument defaultInstrument = OplInstrumentDefaults.DefaultInstrument;
        OplInstrument[] melodic = new OplInstrument[128];
        OplInstrument[] percussion = new OplInstrument[128];
        for (int i = 0; i < 128; i++)
        {
            melodic[i] = defaultInstrument;
            percussion[i] = defaultInstrument;
        }

        OplBank melodicBank = new OplBank("Default", 0, 0, melodic);
        OplBank percussionBank = new OplBank("DefaultDrums", 0, 0, percussion);
        return new OplInstrumentBankSet(new[] { melodicBank }, new[] { percussionBank }, deepTremolo: false,
            deepVibrato: false, volumeModel: OplVolumeModel.Generic, mt32Defaults: false);
    }

    public OplInstrument GetMelodic(byte bankMsb, byte bankLsb, int program)
    {
        OplBank bank = FindBank(MelodicBanks, bankMsb, bankLsb);
        int index = Math.Clamp(program, 0, 127);
        return bank.Instruments[index];
    }

    public OplInstrument GetPercussion(byte bankMsb, byte bankLsb, int note)
    {
        OplBank bank = FindBank(PercussionBanks, bankMsb, bankLsb);
        int index = Math.Clamp(note, 0, 127);
        return bank.Instruments[index];
    }

    private static OplBank FindBank(IReadOnlyList<OplBank> banks, byte bankMsb, byte bankLsb)
    {
        if (banks.Count == 0)
        {
            return new OplBank("Default", 0, 0, CreateDefault().MelodicBanks[0].Instruments);
        }

        foreach (OplBank bank in banks)
        {
            if (bank.BankMsb == bankMsb && bank.BankLsb == bankLsb)
            {
                return bank;
            }
        }

        if (bankLsb != 0)
        {
            foreach (OplBank bank in banks)
            {
                if (bank.BankMsb == bankMsb && bank.BankLsb == 0)
                {
                    return bank;
                }
            }
        }

        return banks[0];
    }
}

public static class WoplBankLoader
{
    private static readonly byte[] BankMagic = Encoding.ASCII.GetBytes("WOPL3-BANK\0");
    private const ushort LatestVersion = 3;

    public static OplInstrumentBankSet LoadFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Bank path is required.", nameof(path));
        }

        return LoadFromBytes(File.ReadAllBytes(path));
    }

    public static OplInstrumentBankSet LoadFromBytes(ReadOnlySpan<byte> data)
    {
        int offset = 0;
        if (data.Length < BankMagic.Length + 2 + 6)
        {
            throw new InvalidDataException("WOPL data is too short.");
        }

        if (!data.Slice(0, BankMagic.Length).SequenceEqual(BankMagic))
        {
            throw new InvalidDataException("Invalid WOPL bank magic.");
        }

        offset += BankMagic.Length;
        ushort version = ReadUInt16LE(data, ref offset);
        if (version > LatestVersion)
        {
            throw new InvalidDataException($"Unsupported WOPL version {version}.");
        }

        ushort melodicCount = ReadUInt16BE(data, ref offset);
        ushort percussionCount = ReadUInt16BE(data, ref offset);
        byte oplFlags = ReadByte(data, ref offset);
        byte volumeModel = ReadByte(data, ref offset);

        if (melodicCount == 0 || percussionCount == 0)
        {
            throw new InvalidDataException("WOPL bank counts must be non-zero.");
        }

        List<OplBank> melodicBanks = new List<OplBank>(melodicCount);
        List<OplBank> percussionBanks = new List<OplBank>(percussionCount);

        if (version >= 2)
        {
            for (int i = 0; i < melodicCount; i++)
            {
                melodicBanks.Add(ReadBankHeader(data, ref offset));
            }

            for (int i = 0; i < percussionCount; i++)
            {
                percussionBanks.Add(ReadBankHeader(data, ref offset));
            }
        }
        else
        {
            for (int i = 0; i < melodicCount; i++)
            {
                melodicBanks.Add(new OplBank($"Melodic{i}", 0, 0, new OplInstrument[128]));
            }

            for (int i = 0; i < percussionCount; i++)
            {
                percussionBanks.Add(new OplBank($"Percussion{i}", 0, 0, new OplInstrument[128]));
            }
        }

        int instrumentSize = version >= 3 ? 66 : 62;
        ReadInstrumentBlocks(data, ref offset, instrumentSize, melodicBanks, version);
        ReadInstrumentBlocks(data, ref offset, instrumentSize, percussionBanks, version);

        bool deepTremolo = (oplFlags & 0x01) != 0;
        bool deepVibrato = (oplFlags & 0x02) != 0;
        bool mt32Defaults = (oplFlags & 0x04) != 0;
        OplVolumeModel model = volumeModel <= (byte)OplVolumeModel.Rsxx
            ? (OplVolumeModel)volumeModel
            : OplVolumeModel.Generic;
        return new OplInstrumentBankSet(melodicBanks, percussionBanks, deepTremolo, deepVibrato, model, mt32Defaults);
    }

    private static OplBank ReadBankHeader(ReadOnlySpan<byte> data, ref int offset)
    {
        string name = ReadFixedString(data, ref offset, 32);
        byte lsb = ReadByte(data, ref offset);
        byte msb = ReadByte(data, ref offset);
        return new OplBank(name, msb, lsb, new OplInstrument[128]);
    }

    private static void ReadInstrumentBlocks(ReadOnlySpan<byte> data, ref int offset, int instrumentSize, List<OplBank> banks, ushort version)
    {
        foreach (OplBank bank in banks)
        {
            for (int i = 0; i < 128; i++)
            {
                bank.Instruments[i] = ReadInstrument(data, ref offset, version);
            }
        }
    }

    private static OplInstrument ReadInstrument(ReadOnlySpan<byte> data, ref int offset, ushort version)
    {
        string name = ReadFixedString(data, ref offset, 32);
        short noteOffset1 = ReadInt16BE(data, ref offset);
        short noteOffset2 = ReadInt16BE(data, ref offset);
        sbyte midiVelocityOffset = ReadSByte(data, ref offset);
        sbyte secondVoiceDetune = ReadSByte(data, ref offset);
        byte percussionKeyNumber = ReadByte(data, ref offset);
        OplInstrumentFlags flags = (OplInstrumentFlags)ReadByte(data, ref offset);
        byte fbConn1 = ReadByte(data, ref offset);
        byte fbConn2 = ReadByte(data, ref offset);

        OplOperatorPatch[] operators = new OplOperatorPatch[4];
        for (int i = 0; i < 4; i++)
        {
            byte amVibEgtKsrMult = ReadByte(data, ref offset);
            byte kslTl = ReadByte(data, ref offset);
            byte arDr = ReadByte(data, ref offset);
            byte slRr = ReadByte(data, ref offset);
            byte waveform = ReadByte(data, ref offset);
            operators[i] = new OplOperatorPatch(amVibEgtKsrMult, kslTl, arDr, slRr, waveform);
        }

        ushort delayOnMs = 0;
        ushort delayOffMs = 0;
        if (version >= 3)
        {
            delayOnMs = ReadUInt16BE(data, ref offset);
            delayOffMs = ReadUInt16BE(data, ref offset);
        }

        return new OplInstrument(name, noteOffset1, noteOffset2, midiVelocityOffset, secondVoiceDetune,
            percussionKeyNumber, flags, fbConn1, fbConn2, operators, delayOnMs, delayOffMs);
    }

    private static string ReadFixedString(ReadOnlySpan<byte> data, ref int offset, int length)
    {
        EnsureLength(data, offset, length);
        string text = Encoding.ASCII.GetString(data.Slice(offset, length));
        offset += length;
        int end = text.IndexOf('\0');
        return end >= 0 ? text.Substring(0, end) : text;
    }

    private static byte ReadByte(ReadOnlySpan<byte> data, ref int offset)
    {
        EnsureLength(data, offset, 1);
        return data[offset++];
    }

    private static sbyte ReadSByte(ReadOnlySpan<byte> data, ref int offset)
    {
        byte value = ReadByte(data, ref offset);
        return unchecked((sbyte)value);
    }

    private static ushort ReadUInt16LE(ReadOnlySpan<byte> data, ref int offset)
    {
        EnsureLength(data, offset, 2);
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    private static ushort ReadUInt16BE(ReadOnlySpan<byte> data, ref int offset)
    {
        EnsureLength(data, offset, 2);
        ushort value = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    private static short ReadInt16BE(ReadOnlySpan<byte> data, ref int offset)
    {
        EnsureLength(data, offset, 2);
        short value = BinaryPrimitives.ReadInt16BigEndian(data.Slice(offset, 2));
        offset += 2;
        return value;
    }

    private static void EnsureLength(ReadOnlySpan<byte> data, int offset, int length)
    {
        if (offset + length > data.Length)
        {
            throw new InvalidDataException("Unexpected end of WOPL data.");
        }
    }
}

internal static class OplInstrumentDefaults
{
    public static readonly OplOperatorPatch DefaultModulator = new OplOperatorPatch(
        amVibEgtKsrMult: 0x21,
        kslTl: 0x20,
        arDr: 0xF3,
        slRr: 0xF5,
        waveform: 0x00);

    public static readonly OplOperatorPatch DefaultCarrier = new OplOperatorPatch(
        amVibEgtKsrMult: 0x01,
        kslTl: 0x00,
        arDr: 0xF3,
        slRr: 0xF5,
        waveform: 0x00);

    public static readonly OplInstrument DefaultInstrument = new OplInstrument(
        name: "Default",
        noteOffset1: 0,
        noteOffset2: 0,
        midiVelocityOffset: 0,
        secondVoiceDetune: 0,
        percussionKeyNumber: 0,
        flags: OplInstrumentFlags.None,
        feedbackConnection1: 0x04,
        feedbackConnection2: 0x00,
        operators: new[]
        {
            DefaultCarrier,
            DefaultModulator,
            DefaultCarrier,
            DefaultModulator
        },
        delayOnMs: 0,
        delayOffMs: 0);
}
