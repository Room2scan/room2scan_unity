using UnityEngine;
using Room2Scan.Bridge;
using UnityEngine.InputSystem;

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

        /// <summary>True while a drag or scale gesture is active.
        /// OrbitCameraController reads this to avoid single-finger orbit conflicts.</summary>
        public bool IsBusy => isDragging || isScaling;
        private Transform dragTransform  = null;
        private float     dragYLevel     = 0f;
        private Vector3   dragOffset     = Vector3.zero;
        private bool      lastCollision  = false;   // throttle: send event only on change
        private bool      isScaling      = false;
        private string    scaleInstanceId = null;
        private float     scaleStartY    = 0f;
        private float     scaleInitial   = 1f;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        private void OnDestroy()
        {
            // Re-enable orbit if we're destroyed mid-drag
            if (isDragging || isScaling) RestoreOrbit();
        }

        // ── Update ─────────────────────────────────────────────────────────────────

        private void Update()
        {
            var bridge = UnityBridge.Instance;
            if (bridge == null) return;

            // ── Input abstraction ─────────────────────────────────────────────────
            if (!TryReadPointer(out var justPressed, out var isHeld, out var justReleased, out var screenPos))
                return;

            var tool = bridge.ActiveTool;

            // ── Tap-to-select (select mode) ───────────────────────────────────────
            if (tool == "select" && justPressed && !isDragging)
            {
                TrySelectAt(screenPos, bridge);
                return;
            }

            // ── Drag (move mode) ──────────────────────────────────────────────────
            if (tool == "scale")
            {
                if      (justPressed  && !isScaling) TryBeginScale(screenPos);
                else if (isHeld       &&  isScaling) ContinueScale(screenPos);
                else if (justReleased &&  isScaling) EndScale();
                return;
            }

            if (tool != "move") return;

            if      (justPressed  && !isDragging) TryBeginDrag(screenPos);
            else if (isHeld       &&  isDragging) ContinueDrag(screenPos, bridge);
            else if (justReleased &&  isDragging) EndDrag();
        }

        // ── Tap-to-select ──────────────────────────────────────────────────────────

        private static bool TryReadPointer(
            out bool justPressed,
            out bool isHeld,
            out bool justReleased,
            out Vector2 screenPos)
        {
            var touch = Touchscreen.current;
            if (touch != null)
            {
                var primary = touch.primaryTouch;
                justPressed  = primary.press.wasPressedThisFrame;
                isHeld       = primary.press.isPressed;
                justReleased = primary.press.wasReleasedThisFrame;
                if (justPressed || isHeld || justReleased)
                {
                    screenPos = primary.position.ReadValue();
                    return true;
                }
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                justPressed  = mouse.leftButton.wasPressedThisFrame;
                isHeld       = mouse.leftButton.isPressed;
                justReleased = mouse.leftButton.wasReleasedThisFrame;
                if (justPressed || isHeld || justReleased)
                {
                    screenPos = mouse.position.ReadValue();
                    return true;
                }
            }

            justPressed = false;
            isHeld = false;
            justReleased = false;
            screenPos = Vector2.zero;
            return false;
        }

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

            // Use Y=0 (floor level) as the drag plane regardless of furniture height.
            // Dragging at furniture.y causes near-parallel ray/plane at steep camera
            // angles, which collapses movement to a single axis.
            var floorY = 0f;
            var plane  = new Plane(Vector3.up, Vector3.zero);
            var ray    = cam.ScreenPointToRay(screenPos);

            if (!plane.Raycast(ray, out float dist)) return;

            var hitWorld   = ray.GetPoint(dist);
            dragInstanceId = fm.SelectedInstanceId;
            dragTransform  = go.transform;
            dragYLevel     = go.transform.position.y;  // keep furniture at its own Y while moving XZ
            dragOffset     = new Vector3(
                dragTransform.position.x - hitWorld.x,
                0f,
                dragTransform.position.z - hitWorld.z);
            // Save undo snapshot before starting drag
            FurnitureManager.Instance?.PushUndo(dragInstanceId);

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
            var plane = new Plane(Vector3.up, Vector3.zero); // always project onto Y=0 floor plane
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
            fm.ClearCollisionColor(dragInstanceId); // restore GLB renderer tints first
            fm.SelectFurniture(dragInstanceId);     // re-applies SelectedColor for cube furniture

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

        private void TryBeginScale(Vector2 screenPos)
        {
            var fm = FurnitureManager.Instance;
            if (fm == null || fm.SelectedInstanceId == null) return;

            var selected = fm.GetSelectedTransform();
            if (!selected.HasValue) return;

            isScaling = true;
            scaleInstanceId = fm.SelectedInstanceId;
            scaleStartY = screenPos.y;
            scaleInitial = selected.Value.Scale;
            lastCollision = false;
            DisableOrbit();
        }

        private void ContinueScale(Vector2 screenPos)
        {
            var fm = FurnitureManager.Instance;
            if (fm == null || scaleInstanceId == null) { EndScale(); return; }

            var delta = (screenPos.y - scaleStartY) * 0.005f;
            var nextScale = Mathf.Clamp(scaleInitial + delta, 0.2f, 4f);
            if (!fm.ScaleSelected(nextScale)) return;

            var hasCollision = fm.CheckCollision(scaleInstanceId);
            fm.SetCollisionColor(scaleInstanceId, hasCollision);
            if (hasCollision != lastCollision)
            {
                lastCollision = hasCollision;
                UnityBridge.Instance?.SendCollisionStatusEvent(scaleInstanceId, hasCollision);
            }
        }

        private void EndScale()
        {
            isScaling = false;
            RestoreOrbit();

            var fm = FurnitureManager.Instance;
            if (fm != null && scaleInstanceId != null)
            {
                fm.SelectFurniture(scaleInstanceId);
                var t = fm.GetSelectedTransform();
                if (t.HasValue)
                {
                    UnityBridge.Instance?.SendFurnitureTransformedEvent(scaleInstanceId, t.Value);
                    UnityBridge.Instance?.SendCollisionStatusEvent(scaleInstanceId, false);
                }
            }

            scaleInstanceId = null;
            lastCollision = false;
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
