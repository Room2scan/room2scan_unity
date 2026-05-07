# room2scan_unity

Unity room editor module for Room2Scan.

## P0

The current spike focuses on the minimum RN <-> Unity bridge loop:

- RN sends `LoadRoom` to Unity.
- Unity replies with `RoomLoaded`.
- RN sends `SaveLayout` to Unity.
- Unity replies with `LayoutSaved`.

See `docs/p0-unity-bridge-spike.md`.
