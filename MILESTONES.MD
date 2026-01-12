# OpenMIDI OPL Parity Milestones

Parity target: Adlib (OPL2) and Sound Blaster (OPL2/OPL3) behavior.

## Foundations
- [x] Define chip clock/timer constants for OPL1/OPL2/OPL3 and sample rate conversion.
- [x] Implement full OPL register maps with correct bit masking and write side-effects.
- [x] Implement OPL timer A/B counters with IRQ status tracking.
- [x] Drive operator/channel state from OPL register writes.
- [ ] Add deterministic reset/power-on state and register defaults.

## Operator Engine
- [x] Oscillator core with phase accumulation and waveform selection.
- [x] Envelope generator with attack/decay/sustain/release rate tables.
- [x] Key scaling level (KSL) and key scaling rate (KSR).
- [x] Tremolo and vibrato LFOs with depth/rate control.

## Channel Algorithms
- [x] 2-operator algorithms (FM/AM) with feedback.
- [ ] Rhythm mode (BD/SD/TT/CY/HH) with proper operator routing.
- [ ] OPL3 4-operator channel pairing and algorithm selection.
- [x] OPL3 stereo output routing (left/right enable per channel).

## MIDI Mapping
- [ ] Instrument bank format for OPL patches (operators + algorithm + feedback).
- [ ] Default GM/Adlib-style patch bank for MIDI program changes.
- [ ] Channel 10 percussion mapping to OPL rhythm mode.
- [ ] Controller mapping (volume, expression, pan, mod wheel, sustain).
- [ ] Pitch bend and optional portamento/glide behavior.

## Output + Mixing
- [ ] Accurate output scaling, clipping, and DC offset handling.
- [ ] Optional oversampling/low-pass to match reference hardware output.

## Verification
- [x] Envelope rate table validation against ymfm reference data.
- [ ] Register-level golden tests against a reference emulator (eg. Nuked OPL3).
- [ ] MIDI playback parity tests using known reference recordings.
- [ ] Performance validation for real-time playback at 44.1/48 kHz.
