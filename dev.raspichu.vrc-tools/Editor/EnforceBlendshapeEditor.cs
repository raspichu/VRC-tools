using System.Collections.Generic;
using System.Linq;
using raspichu.vrc_tools.component;
using UnityEditor;
using UnityEngine;

namespace raspichu.vrc_tools.editor
{
    [CustomEditor(typeof(EnforceBlendshape))]
    public class EnforceBlendshapeEditor : Editor
    {
        private SerializedObject serializedEnforceBlendshape;
        private SerializedProperty skinnedMeshRendererProp;
        private SerializedProperty blendShapeSelectionsProp;

        private string blendShapeSearch = "";
        private bool filterActiveWeights = false; // Added filter toggle state

        private Vector2 scrollPosition;
        private bool showBlendShapeList = true;

        private List<EnforceBlendshape.BlendShapeSelection> allBlendShapeSelections =
            new List<EnforceBlendshape.BlendShapeSelection>();

        private void OnEnable()
        {
            serializedEnforceBlendshape = new SerializedObject(target);
            skinnedMeshRendererProp = serializedEnforceBlendshape.FindProperty(
                "skinnedMeshRenderer"
            );
            blendShapeSelectionsProp = serializedEnforceBlendshape.FindProperty(
                "blendShapeSelections"
            );

            UpdateBlendShapeSelections();
        }

        public override void OnInspectorGUI()
        {
            serializedEnforceBlendshape.Update();

            // Start checking for changes
            EditorGUI.BeginChangeCheck();

            // Draw the SkinnedMeshRenderer field
            EditorGUILayout.PropertyField(skinnedMeshRendererProp);

            if (skinnedMeshRendererProp.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox(
                    "Skinned Mesh Renderer is not assigned.",
                    MessageType.Warning
                );
                return;
            }

            // Layout horizontal to place the button next to the search bar
            EditorGUILayout.BeginHorizontal();
            blendShapeSearch = EditorGUILayout.TextField("Search BlendShapes", blendShapeSearch);
            filterActiveWeights = GUILayout.Toggle(
                filterActiveWeights,
                "Weight > 0",
                "Button",
                GUILayout.Width(80)
            );
            EditorGUILayout.EndHorizontal();

            showBlendShapeList = EditorGUILayout.Foldout(
                showBlendShapeList,
                "BlendShapes List",
                true
            );
            if (showBlendShapeList)
            {
                // Container sub-box
                EditorGUILayout.BeginVertical(GUI.skin.box);

                // Header with simulated column titles
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("BlendShape Name", EditorStyles.boldLabel);
                // MINIMAL FIX: Added Weight header label
                EditorGUILayout.LabelField("Weight", EditorStyles.boldLabel, GUILayout.Width(50));
                EditorGUILayout.LabelField("Enable", EditorStyles.boldLabel, GUILayout.Width(60));
                EditorGUILayout.LabelField(
                    "Apply Default",
                    EditorStyles.boldLabel,
                    GUILayout.Width(90)
                );
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(3);

                // Scroll view start
                scrollPosition = EditorGUILayout.BeginScrollView(
                    scrollPosition,
                    GUILayout.Height(250)
                );

                // Get reference to renderer for checking weights
                var smr = skinnedMeshRendererProp.objectReferenceValue as SkinnedMeshRenderer;

                for (int i = 0; i < blendShapeSelectionsProp.arraySize; i++)
                {
                    var blendShapeProp = blendShapeSelectionsProp.GetArrayElementAtIndex(i);
                    var blendShapeNameProp = blendShapeProp.FindPropertyRelative("blendShapeName");
                    var isSelectedProp = blendShapeProp.FindPropertyRelative("isSelected");
                    var applyAsDefaultProp = blendShapeProp.FindPropertyRelative("applyAsDefault");

                    // Filter by search text
                    bool matchesSearch = blendShapeNameProp
                        .stringValue.ToLower()
                        .Contains(blendShapeSearch.ToLower());

                    // Filter by weight if toggle is active
                    bool matchesWeight =
                        !filterActiveWeights || (smr != null && smr.GetBlendShapeWeight(i) > 0f);

                    if (matchesSearch && matchesWeight)
                    {
                        EditorGUILayout.BeginHorizontal();

                        // Column 1: BlendShape Name (takes remaining space on the left)
                        EditorGUILayout.LabelField(blendShapeNameProp.stringValue);

                        // Get and display current weight directly from SkinnedMeshRenderer
                        float currentWeight = smr != null ? smr.GetBlendShapeWeight(i) : 0f;
                        EditorGUILayout.LabelField(
                            currentWeight.ToString("F0"),
                            GUILayout.Width(50)
                        );

                        // Column 2: "Enable" checkbox centered in its 60px slot
                        EditorGUILayout.BeginHorizontal(GUILayout.Width(60));
                        GUILayout.Space(15); // Tiny offset to visually center the toggle under the "Enable" text
                        isSelectedProp.boolValue = EditorGUILayout.Toggle(
                            isSelectedProp.boolValue,
                            GUILayout.Width(20)
                        );
                        EditorGUILayout.EndHorizontal();

                        // Column 3: "Apply Default" checkbox centered and grayed out if "Enable" is false
                        EditorGUILayout.BeginHorizontal(GUILayout.Width(90));
                        GUILayout.Space(32); // Offset to center the toggle under "Apply Default" text

                        // Start the grayed-out group if the main checkbox is not selected
                        EditorGUI.BeginDisabledGroup(!isSelectedProp.boolValue);

                        applyAsDefaultProp.boolValue = EditorGUILayout.Toggle(
                            applyAsDefaultProp.boolValue,
                            GUILayout.Width(20)
                        );

                        // End the grayed-out group
                        EditorGUI.EndDisabledGroup();

                        // Clear the value if it became disabled to maintain data integrity
                        if (!isSelectedProp.boolValue)
                        {
                            applyAsDefaultProp.boolValue = false;
                        }

                        EditorGUILayout.EndHorizontal(); // Column 3 end

                        EditorGUILayout.EndHorizontal(); // Row end
                    }
                }

                EditorGUILayout.EndScrollView();
                EditorGUILayout.EndVertical();
            }

            // If any changes were detected, apply them and mark the object as dirty
            // if (EditorGUI.EndChangeCheck())
            // {
            serializedEnforceBlendshape.ApplyModifiedProperties();
            UpdateBlendShapeSelections();
            // }
        }

        private void UpdateBlendShapeSelections()
        {
            var enforceBlendshape = (EnforceBlendshape)target;

            if (enforceBlendshape.skinnedMeshRenderer == null)
                return;

            Mesh mesh = enforceBlendshape.skinnedMeshRenderer.sharedMesh;
            if (mesh == null)
                return;

            // Clear temporary list and rebuild it completely from scratch
            allBlendShapeSelections.Clear();

            // Loop through EVERY single blendshape present in the mesh, regardless of its current weight
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendShapeName = mesh.GetBlendShapeName(i);

                // Check if this blendshape already had a saved configuration state in the component
                var existing = enforceBlendshape.blendShapeSelections.FirstOrDefault(selection =>
                    selection.blendShapeName == blendShapeName
                );

                allBlendShapeSelections.Add(
                    new EnforceBlendshape.BlendShapeSelection
                    {
                        blendShapeName = blendShapeName,
                        // Keep the user's toggle state if it existed before. Otherwise, default strictly to false (OFF)
                        isSelected = existing != null ? existing.isSelected : false,
                        applyAsDefault = existing != null ? existing.applyAsDefault : false,
                    }
                );
            }

            serializedEnforceBlendshape.Update();
            blendShapeSelectionsProp.arraySize = allBlendShapeSelections.Count;

            for (int i = 0; i < allBlendShapeSelections.Count; i++)
            {
                var blendShapeProp = blendShapeSelectionsProp.GetArrayElementAtIndex(i);

                // Force sync every single index slot to maintain total data integrity
                blendShapeProp.FindPropertyRelative("blendShapeName").stringValue =
                    allBlendShapeSelections[i].blendShapeName;
                blendShapeProp.FindPropertyRelative("isSelected").boolValue =
                    allBlendShapeSelections[i].isSelected;
                blendShapeProp.FindPropertyRelative("applyAsDefault").boolValue =
                    allBlendShapeSelections[i].applyAsDefault;
            }

            serializedEnforceBlendshape.ApplyModifiedProperties();
        }
    }
}
