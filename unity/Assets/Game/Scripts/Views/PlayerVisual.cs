using System.Collections.Generic;
using GolemSample.GolemGenerated;
using UnityEngine;

namespace GolemSample.Unity
{
    /// <summary>
    /// Player view that registers itself by entity id so local input/camera can resolve the GameObject
    /// without accessing the private GolemEntitySpawner instance map.
    /// </summary>
    public sealed class PlayerVisual : PlayerView
    {
        private static readonly Dictionary<long, PlayerVisual> ByEntityId = new();

        private long registeredEntityId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ByEntityId.Clear();
        }

        public static bool TryGet(long entityId, out PlayerVisual visual)
        {
            if (entityId == 0)
            {
                visual = null;
                return false;
            }

            return ByEntityId.TryGetValue(entityId, out visual) && visual != null;
        }

        protected override void OnSpawned(SyncedPlayer entity)
        {
            Register(entity.EntityId);
        }

        protected override void OnDespawned(SyncedPlayer entity)
        {
            Unregister(entity != null ? entity.EntityId : registeredEntityId);
        }

        private void OnDestroy()
        {
            Unregister(registeredEntityId);
        }

        private void Register(long entityId)
        {
            if (entityId == 0)
            {
                return;
            }

            if (registeredEntityId != 0 && registeredEntityId != entityId)
            {
                Unregister(registeredEntityId);
            }

            ByEntityId[entityId] = this;
            registeredEntityId = entityId;
        }

        private void Unregister(long entityId)
        {
            if (entityId == 0)
            {
                return;
            }

            if (ByEntityId.TryGetValue(entityId, out var existing) && existing == this)
            {
                ByEntityId.Remove(entityId);
            }

            if (registeredEntityId == entityId)
            {
                registeredEntityId = 0;
            }
        }
    }
}
