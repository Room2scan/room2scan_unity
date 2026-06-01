using UnityEngine;
using Room2Scan.Bridge;

namespace Room2Scan.Rooms
{
    /// <summary>
    /// P5-A  Furniture drag — moves the selected piece along the floor plane.
    /// P5-B  Collision feedback — live red/green material tint while dragging.
    /// P5-C  Grid snap (0.25 m) + room-boundary clamping.
    ///
    /// Attach this component to the same GameObject as the Camera.
    /// UnityBridge.EnsureFurnitureDragController() does this automatically
    /// whenever a room finishes loading.
    ///
    /// Input works for both Unity Input System (New) mouse AND mobile touch.
    /// </summary>
    public sealed class FurnitureDragController : MonoBehaviour
    {
        // ── Constants ─────────────────────────────────────────────────────────────
        private const float SnapGrid = 0.25f;   // metres per grid cell

        // ── Runtime state ─────────────────────────────────────────────────────────
        private bool      isDragging     = false;
        private string    dragInstanceId = null;
        private Transform dragTransform  = null;
        private float     dragYLevel     = 0f;
        private Vector3   dragOffset     = Vector3.zero;
        private bool      lastCollision  = false;   // throttle: send event only on change

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            // Re-enable orbit if we're destroyed mid-drag
            if (isDragging) RestoreOrbit();
        }

        // ── Update ─────────────────────────────────────────────────────────────────

        private void Update()
        {
            var bridge = UnityBridge.Instance;
            if (bridge == null) return;

            // ── Input abstraction ─────────────────────────────────────────────────
            bool    justPressed, isHeld, justReleased;
            Vector2 screenPos;

            if (Input.touchCount > 0)
            {
                var t       = Input.GetTouch(0);
                justPressed  = t.phase == TouchPhase.Began;
                isHeld       = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary;
                justReleased = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled;
                screenPos    = t.position;
            }
            else
            {
                justPressed  = Input.GetMouseButtonDown(0);
                isHeld       = Input.GetMouseButton(0);
                justReleased = Input.GetMouseButtonUp(0);
                screenPos    = Input.mousePosition;
            }

            var tool = bridge.ActiveTool;

            // ── Tap-to-select (select mode) ───────────────────────────────────────
            if (tool == "select" && justPressed && !isDragging)
            {
                TrySelectAt(screenPos, bridge);
                return;
            }

            // ── Drag (move mode) ──────────────────────────────────────────────────
            if (tool != "move") return;

            if      (justPressed  && !isDragging) TryBeginDrag(screenPos);
            else if (isHeld       &&  isDragging) ContinueDrag(screenPos, bridge);
            else if (justReleased &&  isDragging) EndDrag();
        }

        // ── Tap-to-select ──────────────────────────────────────────────────────────

        private void TrySelectAt(Vector2 screenPos, UnityBridge bridge)
        {
            var cam = GetComponent<Camera>();
            if (cam == null) return;

            var ray = cam.ScreenPointToRay(screenPos);
            if (!Physics.Raycast(ray, out var hit, 100f)) return;

            var fm         = FurnitureManager.Instance;
            if (fm == null) return;

            var instanceId = fm.GetInstanceIdFromGameObject(hit.collider.gameObject);
            if (instanceId == null) return;

            fm.SelectFurniture(instanceId);
            bridge.SendFurnitureSelectedEvent(instanceId, fm.GetSelectedTransform());
        }

        // ── Drag begin ─────────────────────────────────────────────────────────────

        private void TryBeginDrag(Vector2 screenPos)
        {
            var fm = FurnitureManager.Instance;
            if (fm == null || fm.SelectedInstanceId == null) return;

            var go = fm.GetSelectedGameObject();
            if (go == null) return;

            var cam = GetComponent<Camera>();
            if (cam == null) return;

            var floorY = go.transform.position.y;
            var plane  = new Plane(Vector3.up, new Vector3(0f, floorY, 0f));
            var ray    = cam.ScreenPointToRay(screenPos);

            if (!plane.Raycast(ray, out float dist)) return;

            var hitWorld   = ray.GetPoint(dist);
            dragInstanceId = fm.SelectedInstanceId;
            dragTransform  = go.transform;
            dragYLevel     = floorY;
            dragOffset     = dragTransform.position - new Vector3(hitWorld.x, floorY, hitWorld.z);
            isDragging     = true;
            lastCollision  = false;

            // Suspend orbit while dragging furniture
            DisableOrbit();
        }

        // ── Drag continue ──────────────────────────────────────────────────────────

        private void ContinueDrag(Vector2 screenPos, UnityBridge bridge)
        {
            if (dragTransform == null) { EndDrag(); return; }

            var cam = GetComponent<Camera>();
            if (cam == null) return;

            var ray   = cam.ScreenPointToRay(screenPos);
            var plane = new Plane(Vector3.up, new Vector3(0f, dragYLevel, 0f));
            if (!plane.Raycast(ray, out float dist)) return;

            var hit    = ray.GetPoint(dist);
            var target = new Vector3(hit.x + dragOffset.x, dragYLevel, hit.z + dragOffset.z);

            // Grid snap
            if (bridge.SnapEnabled)
            {
                target.x = Mathf.Round(target.x / SnapGrid) * SnapGrid;
                target.z = Mathf.Round(target.z / SnapGrid) * SnapGrid;
            }

            // Room boundary clamp (keep furniture inside walls)
            var rb = FurnitureManager.RoomBounds;
            if (rb.size.sqrMagnitude > 0f)
            {
                var half = dragTransform.localScale.x * 0.5f;
                target.x = Mathf.Clamp(target.x, rb.min.x + half, rb.max.x - half);
                target.z = Mathf.Clamp(target.z, rb.min.z + half, rb.max.z - half);
            }

            dragTransform.position = target;

            // Live collision color + throttled event to RN
            var fm           = FurnitureManager.Instance;
            var hasCollision = fm != null && fm.CheckCollision(dragInstanceId);

            fm?.SetCollisionColor(dragInstanceId, hasCollision);

            if (hasCollision != lastCollision)
            {
                lastCollision = hasCollision;
                bridge.SendCollisionStatusEvent(dragInstanceId, hasCollision);
            }
        }

        // ── Drag end ───────────────────────────────────────────────────────────────

        private void EndDrag()
        {
            isDragging = false;
            RestoreOrbit();

            var fm = FurnitureManager.Instance;
            if (fm == null || dragTransform == null)
            {
                dragInstanceId = null;
                dragTransform  = null;
                return;
            }

            // Restore selected-yellow color (was green/red during drag)
            fm.SelectFurniture(dragInstanceId);     // re-applies SelectedColor

            var t = fm.GetSelectedTransform();
            if (t.HasValue)
            {
                UnityBridge.Instance?.SendFurnitureTransformedEvent(dragInstanceId, t.Value);
                // Clear any lingering collision status
                UnityBridge.Instance?.SendCollisionStatusEvent(dragInstanceId, false);
            }

            dragInstanceId = null;
            dragTransform  = null;
        }

        // ── Orbit helpers ──────────────────────────────────────────────────────────

        private void DisableOrbit()
        {
            var orbit = GetComponent<OrbitCameraController>();
            if (orbit != null) orbit.enabled = false;
        }

        private void RestoreOrbit()
        {
            var orbit = GetComponent<OrbitCameraController>();
            if (orbit != null) orbit.enabled = true;
        }
    }
}
