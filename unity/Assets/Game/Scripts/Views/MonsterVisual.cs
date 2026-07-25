using GolemSample.GolemGenerated;
using UnityEngine;

namespace GolemSample.Unity
{
    /// <summary>
    /// Resolves a monster's catalog Visual through MonsterList, instances it under the entity,
    /// and feeds EntityInterpolator from replicated position updates.
    /// </summary>
    public sealed class MonsterVisual : MonsterView
    {
        [SerializeField]
        private MonsterList monsterList;

        [SerializeField]
        private EntityInterpolator interpolator;

        private GameObject visualInstance;
        private SpriteRenderer rootRenderer;

        protected override void OnSpawned(SyncedMonster entity)
        {
            ApplyVisual(entity.MonsterTypeId);
            PushSample(entity);
        }

        protected override void OnUpdated(SyncedMonster entity)
        {
            PushSample(entity);
        }

        protected override void OnDespawned(SyncedMonster entity)
        {
            ClearInterpolator();
            ClearVisual();
        }

        private void OnDestroy()
        {
            ClearInterpolator();
            ClearVisual();
        }

        private void PushSample(SyncedMonster entity)
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

        private void ApplyVisual(uint monsterTypeId)
        {
            ClearVisual();

            if (monsterList == null || !monsterList.TryGet(monsterTypeId, out var type) || type == null)
            {
                Debug.LogWarning($"MonsterVisual: no MonsterType for id {monsterTypeId} on {name}.", this);
                return;
            }

            if (type.Visual == null)
            {
                Debug.LogWarning($"MonsterVisual: MonsterType '{type.Name}' (id {monsterTypeId}) has no Visual.", this);
                return;
            }

            if (rootRenderer == null)
            {
                rootRenderer = GetComponent<SpriteRenderer>();
            }

            if (rootRenderer != null)
            {
                rootRenderer.enabled = false;
            }

            visualInstance = Instantiate(type.Visual, transform);
            visualInstance.name = type.Visual.name;
            visualInstance.transform.localPosition = Vector3.zero;
            visualInstance.transform.localRotation = Quaternion.identity;
            visualInstance.transform.localScale = Vector3.one;
        }

        private void ClearVisual()
        {
            if (visualInstance != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(visualInstance);
                }
                else
                {
                    DestroyImmediate(visualInstance);
                }

                visualInstance = null;
            }

            if (rootRenderer != null)
            {
                rootRenderer.enabled = true;
            }
        }
    }
}
