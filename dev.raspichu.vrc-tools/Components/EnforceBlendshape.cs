using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using VRC.SDKBase;

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
            // Ensure skinnedMeshRenderer is set to the component's SkinnedMeshRenderer
            skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
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

            // Apply the cloned mesh with new blendshapes back to the renderer
            renderer.sharedMesh = clonedMesh;

            // Store original blendshape weights and set new blendshapes on clonedMesh
            for (int i = 0; i < blendShapeSelections.Count; i++)
            {
                var selection = blendShapeSelections[i];
                int blendShapeIndex = originalMesh.GetBlendShapeIndex(selection.blendShapeName);

                if (blendShapeIndex == -1)
                {
                    Debug.LogError($"BlendShape '{selection.blendShapeName}' not found in the mesh.");
                    continue;
                }

                // Store original blendshape weight
                originalBlendShapeWeights.Add(i, renderer.GetBlendShapeWeight(blendShapeIndex));



                if (selection.isSelected)
                {


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

                    // Edit mesh directly
                    originalMesh.GetBlendShapeFrameVertices(blendShapeIndex, 0, deltaVertices, deltaNormals, deltaTangents);
                    ModifyMeshBlendShape(clonedMesh, blendShapeIndex, deltaVertices, deltaNormals, deltaTangents);
                    renderer.SetBlendShapeWeight(blendShapeIndex, 0);
                }
            }



            Debug.Log("New blendshapes created and applied to the cloned mesh. Original blendshapes reset.");
        }

        private void ModifyMeshBlendShape(Mesh mesh, int blendShapeIndex, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents)
        {
            // Modify mesh vertices, normals, tangents based on the blendshape frame deltas
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;

            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] += deltaVertices[i];
                normals[i] += deltaNormals[i];
                // Tangents modification might need to be adjusted based on your requirements
                tangents[i] += new Vector4(deltaTangents[i].x, deltaTangents[i].y, deltaTangents[i].z, 0);
            }

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.tangents = tangents;
        }
    }

}