using UnityEngine;
using UnityEngine.InputSystem;

namespace Room2Scan.Rooms
{
    /// <summary>
    /// 런타임 오빗 카메라 (New Input System).
    /// - 왼쪽 드래그  : 피벗 주위 공전
    /// - 오른쪽 드래그 : 패닝
    /// - 스크롤 휠    : 줌
    /// </summary>
    public sealed class OrbitCameraController : MonoBehaviour
    {
        [Header("Orbit")]
        public float orbitSensitivity = 0.25f;
        public float pitchMin = -85f;
        public float pitchMax = 85f;

        [Header("Zoom")]
        public float zoomSensitivity = 0.08f;
        public float minDistance = 0.3f;
        public float maxDistance = 40f;

        [Header("Pan")]
        public float panSensitivity = 0.003f;

        private Vector3 pivot;
        private float distance;
        private float yaw;
        private float pitch;

        // ── Top-down (2D) mode ────────────────────────────────────────────────────
        private bool    isTopDown         = false;
        private Vector3 savedPivot;
        private float   savedDistance;
        private float   savedYaw;
        private float   savedPitch;

        public void SetPivotAndDistance(Vector3 newPivot, float newDistance, float initialYaw = -135f, float initialPitch = 30f)
        {
            pivot    = newPivot;
            distance = Mathf.Clamp(newDistance, minDistance, maxDistance);
            yaw      = initialYaw;
            pitch    = initialPitch;
            ApplyTransform();
        }

        // ── Public: 2D / 3D camera toggle ────────────────────────────────────────────

        /// <summary>
        /// Switches between isometric 3D orbit (false) and orthographic top-down 2D (true).
        /// Called by UnityBridge when it receives a SetViewMode command.
        /// </summary>
        public void SetTopDown(bool enable)
        {
            if (enable == isTopDown) return;
            isTopDown = enable;

            var cam = GetComponent<Camera>();
            if (cam == null) return;

            if (enable)
            {
                // Save current 3D state
                savedPivot    = pivot;
                savedDistance = distance;
                savedYaw      = yaw;
                savedPitch    = pitch;

                // Orthographic top-down view
                cam.orthographic     = true;
                var rb = FurnitureManager.RoomBounds;
                var roomSpan         = Mathf.Max(rb.size.x, rb.size.z);
                cam.orthographicSize = roomSpan > 0.1f ? roomSpan * 0.58f : 5f;

                transform.position   = new Vector3(pivot.x, pivot.y + 20f, pivot.z);
                transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
            }
            else
            {
                // Restore 3D orbit
                cam.orthographic = false;
                pivot    = savedPivot;
                distance = savedDistance;
                yaw      = savedYaw;
                pitch    = savedPitch;
                ApplyTransform();
            }
        }

        private void LateUpdate()
        {
            // Disable orbit input in top-down mode
            if (isTopDown) return;

            var mouse = Mouse.current;
            if (mouse == null) return;

            var delta   = mouse.delta.ReadValue();
            var scroll  = mouse.scroll.ReadValue().y;
            var changed = false;

            // 왼쪽 드래그: 공전
            if (mouse.leftButton.isPressed && delta.sqrMagnitude > 0f)
            {
                yaw   += delta.x * orbitSensitivity;
                pitch -= delta.y * orbitSensitivity;
                pitch  = Mathf.Clamp(pitch, pitchMin, pitchMax);
                changed = true;
            }

            // 오른쪽 드래그: 패닝
            if (mouse.rightButton.isPressed && delta.sqrMagnitude > 0f)
            {
                pivot -= transform.right * (delta.x * panSensitivity * distance);
                pivot -= transform.up    * (delta.y * panSensitivity * distance);
                changed = true;
            }

            // 스크롤: 줌
            if (Mathf.Abs(scroll) > 0.001f)
            {
                distance -= scroll * zoomSensitivity * distance;
                distance  = Mathf.Clamp(distance, minDistance, maxDistance);
                changed   = true;
            }

            if (changed) ApplyTransform();
        }

        private void ApplyTransform()
        {
            var rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = pivot + rotation * new Vector3(0f, 0f, -distance);
            transform.rotation = rotation;
        }
    }
}
