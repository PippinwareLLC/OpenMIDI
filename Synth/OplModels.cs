using System;

namespace OpenMIDI.Synth;

public enum OplVolumeModel : byte
{
    Generic = 0,
    Native = 1,
    Dmx = 2,
    Apogee = 3,
    Win9x = 4,
    DmxFixed = 5,
    ApogeeFixed = 6,
    Ail = 7,
    Win9xGenericFm = 8,
    Hmi = 9,
    HmiOld = 10,
    MsAdlib = 11,
    ImfCreator = 12,
    Oconnell = 13,
    Rsxx = 14
}

internal enum OplVoiceMode
{
    TwoOpFm = 0,
    TwoOpAm = 1,
    FourOp1_2FmFm = 2,
    FourOp1_2AmFm = 3,
    FourOp1_2FmAm = 4,
    FourOp1_2AmAm = 5,
    FourOp3_4FmFm = 6,
    FourOp3_4AmFm = 7,
    FourOp3_4FmAm = 8,
    FourOp3_4AmAm = 9
}

internal struct OplVolumeContext
{
    public byte Velocity;
    public byte ChannelVolume;
    public byte ChannelExpression;
    public byte MasterVolume;
    public OplVoiceMode VoiceMode;
    public byte FeedbackConnection;
    public byte TlMod;
    public byte TlCar;
    public bool DoMod;
    public bool DoCar;
    public bool IsDrum;
}

internal static partial class OplModels
{
    private const double ExpTableDivisor = 0.0169268578;
    private const double MaxToneValue = 30.823808;
    private const int DmxMaxFreqIndex = 283;
    private const int MsAdlibNrStepPitch = 25;
    private const int MsAdlibPrange = 50;
    private const ushort OConnellDrumBoost = 32;

    internal static ushort ComputeTone(OplVolumeModel model, double tone, out int mulOffset)
    {
        return model switch
        {
            OplVolumeModel.Dmx => DmxFrequency(tone, out mulOffset),
            OplVolumeModel.DmxFixed => DmxFrequency(tone, out mulOffset),
            OplVolumeModel.Apogee => ApogeeFrequency(tone, out mulOffset),
            OplVolumeModel.ApogeeFixed => ApogeeFrequency(tone, out mulOffset),
            OplVolumeModel.Win9x => Win9xFrequency(tone, out mulOffset),
            OplVolumeModel.Win9xGenericFm => Win9xFrequency(tone, out mulOffset),
            OplVolumeModel.Hmi => HmiFrequency(tone, out mulOffset),
            OplVolumeModel.HmiOld => HmiFrequency(tone, out mulOffset),
            OplVolumeModel.ImfCreator => HmiFrequency(tone, out mulOffset),
            OplVolumeModel.Ail => AilFrequency(tone, out mulOffset),
            OplVolumeModel.MsAdlib => MsAdlibFrequency(tone, out mulOffset),
            OplVolumeModel.Oconnell => OConnellFrequency(tone, out mulOffset),
            _ => GenericFrequency(tone, out mulOffset)
        };
    }

    internal static void ApplyVolumeModel(OplVolumeModel model, ref OplVolumeContext v)
    {
        switch (model)
        {
            case OplVolumeModel.Native:
                NativeVolume(ref v);
                break;
            case OplVolumeModel.Dmx:
                DmxOriginalVolume(ref v);
                break;
            case OplVolumeModel.DmxFixed:
                DmxFixedVolume(ref v);
                break;
            case OplVolumeModel.Apogee:
                ApogeeOriginalVolume(ref v);
                break;
            case OplVolumeModel.ApogeeFixed:
                ApogeeFixedVolume(ref v);
                break;
            case OplVolumeModel.Win9x:
                ApplyWin9xSb16Volume(ref v);
                break;
            case OplVolumeModel.Win9xGenericFm:
                ApplyWin9xGenericVolume(ref v);
                break;
            case OplVolumeModel.Hmi:
                HmiSosNewVolume(ref v);
                break;
            case OplVolumeModel.HmiOld:
                HmiSosOldVolume(ref v);
                break;
            case OplVolumeModel.MsAdlib:
                MsAdlibVolume(ref v);
                break;
            case OplVolumeModel.ImfCreator:
                DmxFixedVolume(ref v);
                break;
            case OplVolumeModel.Ail:
                AilVolume(ref v);
                break;
            case OplVolumeModel.Oconnell:
                OConnellVolume(ref v);
                break;
            case OplVolumeModel.Rsxx:
                RsxxVolume(ref v);
                break;
            default:
                GenericVolume(ref v);
                break;
        }
    }

    internal static byte MapBrightness(byte brightness)
    {
        return XgBrightness[brightness & 0x7F];
    }

    internal static void ApplyBrightness(byte brightness, ref OplVolumeContext v)
    {
        if (brightness >= 127 || v.IsDrum)
        {
            return;
        }

        int scaled = MapBrightness(brightness);
        if (!v.DoMod)
        {
            v.TlMod = (byte)(63 - scaled + (scaled * v.TlMod) / 63);
        }

        if (!v.DoCar)
        {
            v.TlCar = (byte)(63 - scaled + (scaled * v.TlCar) / 63);
        }
    }

    private static ushort GenericFrequency(double tone, out int mulOffset)
    {
        mulOffset = 0;

        if (tone < 0.0)
        {
            tone = 0.0;
        }

        int octave = 0;
        while (tone > MaxToneValue)
        {
            tone -= 12.0;
            octave++;
        }

        int index = (int)(tone / ExpTableDivisor);
        if (index >= GenericExpTable.Length)
        {
            index = GenericExpTable.Length - 1;
        }

        int freq = GenericExpTable[index];

        while (octave > 7)
        {
            mulOffset++;
            octave--;
        }

        return (ushort)(freq | (octave << 10));
    }

    private static ushort DmxFrequency(double tone, out int mulOffset)
    {
        mulOffset = 0;
        if (tone >= 12)
        {
            tone -= 12;
        }

        uint note = (uint)tone;
        double bendDec = tone - (int)tone;

        if (bendDec > 0.5)
        {
            note += 1;
            bendDec -= 1.0;
        }

        int bend = (int)((bendDec * 128.0) / 2.0) + 128;
        bend >>= 1;

        int oct = 0;
        int freqIndex = (int)(note << 5) + bend;

        if (freqIndex < 0)
        {
            freqIndex = 0;
        }
        else if (freqIndex >= DmxMaxFreqIndex)
        {
            freqIndex -= DmxMaxFreqIndex;
            oct = freqIndex / 384;
            freqIndex = (freqIndex % 384) + DmxMaxFreqIndex;
        }

        int outHz = DmxFreqTable[freqIndex];

        while (oct > 7)
        {
            mulOffset++;
            oct--;
        }

        return (ushort)(outHz | (oct << 10));
    }

    private static ushort ApogeeFrequency(double tone, out int mulOffset)
    {
        mulOffset = 0;
        uint note = (uint)(tone >= 12 ? tone - 12 : tone);
        double bendDec = tone - (int)tone;

        if (bendDec > 0.5)
        {
            note += 1;
            bendDec -= 1.0;
        }

        int bend = (int)(bendDec * 32) + 32;
        note += (uint)(bend / 32);
        note -= 1;

        int scaleNote = (int)(note % 12);
        int octave = (int)(note / 12);
        int outHz = ApogeeFreqTable[bend % 32, scaleNote];

        while (octave > 7)
        {
            mulOffset++;
            octave--;
        }

        return (ushort)(outHz | (octave << 10));
    }

    private static ushort Win9xFrequency(double tone, out int mulOffset)
    {
        mulOffset = 0;
        uint note = (uint)(tone >= 12 ? tone - 12 : tone);
        double bendDec = tone - (int)tone;

        if (bendDec > 0.5)
        {
            note += 1;
            bendDec -= 1.0;
        }

        int bend = (int)(bendDec * 4096) + 8192;
        int bendMsb = (bend >> 7) & 0x7F;
        int bendLsb = bend & 0x7F;

        bend = (bendMsb << 9) | (bendLsb << 2);
        bend = (short)(ushort)(bend + 0x8000);

        int octave = (int)(note / 12);
        uint freq = Win9xFreqTable[note % 12];
        if (octave < 5)
        {
            freq >>= 5 - octave;
        }
        else if (octave > 5)
        {
            freq <<= octave - 5;
        }

        uint pitched = ApplyWin9xPitch(freq, bend);
        pitched *= 2;

        int block = 1;
        while (pitched > 0x3FF)
        {
            pitched /= 2;
            block++;
        }

        while (block > 7)
        {
            mulOffset++;
            block--;
        }

        return (ushort)(pitched | ((uint)block << 10));
    }

    private static uint ApplyWin9xPitch(uint freq, int pitch)
    {
        int diff;

        if (pitch > 0)
        {
            diff = (pitch * 31) >> 8;
            freq += (uint)((diff * (int)freq) >> 15);
        }
        else if (pitch < 0)
        {
            diff = (-pitch * 27) >> 8;
            freq -= (uint)((diff * (int)freq) >> 15);
        }

        return freq;
    }

    private static int ClampRangeFix(int value, int max)
    {
        if (value < 0)
        {
            return 0;
        }

        return value >= max ? max - 1 : value;
    }

    private static ushort HmiFrequency(double tone, out int mulOffset)
    {
        mulOffset = 0;
        int note = (int)tone;
        double bendDec = tone - (int)tone;

        if (bendDec > 0.5)
        {
            note += 1;
            bendDec -= 1.0;
        }

        int bend = (int)(bendDec * 64.0) + 64;
        int octaveOffset = 0;

        while (note < 12)
        {
            octaveOffset--;
            note += 12;
        }

        while (note > 114)
        {
            octaveOffset++;
            note -= 12;
        }

        uint inFreq = bend == 64 ? HmiFreqTable[note - 12] : HmiBendCalc((uint)bend, note);

        int freq = (int)(inFreq & 0x3FF);
        int octave = (int)((inFreq >> 10) & 0x07);
        octave += octaveOffset;

        if (octave < 0)
        {
            octave = 0;
        }

        while (octave > 7)
        {
            mulOffset++;
            octave--;
        }

        return (ushort)(freq | (octave << 10));
    }

    private static uint HmiBendCalc(uint bend, int note)
    {
        const int midiBendRange = 1;
        note -= 12;
        int noteMod12 = note % 12;
        if (noteMod12 < 0)
        {
            noteMod12 += 12;
        }

        uint outFreq = HmiFreqTable[note];
        uint fmOctave = outFreq & 0x1C00;
        uint fmFreq = outFreq & 0x03FF;

        if (bend < 64)
        {
            uint bendFactor = ((63 - bend) * 1000) >> 6;
            int idx = ClampRangeFix(note - midiBendRange, HmiFreqTable.Length);
            uint newFreq = outFreq - HmiFreqTable[idx];

            if (newFreq > 719)
            {
                newFreq = fmFreq - HmiBendTable[midiBendRange - 1];
                newFreq &= 0x03FF;
            }

            newFreq = (newFreq * bendFactor) / 1000;
            outFreq -= newFreq;
        }
        else
        {
            uint bendFactor = ((bend - 64) * 1000) >> 6;
            int idx = ClampRangeFix(note + midiBendRange, HmiFreqTable.Length);
            uint newFreq = HmiFreqTable[idx] - outFreq;

            if (newFreq > 719)
            {
                idx = ClampRangeFix(11 - noteMod12, HmiBendTable.Length);
                fmFreq = HmiBendTable[idx];
                outFreq = (fmOctave + 1024) | fmFreq;
                idx = ClampRangeFix(note + midiBendRange, HmiFreqTable.Length);
                newFreq = HmiFreqTable[idx] - outFreq;
            }

            newFreq = (newFreq * bendFactor) / 1000;
            outFreq += newFreq;
        }

        return outFreq;
    }

    private static ushort MsAdlibFrequency(double tone, out int mulOffset)
    {
        mulOffset = 0;
        int note = (int)tone;
        double bendDec = tone - (int)tone;

        if (bendDec > 0.5)
        {
            note += 1;
            bendDec -= 1.0;
        }

        ushort bend = (ushort)((bendDec * 4096) + 8192);

        if (note < 12)
        {
            note = 0;
        }
        else
        {
            note -= 12;
        }

        int diff = (short)(bend - 0x2000);
        int dwSigned = diff * MsAdlibPrange;
        uint dw = (uint)dwSigned;

        ushort hiword = (ushort)((dw >> 16) & 0xFFFF);
        ushort loword = (ushort)(dw & 0xFFFF);
        byte lowByte = (byte)(hiword & 0xFF);
        byte hiByte = (byte)((loword >> 8) & 0xFF);
        short t1 = (short)((lowByte << 8) | hiByte);
        t1 = (short)(t1 >> 5);

        int halfToneOffset;
        int delta;

        if (t1 < 0)
        {
            int t2 = MsAdlibNrStepPitch - 1 - t1;
            halfToneOffset = -(t2 / MsAdlibNrStepPitch);
            delta = (t2 - MsAdlibNrStepPitch + 1) % MsAdlibNrStepPitch;
            if (delta != 0)
            {
                delta = MsAdlibNrStepPitch - delta;
            }
        }
        else
        {
            halfToneOffset = t1 / MsAdlibNrStepPitch;
            delta = t1 % MsAdlibNrStepPitch;
        }

        note += halfToneOffset;
        int noteIndex = note % 12;
        if (noteIndex < 0)
        {
            noteIndex += 12;
        }

        ushort freq = MsAdlibFreqTable[delta, noteIndex];
        int octave = note / 12;

        while (octave > 7)
        {
            mulOffset++;
            octave--;
        }

        return (ushort)(freq | (octave << 10));
    }

    private static ushort AilFrequency(double tone, out int mulOffset)
    {
        mulOffset = 0;
        int note = (int)tone;
        double bendDec = tone - (int)tone;

        if (bendDec > 0.5)
        {
            note += 1;
            bendDec -= 1.0;
        }

        int pitch = (int)(bendDec * 4096) + 8192;
        pitch = ((pitch - 0x2000) / 0x20) * 2;

        note -= 12;
        int octaveOffset = 0;

        while (note < 0)
        {
            octaveOffset--;
            note += 12;
        }

        while (note > 95)
        {
            octaveOffset++;
            note -= 12;
        }

        pitch += ((note & 0xFF) << 8) + 8;
        pitch /= 16;

        while (pitch < 12 * 16)
        {
            pitch += 12 * 16;
        }

        while (pitch > 96 * 16 - 1)
        {
            pitch -= 12 * 16;
        }

        int pitchIndex = pitch >> 4;
        ushort halftones = (ushort)((AilNoteHalftone[pitchIndex] << 4) + (pitch & 0x0F));
        ushort freq = AilFreqTable[halftones];
        int octave = AilNoteOctave[pitchIndex];

        if ((freq & 0x8000) == 0)
        {
            if (octave > 0)
            {
                octave--;
            }
            else
            {
                freq = (ushort)(freq / 2);
            }
        }

        freq &= 0x03FF;
        octave += octaveOffset;

        while (octave > 7)
        {
            mulOffset++;
            octave--;
        }

        return (ushort)(freq | (octave << 10));
    }

    private static ushort OConnellFrequency(double tone, out int mulOffset)
    {
        mulOffset = 0;
        uint note = (uint)tone;
        double bendDec = tone - (int)tone;

        if (bendDec > 0.5)
        {
            note += 1;
            bendDec -= 1.0;
        }

        int pitch = (int)(bendDec * 4096) + 8192;

        int octave = (int)(note / 12);
        while (octave > 7)
        {
            mulOffset++;
            octave--;
        }

        if (octave > 0)
        {
            octave--;
        }

        ushort freq = OConnellMasterFreqs[note % 12];

        const int bendRange = 2;
        if (pitch > 0x2000)
        {
            uint amount = (uint)(pitch - 0x2000);
            uint idx = (note + bendRange) % 12;
            ushort newFreq = OConnellMasterFreqs[idx];

            if (newFreq <= freq)
            {
                newFreq = (ushort)(newFreq << 1);
            }

            long diff = (long)(newFreq - freq) * amount;
            newFreq = (ushort)((diff >> 13) + freq);

            while (newFreq > 0x3FF)
            {
                if (octave < 7)
                {
                    octave++;
                }
                else
                {
                    mulOffset++;
                }

                newFreq = (ushort)(newFreq >> 1);
            }

            freq = newFreq;
        }
        else if (pitch < 0x2000)
        {
            uint amount = (uint)(0x2000 - pitch);
            uint idx = note > bendRange ? (note - bendRange) % 12 : 0;
            ushort newFreq = OConnellMasterFreqs[idx];

            if (newFreq >= freq)
            {
                newFreq = (ushort)(newFreq >> 1);
            }

            long diff = (long)(freq - newFreq) * amount;
            newFreq = (ushort)(freq - (diff >> 13));

            while (newFreq < OConnellMasterFreqs[0])
            {
                if (octave > 0)
                {
                    octave--;
                    newFreq = (ushort)(newFreq << 1);
                }
                else
                {
                    newFreq = OConnellMasterFreqs[0];
                }
            }

            freq = newFreq;
        }

        return (ushort)(freq | (octave << 10));
    }

    private static void GenericVolume(ref OplVolumeContext v)
    {
        const double c1 = 11.541560327111707;
        const double c2 = 1.601379199767093e+02;
        const uint minVolume = 1108075;

        uint volume = (uint)(v.Velocity * v.MasterVolume * v.ChannelVolume * v.ChannelExpression);

        if (volume > minVolume)
        {
            double lv = Math.Log(volume);
            volume = (uint)(lv * c1 - c2);
            if (volume > 63)
            {
                volume = 63;
            }
        }
        else
        {
            volume = 0;
        }

        if (v.DoMod)
        {
            v.TlMod = (byte)(63 - volume + (volume * v.TlMod) / 63);
        }

        if (v.DoCar)
        {
            v.TlCar = (byte)(63 - volume + (volume * v.TlCar) / 63);
        }
    }

    private static void NativeVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.Velocity * v.ChannelVolume * v.ChannelExpression);
        volume = (volume * v.MasterVolume) / 4096766;

        if (volume > 63)
        {
            volume = 63;
        }

        if (v.DoMod)
        {
            v.TlMod = (byte)(63 - volume + (volume * v.TlMod) / 63);
        }

        if (v.DoCar)
        {
            v.TlCar = (byte)(63 - volume + (volume * v.TlCar) / 63);
        }
    }

    private static void RsxxVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.Velocity * v.ChannelVolume * v.ChannelExpression);
        volume = (volume * v.MasterVolume) / 4096766;

        if (volume > 63)
        {
            volume = 63;
        }

        v.TlCar = (byte)(v.TlCar - volume / 2);
    }

    private static void DmxOriginalVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.ChannelVolume * v.ChannelExpression * v.MasterVolume) / 16129;

        if (volume > 127)
        {
            volume = 127;
        }

        volume = (DmxVolumeModel[volume] + 1) << 1;
        uint vel = v.Velocity < 128 ? v.Velocity : (byte)127;
        volume = (DmxVolumeModel[vel] * volume) >> 9;

        if (volume > 63)
        {
            volume = 63;
        }

        if (v.VoiceMode <= OplVoiceMode.TwoOpFm)
        {
            v.TlCar = (byte)(63 - volume);
            if (v.DoMod && v.TlMod < v.TlCar)
            {
                v.TlMod = v.TlCar;
            }
        }
        else
        {
            if (v.DoMod)
            {
                v.TlMod = (byte)(63 - volume + (volume * v.TlMod) / 63);
            }

            if (v.DoCar)
            {
                v.TlCar = (byte)(63 - volume + (volume * v.TlCar) / 63);
            }
        }
    }

    private static void DmxFixedVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.ChannelVolume * v.ChannelExpression * v.MasterVolume) / 16129;
        volume = (DmxVolumeModel[volume] + 1) << 1;
        uint vel = v.Velocity < 128 ? v.Velocity : (byte)127;
        volume = (DmxVolumeModel[vel] * volume) >> 9;

        if (volume > 63)
        {
            volume = 63;
        }

        if (v.DoMod)
        {
            v.TlMod = (byte)(63 - volume + (volume * v.TlMod) / 63);
        }

        if (v.DoCar)
        {
            v.TlCar = (byte)(63 - volume + (volume * v.TlCar) / 63);
        }
    }

    private static void ApogeeOriginalVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.ChannelVolume * v.ChannelExpression * v.MasterVolume / 16129);

        if (volume > 127)
        {
            volume = 127;
        }

        uint mod = v.TlMod;
        uint car = v.TlCar;

        if (v.DoCar)
        {
            car = 63 - car;
            car *= (uint)(v.Velocity + 0x80);
            car = (volume * car) >> 15;
            car ^= 63u;
            v.TlCar = (byte)car;
        }

        if (v.DoMod)
        {
            uint tmpMod = v.TlCar;
            mod = 63 - mod;
            mod *= (uint)(v.Velocity + 0x80);

            if (v.VoiceMode > OplVoiceMode.TwoOpAm)
            {
                tmpMod = mod;
            }

            mod = (volume * tmpMod) >> 15;
            mod ^= 63u;
            v.TlMod = (byte)mod;
        }
    }

    private static void ApogeeFixedVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.ChannelVolume * v.ChannelExpression * v.MasterVolume / 16129);

        if (volume > 127)
        {
            volume = 127;
        }

        uint mod = v.TlMod;
        uint car = v.TlCar;

        if (v.DoCar)
        {
            car = 63 - car;
            car *= (uint)(v.Velocity + 0x80);
            car = (volume * car) >> 15;
            car ^= 63u;
            v.TlCar = (byte)car;
        }

        if (v.DoMod)
        {
            mod = 63 - mod;
            mod *= (uint)(v.Velocity + 0x80);
            uint tmpMod = mod;
            mod = (volume * tmpMod) >> 15;
            mod ^= 63u;
            v.TlMod = (byte)mod;
        }
    }

    private static void ApplyWin9xGenericVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.ChannelVolume * v.ChannelExpression * v.MasterVolume) / 16129;
        volume = Win9xGenericFmVolume[volume >> 2];

        if (v.DoCar)
        {
            uint car = (uint)(v.TlCar + volume + Win9xGenericFmVolume[v.Velocity >> 2]);
            if (car > 0x3F)
            {
                car = 0x3F;
            }

            v.TlCar = (byte)car;
        }

        if (v.DoMod)
        {
            uint mod = (uint)(v.TlMod + volume + Win9xGenericFmVolume[v.Velocity >> 2]);
            if (mod > 0x3F)
            {
                mod = 0x3F;
            }

            v.TlMod = (byte)mod;
        }
    }

    private static void ApplyWin9xSb16Volume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.ChannelVolume * v.ChannelExpression * v.MasterVolume) / 16129;
        volume = Win9xSb16Volume[volume >> 2];

        if (v.DoCar)
        {
            uint car = (uint)(v.TlCar + volume + Win9xSb16Volume[v.Velocity >> 2]);
            if (car > 0x3F)
            {
                car = 0x3F;
            }

            v.TlCar = (byte)car;
        }

        if (v.DoMod)
        {
            uint mod = (uint)(v.TlMod + volume + Win9xSb16Volume[v.Velocity >> 2]);
            if (mod > 0x3F)
            {
                mod = 0x3F;
            }

            v.TlMod = (byte)mod;
        }
    }

    private static void AilVolume(ref OplVolumeContext v)
    {
        uint midiVolume = (uint)(v.ChannelVolume * v.ChannelExpression) * 2;
        midiVolume >>= 8;

        if (midiVolume != 0)
        {
            midiVolume++;
        }

        int velIndex = (v.Velocity & 0x7F) >> 3;
        uint vel = AilVelocityGraph[velIndex];

        midiVolume = (midiVolume * vel) * 2;
        midiVolume >>= 8;
        if (midiVolume != 0)
        {
            midiVolume++;
        }

        if (v.MasterVolume < 127)
        {
            midiVolume = (midiVolume * v.MasterVolume) / 127;
        }

        if (midiVolume > 127)
        {
            midiVolume = 127;
        }

        uint mod = (uint)(~v.TlMod) & 0x3F;
        uint car = (uint)(~v.TlCar) & 0x3F;

        if (v.DoMod)
        {
            mod = (mod * midiVolume) / 127;
        }

        if (v.DoCar)
        {
            car = (car * midiVolume) / 127;
        }

        v.TlMod = (byte)(~mod & 0x3F);
        v.TlCar = (byte)(~car & 0x3F);
    }

    private static void HmiSosOldVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.ChannelVolume * v.ChannelExpression * v.MasterVolume) / 16129;
        volume = (((volume * 128) / 127) * v.Velocity) >> 7;

        if (volume > 127)
        {
            volume = 127;
        }

        volume = HmiVolumeTable[volume >> 1];

        if (v.FeedbackConnection == 0 && !v.IsDrum)
        {
            uint outVol = (uint)(v.ChannelVolume * v.ChannelExpression * 64) / 16129;
            outVol = (((outVol * 128) / 127) * v.Velocity) >> 7;
            outVol = HmiVolumeTable[outVol >> 1];
            outVol = (64 - outVol) << 1;
            outVol *= (uint)(64 - v.TlCar);
            v.TlMod = (byte)((8192 - outVol) >> 7);
        }

        uint finalVol = v.IsDrum ? (uint)((64 - HmiVolumeTable[v.Velocity >> 1]) << 1) : (uint)((64 - volume) << 1);
        finalVol *= (uint)(64 - v.TlCar);
        v.TlCar = (byte)((8192 - finalVol) >> 7);
    }

    private static void HmiSosNewVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.ChannelVolume * v.ChannelExpression * v.MasterVolume) / 16129;
        volume = (((volume * 128) / 127) * v.Velocity) >> 7;

        if (volume > 127)
        {
            volume = 127;
        }

        volume = HmiVolumeTable[volume >> 1];

        if (v.DoMod)
        {
            uint outVol = (uint)((64 - volume) << 1);
            outVol *= (uint)(64 - v.TlMod);
            v.TlMod = (byte)((8192 - outVol) >> 7);
        }

        if (v.DoCar)
        {
            uint outVol = (uint)((64 - volume) << 1);
            outVol *= (uint)(64 - v.TlCar);
            v.TlCar = (byte)((8192 - outVol) >> 7);
        }
    }

    private static void MsAdlibVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)(v.ChannelVolume * v.ChannelExpression * v.MasterVolume) / 16129;
        volume = (v.Velocity * volume) / 127;

        if (volume > 127)
        {
            volume = 127;
        }

        volume = MsAdlibVolumeTable[volume];

        if (v.DoMod)
        {
            uint outVol = (uint)(63 - (v.TlMod & 0x3F));
            outVol *= volume;
            outVol += outVol + 0x7F;
            v.TlMod = (byte)(63 - outVol / (2 * 0x7F));
        }

        if (v.DoCar)
        {
            uint outVol = (uint)(63 - (v.TlCar & 0x3F));
            outVol *= volume;
            outVol += outVol + 0x7F;
            v.TlCar = (byte)(63 - outVol / (2 * 0x7F));
        }
    }

    private static void OConnellVolume(ref OplVolumeContext v)
    {
        uint volume = (uint)OConnellVelocity[v.Velocity] * OConnellVelocity[v.ChannelVolume] *
                      OConnellVelocity[v.ChannelExpression] * OConnellVelocity[v.MasterVolume];

        if (v.IsDrum)
        {
            volume >>= 19;
            volume += 2;
        }
        else
        {
            volume >>= 18;
            volume += 3;
        }

        if (v.DoCar)
        {
            uint work = volume * (uint)(63 - v.TlCar);
            work >>= 6;

            if (v.IsDrum)
            {
                work += OConnellDrumBoost;
            }

            if (work > 63)
            {
                work = 63;
            }

            v.TlCar = (byte)(63 - work);
        }

        if (v.DoMod)
        {
            uint work = volume * (uint)(63 - v.TlMod);
            work >>= 6;

            if (v.IsDrum)
            {
                work += OConnellDrumBoost;
            }

            if (work > 63)
            {
                work = 63;
            }

            v.TlMod = (byte)(63 - work);
        }
    }
}
