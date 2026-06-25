# CUDA Raycasting Volume Viewer — Implementation Plan

Incrementally turn the current OpenTK + CUDA scaffold into a DICOM volume
raycaster rendered by a CUDA kernel driven by a transfer function. Every
milestone is independently runnable so we can verify before moving on.

## Architecture decisions (defaults — adjust as needed)

- **CUDA → screen:** CUDA ↔ OpenGL PBO interop (zero-copy). Fallback: copy bytes
  back to the host and upload to a GL texture.
- **Volume sampling:** 3D CUDA texture object (hardware trilinear filtering).
  Because the raycaster runs in CUDA, a GL `Texture3D` is **not** required — the
  volume lives in a CUDA 3D array. (If we ever switch to a GLSL raycaster, add a
  GL `Texture3D` helper instead.)
- **Cube stage:** a rasterized proxy cube to validate camera / MVP / depth. The
  final raycast is drawn on a fullscreen quad, with eye rays built from the
  inverse view-projection inside the kernel.
- **Transfer function:** a hardcoded default RGBA lookup table first, structured
  so interactive editing can be added later.
- **Camera:** orbit + zoom mouse controls (better suited to a volume viewer than
  the FPS `Camera`).

## Minimal design / services

- `SomeViewer/Rendering/IRenderer.cs` — rendering seam (`Load` / `Render` /
  `Resize` / `Dispose`). `Window` becomes a thin host that owns a camera and
  delegates to the active `IRenderer`.
- `SomeViewer/Rendering/OrbitCamera.cs` — orbit/zoom camera for volume inspection.
- `SomeViewer/Rendering/VolumeRenderer.cs` — CUDA renderer (M2 gradient + M3
  middle-slice volume sample today; raycaster later, same seam).
- `SomeViewer/Volumes/VolumeData.cs` — volume model (dims + normalized [0,1]
  densities, z-major; raw intensity range retained).
- `SomeViewer/Volumes/IVolumeDataService.cs` + `DicomVolumeDataService.cs` —
  wrap `DicomFolderLoader` behind a service seam.
- `SomeViewer/Volumes/CudaVolumeTexture.cs` — uploads `VolumeData` to a CUDA 3D
  array and exposes a trilinear-filtered `CUtexObject` for kernels.
- `SomeViewer/Volumes/TransferFunction.cs` — builds a 1D RGBA LUT from control
  points; uploaded to the kernel and sampled by density.

## Note on `WindowLevel`

The existing `WindowLevel` kernel and its host code are an **example**. They are
kept for reference (the host code moves to an uncalled `Window.WindowLevelExample`
method, the kernel stays in `Kernels/SomeKernel.cu`) and the new raycaster does
**not** build on them.

## Milestones

- [x] **M1 — Cube rendering.** Replace the triangle with an MVP-transformed,
  depth-tested cube via `CubeRenderer`. *Verify:* a shaded rotating cube. ✅
- [x] **M2 — CUDA → GL display.** Fullscreen quad + PBO registered with CUDA; a
  test kernel writes a gradient. *Verify:* CUDA-generated gradient fills the
  window. ✅
- [x] **M3 — Volume upload.** DICOM data service + `VolumeData`; upload the
  volume into a CUDA 3D array + texture object. *Verify:* middle-slice sample. ✅
- [x] **M4 — Raycasting (grayscale DVR).** Ray/box intersection + front-to-back
  compositing in the kernel. *Verify:* recognizable grayscale volume that
  responds to the camera. ✅
- [x] **M5 — Transfer function.** 1D RGBA LUT sampled by density. *Verify:*
  colored volume rendering. ✅
- [x] **M6 — Interaction.** Orbit/zoom controls + window/level / step-size keys.
  *Verify:* smooth interactive exploration. ✅

## Status

- **Complete.** All milestones (M1–M6) implemented and verified. The viewer loads
  a DICOM volume into a CUDA 3D texture and raycasts it with a transfer function;
  the user orbits/zooms with the mouse and adjusts window/level and step size with
  the keyboard.

## Controls

- **Left-drag:** orbit the volume.
- **Scroll:** zoom in/out.
- **Up/Down:** window level (center).
- **Left/Right:** window width.
- **`[` / `]`:** finer / coarser ray step (quality vs. speed).
- **R:** reset camera and window/level.
- **Esc:** quit.
