using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace raspichu.vrc_tools.editor
{
    public class MaterialPropertyFinder : EditorWindow
    {
        // Inputs the user can edit
        private GameObject targetObject;
        private bool onlyActive = true;
        private string propertyName = "";

        // Filters actually used in the last search
        private GameObject searchTarget;
        private bool searchOnlyActive;
        private string searchPropertyName = "";

        private Vector2 scroll;

        private List<Material> results;

        [MenuItem("Tools/Pichu/Material Property Finder")]
        public static void ShowWindow()
        {
            var window = GetWindow<MaterialPropertyFinder>("Material Property Finder");
            if (Selection.activeGameObject != null)
                window.targetObject = Selection.activeGameObject;
        }

        [MenuItem("GameObject/Pichu/Material Property Finder", false, 49)]
        public static void ShowWindowContext(MenuCommand command)
        {
            var window = GetWindow<MaterialPropertyFinder>("Material Property Finder");
            window.targetObject = command.context as GameObject;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Material Finder", EditorStyles.boldLabel);

            // Wrap the input with Undo
            EditorGUI.BeginChangeCheck();

            targetObject = (GameObject)
                EditorGUILayout.ObjectField(
                    "Target Object",
                    targetObject,
                    typeof(GameObject),
                    true
                );
            onlyActive = EditorGUILayout.Toggle("Only Active Objects", onlyActive);
            propertyName = EditorGUILayout.TextField("Property Name", propertyName);

            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(this, "Change Material Property Finder");
                EditorUtility.SetDirty(this);
            }

            if (GUILayout.Button("Search") && targetObject != null)
            {
                // Copy current input values into search filters
                searchTarget = targetObject;
                searchOnlyActive = onlyActive;
                searchPropertyName = propertyName;

                RefreshResults();
            }

            EditorGUILayout.Space();

            // Add "Results" label
            EditorGUILayout.LabelField("Results", EditorStyles.boldLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (results != null && results.Count > 0)
            {
                foreach (var mat in results)
                {
                    if (mat == null)
                        continue;
                    if (
                        string.IsNullOrEmpty(searchPropertyName)
                        || !mat.HasProperty(searchPropertyName)
                    )
                    {
                        continue;
                    }
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(mat, typeof(Material), false);

                    Shader shader = mat.shader;
                    int propertyCount = ShaderUtil.GetPropertyCount(shader);
                    for (int i = 0; i < propertyCount; i++)
                    {
                        if (ShaderUtil.GetPropertyName(shader, i) != searchPropertyName)
                        {
                            continue;
                        }

                        var type = ShaderUtil.GetPropertyType(shader, i);
                        switch (type)
                        {
                            case ShaderUtil.ShaderPropertyType.Color:
                                Color newColor = EditorGUILayout.ColorField(
                                    mat.GetColor(searchPropertyName)
                                );
                                if (newColor != mat.GetColor(searchPropertyName))
                                {
                                    Undo.RecordObject(mat, "Change Material Color");
                                    mat.SetColor(searchPropertyName, newColor);
                                    EditorUtility.SetDirty(mat);
                                }
                                break;

                            case ShaderUtil.ShaderPropertyType.Vector:
                                Vector4 newVec = EditorGUILayout.Vector4Field(
                                    "",
                                    mat.GetVector(searchPropertyName)
                                );
                                if (newVec != mat.GetVector(searchPropertyName))
                                {
                                    Undo.RecordObject(mat, "Change Material Vector");
                                    mat.SetVector(searchPropertyName, newVec);
                                    EditorUtility.SetDirty(mat);
                                }
                                break;

                            case ShaderUtil.ShaderPropertyType.Float:
                            case ShaderUtil.ShaderPropertyType.Range:
                                float newFloat = EditorGUILayout.FloatField(
                                    mat.GetFloat(searchPropertyName)
                                );
                                if (
                                    Mathf.Abs(newFloat - mat.GetFloat(searchPropertyName)) > 0.0001f
                                )
                                {
                                    Undo.RecordObject(mat, "Change Material Float");
                                    mat.SetFloat(searchPropertyName, newFloat);
                                    EditorUtility.SetDirty(mat);
                                }
                                break;

                            case ShaderUtil.ShaderPropertyType.TexEnv:
                                Texture newTex = (Texture)
                                    EditorGUILayout.ObjectField(
                                        mat.GetTexture(searchPropertyName),
                                        typeof(Texture),
                                        false
                                    );
                                if (newTex != mat.GetTexture(searchPropertyName))
                                {
                                    Undo.RecordObject(mat, "Change Material Texture");
                                    mat.SetTexture(searchPropertyName, newTex);
                                    EditorUtility.SetDirty(mat);
                                }
                                break;
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "No results. Press 'Search' to find materials.",
                    MessageType.Info
                );
            }

            EditorGUILayout.EndScrollView();
        }

        private void RefreshResults()
        {
            results = new List<Material>();
            if (searchTarget == null)
                return;

            var renderers = searchTarget.GetComponentsInChildren<Renderer>(true);

            foreach (var rend in renderers)
            {
                if (searchOnlyActive && !rend.gameObject.activeInHierarchy)
                    continue;

                foreach (var mat in rend.sharedMaterials)
                {
                    if (mat == null)
                        continue;

                    if (
                        !string.IsNullOrEmpty(searchPropertyName)
                        && !mat.HasProperty(searchPropertyName)
                    )
                        continue;

                    if (!results.Contains(mat))
                        results.Add(mat);
                }
            }
        }
    }
}
