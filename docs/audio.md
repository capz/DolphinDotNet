# Audio (GameCube)

`Dolphin.Audio` provides a native AESND (libogc2) implementation with up to
32 simultaneous PCM output voices, including independent volume, pitch, clip looping,
and explicit stop/dispose. No custom application-side audio pump is required:
DSP processing runs independently of managed continuations.

## Example

```csharp
using Dolphin.Audio;
using Dolphin.Storage;

Storage.Mount(StorageDevice.SdSerialPort2);
Audio.Initialize();

using var effect = await Sound.LoadAsync("sd:/audio/click.wav");
using var music = Music.Load("sd:/audio/theme.wav");
music.Volume = 0.5f;
music.Play(loop: true);

using var playback = effect.Play();
playback.Volume = 0.8f;
playback.Pitch = 1.2f;
```

`Sound.Load` is synchronous; `Sound.LoadAsync` reads in 4096-byte slices
on the automatic background executor. Use `Audio.Initialize()` after initializing
`Dolphin.Memory.Aram` if ARAM is used, since DSP and ARAM resources must
be coordinated. Mount storage before loading.

`Load` and `LoadAsync` detect WAV, ADX, AFS, HPS, AST, RWS and AWD by signature.
Use `AudioFormat` for headerless or paired formats, for example
`Sound.LoadAsync(path, AudioFormat.Dsp)` or `AudioFormat.JAudioWaveBank`
with `dataPath`. `AudioFormat.Wav` explicitly selects PCM WAV. The default native
budget is 8 MiB, configurable up to 16 MiB; WAV budgets bound aligned PCM storage.

For ADX and other supported source codecs, use
`sound.PlayLoop(startSample, endSample)` or `music.PlayLoop(startSample, endSample)`
to play the intro and then repeat a custom range. Indices count sample frames per
channel; the end is exclusive. ADX ranges can start or end inside a compressed
frame. `Play(loop: true)` uses embedded loop points instead. See
[loop range details](gamecube-audio-formats.md) for validation and format limits.

## Self-contained generated sounds

An application can synthesize its own unsigned mono PCM8 and load it without
mounting a filesystem:

```csharp
var wave = new byte[2205]; // 100 ms at 22050 Hz
for (int i=0;i<wave.Length;i++)
    wave[i] = (byte)((i%32<16)?190:66);
using var beep = Sound.FromPcm8(wave,22050);
beep.Play();
```

`FromPcm8` copies the bytes into 32-byte-aligned native memory, so the managed
array can be collected or changed without affecting playback. Samples are
unsigned, with silence at 128, and require an 8000–48000 Hz sample rate.
See the [Pong sample](../samples/Pong/README.md) for three procedurally
generated, enveloped effects in a standalone DOL.

## Supported audio formats and limits

- RIFF/WAVE, uncompressed PCM, 8-bit unsigned or 16-bit signed,
  mono or stereo, 8000–48000 Hz; other WAV encodings are rejected.
  The same loading methods handle supported native codecs and banks.
- Native-aligned memory per clip, maximum 16 MiB WAV PCM.
  Loaded sounds are independent of managed garbage collection.
- Native clip slots: 16. Hardware playback slots: up to 32 total.
- Volume is 0–1, pitch is 0.25–3 times the source sampling rate
  (maximum hardware rate 144 kHz). Each playback has its own controls.
- `Sound.Dispose()` stops the sound's currently playing instances before
  releasing native PCM memory. Channels are invalidated upon finish/stop,
  and a stale channel cannot take control of a newly allocated voice.
- `Music` wraps a single `Sound` for a single looping/stoppable track.
  WAV music preloads PCM. `Music.LoadAsync` retains supported GameCube
  audio in compressed native memory and decodes bounded playback buffers.
  Disk streaming and MP3/Ogg/AHX decoding are not implemented. See
  [GameCube game audio formats](gamecube-audio-formats.md) for codecs, banks,
  loop regions, budgets and per-game coverage.
- `System.Media` and `Dolphin.Sound` are not mapped; the bundler rejects
  those unsupported APIs.
- GameCube bundles without reachable networking APIs skip unnecessary native
network initialization on boot, so standalone audio games need no network.

The audio module targets libogc2 AESND on GameCube; standalone Wii builds
  remain experimental, and Wii U needs a separate native backend.

GameCube applications and bundles link `-laesnd` before `-logc`. There is
no desktop runtime for sound; host tests inject a fake DSP backend.

## MP3 to ADX with named loop sidecars

The host-side asset tool converts audio with FFmpeg (`ffmpeg` on PATH, including
MP3 decoding and the `adpcm_adx` encoder). This does not add ADX playback to the
GameCube runtime; the playback formats listed above still apply.

Keep a UTF-8 JSON sidecar beside the source: `forest.mp3.loops.json`.

```json
{
  "version": 1,
  "defaultLoop": "exploration",
  "loops": [
    { "name": "exploration", "startSeconds": 8.0, "endSeconds": 40.0 },
    { "name": "combat", "startSeconds": 42.0, "endSeconds": 74.0 }
  ]
}
```

```sh
dnd-assets audio forest.mp3 forest.adx
dnd-assets audio forest.mp3 forest-combat.adx --loop combat --sample-rate 48000
# Explicit sidecar and FFmpeg executable paths are also supported:
dnd-assets audio forest.mp3 forest.adx --loops forest.mp3.loops.json --ffmpeg /usr/bin/ffmpeg
```

- Source positions are seconds on the decoded, gapless-trimmed MP3 timeline,
  before codec block padding. No silence detection, trimming or normalization is
  applied. Other FFmpeg-readable sources, including WAV/FLAC, are accepted.
- Output is stereo ADX at 32000 Hz by default; `--sample-rate` accepts 8000–48000.
- Ranges are start-inclusive/end-exclusive. Decimal seconds are rounded to the
  nearest output sample frame, with midpoint ties rounded away from zero.
- Names are nonempty, unique and case-sensitive. Overlapping ranges are allowed.
  Unsupported versions, unknown JSON fields, invalid ranges, missing names and
  positions beyond decoded duration fail conversion before publishing outputs.
- ADX stores one built-in loop. `--loop` overrides `defaultLoop`; omitting both
  produces a nonlooping ADX. The complete track, including intro/outro, is kept.
- `forest.adx.loops.json` preserves every name with resolved `startSample` and
  `endSample`, plus `sampleRate`, `sampleCount` and the selected `defaultLoop`.
  Sample positions count frames, not individual interleaved channel values.
  This is conversion metadata for future runtime support, not a current
  `Music.PlayLoop` API. Unmodified ADX players only see the embedded loop.
- Sample indices are exact; seamless musical joins still require auditioning.
  The encoded blocks are not modified to force matching predictor history at a
  loop boundary. No crossfading or adaptive music transitions are generated.

Encoding is performed by your installed FFmpeg, so record its version alongside
release build tooling when reproducible encoded bytes matter.

### Authoring ranges in Audacity

Open the same source audio in Audacity, select a time range, and add a label at
that selection (Ctrl+B on Windows/Linux). Give each region a unique name. Export
labels as a standard tab-delimited text file, then use it directly:

```sh
dnd-assets audio forest.mp3 forest.adx --loops forest.labels.txt --loop exploration
```

Each exported row is `startSeconds<TAB>endSeconds<TAB>name`. Point labels are
ignored; spectral selection continuation rows are rejected. Text sidecars have
no default-loop field, so specify `--loop` to embed a loop. Keep the labels and
source audio on the same timeline: if you edit, trim or move the audio in
Audacity, export the edited audio (preferably WAV) and author labels against it.
Audacity labels alone do not embed looping instructions into ADX audio.
