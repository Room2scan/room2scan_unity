using UnityEngine;
using UnityEngine.InputSystem;

namespace Room2Scan.Rooms
{
    /// <summary>
    /// 런타임 오빗 카메라 (New Input System).
    ///
    /// PC (마우스):
    ///   - 왼쪽 드래그  : 피벗 주위 공전
    ///   - 오른쪽 드래그 : 패닝
    ///   - 스크롤 휠    : 줌
    ///
    /// 모바일 (터치):
    ///   - 한 손가락 드래그 (가구 선택/이동 중 아닐 때) : 공전
    ///   - 두 손가락 핀치                              : 줌
    ///   - 두 손가락 드래그                            : 패닝
    /// </summary>
    public sealed class OrbitCameraController : MonoBehaviour
    {
        [Header("Orbit")]
        public float orbitSensitivity      = 0.25f;
        public float touchOrbitSensitivity = 0.18f;
        public float pitchMin = -85f;
        public float pitchMax = 85f;

        [Header("Zoom")]
        public float zoomSensitivity      = 0.08f;
        public float touchZoomSensitivity = 0.01f;
        public float minDistance = 0.3f;
        public float maxDistance = 40f;

        [Header("Pan")]
        public float panSensitivity      = 0.003f;
        public float touchPanSensitivity = 0.004f;

        private Vector3 pivot;
        private float   distance;
        private float   yaw;
        private float   pitch;

        // ── Two-finger pinch state ────────────────────────────────────────────────
        private float   prevPinchDist   = -1f;
        private Vector2 prevPinchCenter = Vector2.zero;

        // ── Top-down (2D) mode ────────────────────────────────────────────────────
        private bool    isTopDown     = false;
        private Vector3 savedPivot;
        private float   savedDistance;
        private float   savedYaw;
        private float   savedPitch;

        // ── 2D top-down pan state ─────────────────────────────────────────────────
        private Vector2 prevTopDownTouchPos = Vector2.zero;

        public void SetPivotAndDistance(Vector3 newPivot, float newDistance,
                                        float initialYaw = -135f, float initialPitch = 30f)
        {
            pivot    = newPivot;
            distance = Mathf.Clamp(newDistance, minDistance, maxDistance);
            yaw      = initialYaw;
            pitch    = initialPitch;
            ApplyTransform();
        }

        // ── Public: 2D / 3D camera toggle ────────────────────────────────────────

        public void SetTopDown(bool enable)
        {
            if (enable == isTopDown) return;
            isTopDown = enable;

            var cam = GetComponent<Camera>();
            if (cam == null) return;

            if (enable)
            {
                savedPivot    = pivot;
                savedDistance = distance;
                savedYaw      = yaw;
                savedPitch    = pitch;

                cam.orthographic     = true;
                var rb               = FurnitureManager.RoomBounds;
                var roomSpan         = Mathf.Max(rb.size.x, rb.size.z);
                cam.orthographicSize = roomSpan > 0.1f ? roomSpan * 0.58f : 5f;

                transform.position = new Vector3(pivot.x, pivot.y + 20f, pivot.z);
                transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                var cam3d = GetComponent<Camera>();
                if (cam3d != null) cam3d.orthographic = false;
                pivot    = savedPivot;
                distance = savedDistance;
                yaw      = savedYaw;
                pitch    = savedPitch;
                ApplyTransform();
            }
        }

        // ── LateUpdate ────────────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (isTopDown)
            {
                HandleTopDownTouch();
                return;
            }

            HandleMouseOrbit();
            HandleTouchOrbit();
        }

        // ── Mouse (PC/Editor) ─────────────────────────────────────────────────────

        private void HandleMouseOrbit()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            var delta  = mouse.delta.ReadValue();
            var scroll = mouse.scroll.ReadValue().y;
            var changed = false;

            if (mouse.leftButton.isPressed && delta.sqrMagnitude > 0f)
            {
                yaw   += delta.x * orbitSensitivity;
                pitch -= delta.y * orbitSensitivity;
                pitch  = Mathf.Clamp(pitch, pitchMin, pitchMax);
                changed = true;
            }

            if (mouse.rightButton.isPressed && delta.sqrMagnitude > 0f)
            {
                pivot -= transform.right * (delta.x * panSensitivity * distance);
                pivot -= transform.up    * (delta.y * panSensitivity * distance);
                changed = true;
            }

            if (Mathf.Abs(scroll) > 0.001f)
            {
                distance -= scroll * zoomSensitivity * distance;
                distance  = Mathf.Clamp(distance, minDistance, maxDistance);
                changed   = true;
            }

            if (changed) ApplyTransform();
        }

        // ── Touch (Mobile) ────────────────────────────────────────────────────────

        private void HandleTouchOrbit()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return;

            // Count active touches
            int activeCount = 0;
            var t0 = touchscreen.touches[0];
            var t1 = touchscreen.touches[1];
            if (t0.press.isPressed) activeCount++;
            if (t1.press.isPressed) activeCount++;

            if (activeCount == 2)
            {
                // ── Two-finger: pinch zoom + pan ──────────────────────────────────
                prevPinchDist = HandleTwoFingerGesture(
                    t0.position.ReadValue(), t1.position.ReadValue(),
                    t0.press.wasPressedThisFrame || t1.press.wasPressedThisFrame,
                    prevPinchDist, ref prevPinchCenter);
                return;
            }

            // Reset two-finger state when fingers lift
            prevPinchDist = -1f;

            if (activeCount == 1)
            {
                // ── Single finger: orbit (only when furniture drag is NOT active) ──
                var dragCtrl = GetComponent<FurnitureDragController>();
                if (dragCtrl != null && dragCtrl.IsBusy) return;

                var delta = t0.delta.ReadValue();
                if (t0.press.isPressed && delta.sqrMagnitude > 0.01f)
                {
                    yaw   += delta.x * touchOrbitSensitivity;
                    pitch -= delta.y * touchOrbitSensitivity;
                    pitch  = Mathf.Clamp(pitch, pitchMin, pitchMax);
                    ApplyTransform();
                }
            }
        }

        private float HandleTwoFingerGesture(
            Vector2 pos0, Vector2 pos1,
            bool justStarted,
            float prevDist, ref Vector2 prevCenter)
        {
            var currentDist   = Vector2.Distance(pos0, pos1);
            var currentCenter = (pos0 + pos1) * 0.5f;
            var changed       = false;

            if (justStarted || prevDist < 0f)
            {
                prevCenter = currentCenter;
                return currentDist;
            }

            // Pinch → zoom
            var distDelta = currentDist - prevDist;
            if (Mathf.Abs(distDelta) > 0.5f)
            {
                distance -= distDelta * touchZoomSensitivity;
                distance  = Mathf.Clamp(distance, minDistance, maxDistance);
                changed   = true;
            }

            // Two-finger drag → pan
            var panDelta = currentCenter - prevCenter;
            if (panDelta.sqrMagnitude > 0.01f)
            {
                pivot -= transform.right * (panDelta.x * touchPanSensitivity * distance);
                pivot -= transform.up    * (panDelta.y * touchPanSensitivity * distance);
                changed = true;
            }

            prevCenter = currentCenter;
            if (changed) ApplyTransform();
            return currentDist;
        }

        // ── Top-down 2D touch pan ─────────────────────────────────────────────────

        private void HandleTopDownTouch()
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen == null) return;

            int activeCount = 0;
            var t0 = touchscreen.touches[0];
            var t1 = touchscreen.touches[1];
            if (t0.press.isPressed) activeCount++;
            if (t1.press.isPressed) activeCount++;

            if (activeCount == 2)
            {
                // Pinch zoom in top-down: adjust orthographic size
                var currentDist = Vector2.Distance(
                    t0.position.ReadValue(), t1.position.ReadValue());
                if (prevPinchDist > 0f)
                {
                    var cam = GetComponent<Camera>();
                    if (cam != null && cam.orthographic)
                    {
                        cam.orthographicSize -= (currentDist - prevPinchDist) * 0.005f;
                        cam.orthographicSize  = Mathf.Clamp(cam.orthographicSize, 0.5f, 20f);
                    }
                }
                prevPinchDist = currentDist;
                return;
            }

            prevPinchDist = -1f;

            if (activeCount == 1)
            {
                var delta = t0.delta.ReadValue();
                if (t0.press.isPressed && delta.sqrMagnitude > 0.01f)
                {
                    // Pan camera position in top-down mode
                    transform.position -= new Vector3(delta.x, 0f, delta.y) * touchPanSensitivity * 2f;
                }
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private void ApplyTransform()
        {
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = pivot + rotation * new Vector3(0f, 0f, -distance);
            transform.rotation = rotation;
        }
    }
}
