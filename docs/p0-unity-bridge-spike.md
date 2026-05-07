# P0 Unity Bridge Spike

P0 verifies the minimum React Native to Unity round trip.

## Current Goal

1. React Native opens the Unity editor screen.
2. React Native sends a `LoadRoom` bridge message.
3. Unity replies with `RoomLoaded`.
4. React Native sends `SaveLayout`.
5. Unity replies with `LayoutSaved`.

## Unity Mock Implementation

- Runtime bridge: `Assets/Scripts/Bridge/UnityBridge.cs`
- Editor test menu: `Assets/Scripts/Bridge/Editor/UnityBridgeEditorTest.cs`
- Mock load room payload: `docs/schemas/unity-bridge-v1.example.load-room.json`

`UnityBridge` creates a `UnityBridge` GameObject automatically before the first scene loads, so P0 does not require manual scene wiring.

## Editor Test

Open the Unity project at `D:\unity\room2scan_unity`.

Then use:

- `Room2Scan/P0/Send Mock LoadRoom`
- `Room2Scan/P0/Send Mock SaveLayout`

Expected Console output:

- `Room2Scan RN->Unity: LoadRoom`
- `Room2Scan Unity->RN: ... "name":"RoomLoaded" ...`
- `Room2Scan RN->Unity: SaveLayout`
- `Room2Scan Unity->RN: ... "name":"LayoutSaved" ...`

## Runtime Message Entry Point

React Native should call Unity with:

```text
UnityBridge.ReceiveFromRN(envelopeJson)
```

The target GameObject name is:

```text
UnityBridge
```

The method name is:

```text
ReceiveFromRN
```

## Notes

The bridge tries to call `UnityMessageManager.Instance.SendMessageToRN(string)` if the RN Unity package provides it. In the Unity Editor, or before the RN package is installed, it logs the outgoing bridge envelope to the Console.
