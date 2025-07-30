using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace raspichu.vrc_tools.editor
{
    public class MaterialReplacerWindow : EditorWindow
    {
        private GameObject targetObject;

        // Tracks all usages of each material
        private Dictionary<Material, List<MaterialUsage>> materialUsages = new Dictionary<Material, List<MaterialUsage>>();

        // Current replacements
        private Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();

        private Vector2 scroll;

        private class MaterialUsage
        {
            public SkinnedMeshRenderer renderer;
            public int index;
            public Material originalMaterial;
        }

        [MenuItem("Window/Pichu/Material replacer")]
        private static void OpenWindowFromTopMenu()
        {
            var selected = Selection.activeGameObject;
            ShowWindow(selected);
        }

        [MenuItem("GameObject/Pichu/Material replacer", false, 0)]
        private static void OpenWindowFromContext(MenuCommand command)
        {
            var selected = command.context as GameObject;
            ShowWindow(selected);
        }

        private static void ShowWindow(GameObject obj)
        {
            var window = MaterialReplacerWindow.GetWindow<MaterialReplacerWindow>("Material Replacer");
            window.targetObject = obj;
            window.FindMaterials();
        }

        private void FindMaterials()
        {
            materialUsages.Clear();
            materialMap.Clear();

            var renderers = targetObject.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (var renderer in renderers)
            {
                var mats = renderer.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var mat = mats[i];
                    if (mat == null) continue;

                    if (!materialUsages.ContainsKey(mat))
                    {
                        materialUsages[mat] = new List<MaterialUsage>();
                        materialMap[mat] = null;
                    }

                    materialUsages[mat].Add(new MaterialUsage
                    {
                        renderer = renderer,
                        index = i,
                        originalMaterial = mat
                    });
                }
            }
        }

        private void OnGUI()
        {
            GameObject newTarget = (GameObject)EditorGUILayout.ObjectField("Target", targetObject, typeof(GameObject), true);
            if (newTarget != targetObject)
            {
                targetObject = newTarget;
                FindMaterials(); // Refresh the materials for the new object
            }
            EditorGUILayout.Space();

            scroll = EditorGUILayout.BeginScrollView(scroll);

            foreach (var originalMat in materialUsages.Keys.ToList())
            {
                EditorGUILayout.BeginHorizontal("box");

                // Original Material (disabled)
                EditorGUILayout.LabelField("Original", GUILayout.Width(60));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.ObjectField(originalMat, typeof(Material), false, GUILayout.Width(180));
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(10);

                // Replacement Material
                EditorGUILayout.LabelField("Replace", GUILayout.Width(60));
                var newMat = (Material)EditorGUILayout.ObjectField(materialMap[originalMat], typeof(Material), false, GUILayout.ExpandWidth(true));
                if (newMat != materialMap[originalMat])
                {
                    materialMap[originalMat] = newMat;
                    Debug.Log($"Replacing {originalMat.name} -> {newMat?.name}");
                    ReplaceMaterial(originalMat, newMat);
                }

                EditorGUILayout.EndHorizontal();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
        }

        private void ReplaceMaterial(Material original, Material replacement)
        {
            if (!materialUsages.ContainsKey(original) || replacement == null) return;

            foreach (var usage in materialUsages[original])
            {
                var mats = usage.renderer.sharedMaterials;
                if (mats[usage.index] != replacement)
                {
                    Undo.RecordObject(usage.renderer, "Replace Material");
                    mats[usage.index] = replacement;
                    usage.renderer.sharedMaterials = mats;
                    EditorUtility.SetDirty(usage.renderer);
                }
            }
        }
    }
}
