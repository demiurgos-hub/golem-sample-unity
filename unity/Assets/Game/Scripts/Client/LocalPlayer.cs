using System;
using GolemSample.GolemGenerated;
using UnityEngine;

namespace GolemSample.Unity
{
    /// <summary>
    /// Tracks the server-assigned local player entity id from LocalPlayerAssigned.
    /// Registers after CreateClient (Awake) and before Connect (Start).
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class LocalPlayer : MonoBehaviour
    {
        [SerializeField] private SampleGolemClient clientBehaviour;

        private bool handlerRegistered;

        public long EntityId { get; private set; }

        public bool IsAssigned => EntityId != 0;

        public event Action<long> Assigned;

        private void Awake()
        {
            if (clientBehaviour == null)
            {
                clientBehaviour = GetComponent<SampleGolemClient>();
            }

            // SampleGolemClient.Awake (order 0) creates Client; this runs at 50, before Connect in Start.
            TryRegisterHandler();
        }

        private void Start()
        {
            if (!TryRegisterHandler())
            {
                Debug.LogError("LocalPlayer: client EventManager unavailable before Connect; LocalPlayerAssigned may be missed.", this);
            }
        }

        private bool TryRegisterHandler()
        {
            if (handlerRegistered)
            {
                return true;
            }

            var client = clientBehaviour != null ? clientBehaviour.Client : null;
            if (client?.Events is not EventManager events)
            {
                return false;
            }

            events.OnLocalPlayerAssigned(OnLocalPlayerAssigned);
            handlerRegistered = true;
            return true;
        }

        private void OnLocalPlayerAssigned(LocalPlayerAssignedEvent evt)
        {
            if (evt == null || evt.EntityId == 0)
            {
                return;
            }

            EntityId = evt.EntityId;
            Assigned?.Invoke(EntityId);
        }
    }
}
