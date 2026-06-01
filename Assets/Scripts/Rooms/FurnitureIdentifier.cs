using UnityEngine;

namespace Room2Scan.Rooms
{
    /// <summary>
    /// Lightweight marker component attached to every furniture root GameObject.
    /// Lets FurnitureDragController identify which furniture was tapped/raycasted
    /// without relying on Unity tags (which must be pre-registered in Project Settings).
    /// </summary>
    public sealed class FurnitureIdentifier : MonoBehaviour
    {
        public string InstanceId;
    }
}
