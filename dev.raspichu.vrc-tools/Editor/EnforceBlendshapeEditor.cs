using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using raspichu.vrc_tools.component;

namespace raspichu.vrc_tools.editor
{
    [CustomEditor(typeof(EnforceBlendshape))]
    public class EnforceBlendshapeEditor : Editor
    {
        private SerializedObject serializedEnforceBlendshape;
        private SerializedProperty skinnedMeshRendererProp;
        private SerializedProperty blendShapeSelectionsProp;

        private string blendShapeSearch = "";

        private Vector2 scrollPosition;
        private bool showBlendShapeList = true;

        private List<EnforceBlendshape.BlendShapeSelection> allBlendShapeSelections = new List<EnforceBlendshape.BlendShapeSelection>();

        private void OnEnable()
        {
            serializedEnforceBlendshape = new SerializedObject(target);
            skinnedMeshRendererProp = serializedEnforceBlendshape.FindProperty("skinnedMeshRenderer");
            blendShapeSelectionsProp = serializedEnforceBlendshape.FindProperty("blendShapeSelections");

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
                EditorGUILayout.HelpBox("Skinned Mesh Renderer is not assigned.", MessageType.Warning);
                return;
            }

            blendShapeSearch = EditorGUILayout.TextField("Search BlendShapes", blendShapeSearch);

            // Toggle for blendshape list
            showBlendShapeList = EditorGUILayout.Foldout(showBlendShapeList, "BlendShapes List");
            if (showBlendShapeList)
            {
                // Begin sub box for blendshape list
                EditorGUILayout.BeginVertical(GUI.skin.box);
                EditorGUILayout.LabelField("BlendShapes", EditorStyles.boldLabel);
                // Begin scroll view for blendshape list
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

                // Display blendshapes
                for (int i = 0; i < blendShapeSelectionsProp.arraySize; i++)
                {
                    var blendShapeProp = blendShapeSelectionsProp.GetArrayElementAtIndex(i);
                    var blendShapeNameProp = blendShapeProp.FindPropertyRelative("blendShapeName");
                    var isSelectedProp = blendShapeProp.FindPropertyRelative("isSelected");

                    if (blendShapeNameProp.stringValue.ToLower().Contains(blendShapeSearch.ToLower()))
                    {
                        EditorGUILayout.PropertyField(isSelectedProp, new GUIContent(blendShapeNameProp.stringValue));
                    }
                }

                // End scroll view for blendshape list
                EditorGUILayout.EndScrollView();
                // End sub box for blendshape list
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

            // Create a temporary list to hold blendshapes with values greater than 0
            List<string> blendShapesToRemove = new List<string>();

            // Mark blendshapes with value 0 for removal
            for (int i = 0; i < allBlendShapeSelections.Count; i++)
            {
                int blendShapeIndex = mesh.GetBlendShapeIndex(allBlendShapeSelections[i].blendShapeName);
                if (blendShapeIndex >= 0)
                {
                    float weight = enforceBlendshape.skinnedMeshRenderer.GetBlendShapeWeight(blendShapeIndex);
                    if (weight == 0)
                    {
                        blendShapesToRemove.Add(allBlendShapeSelections[i].blendShapeName);
                    }
                }
            }

            // Remove blendshapes with value 0 or that doesn't exist in the mesh
            allBlendShapeSelections.RemoveAll(selection => blendShapesToRemove.Contains(selection.blendShapeName) || mesh.GetBlendShapeIndex(selection.blendShapeName) == -1);

            // Add new blendshapes with value greater than 0
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendShapeName = mesh.GetBlendShapeName(i);
                float weight = enforceBlendshape.skinnedMeshRenderer.GetBlendShapeWeight(i);
                if (weight > 0 && !allBlendShapeSelections.Exists(selection => selection.blendShapeName == blendShapeName))
                {
                    allBlendShapeSelections.Add(new EnforceBlendshape.BlendShapeSelection
                    {
                        blendShapeName = blendShapeName,
                        isSelected = enforceBlendshape.blendShapeSelections.Exists(selection => selection.blendShapeName == blendShapeName && selection.isSelected)
                    });
                }
            }

            // Apply search filter
            // enforceBlendshape.blendShapeSelections = new List<EnforceBlendshape.BlendShapeSelection>(allBlendShapeSelections
            //     .Where(selection => selection.blendShapeName.ToLower().Contains(blendShapeSearch.ToLower())).ToList());
            serializedEnforceBlendshape.Update();
            blendShapeSelectionsProp.arraySize = allBlendShapeSelections.Count;
            for (int i = 0; i < allBlendShapeSelections.Count; i++)
            {
                var blendShapeProp = blendShapeSelectionsProp.GetArrayElementAtIndex(i);
                blendShapeProp.FindPropertyRelative("blendShapeName").stringValue = allBlendShapeSelections[i].blendShapeName;
            }
            serializedEnforceBlendshape.ApplyModifiedProperties();
        }
    }
}
