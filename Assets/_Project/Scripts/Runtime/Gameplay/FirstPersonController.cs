using MurderMystery.Level;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MurderMystery.Gameplay
{
    /// <summary>
    /// Minimal walk-around controller for checking routes and sightlines in the manor.
    /// WASD move, Shift run, Space jump (low: stones and small sills), C sit / stand, mouse look,
    /// Esc toggles the cursor.
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
        private float assistStep;
        private float eyeHeight;

        // Enough for a stone or a sill, not for the 0.6 m garden walls or anything that seals an area.
        private const float JumpHeight = 0.45f;
        private const float SitEyeRatio = 0.62f;

        public bool IsSitting { get; private set; }

        public bool InputEnabled { get; set; } = true;

        public void Init(Transform pivot)
        {
            cameraPivot = pivot;
            eyeHeight = pivot.localPosition.y;
        }

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            var profile = PlayerBodyProfile.Current;
            assistStep = profile != null ? profile.assistStep : 0f;
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
            if (InputEnabled && controller.isGrounded && keyboard.cKey.wasPressedThisFrame) IsSitting = !IsSitting;
            // Moving stands you up.
            if (IsSitting && move != Vector2.zero) IsSitting = false;
            if (cameraPivot != null)
            {
                var targetEye = IsSitting ? eyeHeight * SitEyeRatio : eyeHeight;
                var p = cameraPivot.localPosition;
                p.y = Mathf.MoveTowards(p.y, targetEye, 2.5f * Time.deltaTime);
                cameraPivot.localPosition = p;
            }

            var speed = keyboard.leftShiftKey.isPressed ? runSpeed : walkSpeed;
            var horizontal = (transform.right * move.x + transform.forward * move.y).normalized * speed;

            var jumped = false;
            if (controller.isGrounded)
            {
                verticalVelocity = -1f;
                if (InputEnabled && !IsSitting && keyboard.spaceKey.wasPressedThisFrame)
                {
                    verticalVelocity = Mathf.Sqrt(2f * -gravity * JumpHeight);
                    jumped = true;
                }
            }

            // Average of the velocity before and after this frame's gravity: exact for constant gravity, so the
            // jump height does not depend on the frame rate (a slow frame used to launch players far higher).
            var dt = Time.deltaTime;
            var before = verticalVelocity;
            if (!controller.isGrounded || jumped) verticalVelocity += gravity * dt;
            var rise = (before + verticalVelocity) * 0.5f * dt;

            if (!jumped) StepAssist.TryStepUp(controller, horizontal, assistStep);
            controller.Move(horizontal * dt + Vector3.up * rise);
        }

        public static void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }
    }
}
