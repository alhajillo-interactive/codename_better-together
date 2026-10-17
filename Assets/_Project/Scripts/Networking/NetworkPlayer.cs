#if FUSION_WEAVER
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Networking
{
    /// <summary>
    /// Minimal networked player for testing the connection. Requires NetworkObject + NetworkTransform on the prefab.
    /// Only the owner (state authority) moves it; Fusion replicates the transform to everyone else.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private float _speed = 5f;
        [Tooltip("On: moves on X/Y (2D). Off: moves on X/Z (3D).")]
        [SerializeField] private bool _moveOnXY = true;

        public static NetworkPlayer Local { get; private set; }

        public override void Spawned()
        {
            if (HasStateAuthority)
                Local = this;

            name = $"Player {Object.InputAuthority.PlayerId}{(HasStateAuthority ? " (local)" : "")}";
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Local == this)
                Local = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            var input = ReadMoveInput();
            var direction = _moveOnXY ? new Vector3(input.x, input.y, 0f) : new Vector3(input.x, 0f, input.y);
            transform.position += direction * (_speed * Runner.DeltaTime);
        }

        private static Vector2 ReadMoveInput()
        {
            var input = Vector2.zero;

            if (Keyboard.current != null)
            {
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
            }

            if (Gamepad.current != null)
                input += Gamepad.current.leftStick.ReadValue();

            return Vector2.ClampMagnitude(input, 1f);
        }
    }
}
#endif
