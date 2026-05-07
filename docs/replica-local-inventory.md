# Local Replica Inventory

This inventory was derived from the local `replica` directory.

These local PLY files are development source samples. In app runtime, the server is expected to send converted GLB assets through `room-json/v1.mesh.uri`.

## Shared Files

- `replica/cam_params.json`
- `replica/traj.txt`

## Mesh Files

All mesh files are binary little-endian PLY files with vertex positions, normals, RGB vertex colors, and triangle faces.

| Mesh | Vertices | Faces | Source Bounds Min `(x,y,z)` | Source Bounds Max `(x,y,z)` |
| --- | ---: | ---: | --- | --- |
| `office0_mesh.ply` | 589517 | 588759 | `(-2.0056, -3.1537, -1.1689)` | `(2.3944, 1.8561, 1.8230)` |
| `office1_mesh.ply` | 423007 | 422691 | `(-1.8204, -1.5824, -1.0477)` | `(2.9904, 2.5231, 1.7491)` |
| `office2_mesh.ply` | 858623 | 857845 | `(-3.4272, -2.8455, -1.2265)` | `(3.0453, 5.2980, 1.5414)` |
| `office3_mesh.ply` | 1187140 | 1185992 | `(-5.1116, -5.9395, -1.2207)` | `(3.5329, 3.2652, 1.8816)` |
| `office4_mesh.ply` | 993008 | 992909 | `(-1.2047, -2.3258, -1.2093)` | `(5.3415, 4.1794, 1.6078)` |
| `room0_mesh.ply` | 954492 | 953647 | `(-0.8794, -1.1860, -1.5274)` | `(6.8852, 3.5123, 1.2804)` |
| `room1_mesh.ply` | 645512 | 645078 | `(-5.4027, -3.0385, -1.4080)` | `(1.2436, 2.6891, 1.3452)` |
| `room2_mesh.ply` | 722496 | 722398 | `(-0.8171, -3.2454, -2.9081)` | `(5.9533, 1.7000, 0.6861)` |

## V1 Assumption

The source PLY bounds suggest the local Replica meshes are `Z-up`. Room JSON v1 still exposes only normalized Unity room-local coordinates: `meter`, `left`, `+Y` up, `+Z` forward. Unity should load the server-provided GLB, not these local PLY files.
