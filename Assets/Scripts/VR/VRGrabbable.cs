using UnityEngine;

namespace Classwork18.VR
{
    /// <summary>
    /// Put this on any object with a Rigidbody to make it grabbable by a VRHand.
    /// While held, the object follows the hand using physics velocities, so it still
    /// collides with things and keeps its speed when you let go (you can throw it).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class VRGrabbable : MonoBehaviour
    {
        [Tooltip("Tint applied while a hand is close enough to grab.")]
        public Color hoverColor = new Color(1f, 0.85f, 0.2f);

        [Tooltip("Caps how fast a held object can move (m/s), so it can't fly off at crazy speeds.")]
        public float maxLinearSpeed = 20f;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Rigidbody _rb;
        VRHand _holder;
        Vector3 _posOffset;       // object position relative to the grab point
        Quaternion _rotOffset;    // object rotation relative to the grab point
        bool _usedGravity;

        Renderer[] _renderers;
        Color[] _baseColors;
        MaterialPropertyBlock _mpb;
        int _hoverCount;

        public bool IsHeld => _holder != null;
        public VRHand Holder => _holder;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.maxAngularVelocity = 50f;

            _renderers = GetComponentsInChildren<Renderer>();
            _baseColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                var mat = _renderers[i].sharedMaterial;
                _baseColors[i] = mat != null && mat.HasProperty(BaseColorId) ? mat.GetColor(BaseColorId) : Color.white;
            }
            _mpb = new MaterialPropertyBlock();
        }

        public void BeginGrab(VRHand hand)
        {
            // Grabbing with the other hand takes it from the first hand.
            if (_holder != null && _holder != hand)
                _holder.ForceRelease();

            _holder = hand;
            Transform gp = hand.GrabPoint;
            Quaternion inv = Quaternion.Inverse(gp.rotation);
            _posOffset = inv * (_rb.position - gp.position);
            _rotOffset = inv * _rb.rotation;

            _usedGravity = _rb.useGravity;
            _rb.useGravity = false;

            // Don't let a held object block your own body (the rig's CharacterController).
            var body = hand.GetComponentInParent<CharacterController>();
            if (body != null)
                foreach (var col in GetComponentsInChildren<Collider>())
                    Physics.IgnoreCollision(col, body, true);
        }

        public void EndGrab(VRHand hand)
        {
            if (_holder != hand) return;
            _holder = null;
            _rb.useGravity = _usedGravity;
            // Velocity is kept as-is, which is what makes throwing work.
        }

        void FixedUpdate()
        {
            if (_holder == null) return;

            Transform gp = _holder.GrabPoint;
            Vector3 targetPos = gp.position + gp.rotation * _posOffset;
            Quaternion targetRot = gp.rotation * _rotOffset;
            float dt = Time.fixedDeltaTime;

            // Move toward the hand in exactly one physics step.
            _rb.linearVelocity = Vector3.ClampMagnitude((targetPos - _rb.position) / dt, maxLinearSpeed);

            // Rotate toward the hand's rotation in one physics step.
            Quaternion delta = targetRot * Quaternion.Inverse(_rb.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (Mathf.Abs(angle) > 0.01f && !float.IsNaN(axis.x))
                _rb.angularVelocity = axis.normalized * (angle * Mathf.Deg2Rad / dt);
            else
                _rb.angularVelocity = Vector3.zero;
        }

        /// <summary>Called by hands when they start/stop hovering this object.</summary>
        public void SetHovered(bool hovered)
        {
            _hoverCount = Mathf.Max(0, _hoverCount + (hovered ? 1 : -1));
            bool on = _hoverCount > 0;
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].GetPropertyBlock(_mpb);
                _mpb.SetColor(BaseColorId, on ? Color.Lerp(_baseColors[i], hoverColor, 0.6f) : _baseColors[i]);
                _renderers[i].SetPropertyBlock(_mpb);
            }
        }

        void OnDisable()
        {
            if (_holder != null) _holder.ForceRelease();
        }
    }
}
