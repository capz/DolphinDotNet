# Neutral studio baking: proposal, implementation and remaining work

## Intended use

The `neutral-studio-v1` preset provides a reproducible starting point when no
authoring light rig or environment has been supplied. It is an explicit offline
conversion of a **static snapshot viewed from the front** into display-ready GX
textures. It evaluates a metallic/roughness BRDF per covered texture texel;
it is not the earlier base/emissive/AO diffuse approximation.

This is faithful to the direct-light model defined below within texture precision
and sampling limits. It is **not full source-renderer parity or dynamic PBR**.
An orbiting browser can display the result, but highlights and illumination stay
painted onto the object when the camera, object or lights move. Use ordinary
imported GX materials for lighting that must continue responding at runtime.
No asset-browser layout, controls, fonts, colors or artwork are changed here.

## Proposed neutral setup (implemented preset)

All lighting calculations use scene/world coordinates, Y up. The front view is
orthographic: direction from every surface point toward the camera is `(0,0,1)`.
Framing and camera distance do not affect the bake. Source node transforms are
included; move/rotate/scale the source into the intended presentation pose before
conversion. The resulting model should retain that presentation at runtime.

| Component | Direction from surface toward light (normalized by baker) | Linear RGB radiance |
| --- | --- | --- |
| Key | `(-0.45, 0.65, 0.60)` | white, `2.5` |
| Fill | `(0.55, 0.20, 0.80)` | white, `0.7` |
| Rim | `(0.20, 0.70, -0.65)` | white, `1.5` |
| Uniform diffuse ambient | all directions | diffuse multiplier `0.08` |

These are ideal directional sources, not area lights or an HDR environment.
Their intensities are relative linear scene units, not calibrated lux. Ambient
is a constant diffuse term `baseColor * (1-metallic) * 0.08 * AO`; it is not a
sampled environment or a reflected studio room. There is no ambient specular.
Source cameras/lights are replaced by this preset.

Exposure is 0 EV. Lit output uses the ACES fitted curve
`clamp(x*(2.51*x+0.03)/(x*(2.43*x+0.59)+0.14),0,1)`, then the standard sRGB
transfer and RGBA8 quantization. This is a convenient display curve, not a complete
ACES color-management pipeline. Authored unlit materials bypass lighting and
tone mapping; their linear base color is encoded directly to sRGB. Alpha is linear
and never tone-mapped.

## Using the implementation

```sh
dnd-assets model presentation.glb presentation.dndmodel --bake-neutral-studio --texture-limit 256
```

```csharp
ModelConverter.Convert(input, output, new ImportOptions {
    BakeNeutralStudio = true, TextureLimit = 256
});
```

The flag is mutually exclusive with `--compile-gx-materials` and
`--allow-material-approximation`. Ordinary conversions retain existing behavior.
Direct glTF/GLB input is supported. Existing Assimp 5.3.x adapters can supply static
scenes from other supported formats, subject to what that adapter preserves.
PSO GJ/XJ baking is rejected. Prefer glTF/GLB for the clearest material contract.

Each active mesh primitive receives a square baked texture, with four texels of
edge dilation. Identical final texture/material records may be deduplicated.
Output is ordinary DNMD v1 with white, unlit materials: runtime diffuse lighting
and vertex color must not multiply the already baked result a second time.
Original MASK/BLEND mode, alpha cutoff and double-sided setting are retained.
The package is loadable through existing native and managed APIs, including ARAM
unload/restore. Dependency hashes remain in `.deps.json`. A `.studio.json` sidecar
records the preset, normalized light directions, radiances, view, resolution,
transfer/BRDF policy and principal limitations. Keep both sidecars with the asset.

### Material evaluation

- Cook-Torrance specular: GGX distribution, height-correlated Smith visibility,
  Schlick Fresnel, dielectric F0 `0.04`; metallic F0 is the linear base color.
- Perceptual roughness is clamped to `0.045`, squared for GGX alpha. Diffuse is
  `(1-Fresnel)*(1-metallic)*baseColor/pi`; direct sources multiply by `N dot L`.
- Base textures are decoded from sRGB before filtering and multiplying by linear
  material factors and interpolated vertex colors. Emission textures are also
  decoded from sRGB. Metallic/roughness, normal and occlusion maps are linear data.
- Metallic uses the blue channel; roughness uses green. AO uses red and authored
  strength, and attenuates ambient diffuse only. Emission is added independently
  of diffuse lighting, including `KHR_materials_emissive_strength`.
- Normal maps use tangent-space RGB, scale, normalized smooth/face normals,
  inverse-transpose world normal transforms and tangent handedness. Authored
  tangents are used for the matching UV0 basis; otherwise a per-triangle UV-derived
  tangent basis is used. This fallback is not a full MikkTSpace generator.
- Each map independently supports UV0/UV1, `KHR_texture_transform`, repeat,
  mirror/clamp and nearest/bilinear sampling. Source mip/anisotropic filtering is
  not reproduced. The baked output uses UV0, clamp and existing GX mip generation.

Material channel/color-space conventions follow the
[Khronos glTF 2.0 material specification](https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#materials).

### Required source preparation and strict failures

Export the desired static pose with skins, animation and morphs removed. UV0 must
be a usable atlas within `[0,1]`; triangles must cover texel centers at the selected
resolution. Conflicting overlapping UV0 values, degenerate UV triangles, missing
required UV sets, singular transforms, invalid material ranges and unsupported
material extensions fail explicitly. Identical-color overlaps are allowed.
No automatic UV unwrapping, pose evaluation or silent feature approximation occurs.

The existing 1..1024 nodes / 1..4096 primitives limits apply. Resolution is a power
of two from 4 to 1024; output base-level pixels are limited to 8 MiB in total and
decoded maps to 64 MiB per material. Mips, encoded images and importer temporaries
add memory beyond these pixel budgets. Lower resolution or split large scenes.
Double-sided materials are baked for the facing side of this fixed camera; one
texture cannot preserve two different front/back lighting results.

## Verification

Desktop fixtures compare quantized output against independently calculated
double-precision reference pixels for dielectric and metallic BRDFs, metallic/
roughness maps, a tangent-space normal map, AO plus emission, transformed normals,
unlit sRGB and alpha. They check repeatability, report generation and strict input
rejection. Native tests load the generated package and verify unlit material
selection; AOT smoke loads, draws, unloads and restores it. Existing GX command
recording verifies the shared material path. These are numerical/structural tests,
not independent photographic or physical-console acceptance.

## Still to do

- [ ] Compare textured spheres and representative production assets against an
  independent renderer using this exact rig, view, exposure and display curve.
  Establish image-error and seam tolerances; do not claim renderer parity yet.
- [ ] Validate actual Dolphin/GameCube pixels, GX gamma/display behavior,
  alpha edges, mip seams, performance and memory on representative assets.
- [ ] Add shadow visibility, area-light integration, indirect illumination and
  specular image-based lighting if the intended studio includes these effects.
  None is currently computed; the uniform ambient term is an explicit shortcut.
- [ ] Define a dynamic/view-sampled runtime representation for freely orbiting
  cameras and moving lights/objects. A single fixed-view RGB texture cannot retain
  view-dependent specular, reflections or lighting changes, regardless of TEV API.
- [ ] Add custom environment/light/camera/exposure inputs with versioned manifests
  if callers need an authored studio rather than this fixed neutral preset.
- [ ] Implement MikkTSpace-compatible fallback tangents and evaluate mirrored UVs,
  seam padding and supersampled rasterization against independent reference assets.
- [ ] Support static pose/morph extraction and automatic charting/packing if desired;
  currently the authoring application must produce a valid static UV atlas.
- [ ] Add transmission, volume, clearcoat, sheen, anisotropy and other material
  extensions only with defined physical evaluation. They currently fail explicitly.
- [ ] Consider HDR/intermediate precision, linear-light mip generation, source
  minification/anisotropy and improved alpha-coverage preservation. Final output is
  currently 8-bit display color using the existing texture/mip pipeline.
- [ ] Qualify Assimp material/normal conversion with real FBX/OBJ/DAE assets;
  fidelity can only cover information preserved by the upstream adapter.

These remaining items are separate from the completed two-texture TEV mapping and
global transparency work described in [phase 2 materials](phase2-materials.md).
