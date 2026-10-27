using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;
using UnityEngine.XR.OpenXR.Input;

namespace Classwork18.VR
{
    public enum HandSide { Left, Right }

    /// <summary>
    /// One per hand. Put it on an empty child of "Camera Offset" (inside the XR Origin).
    ///
    /// - Hand tracking on (controllers put down): draws your real hand as joints + bones,
    ///   grab by pinching (thumb + index) or making a fist.
    /// - Holding a controller: shows a simple hand whose fingers curl with grip/trigger,
    ///   grab with the grip button.
    /// </summary>
    public class VRHand : MonoBehaviour
    {
        public HandSide side = HandSide.Left;
        public Color handColor = new Color(0.93f, 0.76f, 0.64f);

        [Header("Grabbing")]
        [Tooltip("How close (m) an object must be to the grab point to be grabbed.")]
        public float grabRadius = 0.07f;
        public LayerMask grabLayers = ~0;

        [Header("Hand tracking gestures (meters)")]
        public float pinchStart = 0.025f;
        public float pinchRelease = 0.045f;
        public float fistStart = 0.065f;
        public float fistRelease = 0.085f;

        [Header("Controller")]
        [Range(0f, 1f)] public float gripStart = 0.55f;
        [Range(0f, 1f)] public float gripRelease = 0.35f;

        public Transform GrabPoint => _grabPoint;
        public bool IsHandTracking => _mode == Mode.Hand;
        public VRGrabbable Held => _held;

        enum Mode { None, Controller, Hand }
        enum Gesture { None, Pinch, Fist, Grip }

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<XRHandSubsystem> s_Subsystems = new List<XRHandSubsystem>();
        static readonly int JointCount = XRHandJointID.EndMarker.ToIndex();

        Mode _mode = Mode.None;
        Gesture _gesture = Gesture.None;
        XRHandSubsystem _subsystem;

        InputAction _posAction, _rotAction, _trackedAction, _gripAction, _triggerAction, _hapticAction;

        Transform _controllerRoot, _jointsRoot, _grabPoint;
        Transform[] _jointViz, _boneViz;
        Transform[,] _fingerPivots; // [finger, segment] for the controller hand model
        MaterialPropertyBlock _mpb;

        VRGrabbable _held, _hovered;
        readonly Collider[] _overlap = new Collider[16];

        // ------------------------------------------------------------------ setup

        void Awake()
        {
            // This object stays at the origin of "Camera Offset", so local space == tracking space.
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;

            string h = side == HandSide.Left ? "{LeftHand}" : "{RightHand}";
            _posAction = MakeAction("Position", "<XRController>" + h + "/devicePosition", "Vector3");
            _rotAction = MakeAction("Rotation", "<XRController>" + h + "/deviceRotation", "Quaternion");
            _trackedAction = MakeAction("IsTracked", "<XRController>" + h + "/isTracked", "Button");
            _gripAction = MakeAction("Grip", "<XRController>" + h + "/{Grip}", "Axis");
            _triggerAction = MakeAction("Trigger", "<XRController>" + h + "/{Trigger}", "Axis");
            _hapticAction = new InputAction("Haptic", InputActionType.PassThrough, "<XRController>" + h + "/{Haptic}");

            _mpb = new MaterialPropertyBlock();
            _grabPoint = new GameObject("Grab Point").transform;
            _grabPoint.SetParent(transform, false);

            BuildControllerHand();
            BuildTrackedHand();
            SetMode(Mode.None);
        }

        static InputAction MakeAction(string name, string binding, string type)
        {
            var a = new InputAction(name, InputActionType.Value, binding, expectedControlType: type);
            return a;
        }

        IEnumerable<InputAction> Actions()
        {
            yield return _posAction; yield return _rotAction; yield return _trackedAction;
            yield return _gripAction; yield return _triggerAction; yield return _hapticAction;
        }

        void OnEnable() { foreach (var a in Actions()) a.Enable(); }

        void OnDisable()
        {
            foreach (var a in Actions()) a.Disable();
            ForceRelease();
            SetHovered(null);
        }

        void OnDestroy() { foreach (var a in Actions()) a.Dispose(); }

        // ------------------------------------------------------------------ per frame

        void Update()
        {
            XRHand hand = default;
            bool handTracked = TryGetHand(out hand);
            bool controllerTracked = !handTracked && _trackedAction.IsPressed();

            SetMode(handTracked ? Mode.Hand : controllerTracked ? Mode.Controller : Mode.None);

            bool wantsGrab = false;
            switch (_mode)
            {
                case Mode.Hand: wantsGrab = UpdateHand(hand); break;
                case Mode.Controller: wantsGrab = UpdateController(); break;
            }

            UpdateGrab(wantsGrab);
        }

        bool TryGetHand(out XRHand hand)
        {
            hand = default;
            if (_subsystem == null || !_subsystem.running)
            {
                _subsystem = null;
                SubsystemManager.GetSubsystems(s_Subsystems);
                foreach (var s in s_Subsystems)
                    if (s.running) { _subsystem = s; break; }
            }
            if (_subsystem == null) return false;

            hand = side == HandSide.Left ? _subsystem.leftHand : _subsystem.rightHand;
            return hand.isTracked;
        }

        // ------------------------------------------------------------------ hand tracking

        bool UpdateHand(XRHand hand)
        {
            // Joints
            for (int i = 0; i < JointCount; i++)
            {
                var id = XRHandJointIDUtility.FromIndex(i);
                var joint = hand.GetJoint(id);
                if (joint.TryGetPose(out UnityEngine.Pose pose))
                {
                    _jointViz[i].gameObject.SetActive(id != XRHandJointID.Palm);
                    _jointViz[i].localPosition = pose.position;
                    _jointViz[i].localRotation = pose.rotation;
                    float r = joint.TryGetRadius(out float radius) && radius > 0f ? radius : 0.008f;
                    _jointViz[i].localScale = Vector3.one * (r * 2f);
                }
                else
                {
                    _jointViz[i].gameObject.SetActive(false);
                }
            }

            // Bones (cylinders between each joint and its parent)
            for (int i = 0; i < JointCount; i++)
            {
                var id = XRHandJointIDUtility.FromIndex(i);
                int parent = ParentIndex(id);
                Transform bone = _boneViz[i];
                if (bone == null) continue;
                if (parent < 0 || !_jointViz[i].gameObject.activeSelf || !_jointViz[parent].gameObject.activeSelf)
                {
                    bone.gameObject.SetActive(false);
                    continue;
                }
                Vector3 a = _jointViz[parent].localPosition, b = _jointViz[i].localPosition;
                Vector3 d = b - a;
                float len = d.magnitude;
                bone.gameObject.SetActive(len > 0.001f);
                if (len <= 0.001f) continue;
                bone.localPosition = (a + b) * 0.5f;
                bone.localRotation = Quaternion.FromToRotation(Vector3.up, d / len);
                bone.localScale = new Vector3(0.012f, len * 0.5f, 0.012f);
            }

            // Gestures
            Vector3 thumbTip = JointPos(XRHandJointID.ThumbTip);
            Vector3 indexTip = JointPos(XRHandJointID.IndexTip);
            Vector3 palm = JointPos(XRHandJointID.Palm);
            float pinch = Vector3.Distance(thumbTip, indexTip);
            float fist = (Vector3.Distance(JointPos(XRHandJointID.MiddleTip), palm)
                        + Vector3.Distance(JointPos(XRHandJointID.RingTip), palm)
                        + Vector3.Distance(JointPos(XRHandJointID.LittleTip), palm)) / 3f;

            bool holding = _gesture == Gesture.Pinch || _gesture == Gesture.Fist;
            bool pinching = pinch < (_gesture == Gesture.Pinch ? pinchRelease : pinchStart);
            bool fisting = fist < (_gesture == Gesture.Fist ? fistRelease : fistStart);

            if (!holding)
                _gesture = pinching ? Gesture.Pinch : fisting ? Gesture.Fist : Gesture.None;
            else if ((_gesture == Gesture.Pinch && !pinching) || (_gesture == Gesture.Fist && !fisting))
                _gesture = Gesture.None;

            // Grab point: between the fingers when pinching, at the palm otherwise.
            Quaternion palmRot = _jointViz[XRHandJointID.Palm.ToIndex()].localRotation;
            _grabPoint.localPosition = _gesture == Gesture.Pinch || (_gesture == Gesture.None && pinch < 0.06f)
                ? (thumbTip + indexTip) * 0.5f
                : palm;
            _grabPoint.localRotation = palmRot;

            return _gesture != Gesture.None;
        }

        Vector3 JointPos(XRHandJointID id) => _jointViz[id.ToIndex()].localPosition;

        static int ParentIndex(XRHandJointID id)
        {
            switch (id)
            {
                case XRHandJointID.Wrist:
                case XRHandJointID.Palm:
                    return -1;
                case XRHandJointID.ThumbMetacarpal:
                case XRHandJointID.IndexMetacarpal:
                case XRHandJointID.MiddleMetacarpal:
                case XRHandJointID.RingMetacarpal:
                case XRHandJointID.LittleMetacarpal:
                    return XRHandJointID.Wrist.ToIndex();
                default:
                    return id.ToIndex() - 1;
            }
        }

        // ------------------------------------------------------------------ controller

        bool UpdateController()
        {
            _controllerRoot.localPosition = _posAction.ReadValue<Vector3>();
            _controllerRoot.localRotation = _rotAction.ReadValue<Quaternion>();

            float grip = _gripAction.ReadValue<float>();
            float trigger = _triggerAction.ReadValue<float>();

            // Curl the fake fingers: index follows the trigger, the rest follow grip.
            float sign = side == HandSide.Right ? -1f : 1f;
            for (int f = 0; f < 5; f++)
            {
                float curl = f == 0 ? Mathf.Max(grip, trigger) * 0.6f : f == 1 ? trigger : grip;
                for (int s = 0; s < 3; s++)
                {
                    if (_fingerPivots[f, s] == null) continue;
                    float maxAngle = f == 0 ? 35f : 75f;
                    _fingerPivots[f, s].localRotation = Quaternion.Euler(0f, sign * curl * maxAngle, 0f);
                }
            }

            _grabPoint.localPosition = _controllerRoot.localPosition + _controllerRoot.localRotation * new Vector3(0f, 0f, 0.03f);
            _grabPoint.localRotation = _controllerRoot.localRotation;

            bool held = _gesture == Gesture.Grip;
            _gesture = grip > (held ? gripRelease : gripStart) ? Gesture.Grip : Gesture.None;
            return _gesture == Gesture.Grip;
        }

        // ------------------------------------------------------------------ grabbing

        bool _wasGrabbing;

        void UpdateGrab(bool wantsGrab)
        {
            if (_held == null)
                SetHovered(FindClosestGrabbable());

            // Only grab on the moment the gesture starts (not while already closed).
            if (wantsGrab && !_wasGrabbing && _hovered != null)
            {
                _held = _hovered;
                SetHovered(null);
                _held.BeginGrab(this);
                Pulse(0.5f, 0.06f);
            }
            else if (!wantsGrab && _held != null)
            {
                ForceRelease();
                Pulse(0.2f, 0.03f);
            }

            _wasGrabbing = wantsGrab;
        }

        VRGrabbable FindClosestGrabbable()
        {
            if (_mode == Mode.None) return null;

            Vector3 p = _grabPoint.position;
            int n = Physics.OverlapSphereNonAlloc(p, grabRadius, _overlap, grabLayers, QueryTriggerInteraction.Ignore);
            VRGrabbable best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var rb = _overlap[i].attachedRigidbody;
                if (rb == null) continue;
                var g = rb.GetComponent<VRGrabbable>();
                if (g == null || !g.isActiveAndEnabled) continue;
                float d = (_overlap[i].ClosestPoint(p) - p).sqrMagnitude;
                if (d < bestDist) { bestDist = d; best = g; }
            }
            return best;
        }

        void SetHovered(VRGrabbable g)
        {
            if (g == _hovered) return;
            if (_hovered != null) _hovered.SetHovered(false);
            _hovered = g;
            if (_hovered != null) _hovered.SetHovered(true);
        }

        /// <summary>Drops whatever this hand is holding.</summary>
        public void ForceRelease()
        {
            if (_held == null) return;
            var g = _held;
            _held = null;
            g.EndGrab(this);
        }

        void Pulse(float amplitude, float duration)
        {
            if (_mode != Mode.Controller) return;
            OpenXRInput.SendHapticImpulse(_hapticAction, amplitude, duration);
        }

        // ------------------------------------------------------------------ visuals

        void SetMode(Mode mode)
        {
            if (mode == _mode && _controllerRoot.gameObject.activeSelf == (mode == Mode.Controller)) return;
            if (mode != _mode)
            {
                ForceRelease();
                _gesture = Gesture.None;
                _wasGrabbing = false;
                if (mode == Mode.None) SetHovered(null);
            }
            _mode = mode;
            _controllerRoot.gameObject.SetActive(mode == Mode.Controller);
            _jointsRoot.gameObject.SetActive(mode == Mode.Hand);
        }

        Transform MakePart(PrimitiveType type, string name, Transform parent)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            Destroy(go.GetComponent<Collider>()); // hands shouldn't push things around by accident
            go.transform.SetParent(parent, false);
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, handColor);
            r.SetPropertyBlock(_mpb);
            return go.transform;
        }

        void BuildTrackedHand()
        {
            _jointsRoot = new GameObject("Tracked Hand").transform;
            _jointsRoot.SetParent(transform, false);
            _jointViz = new Transform[JointCount];
            _boneViz = new Transform[JointCount];
            for (int i = 0; i < JointCount; i++)
            {
                var id = XRHandJointIDUtility.FromIndex(i);
                _jointViz[i] = MakePart(PrimitiveType.Sphere, id.ToString(), _jointsRoot);
                if (ParentIndex(id) >= 0)
                    _boneViz[i] = MakePart(PrimitiveType.Cylinder, id + " Bone", _jointsRoot);
            }
        }

        /// <summary>
        /// Simple blocky hand used while holding a controller. Built around the OpenXR grip pose:
        /// fingers point forward (+Z), thumb on top (+Y), palm facing the controller.
        /// </summary>
        void BuildControllerHand()
        {
            _controllerRoot = new GameObject("Controller Hand").transform;
            _controllerRoot.SetParent(transform, false);

            float palmSide = side == HandSide.Right ? 1f : -1f; // back of the hand is on this X side
            var palm = MakePart(PrimitiveType.Cube, "Palm", _controllerRoot);
            palm.localPosition = new Vector3(palmSide * 0.02f, -0.01f, -0.03f);
            palm.localScale = new Vector3(0.025f, 0.085f, 0.09f);

            _fingerPivots = new Transform[5, 3];
            float[] fingerY = { 0.035f, 0.025f, 0.005f, -0.015f, -0.035f }; // thumb, index, middle, ring, little
            float[] fingerScale = { 0.8f, 1f, 1.08f, 1f, 0.82f };
            float[] segLen = { 0.035f, 0.025f, 0.02f };

            for (int f = 0; f < 5; f++)
            {
                Transform parent = _controllerRoot;
                Vector3 start = f == 0
                    ? new Vector3(palmSide * 0.0f, 0.04f, -0.035f)   // thumb starts near the wrist, on top
                    : new Vector3(palmSide * 0.02f, fingerY[f], 0.015f);
                for (int s = 0; s < 3; s++)
                {
                    var pivot = new GameObject($"Finger{f}_{s}").transform;
                    pivot.SetParent(parent, false);
                    pivot.localPosition = s == 0 ? start : new Vector3(0f, 0f, segLen[s - 1] * fingerScale[f]);
                    _fingerPivots[f, s] = pivot;

                    float len = segLen[s] * fingerScale[f];
                    var seg = MakePart(PrimitiveType.Cube, "Segment", pivot);
                    seg.localPosition = new Vector3(0f, 0f, len * 0.5f);
                    seg.localScale = new Vector3(0.018f, 0.017f, len);
                    parent = pivot;
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            if (_grabPoint == null) return;
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_grabPoint.position, grabRadius);
        }
    }
}
