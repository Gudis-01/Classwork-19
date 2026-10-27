using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;

namespace Classwork18.VR.Editor
{
    /// <summary>
    /// Menu: Tools > VR Classwork > Build VR Scene
    /// Adds an XR rig (head + both hands + movement), a floor, a table and grabbable cubes
    /// to the currently open scene. Everything can be undone with Ctrl+Z.
    /// </summary>
    public static class VRSceneBuilder
    {
        const string MaterialFolder = "Assets/Materials";

        [MenuItem("Tools/VR Classwork/Build VR Scene")]
        public static void Build()
        {
            if (Object.FindAnyObjectByType<XROrigin>() != null)
            {
                EditorUtility.DisplayDialog("VR Scene", "This scene already has an XR Origin. Delete it first if you want to rebuild.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Build VR Scene");
            int group = Undo.GetCurrentGroup();

            // Turn off the old desktop camera (kept so you can go back to it).
            foreach (var cam in Camera.allCameras)
            {
                if (cam.gameObject.scene != EditorSceneManager.GetActiveScene()) continue; // skip editor cameras
                if (cam.GetComponentInParent<XROrigin>() != null) continue;
                Undo.RecordObject(cam.gameObject, "Disable old camera");
                cam.gameObject.SetActive(false);
            }

            BuildRig();
            BuildEnvironment();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[VR Classwork] VR scene built. Save the scene (Ctrl+S) and press Play with the headset connected.");
        }

        static void BuildRig()
        {
            // XR Origin (root) -> Camera Offset -> Main Camera / Left Hand / Right Hand
            var root = new GameObject("XR Origin");
            Undo.RegisterCreatedObjectUndo(root, "XR Origin");

            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(root.transform, false);

            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.nearClipPlane = 0.01f;
            camGo.AddComponent<AudioListener>();

            var tpd = camGo.AddComponent<TrackedPoseDriver>();
            tpd.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
            tpd.positionInput = new InputActionProperty(new InputAction("Head Position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));
            tpd.rotationInput = new InputActionProperty(new InputAction("Head Rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion"));
            tpd.trackingStateInput = new InputActionProperty(new InputAction("Head Tracking State", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer"));

            var origin = root.AddComponent<XROrigin>();
            origin.Origin = root;
            origin.CameraFloorOffsetObject = offset;
            origin.Camera = cam;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 1.6f;

            var cc = root.AddComponent<CharacterController>();
            cc.radius = 0.2f;
            cc.height = 1.7f;
            cc.center = new Vector3(0f, 0.85f, 0f);
            cc.minMoveDistance = 0f;

            var loco = root.AddComponent<VRLocomotion>();
            loco.head = camGo.transform;

            MakeHand(offset.transform, HandSide.Left);
            MakeHand(offset.transform, HandSide.Right);
        }

        static void MakeHand(Transform parent, HandSide side)
        {
            var go = new GameObject(side + " Hand");
            go.transform.SetParent(parent, false);
            var hand = go.AddComponent<VRHand>();
            hand.side = side;
        }

        static void BuildEnvironment()
        {
            var floorMat = GetMaterial("VR_Floor", new Color(0.55f, 0.58f, 0.6f));
            var tableMat = GetMaterial("VR_Table", new Color(0.45f, 0.3f, 0.2f));
            Color[] cubeColors =
            {
                new Color(0.9f, 0.25f, 0.25f),
                new Color(0.25f, 0.6f, 0.95f),
                new Color(0.3f, 0.8f, 0.4f),
            };

            if (GameObject.Find("Floor") == null)
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                Undo.RegisterCreatedObjectUndo(floor, "Floor");
                floor.name = "Floor";
                floor.transform.localScale = new Vector3(5f, 1f, 5f); // 50 x 50 m
                floor.GetComponent<Renderer>().sharedMaterial = floorMat;
                floor.isStatic = true;
            }

            var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Undo.RegisterCreatedObjectUndo(table, "Table");
            table.name = "Table";
            table.transform.position = new Vector3(0f, 0.4f, 0.8f);
            table.transform.localScale = new Vector3(1.2f, 0.8f, 0.6f);
            table.GetComponent<Renderer>().sharedMaterial = tableMat;
            table.isStatic = true;

            for (int i = 0; i < cubeColors.Length; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(cube, "Cube");
                cube.name = "Grab Cube " + (i + 1);
                cube.transform.position = new Vector3(-0.3f + i * 0.3f, 0.86f, 0.75f);
                cube.transform.localScale = Vector3.one * 0.1f;
                cube.GetComponent<Renderer>().sharedMaterial = GetMaterial("VR_Cube" + (i + 1), cubeColors[i]);

                var rb = cube.AddComponent<Rigidbody>();
                rb.mass = 0.5f;
                cube.AddComponent<VRGrabbable>();
            }
        }

        static Material GetMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            if (!AssetDatabase.IsValidFolder(MaterialFolder))
                AssetDatabase.CreateFolder("Assets", "Materials");

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            else mat.color = color;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}
