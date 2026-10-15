# Audio (GameCube)

`Dolphin.Audio` provides a native AESND (libogc2) implementation with up to
32 simultaneous PCM voices, including independent volume, pitch, clip looping,
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
via the automatic async scheduler. Use `Audio.Initialize()` after initializing
`Dolphin.Memory.Aram` if ARAM is used, since DSP and ARAM resources must
be coordinated. Mount storage before loading.

## Supported audio formats and limits

- RIFF/WAVE, uncompressed PCM, 8-bit unsigned or 16-bit signed,
  mono or stereo, 8000–48000 Hz; other formats are rejected.
- Native-aligned memory per clip, maximum 16 MiB WAV PCM.
  Loaded sounds are independent of managed garbage collection.
- Native clip slots: 16. Hardware playback slots: up to 32 total.
- Volume is 0–1, pitch is 0.25–3 times the source sampling rate
  (maximum hardware rate 144 kHz). Each playback has its own controls.
- `Sound.Dispose()` stops the sound's currently playing instances before
  releasing native PCM memory. Channels are invalidated upon finish/stop,
  and a stale channel cannot take control of a newly allocated voice.
- `Music` wraps a single `Sound` for a single looping/stoppable track.
  **Music currently preloads the entire WAV. Streaming from disk and MP3/OGG
  decoding are not yet implemented.** Short tracks within the native memory
  budget work; long background music requires a future stream reader.
- `System.Media` and `Dolphin.Sound` are not mapped; the bundler rejects
  those unsupported APIs.
- The audio module targets libogc2 AESND on GameCube; standalone Wii builds
  remain experimental, and Wii U needs a separate native backend.

GameCube applications and bundles link `-laesnd` before `-logc`. There is
no desktop runtime for sound; host tests inject a fake DSP backend.
