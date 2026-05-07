# furniture catalog JSON v1

This document defines the `furniture-catalog-json/v1` payload used by Unity to resolve furniture assets and placement metadata.

## Scope

- The catalog maps `catalogId` to a Unity prefab or GLB asset.
- The catalog provides dimensions, footprint, collision proxy, and placement rules.
- Coordinates are fixed to the same Unity room-local coordinate policy as `room-json/v1` and `layout-json/v1`.
- V1 supports floor-placed furniture only.

## Required root fields

- `schemaVersion`: must be `furniture-catalog-json/v1`
- `catalogId`: unique catalog identifier
- `updatedAt`: ISO 8601 UTC timestamp
- `coordinateSystem`: fixed Unity room-local coordinate policy
- `items`: available furniture definitions

## Fixed coordinate policy

- `coordinateSystem.unit`: `meter`
- `coordinateSystem.handedness`: `left`
- `coordinateSystem.upAxis`: `+Y`
- `coordinateSystem.forwardAxis`: `+Z`

All dimensions, footprints, collision proxies, and default transforms are expressed in Unity room-local/model-local meters.

## Catalog item fields

- `catalogId`: id used by `UnityBridge.AddFurniture(catalogId)`
- `displayName`: user-facing furniture name
- `category`: broad furniture type
- `asset`: prefab or GLB source
- `dimensions`: visual/model size and pivot convention
- `footprint`: top-down placement footprint, fixed to `box` in v1
- `collisionProxy`: 3D overlap proxy, fixed to `box` in v1
- `placementRules`: rotation step, clearance, and allowed surfaces
- `defaultTransform`: default rotation, scale, and optional spawn offset

## Asset loading policy

- `asset.sourceType=unity_resources`: Unity loads `asset.uri` with `Resources.Load`.
- `asset.sourceType=unity_addressable`: Unity resolves `asset.uri` as an Addressables key.
- `asset.sourceType=local_glb`: Unity loads a bundled/local GLB file.
- `asset.sourceType=remote_glb`: Unity downloads or streams a remote GLB file.

For the first Unity implementation, prefer `unity_resources` with `unity_prefab`. It keeps mobile builds predictable while the editor logic is still changing.

## Placement policy

- `placementRules.allowedSurfaces` is fixed to `floor` in v1.
- `footprint.shape` is fixed to `box`.
- `collisionProxy.shape` is fixed to `box`.
- `rotationStepDeg` controls how much the rotate command changes `rotationYDeg`.
- `clearanceMeters` can be added to footprint/proxy checks to create a small safety margin.

## Relationship to layout JSON

- `layout-json/v1.items[].catalogId` must match one `furniture-catalog-json/v1.items[].catalogId`.
- `layout-json/v1.items[].transform` stores the user's placed transform.
- Deleted furniture is represented by omission from the layout payload, not by a deleted flag in the catalog.

## Files

- JSON Schema: `docs/schemas/furniture-catalog-json-v1.schema.json`
- Example payload: `docs/schemas/furniture-catalog-json-v1.example.json`
