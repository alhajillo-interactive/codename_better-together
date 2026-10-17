using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

namespace BetterTogether.Player
{
    /// <summary>
    /// First-person walk/sprint/jump on a CharacterController. Shared mode: only the state authority
    /// simulates; NetworkTransform replicates the result. All tuning lives on the prefab so it can be reused per role.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerMovement : NetworkBehaviour
    {
        [SerializeField] private float _walkSpeed = 2f;
        [SerializeField] private float _sprintSpeed = 3.5f;
        [SerializeField] private float _jumpHeight = 0.5f;
        [SerializeField] private float _gravity = -20f;

        private CharacterController _controller;
        private InputAction _move;
        private InputAction _jump;
        private InputAction _sprint;
        private float _verticalVelocity;
        private bool _jumpQueued;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public override void Spawned()
        {
            if (!HasStateAuthority)
                return;

            var actions = InputSystem.actions;
            _move = actions.FindAction("Player/Move");
            _jump = actions.FindAction("Player/Jump");
            _sprint = actions.FindAction("Player/Sprint");
        }

        private void Update()
        {
            // FixedUpdateNetwork may not run every rendered frame, so buffer the press.
            // _jump is only assigned on the state authority (see Spawned).
            if (_jump != null && _jump.WasPressedThisFrame())
                _jumpQueued = true;
        }

        public override void FixedUpdateNetwork()
        {
            if (!HasStateAuthority)
                return;

            var grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -1f; // Small push keeps the controller stuck to the ground.

            var yaw = FirstPersonCamera.Instance != null ? FirstPersonCamera.Instance.Yaw : transform.eulerAngles.y;
            var move = _move.ReadValue<Vector2>();
            var direction = Vector3.ClampMagnitude(Quaternion.Euler(0f, yaw, 0f) * new Vector3(move.x, 0f, move.y), 1f);
            var speed = _sprint.IsPressed() ? _sprintSpeed : _walkSpeed;

            if (_jumpQueued && grounded)
                _verticalVelocity = Mathf.Sqrt(2f * _jumpHeight * -_gravity);
            _jumpQueued = false;

            _verticalVelocity += _gravity * Runner.DeltaTime;
            _controller.Move((direction * speed + Vector3.up * _verticalVelocity) * Runner.DeltaTime);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }
    }
}
