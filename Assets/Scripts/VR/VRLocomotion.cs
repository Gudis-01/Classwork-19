using UnityEngine;
using UnityEngine.InputSystem;

namespace Classwork18.VR
{
    /// <summary>
    /// Goes on the XR Origin (rig root).
    /// Left thumbstick = walk (in the direction you are looking).
    /// Right thumbstick left/right = snap turn.
    /// Uses a CharacterController so you can't walk through colliders and you stay on the floor.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class VRLocomotion : MonoBehaviour
    {
        [Tooltip("The VR camera (your head).")]
        public Transform head;

        [Header("Move")]
        public float moveSpeed = 2f;
        [Range(0f, 0.5f)] public float deadzone = 0.15f;
        public float gravity = -9.81f;

        [Header("Snap turn")]
        public float snapAngle = 45f;
        [Range(0.3f, 1f)] public float turnThreshold = 0.75f;

        CharacterController _cc;
        InputAction _move;
        InputAction _turn;
        float _verticalSpeed;
        bool _turnReady = true;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            if (head == null && Camera.main != null) head = Camera.main.transform;

            _move = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
            _move.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
            // Keyboard fallback for quick tests in the editor.
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");

            _turn = new InputAction("Turn", InputActionType.Value, expectedControlType: "Vector2");
            _turn.AddBinding("<XRController>{RightHand}/{Primary2DAxis}");
            _turn.AddCompositeBinding("2DVector")
                .With("Left", "<Keyboard>/q").With("Right", "<Keyboard>/e");
        }

        void OnEnable() { _move.Enable(); _turn.Enable(); }
        void OnDisable() { _move.Disable(); _turn.Disable(); }
        void OnDestroy() { _move.Dispose(); _turn.Dispose(); }

        void Update()
        {
            if (head == null) return;

            FitCapsuleToHead();

            // --- Move ---
            Vector2 input = _move.ReadValue<Vector2>();
            if (input.magnitude < deadzone) input = Vector2.zero;

            Vector3 up = transform.up;
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(head.right, up).normalized;
            Vector3 velocity = (forward * input.y + right * input.x) * moveSpeed;

            if (_cc.isGrounded && _verticalSpeed < 0f) _verticalSpeed = -1f;
            else _verticalSpeed += gravity * Time.deltaTime;

            _cc.Move((velocity + up * _verticalSpeed) * Time.deltaTime);

            // --- Snap turn (rotate the rig around the head so you don't slide sideways) ---
            float x = _turn.ReadValue<Vector2>().x;
            if (_turnReady && Mathf.Abs(x) > turnThreshold)
            {
                transform.RotateAround(head.position, up, Mathf.Sign(x) * snapAngle);
                _turnReady = false;
            }
            else if (Mathf.Abs(x) < 0.3f)
            {
                _turnReady = true;
            }
        }

        /// <summary>Keeps the collision capsule under your head as you walk around the room for real.</summary>
        void FitCapsuleToHead()
        {
            Vector3 headLocal = transform.InverseTransformPoint(head.position);
            float height = Mathf.Clamp(headLocal.y, 0.5f, 2.2f);
            _cc.height = height;
            _cc.center = new Vector3(headLocal.x, height * 0.5f + _cc.skinWidth, headLocal.z);
        }
    }
}
