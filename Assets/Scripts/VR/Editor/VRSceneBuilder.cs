using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Classwork18.VR.Editor
{
    /// <summary>
    /// Menu: Tools > VR Classwork
    ///   1. Import XRI Samples  -> imports Starter Assets + Hands Interaction Demo (+ XR Hands visualizer)
    ///   2. Build XRI Scene     -> puts the official XRI hands rig in the open scene, plus floor,
    ///                             table and grabbable cubes. Replaces the old hand-made rig.
    /// Everything done by step 2 can be undone with Ctrl+Z.
    /// </summary>
    public static class VRSceneBuilder
    {
        const string MaterialFolder = "Assets/Materials";
        const string XriPackage = "com.unity.xr.interaction.toolkit";
        const string HandsPackage = "com.unity.xr.hands";

        // ------------------------------------------------------------------ step 1

        [MenuItem("Tools/VR Classwork/1. Import XRI Samples", priority = 1)]
        public static void ImportSamples()
        {
            var wanted = new List<(string package, string sample)>
            {
                (XriPackage, "Starter Assets"),
                (HandsPackage, "HandVisualizer"),
                (XriPackage, "Hands Interaction Demo"),
            };

            var log = new List<string>();
            foreach (var (package, sampleName) in wanted)
            {
                var sample = Sample.FindByPackage(package, null)
                    .FirstOrDefault(s => s.displayName.Replace(" ", "").Contains(sampleName.Replace(" ", "")));

                if (string.IsNullOrEmpty(sample.displayName))
                    log.Add($"Not found: {sampleName} ({package}). Is the package installed?");
                else if (sample.isImported)
                    log.Add($"Already imported: {sample.displayName}");
                else
                    log.Add((sample.Import(Sample.ImportOptions.OverridePreviousImports) ? "Imported: " : "FAILED: ") + sample.displayName);
            }

            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("XRI Samples", string.Join("\n", log) +
                "\n\nIf Unity shows a Project Validation popup, click Fix All. Then run step 2.", "OK");
        }

        // ------------------------------------------------------------------ step 2

        [MenuItem("Tools/VR Classwork/2. Build XRI Scene", priority = 2)]
        public static void Build()
        {
            var rigPrefab = FindPrefab("XR Origin Hands (XR Rig)") ?? FindPrefab("XR Origin (XR Rig)");
            if (rigPrefab == null)
            {
                EditorUtility.DisplayDialog("VR Scene",
                    "Couldn't find the XRI rig prefab.\nRun 'Tools > VR Classwork > 1. Import XRI Samples' first.", "OK");
                return;
            }

            Undo.SetCurrentGroupName("Build XRI Scene");
            int group = Undo.GetCurrentGroup();

            // Remove the old hand-made rig (the one with VRLocomotion).
            foreach (var origin in Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (origin.GetComponent<VRLocomotion>() != null)
                    Undo.DestroyObjectImmediate(origin.gameObject);

            if (Object.FindAnyObjectByType<XROrigin>() != null)
            {
                EditorUtility.DisplayDialog("VR Scene", "This scene already has an XRI rig. Delete it first if you want to rebuild.", "OK");
                Undo.CollapseUndoOperations(group);
                return;
            }

            // Turn off any other camera so only the headset camera renders.
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (cam.gameObject.scene != EditorSceneManager.GetActiveScene()) continue;
                Undo.RecordObject(cam.gameObject, "Disable old camera");
                cam.gameObject.SetActive(false);
            }

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            Undo.RegisterCreatedObjectUndo(rig, "XRI Rig");
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            EnsureInteractionManager();
            EnsureInputActionManager();
            BuildEnvironment();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Selection.activeGameObject = rig;
            Debug.Log($"[VR Classwork] Added '{rigPrefab.name}'. Save the scene (Ctrl+S) and press Play with the headset connected.");
        }

        static GameObject FindPrefab(string name)
        {
            foreach (var guid in AssetDatabase.FindAssets($"\"{name}\" t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(path) == name)
                    return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return null;
        }

        static void EnsureInteractionManager()
        {
            if (Object.FindAnyObjectByType<XRInteractionManager>() != null) return;
            var go = new GameObject("XR Interaction Manager");
            Undo.RegisterCreatedObjectUndo(go, "XR Interaction Manager");
            go.AddComponent<XRInteractionManager>();
        }

        /// <summary>The rig's controller/hand input only works if its input actions are enabled.</summary>
        static void EnsureInputActionManager()
        {
            if (Object.FindAnyObjectByType<InputActionManager>() != null) return;

            var guid = AssetDatabase.FindAssets("\"XRI Default Input Actions\" t:InputActionAsset").FirstOrDefault();
            if (guid == null) return;

            var go = new GameObject("Input Action Manager");
            Undo.RegisterCreatedObjectUndo(go, "Input Action Manager");
            var mgr = go.AddComponent<InputActionManager>();
            mgr.actionAssets = new List<InputActionAsset>
            {
                AssetDatabase.LoadAssetAtPath<InputActionAsset>(AssetDatabase.GUIDToAssetPath(guid))
            };
        }

        // ------------------------------------------------------------------ environment

        static void BuildEnvironment()
        {
            // Floor (you can teleport onto it)
            var floor = GameObject.Find("Floor");
            if (floor == null)
            {
                floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
                Undo.RegisterCreatedObjectUndo(floor, "Floor");
                floor.name = "Floor";
                floor.transform.localScale = new Vector3(5f, 1f, 5f); // 50 x 50 m
                floor.GetComponent<Renderer>().sharedMaterial = GetMaterial("VR_Floor", new Color(0.55f, 0.58f, 0.6f));
                floor.isStatic = true;
            }
            if (floor.GetComponent<TeleportationArea>() == null)
            {
                var area = Undo.AddComponent<TeleportationArea>(floor);
                int teleportLayer = InteractionLayerMask.GetMask("Teleport");
                if (teleportLayer != 0) area.interactionLayers = teleportLayer;
            }

            // Table
            if (GameObject.Find("Table") == null)
            {
                var table = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(table, "Table");
                table.name = "Table";
                table.transform.position = new Vector3(0f, 0.4f, 0.8f);
                table.transform.localScale = new Vector3(1.2f, 0.8f, 0.6f);
                table.GetComponent<Renderer>().sharedMaterial = GetMaterial("VR_Table", new Color(0.45f, 0.3f, 0.2f));
                table.isStatic = true;
            }

            // Cubes: convert the old ones, or make new ones.
            var oldGrabbables = Object.FindObjectsByType<VRGrabbable>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (oldGrabbables.Length > 0)
            {
                foreach (var old in oldGrabbables)
                {
                    var go = old.gameObject;
                    Undo.DestroyObjectImmediate(old);
                    MakeGrabbable(go);
                }
                return;
            }

            Color[] colors = { new Color(0.9f, 0.25f, 0.25f), new Color(0.25f, 0.6f, 0.95f), new Color(0.3f, 0.8f, 0.4f) };
            for (int i = 0; i < colors.Length; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Undo.RegisterCreatedObjectUndo(cube, "Cube");
                cube.name = "Grab Cube " + (i + 1);
                cube.transform.position = new Vector3(-0.3f + i * 0.3f, 0.86f, 0.75f);
                cube.transform.localScale = Vector3.one * 0.1f;
                cube.GetComponent<Renderer>().sharedMaterial = GetMaterial("VR_Cube" + (i + 1), colors[i]);
                MakeGrabbable(cube);
            }
        }

        static void MakeGrabbable(GameObject go)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(go);
            rb.mass = 0.5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var grab = go.GetComponent<XRGrabInteractable>();
            if (grab == null) grab = Undo.AddComponent<XRGrabInteractable>(go);
            grab.useDynamicAttach = true; // hold it where you grabbed it, not snapped to its center
            grab.throwOnDetach = true;
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
