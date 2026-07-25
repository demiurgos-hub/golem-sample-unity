using System.Collections.Generic;
using GolemSample.GolemGenerated;
using UnityEngine;

namespace GolemSample.Unity
{
    /// <summary>
    /// Player view that registers itself by entity id so local input/camera can resolve the GameObject
    /// without accessing the private GolemEntitySpawner instance map, and feeds EntityInterpolator.
    /// </summary>
    public sealed class PlayerVisual : PlayerView
    {
        private static readonly Dictionary<long, PlayerVisual> ByEntityId = new();

        [SerializeField]
        private EntityInterpolator interpolator;

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
            PushSample(entity);
        }

        protected override void OnUpdated(SyncedPlayer entity)
        {
            PushSample(entity);
        }

        protected override void OnDespawned(SyncedPlayer entity)
        {
            ClearInterpolator();
            Unregister(entity != null ? entity.EntityId : registeredEntityId);
        }

        private void OnDestroy()
        {
            ClearInterpolator();
            Unregister(registeredEntityId);
        }

        private void PushSample(SyncedPlayer entity)
        {
            if (entity == null)
            {
                return;
            }

            EnsureInterpolator();
            interpolator.PushSample(entity.PosX, entity.PosY);
        }

        private void ClearInterpolator()
        {
            if (interpolator != null)
            {
                interpolator.Clear();
            }
        }

        private void EnsureInterpolator()
        {
            if (interpolator == null)
            {
                interpolator = GetComponent<EntityInterpolator>();
            }
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
