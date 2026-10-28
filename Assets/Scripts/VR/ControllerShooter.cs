using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Classwork18.VR
{
    /// <summary>
    /// B (right controller): spawns a sphere and shoots it where the controller points.
    /// Y (left controller): destroys every shot sphere and every grabbable object (the cubes).
    /// Buttons can be changed in the Inspector.
    /// </summary>
    public class ControllerShooter : MonoBehaviour
    {
        [Header("Shooting")]
        [Tooltip("Where spheres come from and which way they fly (use the rig's 'Right Controller').")]
        public Transform shootOrigin;
        [Tooltip("Optional. If empty, a plain sphere is created.")]
        public GameObject spherePrefab;
        public Material sphereMaterial;
        public float sphereSize = 0.1f;
        public float shootSpeed = 8f;

        [Header("Destroy")]
        [Tooltip("Also destroy every object with an XR Grab Interactable (the cubes).")]
        public bool destroyGrabbables = true;

        [Header("Buttons")]
        public InputActionProperty shootButton = new InputActionProperty(
            new InputAction("Shoot", InputActionType.Button, "<XRController>{RightHand}/{SecondaryButton}"));
        public InputActionProperty destroyButton = new InputActionProperty(
            new InputAction("Destroy All", InputActionType.Button, "<XRController>{LeftHand}/{SecondaryButton}"));

        readonly List<GameObject> _spawned = new List<GameObject>();

        void OnEnable()
        {
            // Embedded actions need enabling by hand; actions from an asset are enabled by the Input Action Manager.
            if (shootButton.reference == null) shootButton.action?.Enable();
            if (destroyButton.reference == null) destroyButton.action?.Enable();
        }

        void OnDisable()
        {
            if (shootButton.reference == null) shootButton.action?.Disable();
            if (destroyButton.reference == null) destroyButton.action?.Disable();
        }

        void Update()
        {
            if (shootButton.action != null && shootButton.action.WasPressedThisFrame())
                Shoot();

            if (destroyButton.action != null && destroyButton.action.WasPressedThisFrame())
                DestroyAll();
        }

        public void Shoot()
        {
            if (shootOrigin == null) return;

            Vector3 dir = shootOrigin.forward;
            Vector3 pos = shootOrigin.position + dir * 0.15f; // start a bit ahead of the controller

            GameObject sphere;
            if (spherePrefab != null)
            {
                sphere = Instantiate(spherePrefab, pos, Quaternion.identity);
            }
            else
            {
                sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphere.transform.SetPositionAndRotation(pos, Quaternion.identity);
                sphere.transform.localScale = Vector3.one * sphereSize;
                if (sphereMaterial != null) sphere.GetComponent<Renderer>().sharedMaterial = sphereMaterial;
            }
            sphere.name = "Shot Sphere";

            var rb = sphere.GetComponent<Rigidbody>();
            if (rb == null) rb = sphere.AddComponent<Rigidbody>();
            rb.mass = 0.2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.linearVelocity = dir * shootSpeed;

            _spawned.Add(sphere);
        }

        public void DestroyAll()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();

            if (!destroyGrabbables) return;
            foreach (var grab in FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None))
                Destroy(grab.gameObject);
        }
    }
}
