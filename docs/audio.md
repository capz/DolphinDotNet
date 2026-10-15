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
