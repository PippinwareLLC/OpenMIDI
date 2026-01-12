# OpenMIDI

OpenMIDI is a pure C# MIDI loader and playback path that targets classic OPL
(Adlib/Sound Blaster) synthesis. The current synth is a placeholder oscillator
to validate the MIDI timing and PCM output pipeline while the OPL core is built.

## Status
- MIDI parsing and event scheduling are implemented.
- A placeholder synth produces PCM output for validation.
- An SDL example host plays a bundled sample MIDI file.

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
| Chip clock/timer constants and sample rate conversion | [ ] | [ ] | [ ] |
| Register map with write side-effects | [ ] | [ ] | [ ] |
| Operator envelopes (attack/decay/sustain/release) | [ ] | [ ] | [ ] |
| Waveform selection | [ ] | [ ] | [ ] |
| Tremolo/vibrato LFOs | [ ] | [ ] | [ ] |
| 2-operator algorithms and feedback | [ ] | [ ] | [ ] |
| Rhythm mode (BD/SD/TT/CY/HH) | [ ] | [ ] | [ ] |
| 4-operator channel pairing | [ ] | [ ] | [ ] |
| Stereo output routing | [ ] | [ ] | [ ] |
| MIDI instrument bank mapping | [ ] | [ ] | [ ] |
| MIDI percussion mapping | [ ] | [ ] | [ ] |
| Pitch bend and controller mapping | [ ] | [ ] | [ ] |
| Register-level verification vs reference core | [ ] | [ ] | [ ] |

## License
MIT. See `OpenMIDI/LICENSE`.
