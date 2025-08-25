using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace raspichu.vrc_tools.editor
{
    public class MaterialReplacerWindow : EditorWindow
    {
        private List<GameObject> targetObjects = new List<GameObject>();

        // Tracks all usages of each material
        private Dictionary<Material, List<MaterialUsage>> materialUsages = new Dictionary<Material, List<MaterialUsage>>();

        // Current replacements
        private Dictionary<Material, Material> materialMap = new Dictionary<Material, Material>();

        private Vector2 scroll;

        private GUIStyle headerStyle;       
        private GUIStyle assetStyle;
        private GUIStyle buttonStyle;

        SerializedObject so;
        SerializedProperty targetsProp;

        private void OnEnable()
        {
            so = new SerializedObject(this);
            targetsProp = so.FindProperty("targetObjects");
        }

        private class MaterialUsage
        {
            public SkinnedMeshRenderer renderer;
            public int index;
            public Material originalMaterial;
        }

        [MenuItem("Window/Pichu/Material replacer")]
        private static void OpenWindowFromTopMenu()
        {
            var selected = Selection.gameObjects.ToList();
            ShowWindow(selected);
        }

        [MenuItem("GameObject/Pichu/Material replacer", false, 0)]
        private static void OpenWindowFromContext(MenuCommand command)
        {
            // var selected = new List<GameObject> { command.context as GameObject };
            var selected = Selection.gameObjects.ToList();
            if (selected.Count == 0 && command.context is GameObject go)
            {
                selected.Add(go);
            }
            ShowWindow(selected);
        }

        private static void ShowWindow(List<GameObject> objs)
        {
            var window = MaterialReplacerWindow.GetWindow<MaterialReplacerWindow>("Material Replacer");
            window.targetObjects = objs;
            window.FindMaterials();
        }

        private void FindMaterials()
        {
            materialUsages.Clear();
            materialMap.Clear();

            foreach (var obj in targetObjects)
            {
                if (obj == null) continue;

                var renderers = obj.GetComponentsInChildren<SkinnedMeshRenderer>();
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
        }

        private void EnsureStyles()
        {
            if (headerStyle == null)
                headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14, richText = true };
            if (assetStyle == null)
                assetStyle = new GUIStyle(EditorStyles.label) { wordWrap = true };
            if (buttonStyle == null)
                buttonStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold };
        }

        private void OnGUI()
        {
            Event evt = Event.current;

            EnsureStyles();

            Rect dropArea = EditorGUILayout.GetControlRect(false, 20, GUILayout.ExpandWidth(true));
            GUI.Label(dropArea, "Targets <size=10>(drag and drop)</size>", headerStyle);

            if ((evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform) && dropArea.Contains(evt.mousePosition))
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    // Empty targets
                    targetObjects = new List<GameObject>();
                    foreach (Object dragged in DragAndDrop.objectReferences)
                    {
                        GameObject go = dragged as GameObject;
                        targetObjects.Add(go);
                    }
                    FindMaterials();
                }
                evt.Use();
            }

            int removeIndex = -1;
            bool changed = false;

            for (int i = 0; i < targetObjects.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                GameObject newTarget = (GameObject)EditorGUILayout.ObjectField(targetObjects[i], typeof(GameObject), true);
                if (newTarget != targetObjects[i])
                {
                    targetObjects[i] = newTarget;
                    changed = true;
                }

                if (i == targetObjects.Count - 1)
                {
                    if (GUILayout.Button("+", GUILayout.Width(30)))
                    {
                        targetObjects.Add(null);
                        changed = true;
                    }
                }

                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    removeIndex = i;
                    changed = true;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (removeIndex >= 0)
                targetObjects.RemoveAt(removeIndex);

            if (changed)
                FindMaterials(); // Reload if something changes

            if (GUILayout.Button("Refresh Materials"))
                FindMaterials();

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
