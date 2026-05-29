# Unity Bridge v1

This document defines the message contract between React Native and the Room2Scan Unity editor.

## Transport

React Native sends one JSON string to Unity:

```text
UnityBridge.ReceiveFromRN(envelopeJson)
```

Unity sends one JSON string back to React Native through the Unity/RN message manager:

```text
SendMessageToRN(envelopeJson)
```

Both directions use the same `unity-bridge/v1` envelope.

## Envelope

- `schemaVersion`: must be `unity-bridge/v1`
- `messageId`: unique id for this message
- `requestId`: optional id used to correlate a command with Unity events
- `kind`: `command` or `event`
- `direction`: `rn_to_unity` or `unity_to_rn`
- `name`: command or event name
- `sentAt`: ISO 8601 UTC timestamp
- `payload`: command/event body
- `error`: optional error object for `EditorError`

## RN to Unity Commands

- `LoadRoom`: payload `{ "room": room-json/v1 }`
- `LoadFurnitureCatalog`: payload `{ "catalog": furniture-catalog-json/v1 }`
- `LoadLayout`: payload `{ "layout": layout-json/v1 }`
- `AddFurniture`: payload `{ "catalogId": "...", "instanceId": optional string, "spawn": optional spawn config }`
- `SelectFurniture`: payload `{ "instanceId": "..." }`
- `RotateSelected`: payload `{ "degrees": optional number, "direction": optional "clockwise" | "counterclockwise" }`
- `DeleteSelected`: payload `{}`
- `SaveLayout`: payload `{}`
- `ResetEditor`: payload `{}`

## Unity to RN Events

- `RoomLoaded`: payload `{ "roomId": "...", "success": boolean, "meshUri": "...", "normalizedMeshUri": optional string, "colliderCount": optional number, "bounds": optional AABB, "error": optional error }`
- `FurnitureCatalogLoaded`: payload `{ "catalogId": "...", "success": true, "itemCount": number }`
- `LayoutLoaded`: payload `{ "layoutId": optional string, "success": true }`
- `FurnitureAdded`: payload `{ "instanceId": "...", "catalogId": "...", "validation": item validation }`
- `FurnitureSelected`: payload `{ "instanceId": optional string }`
- `FurnitureChanged`: payload `{ "instanceId": "...", "transform": layout transform, "validation": item validation }`
- `FurnitureDeleted`: payload `{ "instanceId": "..." }`
- `PlacementChanged`: payload `{ "layoutValidation": layout validation, "changedItemIds": string[] }`
- `LayoutSaved`: payload `{ "layout": layout-json/v1 }`
- `EditorReset`: payload `{ "success": true }`
- `EditorError`: payload `{ "failedCommand": optional string }` plus `error`

## Recommended Load Order

1. RN sends `LoadRoom`.
2. Unity replies with `RoomLoaded`.
3. RN sends `LoadFurnitureCatalog`.
4. Unity replies with `FurnitureCatalogLoaded`.
5. RN optionally sends `LoadLayout`.
6. User edits inside Unity.
7. RN sends `SaveLayout`.
8. Unity replies with `LayoutSaved`.

## Files

- JSON Schema: `docs/schemas/unity-bridge-v1.schema.json`
- Load room example: `docs/schemas/unity-bridge-v1.example.load-room.json`
- Layout saved example: `docs/schemas/unity-bridge-v1.example.layout-saved.json`
