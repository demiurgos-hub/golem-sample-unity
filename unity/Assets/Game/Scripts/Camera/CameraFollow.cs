using UnityEngine;

namespace GolemSample.Unity
{
    /// <summary>
    /// Damped 2D follow of the local player's transform. Preserves the camera's own Z depth.
    /// </summary>
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private LocalPlayer localPlayer;
        [SerializeField] private float dampTime = 0.15f;
        [SerializeField] private float deadZone;

        private Transform target;
        private long targetEntityId;
        private Vector3 velocity;

        private void OnEnable()
        {
            if (localPlayer != null)
            {
                localPlayer.Assigned += OnLocalPlayerAssigned;
                if (localPlayer.IsAssigned)
                {
                    OnLocalPlayerAssigned(localPlayer.EntityId);
                }
            }
        }

        private void OnDisable()
        {
            if (localPlayer != null)
            {
                localPlayer.Assigned -= OnLocalPlayerAssigned;
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                TryResolveTarget();
                if (target == null)
                {
                    return;
                }
            }

            var current = transform.position;
            var desired = target.position;
            desired.z = current.z;

            var offset = desired - current;
            offset.z = 0f;
            if (deadZone > 0f && offset.sqrMagnitude <= deadZone * deadZone)
            {
                velocity = Vector3.zero;
                return;
            }

            transform.position = Vector3.SmoothDamp(current, desired, ref velocity, dampTime);
        }

        private void OnLocalPlayerAssigned(long entityId)
        {
            targetEntityId = entityId;
            target = null;
            velocity = Vector3.zero;
            TryResolveTarget();
        }

        private void TryResolveTarget()
        {
            if (targetEntityId == 0 && localPlayer != null && localPlayer.IsAssigned)
            {
                targetEntityId = localPlayer.EntityId;
            }

            if (targetEntityId == 0)
            {
                return;
            }

            if (PlayerVisual.TryGet(targetEntityId, out var visual) && visual != null)
            {
                target = visual.transform;
            }
        }
    }
}
