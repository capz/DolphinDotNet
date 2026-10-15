# Native GameCube game audio

GameCube games share several codecs but use different containers and sample banks. The new loaders retain compressed bytes in native RAM and decode small PCM buffers for AESND. Files are loaded on the device; no desktop conversion is required.

```csharp
using Dolphin.Audio;

using var music = await Music.LoadGameCubeAsync("sd:/audio/theme.hps");
music.Play(); // honors the file's loop region, if present

using var voice = await Sound.LoadGameCubeAsync(
    "sd:/audio/fighter.ssm", GameCubeAudioFormat.Ssm, entryIndex: 3);
using var playing = voice.Play();

using var wave = await Sound.LoadGameCubeAsync(
    "sd:/audio/JaiInit.aaf", GameCubeAudioFormat.JAudioWaveBank,
    dataPath: "sd:/audio/voice.aw", entryIndex: 0);
```

Mount storage first. Initialize ARAM before audio if both are used. Await continuations retain their executor; voice operations run on the application executor. Loading and validation use the background executor in bounded read/decode slices. Cancellation disposes partially loaded buffers and closes both files. Synchronous `Sound.LoadGameCube` and `Music.LoadGameCube` counterparts are also available.

## Game research and coverage

| Game / family (GameCube only) | Identified audio | Coverage and remaining work |
| --- | --- | --- |
| PSO Episodes I/II | CRI ADX tracks are explicitly named in newserv's reverse-engineered quest and map commands | ADX streams are supported. Adaptive battle scheduling and game-specific effect banks are not implemented. |
| PSO Episode III | Same console and related engine do not establish every audio container's layout | Verified shared ADX/DSP containers can load when identified; Episode III-specific adaptive music and effect-bank packaging remain unverified. No claim of complete Episode III audio support. |
| F-Zero GX | File inventory identifies ADX BGM, AHX voice files, BIN effects and SFD movies | ADX music supported. AHX is a separate MPEG-based codec, not ADX ADPCM; AHX voices, unknown BIN effect banks and SFD demuxing are unsupported. |
| Super Mario Sunshine | AFC streams, JAudio `JaiInit.aaf` WSYS wave descriptions with external AW data; BMS sequences and IBNK instruments | AFC streams and selectable AW waves supported, including AFC 4-bit/2-bit and PCM. BMS/IBNK sequencing, instrument envelopes and the complete game's music engine are unsupported. |
| Resident Evil 4 | Capcom stream blocks: RIT selection, SHD DSP-ADPCM descriptions and SBB data; separate ISS/SYN wavetable/sequence system | Selectable stream-header/data pairs supported. ISS/DLS instruments, MIDI sequencing, movie demuxing and automatically locating headers inside other archives are unsupported. This is Capcom's GameCube format, not later ports. |
| Super Smash Bros. Melee | HAL HPS music and SSM sample banks, using Nintendo DSP ADPCM | HPS linked blocks and embedded loops; mono/stereo SSM entries supported. Game sound-ID lookup/panning/event logic is outside the clip loader. |
| Burnout 2 / RenderWare Audio | RWS 0x809 and 0x80d streams; the latter supports segments and layers | GameCube DSP and supported BE PCM profiles implemented. Segment/layer selection is explicit. AWD GameCube DSP wave dictionaries are also implemented. Automatic adaptive layer mixing is not provided. |
| Burnout 1 | Complete title-specific audio-bank mapping not established by the examined independent readers | Shared verified formats can be loaded explicitly. Do not infer Burnout 1 compatibility from the Burnout 2 RWS profile or later Burnout platforms. |
| Sonic / other RenderWare titles | Engine choice alone does not identify audio; Sega titles can use CRI ADX and AFS | ADX and selected ADX members of AFS are supported. AFS containing another codec is rejected. Other Sonic games/platforms and game-specific bank/sequence variants are not implied. |

## Implemented readers

`GameCubeAudioFormat.Auto` recognizes strong signatures: ADX, AFS, HPS, AST, RWS and GameCube AWD. Choose an explicit format for plain DSP, AFC, SSM, DTK, JAudio wave banks and RE4 pairs. Extensions alone never select a headerless codec.

- **Dsp:** standard big-endian 0x60-byte mono Nintendo DSP header. Decoder coefficients and initial histories are honored. Platform-specific interleaved `.dsp` variants and dual-file stereo are not guessed.
- **Adx / Afs:** unencrypted CRI ADX types 2/3/4, versions 3/4/5; mono/stereo, initial histories and supported loop headers. AFS has a bounded LE directory; `entryIndex` selects one ADX member. Encrypted ADX, AHX, SFD and other CRI codecs are rejected.
- **Afc:** big-endian Nintendo AFC stereo streams, 9-byte channel interleave. JAudio additionally supports mono 5-byte/2-bit AFC, signed PCM8 and BE PCM16 wave entries.
- **Hps:** HALPST linked DSP blocks. Per-block predictor histories are restored. Backlinks must identify an earlier visited block or the current block; invalid cycles/targets fail. The loop state includes cached partial frames, preserving sample-exact loop boundaries.
- **Ssm:** bounded HAL bank table, per-channel DSP coefficients/histories and nibble offsets. `entryIndex` is zero-based.
- **Ast:** GameCube BE STRM/BLCK streams, AFC or PCM16. No Wii/Switch extensions or LE variants are assumed.
- **JAudioWaveBank:** raw WSYS or GameCube AAF chunk tables and relative WSYS/WINF wave metadata. Supply the corresponding AW file explicitly. The index counts waves in bank/WSYS order; it is not an instrument or sound-effect ID. The caller must pair the selected wave with its correct AW bank.
- **RenderWareStream:** LE RWS chunk headers with BE GameCube metadata. 0x809 supports selectable mono DSP/PCM entries; 0x80d supports selectable segments/layers, mono/stereo, interleaved DSP or BE PCM. Entry index is `segment * layerCount + layer`, starting at zero. Other platform codecs are rejected.
- **RenderWareWaveDictionary:** BE AWD with the GameCube platform UUID, bounded linked entries and mono GCNADPCM codec. RWS and AWD both use an 0x809 value in different byte orders/layouts; they have separate readers.
- **ResidentEvil4Stream:** stream-block metadata containing RIT and SHD tables, plus its explicitly supplied SBB data file. Stereo uses the verified 0x4000-byte channel blocks. Sound event IDs and unrelated executable REL data are not interpreted as headers.
- **Dtk:** explicit headerless 32-byte GameCube disc-track ADPCM, stereo 48 kHz, with strict repeated-header/predictor validation. This decodes from mounted files; it does not reprogram DVD hardware streaming.

`Sound` exposes `Channels`, `SampleCount`, `HasLoop`, `LoopStartSample`, `LoopEndSample` and `NativeBytes`. `LengthBytes` describes decoded PCM size even when the stored source remains compressed. `Play(loop: true)` uses the file loop interval when present, otherwise the full clip. Nonlooping playback includes the whole source.

`Sound.PlayLoop(loopStartSample, loopEndSample)` and `Music.PlayLoop(loopStartSample, loopEndSample)` override the interval for that playback. Indices are sample frames per channel, starting at zero, with an **exclusive end**: playback emits the intro `[0, loopEndSample)`, then repeats `[loopStartSample, loopEndSample)`. Require `0 <= start < end <= SampleCount`. For ADX, boundaries may fall anywhere inside a 32-sample frame; cached samples and predictor histories are restored together. Each voice can use a different range without changing the file metadata. This also works for ADX entries selected from AFS and other supported `LoadGameCube/Async` sources. WAV/`FromPcm8` custom ranges throw `NotSupportedException`; their existing whole-clip looping remains available.

```csharp
using var track = await Music.LoadGameCubeAsync("sd:/audio/theme.adx");
track.PlayLoop(22050, 220500); // repeat 1s–10s at 22050 Hz, after the intro
```

ADX v3/v4 embedded loop points use the same sample interval semantics. Version 5 has no embedded loop header; explicit ranges and whole-clip looping still work. Invalid custom ranges throw `ArgumentOutOfRangeException` before starting a voice or replacing a music track.

## Memory and playback

The default per-clip native loading budget is 8 MiB, configurable from 1 KiB to 16 MiB. It includes both complete input files, span metadata and validation state; allocation fails cleanly when the budget is exceeded. Large archives/paired banks therefore need to fit as a whole, even if only one entry is selected. The PCM WAV path retains its previous 16 MiB decoded-data limit.

Each compressed playback has independent predictor/loop state and one 1152-byte aligned PCM buffer. The AESND callback performs bounded native integer decoding with no file I/O, allocation or managed-heap access. Source data remains alive until all voices using it are stopped. Disposing a clip stops its voices before freeing the source. PCM conversion and channel interleaving are native; these codecs are not passed directly to the DSP as compressed bitstreams.

This is **compressed resident playback**, not disk streaming. It avoids expanding an entire music track into RAM but still requires its compressed input to fit the configured budget. Supported game sources use 5000–48000 Hz mono/stereo audio. There are 16 clip slots and up to 32 voices. Callback CPU cost, maximum simultaneous compressed voices, DSP/ARAM coexistence and actual audible output still need physical-console measurement; a successful target build is not an audio listening test.

## Validation and research sources

Original generated fixtures and host fakes test twelve container profiles, codec sample vectors, coefficient/history behavior, channel ordering, partial reads, partial-frame loops, independent voices, end-buffer draining, truncation, encrypted/unsupported codecs, malformed pointers, deterministic mutations, budgets and cleanup. Managed AOT tests use the real automatic service thread, cancellation and forced GC. GameCube cross-builds check the actual AESND API and complete DOL linkage.

Research only; no external decoder implementation or game data is copied or linked:

- [vgmstream](https://github.com/vgmstream/vgmstream/tree/7c520e8f9b078dfde2f84f1b5bcbad0c6c415806/src): `meta/ngc_dsp_std.c`, `adx.c`, `ahx.c`, `afc.c`, `ast.c`, `halpst.c`, `ngc_ssm.c`, `jaudio.c`, `rws_809.c`, `rws_80d.c`, `awd.c`, `ngc_adpdtk.c`, their block layouts and codec descriptions.
- [F-Zero GX file inventory](https://gitlab.com/RaphaelTetreault/fzgx_documentation/-/blob/4ac9c9c959c91c6084f58751f679294e82e11dbd/asset/file-extension-analytics.md), explicitly distinguishes ADX music and AHX voices.
- [PSO quest BGM names](https://github.com/fuzziqersoftware/newserv/blob/ce2273a8e48c5c6fec9e21d85c490028498d8682/src/QuestScript.cc) and map BGM commands at the same revision.
- [RE4 GameCube reverse engineering](https://github.com/adonis-singh/re4/tree/9b76ff003081d6ece8b838453fb8310743a972c7): `include/snd_drv.h`, `src/game/snd_sub3.cpp`, `snd_str0.cpp`, `snd_str2.cpp` and `snd_str3.cpp` specify SHD, RIT, offsets and stereo block sizes.
- [resource_dasm JAudio research](https://github.com/fuzziqersoftware/resource_dasm/tree/8930baa5ae99bf41e8d70b2d21ae8423dd38e429/src/Audio), including Sunshine sound-environment and sequence/instrument separation.
- [libogc2 AESND buffer lifecycle](https://github.com/extremscorner/libogc2/blob/048cd314e5b71e18720200735c30ba278cc0ec56/libaesnd/aesndlib.c), checked against the installed SDK headers.
