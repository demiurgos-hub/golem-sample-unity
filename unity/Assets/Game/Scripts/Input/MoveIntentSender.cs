using GolemSample.GolemGenerated;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GolemSample.Unity
{
    /// <summary>
    /// Sends Move intent for the assigned local player at a hard-capped ~30 Hz while held,
    /// with exactly one zero-vector send on release (not subject to the hold cadence).
    /// </summary>
    public sealed class MoveIntentSender : MonoBehaviour
    {
        private const float SendHz = 30f;
        private const float InputDeadZoneSqr = 0.0001f;

        [SerializeField] private SampleGolemClient clientBehaviour;
        [SerializeField] private LocalPlayer localPlayer;
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string moveActionName = "Move";

        private InputAction moveAction;
        private float sendInterval;
        private float nextSendTime;
        private Vector2 lastSent;
        private bool hasLastSent;

        private void Awake()
        {
            sendInterval = 1f / SendHz;

            if (clientBehaviour == null)
            {
                clientBehaviour = GetComponent<SampleGolemClient>();
            }

            if (localPlayer == null)
            {
                localPlayer = GetComponent<LocalPlayer>();
            }

            if (inputActions == null)
            {
                Debug.LogError("MoveIntentSender requires an InputActionAsset reference.", this);
                return;
            }

            var actionMap = inputActions.FindActionMap(actionMapName, throwIfNotFound: true);
            moveAction = actionMap.FindAction(moveActionName, throwIfNotFound: true);
        }

        private void OnEnable()
        {
            moveAction?.Enable();
        }

        private void OnDisable()
        {
            moveAction?.Disable();
        }

        private void Update()
        {
            var client = clientBehaviour != null ? clientBehaviour.Client : null;
            if (client == null || !client.Connected || moveAction == null)
            {
                return;
            }

            if (localPlayer == null || !localPlayer.IsAssigned)
            {
                return;
            }

            var entityId = localPlayer.EntityId;
            if (entityId == 0)
            {
                return;
            }

            var move = moveAction.ReadValue<Vector2>();
            if (move.sqrMagnitude <= InputDeadZoneSqr)
            {
                // Release path: exactly one zero send, independent of the hold cadence so a
                // stop is not delayed by the last non-zero tick.
                if (hasLastSent && lastSent.sqrMagnitude > InputDeadZoneSqr)
                {
                    ClientFactory.SendMoveReliableUnordered(client, entityId, 0f, 0f);
                    lastSent = Vector2.zero;
                    hasLastSent = true;
                    nextSendTime = Time.time + sendInterval;
                }

                return;
            }

            move = Vector2.ClampMagnitude(move, 1f);

            // Hard cap every non-zero send (including after a genuine direction change).
            // Comparing against lastSent alone is not enough: analog jitter can exceed any
            // epsilon from the previous send on successive frames and would fire uncapped.
            if (Time.time < nextSendTime)
            {
                return;
            }

            ClientFactory.SendMoveReliableUnordered(client, entityId, move.x, move.y);
            lastSent = move;
            hasLastSent = true;
            nextSendTime = Time.time + sendInterval;
        }
    }
}
