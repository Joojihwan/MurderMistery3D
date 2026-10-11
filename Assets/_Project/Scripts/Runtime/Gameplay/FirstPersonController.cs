using UnityEngine;
using UnityEngine.InputSystem;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Minimal walk-around controller for checking routes and sightlines in the manor.
    /// WASD move, Shift run, mouse look, Esc toggles the cursor.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [SerializeField] private Transform cameraPivot;
        [SerializeField] private float walkSpeed = 2.2f;
        [SerializeField] private float runSpeed = 4.0f;
        [SerializeField] private float lookSensitivity = 0.12f;
        [SerializeField] private float gravity = -15f;

        private CharacterController controller;
        private float pitch;
        private float verticalVelocity;

        public bool InputEnabled { get; set; } = true;

        public void Init(Transform pivot) => cameraPivot = pivot;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            SetCursorLocked(true);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null || mouse == null) return;

            if (keyboard.escapeKey.wasPressedThisFrame) SetCursorLocked(Cursor.lockState != CursorLockMode.Locked);

            var looking = InputEnabled && Cursor.lockState == CursorLockMode.Locked;
            if (looking)
            {
                var delta = mouse.delta.ReadValue() * lookSensitivity;
                transform.Rotate(0f, delta.x, 0f);
                pitch = Mathf.Clamp(pitch - delta.y, -80f, 80f);
                if (cameraPivot != null) cameraPivot.localEulerAngles = new Vector3(pitch, 0f, 0f);
            }

            var move = Vector2.zero;
            if (InputEnabled)
            {
                if (keyboard.wKey.isPressed) move.y += 1f;
                if (keyboard.sKey.isPressed) move.y -= 1f;
                if (keyboard.dKey.isPressed) move.x += 1f;
                if (keyboard.aKey.isPressed) move.x -= 1f;
            }
            var speed = keyboard.leftShiftKey.isPressed ? runSpeed : walkSpeed;
            var horizontal = (transform.right * move.x + transform.forward * move.y).normalized * speed;

            verticalVelocity = controller.isGrounded ? -1f : verticalVelocity + gravity * Time.deltaTime;
            controller.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
