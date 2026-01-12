# OpenMIDI

OpenMIDI is a pure C# MIDI loader and playback path that targets classic OPL
(Adlib/Sound Blaster) synthesis. MIDI playback now drives the register-accurate
OPL core render path with a default 2-op patch while instrument mapping is expanded.

## Status
- MIDI parsing and event scheduling are implemented.
- MIDI playback uses the register-accurate OPL core render path.
- An SDL example host plays a bundled sample MIDI file.
- An OPL register map with timer/control/rhythm side-effects is defined.
- The OPL core tracks register writes, timer counters, operator envelopes, and keycode scaling.
- A 2-operator render path now drives phase accumulation, waveform selection, and feedback mixing.
- Envelope rate tables are aligned with the ymfm reference and validated by tests.
- The SDL example renders a live console VU/channel activity view during playback.
- Log-sine waveform tables and key scale attenuation are now in use.
- Tremolo and vibrato LFOs are implemented with depth control.
- OPL rhythm mode and percussion channels (BD/SD/TT/CY/HH) are implemented.
- Sustain pedal (CC64) is mapped to hold/release OPL voices.
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

## Parity Matrix (Target: Adlib/Sound Blaster)
This matrix tracks parity goals for OPL1/OPL2/OPL3 behavior. The current synth
is a stub, so these are largely unchecked until the register-accurate core
lands. Detailed milestones live in `OpenMIDI/MILESTONES.MD`.

| Feature | OPL1 | OPL2 | OPL3 |
| --- | --- | --- | --- |
| Chip clock/timer constants and sample rate conversion | [x] | [x] | [x] |
| Register map with write side-effects | [x] | [x] | [x] |
| Timer A/B counters and IRQ status | [x] | [x] | [x] |
| Operator envelopes (attack/decay/sustain/release) | [x] | [x] | [x] |
| Key scale level/rate | [x] | [x] | [x] |
| Waveform selection | [x] | [x] | [x] |
| Tremolo/vibrato LFOs | [x] | [x] | [x] |
| 2-operator algorithms and feedback | [x] | [x] | [x] |
| Rhythm mode (BD/SD/TT/CY/HH) | [ ] | [x] | [x] |
| 4-operator channel pairing | [ ] | [ ] | [ ] |
| Stereo output routing | [ ] | [ ] | [x] |
| MIDI instrument bank mapping | [ ] | [ ] | [ ] |
| MIDI percussion mapping | [ ] | [ ] | [ ] |
| Pitch bend and controller mapping (volume/expression/pan/sustain) | [x] | [x] | [x] |
| Mod wheel/vibrato controller mapping | [ ] | [ ] | [ ] |
| Envelope rate table validation (ymfm) | [x] | [x] | [x] |
| Register-level verification vs reference core | [ ] | [ ] | [ ] |

## License
MIT. See `OpenMIDI/LICENSE`.
