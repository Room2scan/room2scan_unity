# layout JSON v1

This document defines the `layout-json/v1` payload sent from Unity to React Native when the user saves a furniture layout.

## Scope

- The payload stores the current furniture layout for one `room-json/v1` room.
- Coordinates are fixed to the same Unity room-local coordinate policy as `room-json/v1`.
- The payload includes both item transforms and placement validation results.
- Deleted furniture is omitted from `items`.

## Required root fields

- `schemaVersion`: must be `layout-json/v1`
- `roomId`: the source room id from `room-json/v1`
- `savedAt`: ISO 8601 UTC timestamp
- `coordinateSystem`: fixed Unity room-local coordinate policy
- `items`: current furniture instances
- `validation`: whole-layout validity summary

## Fixed coordinate policy

- `coordinateSystem.unit`: `meter`
- `coordinateSystem.handedness`: `left`
- `coordinateSystem.upAxis`: `+Y`
- `coordinateSystem.forwardAxis`: `+Z`

All saved transforms are room-local. Unity should not send screen-space, world-session, AR-session, or camera-relative coordinates in this payload.

## Item fields

- `instanceId`: stable id for this placed furniture instance
- `catalogId`: furniture catalog/prefab id
- `transform.position`: room-local position in meters
- `transform.rotationYDeg`: Y-axis rotation in degrees
- `transform.scale`: local scale multiplier
- `validation.isValid`: item-level placement status
- `validation.reasons`: zero or more validation failure reasons

## Validation reason codes

- `out_of_room`: footprint is outside every allowed floor polygon
- `hit_wall`: furniture overlaps a wall collider
- `hit_furniture`: furniture overlaps another furniture item
- `hit_blocked_zone`: furniture overlaps a non-placeable polygon
- `unknown`: fallback for unexpected validation failures

## Optional fields

- `layoutId`: backend or client generated layout id
- `roomSchemaVersion`: expected to be `room-json/v1`
- `editorSessionId`: Unity editor session id for debugging
- `displayName`: user-facing item name
- `isLocked`: item lock state
- `metadata`: item-specific metadata
- `camera`: optional editor camera state for restoring the same view
- `extensions`: project-specific metadata

## Files

- JSON Schema: `docs/schemas/layout-json-v1.schema.json`
- Example payload: `docs/schemas/layout-json-v1.example.json`
