# OpenMIDI

OpenMIDI is a pure C# MIDI loader and playback path that targets classic OPL
(Adlib/Sound Blaster) synthesis. MIDI playback now drives the register-accurate
OPL core render path with WOPL instrument bank support for program-based patch mapping.

## Status
- MIDI parsing and event scheduling are implemented.
- MIDI playback uses the register-accurate OPL core render path.
- An SDL example host plays a bundled sample MIDI file.
- An OPL register map with timer/control/rhythm side-effects is defined.
- The OPL core tracks register writes, timer counters, operator envelopes, and keycode scaling.
- A 2-operator render path now drives phase accumulation, waveform selection, and feedback mixing.
- Envelope rate tables are aligned with the ymfm reference and validated by tests.
- The SDL example renders a live console VU/channel activity view during playback.
- The SDL example includes a per-channel piano roll display.
- Log-sine waveform tables and key scale attenuation are now in use.
- Tremolo and vibrato LFOs are implemented with depth control.
- OPL rhythm mode and percussion channels (BD/SD/TT/CY/HH) are implemented.
- Sustain pedal (CC64) is mapped to hold/release OPL voices.
- Mod wheel (CC1) and aftertouch drive vibrato/tremolo modulation, with RPN pitch-bend range and portamento glide support.
- OplSynth voice allocation prioritizes released notes, uses channel priority and instrument-aware scoring, and treats sustained notes as lower-priority steal candidates.
- MIDI channel 10 percussion is mapped to OPL rhythm flags with reserved rhythm channels.
- WOPL instrument banks can be loaded for program/bank-based patch mapping.
- Reference render clip tests cover the OPL core output.

## Quickstart
Build everything:
```
dotnet build
```

Run the SDL example (requires SDL2.dll on PATH or beside the executable):
```
dotnet run --project OpenMIDI/OpenMIDI.Example
```

Play a specific file:
```
dotnet run --project OpenMIDI/OpenMIDI.Example -- --file path\to\song.mid
```

Play with multiple OPL chips (higher polyphony):
```
dotnet run --project OpenMIDI/OpenMIDI.Example -- --file path\to\song.mid --chips 2
```

Play with a custom WOPL bank:
```
dotnet run --project OpenMIDI/OpenMIDI.Example -- --file path\to\song.mid --bank path\to\bank.wopl
```

Boost output gain (default is 1.0 / chip count):
```
dotnet run --project OpenMIDI/OpenMIDI.Example -- --file path\to\song.mid --gain 1.0
```

## Parity Matrix (Target: Adlib/Sound Blaster)
This matrix tracks parity goals for OPL1/OPL2/OPL3 behavior as implemented in
OpenMIDI. OplSynth currently exposes OPL2/OPL3 playback; OPL1 support is
core-level only for now. Detailed milestones live in `OpenMIDI/MILESTONES.MD`.

| Feature | OPL1 | OPL2 | OPL3 |
| --- | --- | --- | --- |
| Chip clock/timer constants and sample rate conversion | [x] | [x] | [x] |
| Register map with write side-effects | [x] | [x] | [x] |
| Timer A/B counters and IRQ status | [x] | [x] | [x] |
| Operator envelopes (attack/decay/sustain/release) | [x] | [x] | [x] |
| Key scale level/rate | [x] | [x] | [x] |
| Waveform selection | [ ] | [x] | [x] |
| Tremolo/vibrato LFOs | [x] | [x] | [x] |
| 2-operator algorithms and feedback | [x] | [x] | [x] |
| Rhythm mode (BD/SD/TT/CY/HH) | [ ] | [x] | [x] |
| 4-operator channel pairing and algorithms | [ ] | [ ] | [x] |
| Stereo output routing | [ ] | [ ] | [x] |
| Pseudo-4op (dual 2-op voice mode) | [ ] | [x] | [x] |
| MIDI instrument bank mapping (WOPL) | [ ] | [x] | [x] |
| MIDI percussion mapping (channel 10 -> rhythm) | [ ] | [x] | [x] |
| Pitch bend + RPN pitch bend range | [ ] | [x] | [x] |
| CC mapping (volume/expression/pan/sustain/sostenuto/soft/brightness) | [ ] | [x] | [x] |
| Mod wheel + aftertouch vibrato/tremolo | [ ] | [x] | [x] |
| Portamento glide (CC65/5/37) | [ ] | [x] | [x] |
| GM/GS/XG SysEx handling + XG drum banks | [ ] | [x] | [x] |
| Volume model curves (DMX/Apogee/Win9x/HMI/etc) | [ ] | [x] | [x] |
| Envelope rate table validation (ymfm) | [x] | [x] | [x] |
| Reference render clip tests | [ ] | [x] | [x] |
| Register-level verification vs reference core | [ ] | [ ] | [ ] |

## License
MIT. See `OpenMIDI/LICENSE`.
