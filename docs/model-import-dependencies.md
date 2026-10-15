# On-device importer dependencies

These unmodified upstream sources are compiled into the native runtime. License
text is retained in each header or the vendored notice. MIT is the chosen license
where dual licensing is offered.

| Component | Pin | Use |
|---|---|---|
| [cgltf](https://github.com/jkuhlmann/cgltf) | `85cd62382dfea638278962690cf515023f33ed00` | `include/dnd_cgltf.h`: JSON/schema parsing; the adapter reads binary scalars explicitly as little-endian |
| [stb_image](https://github.com/nothings/stb) | `2c980bb59875b0d32144a71867fbdebb2f77cd20` | `include/dnd_stb_image.h`: PNG/JPEG/TGA/BMP memory decoding with native budget allocators |
| [ufbx](https://github.com/ufbx/ufbx) | `1d097696e33b367bb3152b175aeb2d95193f9b6e` | `include/dnd_ufbx.h`, `include/dnd_ufbx_impl.h`, `source/model_ufbx.c`: native FBX/OBJ/MTL parsing and animation evaluation |

SHA-256:

```
efb169dee911696b5d35fc8e3f7ea0c56d679debc529eba9ca6aa6443ba9d5e9  dnd_cgltf.h
594c2fe35d49488b4382dbfaec8f98366defca819d916ac95becf3e75f4200b3  dnd_stb_image.h
f787be529af577efc04f3e3e735844a08556718dbb613d1211c2b313d72895dc  dnd_ufbx.h
4a956c26a708e40d82ecb0aeaee54da709d5ba7c144c9642174970059bda7e1d  dnd_ufbx_impl.h
```

The ufbx [MIT notice](../vendor/ufbx/LICENSE) is retained. Its documented header-path
and allocator hooks allow the implementation to remain unmodified while preventing
unbudgeted allocation and direct stdio access. The wrapper supplies correctly typed
NULL-returning global allocation fallbacks for this pin's C no-malloc configuration.

PSO parsing follows the reverse-engineered
[PSO specification](https://github.com/theanine3D/pso_ultimate_importer/wiki/Format-Specification)
and Ninja/GX definitions, with independent implementations of texture decoding.
No Dolphin GPL source was copied.

J3D, BTI, Yaz0/Yay0, RARC and GameCube RE4 BIN/TPL/FCV/DAT/DRS adapters
are independent runtime implementations with no new linked library. Their pinned
research references and profile boundaries are recorded in
[Sunshine and RE4 formats](gamecube-model-formats.md).
