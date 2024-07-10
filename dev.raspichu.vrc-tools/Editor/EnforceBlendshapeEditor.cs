using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

using raspichu.vrc_tools.component;

namespace raspichu.vrc_tools.editor
{
    [CustomEditor(typeof(EnforceBlendshape))]
    public class EnforceBlendshapeEditor : Editor
    {
        private SkinnedMeshRenderer lastSkinnedMeshRenderer;

        private EnforceBlendshape enforceBlendshape;

        private bool isAnyBlendShapeSelected;

        public override void OnInspectorGUI()
        {
            enforceBlendshape = (EnforceBlendshape)target;

            // Draw the SkinnedMeshRenderer field
            enforceBlendshape.skinnedMeshRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField(
                "Skinned Mesh Renderer",
                enforceBlendshape.skinnedMeshRenderer,
                typeof(SkinnedMeshRenderer),
                true
            );
            
            if (enforceBlendshape.skinnedMeshRenderer == null)
            {
                EditorGUILayout.HelpBox("Skinned Mesh Renderer is not assigned.", MessageType.Warning);
                return;
            }

            // Check if the skinned mesh renderer has changed
            lastSkinnedMeshRenderer = enforceBlendshape.skinnedMeshRenderer;
            UpdateBlendShapeSelections();

            if (enforceBlendshape.blendShapeSelections.Count > 0)
            {
                EditorGUILayout.LabelField("Select BlendShapes to Duplicate:");
                foreach (var selection in enforceBlendshape.blendShapeSelections)
                {
                    selection.isSelected = EditorGUILayout.Toggle(selection.blendShapeName, selection.isSelected);
                }

                if (GUILayout.Button("Generate Selected BlendShapes")  && isAnyBlendShapeSelected == true)
                {
                    enforceBlendshape.GenerateSelectedBlendShapes();
                }
            }
        }

        private void UpdateBlendShapeSelections()
        {
            if (enforceBlendshape.skinnedMeshRenderer == null)
                return;

            Mesh mesh = enforceBlendshape.skinnedMeshRenderer.sharedMesh;
            if (mesh == null)
                return;

            // Create a temporary list to hold blendshapes with values greater than 0
            List<string> blendShapesToRemove = new List<string>();

            // Mark blendshapes with value 0 for removal
            for (int i = 0; i < enforceBlendshape.blendShapeSelections.Count; i++)
            {
                int blendShapeIndex = mesh.GetBlendShapeIndex(enforceBlendshape.blendShapeSelections[i].blendShapeName);
                if (blendShapeIndex >= 0)
                {
                    float weight = enforceBlendshape.skinnedMeshRenderer.GetBlendShapeWeight(blendShapeIndex);
                    if (weight == 0)
                    {
                        blendShapesToRemove.Add(enforceBlendshape.blendShapeSelections[i].blendShapeName);
                    }
                    else
                    {
                        if (enforceBlendshape.blendShapeSelections[i].isSelected)
                        {
                            isAnyBlendShapeSelected = true;
                        }
                    }
                }
            }

            // Remove blendshapes with value 0 or that doens't exists in the mesh
            enforceBlendshape.blendShapeSelections.RemoveAll(selection => blendShapesToRemove.Contains(selection.blendShapeName) || mesh.GetBlendShapeIndex(selection.blendShapeName) == -1);

            // Add new blendshapes with value greater than 0
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string blendShapeName = mesh.GetBlendShapeName(i);
                float weight = enforceBlendshape.skinnedMeshRenderer.GetBlendShapeWeight(i);
                if (weight > 0 && !enforceBlendshape.blendShapeSelections.Exists(selection => selection.blendShapeName == blendShapeName))
                {
                    enforceBlendshape.blendShapeSelections.Add(new EnforceBlendshape.BlendShapeSelection
                    {
                        blendShapeName = blendShapeName,
                        isSelected = false
                    });
                }
            }
        }


    }
}
