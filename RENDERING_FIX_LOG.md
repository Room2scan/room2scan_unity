# Rendering Fix Log

Date: 2026-06-03

## Problem

- Android APK ran without crashing, but GLB materials rendered as flat colors instead of showing embedded textures.
- Directly injecting `UrpFallbackMaterialGenerator` was avoided because it changes GLTFast's material generation path and had previously triggered a Burst/mesh-processing crash.
- `BuiltInMaterialGenerator` was not used because GLTFast compiles it out for URP players.

## Fix

- Added `Room2Scan/TexturedUnlit`, a small URP-compatible runtime shader that samples `_BaseMap`.
- Added GLB material normalization after `InstantiateMainSceneAsync`.
- The normalizer reads GLTFast material properties such as `baseColorTexture`, `diffuseTexture`, and `baseColorFactor`, then creates a stable `Room2Scan/TexturedUnlit` material.
- Added a GLB fallback extractor that reads the embedded `baseColorTexture` PNG directly from the local `.glb` JSON/BIN chunks when Android GLTFast returns a null/error material because its shader graph was unavailable.
- Room and furniture load paths pass that extracted texture into material normalization, so fallback materials can still render with the original GLB texture.
- Unsupported/null/error/legacy Standard materials still fall back to `Room2Scan/SolidColor`.
- Room and furniture GLB load paths now call `RuntimeMaterialFactory.NormalizeLoadedMaterials(...)`.
- Added the new shader to `ProjectSettings/GraphicsSettings.asset` always-included shaders for Android builds.

## Verification

- Unity batchmode import completed successfully with no C# or shader compile errors.
- Editor probe loaded local furniture GLB:
  `C:\Users\park\Downloads\unity_delivery_room1_final (1)\unity_delivery_room1_final\unity_y_up\movable_assets_local_pivot\001_basket-001_local_pivot.glb`
- Probe result:
  `MATERIAL_NORMALIZATION_PROBE_OK before=1 converted=1 after=1`
- Extraction probe loaded the app-packaged basket GLB and verified fallback conversion:
  `MATERIAL_EXTRACTION_PROBE_OK converted=1 fallback=0 shader=Room2Scan/TexturedUnlit`
- Installed the debug APK on `SM_G781N`, launched `com.scan2room.app`, and opened the room edit screen without an immediate `FATAL EXCEPTION`.
- Device log confirmed 12/12 placed furniture items normalized as `(1 textured, 0 fallback)`.

## Notes

- `room1_empty_floor_wall.glb` contains no embedded textures, so the room shell itself can still appear as a designed solid fallback color.
- The textured furniture GLBs contain `baseColorTexture`; those are the assets this fix targets.
