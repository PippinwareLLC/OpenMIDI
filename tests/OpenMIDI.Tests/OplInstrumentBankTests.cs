using System.Buffers.Binary;
using System.Text;
using OpenMIDI.Synth;

namespace OpenMIDI.Tests;

public sealed class OplInstrumentBankTests
{
    [Fact]
    public void LoadFromBytes_ReadsBankHeadersAndInstruments()
    {
        byte[] data = BuildWoplBank();

        OplInstrumentBankSet bankSet = WoplBankLoader.LoadFromBytes(data);

        Assert.True(bankSet.DeepTremolo);
        Assert.True(bankSet.DeepVibrato);
        Assert.Equal(5, bankSet.VolumeModel);

        OplBank melodic = bankSet.MelodicBanks[0];
        Assert.Equal("MelBank", melodic.Name);
        Assert.Equal(1, melodic.BankMsb);
        Assert.Equal(2, melodic.BankLsb);

        OplInstrument instrument = melodic.Instruments[0];
        Assert.Equal("Inst0", instrument.Name);
        Assert.Equal(3, instrument.NoteOffset1);
        Assert.Equal(-2, instrument.NoteOffset2);
        Assert.Equal(4, instrument.MidiVelocityOffset);
        Assert.Equal(-5, instrument.SecondVoiceDetune);
        Assert.Equal(60, instrument.PercussionKeyNumber);
        Assert.Equal(OplInstrumentFlags.FixedNote, instrument.Flags);
        Assert.Equal(0x07, instrument.FeedbackConnection1);
        Assert.Equal(0x09, instrument.FeedbackConnection2);
        Assert.Equal(0x11, instrument.Operators[0].AmVibEgtKsrMult);
        Assert.Equal(0x22, instrument.Operators[0].KslTl);
        Assert.Equal(0x33, instrument.Operators[0].ArDr);
        Assert.Equal(0x44, instrument.Operators[0].SlRr);
        Assert.Equal(0x55, instrument.Operators[0].Waveform);
        Assert.Equal(0x66, instrument.Operators[1].AmVibEgtKsrMult);
        Assert.Equal(0x77, instrument.Operators[1].KslTl);
        Assert.Equal(0x88, instrument.Operators[1].ArDr);
        Assert.Equal(0x99, instrument.Operators[1].SlRr);
        Assert.Equal(0xAA, instrument.Operators[1].Waveform);
    }

    [Fact]
    public void LoadBank_AppliesInstrumentPatchToRegisters()
    {
        OplOperatorPatch carrier = new OplOperatorPatch(0x11, 0x05, 0x33, 0x44, 0x07);
        OplOperatorPatch modulator = new OplOperatorPatch(0x22, 0x06, 0x55, 0x66, 0x08);
        OplInstrument instrument = new OplInstrument(
            name: "Test",
            noteOffset1: 0,
            noteOffset2: 0,
            midiVelocityOffset: 0,
            secondVoiceDetune: 0,
            percussionKeyNumber: 0,
            flags: OplInstrumentFlags.None,
            feedbackConnection1: 0x05,
            feedbackConnection2: 0x00,
            operators: new[] { carrier, modulator, carrier, modulator },
            delayOnMs: 0,
            delayOffMs: 0);

        OplInstrument[] melodic = new OplInstrument[128];
        OplInstrument[] percussion = new OplInstrument[128];
        for (int i = 0; i < 128; i++)
        {
            melodic[i] = instrument;
            percussion[i] = instrument;
        }

        OplInstrumentBankSet bankSet = new OplInstrumentBankSet(
            new[] { new OplBank("Test", 0, 0, melodic) },
            new[] { new OplBank("Drums", 0, 0, percussion) },
            deepTremolo: true,
            deepVibrato: true,
            volumeModel: 0);

        OplSynth synth = new OplSynth(OplSynthMode.Opl2);
        synth.LoadBank(bankSet);
        synth.ControlChange(0, 7, 127);
        synth.ControlChange(0, 11, 127);
        synth.NoteOn(0, 60, 127);

        OplRegisterMap regs = synth.Core.Registers;
        Assert.True(regs.TremoloDepth);
        Assert.True(regs.VibratoDepth);
        Assert.Equal(0x22, regs.Read(0x20));
        Assert.Equal(0x06, regs.Read(0x40));
        Assert.Equal(0x55, regs.Read(0x60));
        Assert.Equal(0x66, regs.Read(0x80));
        Assert.Equal(0x08, regs.Read(0xE0));
        Assert.Equal(0x11, regs.Read(0x23));
        Assert.Equal(0x05, regs.Read(0x43));
        Assert.Equal(0x33, regs.Read(0x63));
        Assert.Equal(0x44, regs.Read(0x83));
        Assert.Equal(0x07, regs.Read(0xE3));
        Assert.Equal(0x05, regs.Read(0xC0));
    }

    private static byte[] BuildWoplBank()
    {
        List<byte> data = new List<byte>();
        data.AddRange(Encoding.ASCII.GetBytes("WOPL3-BANK\0"));
        WriteUInt16LE(data, 2);
        WriteUInt16BE(data, 1);
        WriteUInt16BE(data, 1);
        data.Add(0x03);
        data.Add(0x05);

        WriteBankHeader(data, "MelBank", 2, 1);
        WriteBankHeader(data, "PercBank", 4, 3);

        for (int i = 0; i < 128; i++)
        {
            if (i == 0)
            {
                WriteInstrument(data, "Inst0");
            }
            else
            {
                WriteInstrument(data, string.Empty);
            }
        }

        for (int i = 0; i < 128; i++)
        {
            WriteInstrument(data, string.Empty);
        }

        return data.ToArray();
    }

    private static void WriteBankHeader(List<byte> data, string name, byte lsb, byte msb)
    {
        WriteFixedString(data, name, 32);
        data.Add(lsb);
        data.Add(msb);
    }

    private static void WriteInstrument(List<byte> data, string name)
    {
        WriteFixedString(data, name, 32);
        WriteInt16BE(data, name.Length == 0 ? (short)0 : (short)3);
        WriteInt16BE(data, name.Length == 0 ? (short)0 : (short)-2);
        data.Add(name.Length == 0 ? (byte)0 : (byte)4);
        data.Add(name.Length == 0 ? (byte)0 : unchecked((byte)-5));
        data.Add(name.Length == 0 ? (byte)0 : (byte)60);
        data.Add(name.Length == 0 ? (byte)0 : (byte)OplInstrumentFlags.FixedNote);
        data.Add(name.Length == 0 ? (byte)0 : (byte)0x07);
        data.Add(name.Length == 0 ? (byte)0 : (byte)0x09);

        if (name.Length == 0)
        {
            for (int i = 0; i < 20; i++)
            {
                data.Add(0);
            }
            return;
        }

        data.AddRange(new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 });
        data.AddRange(new byte[] { 0x66, 0x77, 0x88, 0x99, 0xAA });
        data.AddRange(new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 });
        data.AddRange(new byte[] { 0x60, 0x70, 0x80, 0x90, 0xA0 });
    }

    private static void WriteFixedString(List<byte> data, string value, int length)
    {
        byte[] buffer = new byte[length];
        if (!string.IsNullOrEmpty(value))
        {
            Encoding.ASCII.GetBytes(value, 0, Math.Min(value.Length, length), buffer, 0);
        }

        data.AddRange(buffer);
    }

    private static void WriteUInt16LE(List<byte> data, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(buffer, value);
        data.AddRange(buffer.ToArray());
    }

    private static void WriteUInt16BE(List<byte> data, ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        data.AddRange(buffer.ToArray());
    }

    private static void WriteInt16BE(List<byte> data, short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16BigEndian(buffer, value);
        data.AddRange(buffer.ToArray());
    }
}
