# Interactive audio lab

Place `audio/laser.wav` and `audio/theme.wav` on an inserted GameCube ISO;
optionally provide `audio/theme.adx`. The default reads `dvd:/audio/`. With no
disc the UI still starts and A plays a generated tone, allowing volume/pitch
experiments without external assets.

For an explicitly configured SD Gecko, change `AudioStorage` to
`StorageDevice.SdSlotA` and `AudioRoot` to `"carda:/audio/"`. Physical SD2SP2
uses `StorageDevice.SdSerialPort2` and `"sd:/audio/"`. Do not select an adapter
which is absent or not emulated: its native mount can block. The default avoids
blind SD probes. Set Dolphin's Default ISO to provide disc audio assets.

Music and effects load asynchronously while the UI renders.

- A plays the effect; Start toggles music.
- Up/down changes volume; left/right changes pitch.
- X reloads WAV; Y loads ADX.
- L requests ADX loop samples 8000–24000. Short or unsupported files report an error.
- B cancels pending loads and exits; Z toggles statistics.

Playback handles, music and samples are disposed before storage is unmounted.
Missing media/files produce a visible status rather than preventing the UI from
starting. WAV is the baseline; the explicit compressed loop example requires a
long enough compatible ADX. See [audio](../../docs/audio.md).

