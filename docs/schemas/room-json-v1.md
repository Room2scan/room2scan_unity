# room JSON v1 (Replica v1 + GLB)

This document defines the `room-json/v1` payload used by the Room2Scan Unity editor.

## Scope

- Dataset source is fixed to Replica v1.
- Runtime mesh format is fixed to GLB.
- Runtime coordinates are fixed to Unity room-local coordinates.
- Payload includes both rendering data (`mesh`) and placement validation data (`placement`).
- Semantics are optional in v1.
- The server must send a GLB URI. Unity does not load PLY in app runtime.

## Required root fields

- `schemaVersion`: must be `room-json/v1`
- `roomId`: unique room identifier
- `source`: dataset provenance
- `coordinateSystem`: unit + axis conventions
- `mesh`: renderable GLB source
- `bounds`: world-space AABB
- `placement`: floor polygons and optional wall/opening/blocked zones

## Fixed coordinate policy

- `coordinateSystem.unit`: `meter`
- `coordinateSystem.handedness`: `left`
- `coordinateSystem.upAxis`: `+Y`
- `coordinateSystem.forwardAxis`: `+Z`
- `coordinateSystem.toUnity.positionOffset`: `{ "x": 0, "y": 0, "z": 0 }`
- `coordinateSystem.toUnity.rotationEulerDeg`: `{ "x": 0, "y": 0, "z": 0 }`
- `coordinateSystem.toUnity.scaleMultiplier`: `1`

All geometry fields in this payload must already be normalized to this coordinate system before Unity receives the JSON.

## Server GLB policy

The local `replica` directory contains development source samples such as `*_mesh.ply`, `cam_params.json`, and `traj.txt`. These files are useful for testing conversion, but they are not part of the Unity runtime contract.

For v1, the server should:

- Convert the source Replica mesh to GLB before sending room JSON.
- Send the runtime GLB location in `mesh.uri`.
- Remap geometry to Unity room-local coordinates before writing room JSON.
- Send `bounds` and `placement` in the same normalized coordinate system.
- Optionally preserve source PLY paths under `source.provenance.originalFiles` for debugging.
- Optionally preserve conversion metadata under `source.conversion`.

For the current local sample, `room0_mesh.ply` appears to be `Z-up`. The example records that source detail only as provenance. Unity should still consume only the final `mesh.uri` GLB and the normalized room JSON fields.

## Local PLY test fallback

Unity includes an editor/development-only fallback for local Replica PLY files so we can preview rooms before the server-side GLB converter is ready.

- Use Unity menu `Room2Scan > P1 > Send Local PLY LoadRoom`.
- This sends `replica/room0_mesh.ply` through the same `LoadRoom` bridge path.
- The fallback remaps Replica source coordinates from `(x, y, z)` to Unity room coordinates `(x, z, y)` and floor-normalizes the mesh.
- This fallback is intentionally not the production mobile contract. Release runtime should still receive `mesh.format = "glb"` and a GLB `mesh.uri`.

## Optional fields

- `semantics`: optional Replica semantic labels and instances.
- `initialLayout`: optional existing furniture layout.
- `extensions`: optional project-specific metadata.

## Why this structure

- Replica v1 distributes core geometry as `mesh.ply`, semantics, and habitat metadata.
- Unity runtime uses GLB for efficient loading, so conversion metadata is explicitly tracked under `source.conversion`.
- GLB files may originate from a different source coordinate convention, but the final imported room and JSON placement data must be aligned to Unity room-local coordinates.
- Placement logic (inside room, wall overlap, blocked zones) should not depend on heavy render mesh collision only.
- When true floor segmentation is unavailable, `placement.derivation.method = "aabb"` is allowed as a temporary V1 fallback.

## Files

- JSON Schema: `docs/schemas/room-json-v1.schema.json`
- Example payload: `docs/schemas/room-json-v1.example.replica-glb.json`

## Notes for implementation

- Keep all linear units in meters.
- Keep saved furniture transforms in room-local coordinates.
- Use `placement.floorPolygons` as the primary in/out boundary check.
- Use `placement.walls` and `placement.blockedZones` for fast overlap checks.
