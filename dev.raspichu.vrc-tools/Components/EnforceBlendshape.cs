using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using VRC.SDKBase;
using System.Linq;

namespace raspichu.vrc_tools.component
{
    [AddComponentMenu("Pichu/Enforce Blendshape")]
    public class EnforceBlendshape : MonoBehaviour, IEditorOnly
    {
        public SkinnedMeshRenderer skinnedMeshRenderer;
        public List<BlendShapeSelection> blendShapeSelections = new List<BlendShapeSelection>();

        [System.Serializable]
        public class BlendShapeSelection
        {
            public string blendShapeName;
            public bool isSelected;
        }


        private void OnValidate()
        {
            if (skinnedMeshRenderer == null)
            {
                skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
            }
            Debug.Log("OnValidate");
            Undo.RecordObject(this, "Modified Blendshape Selection");
            EditorUtility.SetDirty(this);
        }

        public void GenerateSelectedBlendShapes()
        {
            if (skinnedMeshRenderer == null)
            {
                Debug.LogError("SkinnedMeshRenderer not assigned.");
                return;
            }

            SkinnedMeshRenderer renderer = skinnedMeshRenderer;
            Mesh originalMesh = renderer.sharedMesh;

            if (originalMesh == null)
            {
                Debug.LogError("SkinnedMeshRenderer has no mesh.");
                return;
            }

            // Clone the original mesh
            Mesh clonedMesh = Object.Instantiate(originalMesh);

            // Dictionary to store original blendshape weights
            Dictionary<int, float> originalBlendShapeWeights = new Dictionary<int, float>();

            // Store original blendshape weights and set new blendshapes on clonedMesh
            List<string> blendshapesToRemove = new List<string>();

            renderer.sharedMesh = clonedMesh;

            for (int i = 0; i < blendShapeSelections.Count; i++)
            {
                var selection = blendShapeSelections[i];
                int blendShapeIndex = originalMesh.GetBlendShapeIndex(selection.blendShapeName);

                if (blendShapeIndex == -1)
                {
                    Debug.LogError($"BlendShape '{selection.blendShapeName}' not found in the mesh.");
                    continue;
                }

                if (!selection.isSelected)
                {
                    Debug.Log($"BlendShape '{selection.blendShapeName}' is not selected.");
                    continue;
                }


                // Store original blendshape weight
                float weight = renderer.GetBlendShapeWeight(blendShapeIndex);
                originalBlendShapeWeights.Add(i, weight);

                // Get delta vertices, normals, tangents for the blendshape
                int vertexCount = originalMesh.vertexCount;
                Vector3[] deltaVertices = new Vector3[vertexCount];
                Vector3[] deltaNormals = new Vector3[vertexCount];
                Vector3[] deltaTangents = new Vector3[vertexCount];

                // As a new blendshape
                // originalMesh.GetBlendShapeFrameVertices(blendShapeIndex, 0, deltaVertices, deltaNormals, deltaTangents);
                // string newBlendShapeName = "[PI]_" + selection.blendShapeName;
                // int newBlendShapeIndex = clonedMesh.blendShapeCount; // Get the index of the new blendshape frame
                // clonedMesh.AddBlendShapeFrame(newBlendShapeName, 100f, deltaVertices, deltaNormals, deltaTangents);
                // float originalWeight = originalBlendShapeWeights[i];
                // renderer.SetBlendShapeWeight(blendShapeIndex, 0);
                // newBlendShapeIndex = clonedMesh.GetBlendShapeIndex(newBlendShapeName);
                // renderer.SetBlendShapeWeight(newBlendShapeIndex, originalWeight);
                // Debug.Log($"New blendshape '{newBlendShapeName}' added with weight {originalWeight}.");

                // Edit mesh directly
                // originalMesh.GetBlendShapeFrameVertices(blendShapeIndex, 0, deltaVertices, deltaNormals, deltaTangents);
                // ModifyMeshBlendShape(renderer, blendShapeIndex, deltaVertices, deltaNormals, deltaTangents, weight);
                // renderer.SetBlendShapeWeight(blendShapeIndex, 0);


                // Add the original blendshape to the list of blendshapes to remove
                blendshapesToRemove.Add(selection.blendShapeName);
            }

            // Remove the original blendshapes from the cloned mesh
            RemoveBlendShapes(renderer, blendshapesToRemove);

            // Apply the cloned mesh with new blendshapes back to the renderer



            Debug.Log("New blendshapes created and applied to the cloned mesh. Original blendshapes reset.");
        }

        private void ModifyMeshBlendShape(SkinnedMeshRenderer renderer, int blendShapeIndex, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents, float weight)
        {

            Mesh mesh = renderer.sharedMesh;

            // Modify mesh vertices, normals, tangents based on the blendshape frame deltas and weight
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;

            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] += deltaVertices[i] * (weight / 100f);
                normals[i] += deltaNormals[i] * (weight / 100f);
                // Tangents modification might need to be adjusted based on your requirements
                tangents[i] += new Vector4(deltaTangents[i].x * (weight / 100f), deltaTangents[i].y * (weight / 100f), deltaTangents[i].z * (weight / 100f), 0);
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
        }

        private void RemoveBlendShapes(SkinnedMeshRenderer renderer, List<string> blendShapesToRemove)
        {
            Mesh mesh = renderer.sharedMesh;

            // Store the blend shapes to keep
            var blendShapeCount = mesh.blendShapeCount;
            var blendShapesToKeep = new List<(string name, List<(int index, int frame, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents, float weight)> frameData)>();

            // Gather blend shapes that will be kept
            for (int i = 0; i < blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (blendShapesToRemove.Contains(name))
                {
                    name = "[IGNORED]_" + name;
                    // Debug.Log($"Skipping blendshape: {name}");
                    // continue;
                }

                int frameCount = mesh.GetBlendShapeFrameCount(i);
                var frameData = new List<(int index, int frame, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents, float weight)>();

                for (int frame = 0; frame < frameCount; frame++)
                {
                    Vector3[] deltaVertices = new Vector3[mesh.vertexCount];
                    Vector3[] deltaNormals = new Vector3[mesh.vertexCount];
                    Vector3[] deltaTangents = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(i, frame, deltaVertices, deltaNormals, deltaTangents);
                    float weight = renderer.GetBlendShapeWeight(i);
                    frameData.Add((i, frame, deltaVertices, deltaNormals, deltaTangents, weight));
                }

                // mesh.AddBlendShapeFrame(name, weight, deltaVertices, deltaNormals, deltaTangents);

                blendShapesToKeep.Add((name, frameData));
            }

            // Clear the existing blend shapes from the new mesh
            mesh.ClearBlendShapes();

            // Re-add the blend shapes
            foreach (var (name, frameData) in blendShapesToKeep)
            {
                foreach (var (index, frame, deltaVertices, deltaNormals, deltaTangents, weight) in frameData)
                {
                    mesh.AddBlendShapeFrame(name, 100f, deltaVertices, deltaNormals, deltaTangents);
                    // Set the weight of the blend shape frame
                    renderer.SetBlendShapeWeight(index, weight);
                }
            }
        }
    }

}