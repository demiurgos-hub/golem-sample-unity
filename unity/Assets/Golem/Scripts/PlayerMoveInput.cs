using GolemEngine.Unity;
using GolemSample.GolemGenerated;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GolemSample.Unity
{
    public sealed class PlayerMoveInput : MonoBehaviour
    {
        [SerializeField] private SampleGolemClient clientBehaviour;
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string moveActionName = "Move";

        private InputAction _moveAction;
        private long _playerEntityId;

        private void Awake()
        {
            if (clientBehaviour == null)
            {
                clientBehaviour = GetComponent<SampleGolemClient>();
            }

            if (inputActions == null)
            {
                Debug.LogError("PlayerMoveInput requires an InputActionAsset reference.", this);
                return;
            }

            var actionMap = inputActions.FindActionMap(actionMapName, throwIfNotFound: true);
            _moveAction = actionMap.FindAction(moveActionName, throwIfNotFound: true);
        }

        private void OnEnable()
        {
            _moveAction?.Enable();
        }

        private void OnDisable()
        {
            _moveAction?.Disable();
        }

        private void Update()
        {
            if (clientBehaviour?.Client == null || !clientBehaviour.Client.Connected || _moveAction == null)
            {
                return;
            }

            var move = _moveAction.ReadValue<Vector2>();
            if (move.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            var player = ResolvePlayer();
            if (player == null)
            {
                return;
            }

            move = Vector2.ClampMagnitude(move, 1f);
            clientBehaviour.Client.Send(CommandBuilders.BuildMoveCommand(player.EntityId, move.x, move.y));
        }

        private SyncedPlayer ResolvePlayer()
        {
            if (clientBehaviour.Client.Entities is not EntityManager entityManager)
            {
                return null;
            }

            if (_playerEntityId != 0 && entityManager.Get(_playerEntityId) is SyncedPlayer existing)
            {
                return existing;
            }

            foreach (var entity in entityManager.GetAll().Values)
            {
                if (entity is SyncedPlayer player)
                {
                    _playerEntityId = player.EntityId;
                    return player;
                }
            }

            return null;
        }
    }
}
