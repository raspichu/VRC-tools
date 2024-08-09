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

            // Store original blendshape weights and set new blendshapes on clonedMesh
            List<string> blendshapesToRemove = new List<string>();

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

                // Edit mesh directly
                originalMesh.GetBlendShapeFrameVertices(blendShapeIndex, 0, deltaVertices, deltaNormals, deltaTangents);
                ModifyMeshBlendShape(clonedMesh, blendShapeIndex, deltaVertices, deltaNormals, deltaTangents, weight);
                renderer.SetBlendShapeWeight(blendShapeIndex, 0);

                blendshapesToRemove.Add(selection.blendShapeName);
                // RemoveBlendShape_old(clonedMesh, selection.blendShapeName);
            }



            Mesh finalMesh = RemoveBlendShapes(clonedMesh, blendshapesToRemove);
            renderer.sharedMesh = finalMesh;



            Debug.Log("New blendshapes created and applied to the cloned mesh. Original blendshapes reset.");
        }

        private void ModifyMeshBlendShape(Mesh mesh, int blendShapeIndex, Vector3[] deltaVertices, Vector3[] deltaNormals, Vector3[] deltaTangents, float weight)
        {
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

        private Mesh RemoveBlendShapes(Mesh mesh, List<string> blendShapesToRemove)
        {
            var blendShapeCount = mesh.blendShapeCount;


            // Create a new mesh

            Mesh newMesh = CopyMesh(mesh);


            // Mesh newMesh = new Mesh
            // {
            //     vertices = mesh.vertices,
            //     normals = mesh.normals,
            //     tangents = mesh.tangents,
            //     uv = mesh.uv,
            //     uv2 = mesh.uv2,
            //     triangles = mesh.triangles,
            //     boneWeights = mesh.boneWeights,
            //     bindposes = mesh.bindposes
            // };

            // Add all blend shapes except the ones to be removed
            for (int i = 0; i < blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                if (blendShapesToRemove.Contains(name))
                {
                    Debug.Log($"Removing blendshape: {name}");
                    continue;
                }
                int frameCount = mesh.GetBlendShapeFrameCount(i);
                for (int frame = 0; frame < frameCount; frame++)
                {
                    Vector3[] deltaVertices = new Vector3[mesh.vertexCount];
                    Vector3[] deltaNormals = new Vector3[mesh.vertexCount];
                    Vector3[] deltaTangents = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(i, frame, deltaVertices, deltaNormals, deltaTangents);
                    newMesh.AddBlendShapeFrame(name, mesh.GetBlendShapeFrameWeight(i, frame), deltaVertices, deltaNormals, deltaTangents);
                }
            }

            return newMesh;

            // Replace the original mesh with the new mesh
            // CopyMesh(newMesh, mesh);
            // mesh.Clear();
            // mesh.vertices = newMesh.vertices;
            // mesh.normals = newMesh.normals;
            // mesh.tangents = newMesh.tangents;
            // mesh.uv = newMesh.uv;
            // mesh.uv2 = newMesh.uv2;
            // mesh.triangles = newMesh.triangles;
            // mesh.boneWeights = newMesh.boneWeights;
            // mesh.bindposes = newMesh.bindposes;

            // Re-add blend shapes from the newMesh
            // for (int i = 0; i < newMesh.blendShapeCount; i++)
            // {
            //     string name = newMesh.GetBlendShapeName(i);
            //     int frameCount = newMesh.GetBlendShapeFrameCount(i);
            //     for (int frame = 0; frame < frameCount; frame++)
            //     {
            //         Vector3[] deltaVertices = new Vector3[newMesh.vertexCount];
            //         Vector3[] deltaNormals = new Vector3[newMesh.vertexCount];
            //         Vector3[] deltaTangents = new Vector3[newMesh.vertexCount];
            //         newMesh.GetBlendShapeFrameVertices(i, frame, deltaVertices, deltaNormals, deltaTangents);
            //         mesh.AddBlendShapeFrame(name, newMesh.GetBlendShapeFrameWeight(i, frame), deltaVertices, deltaNormals, deltaTangents);
            //     }
            // }
        }


        private Mesh CopyMesh(Mesh from, Mesh to = null)
        {
            if (to == null)
            {
                to = new Mesh();
            }
            to.Clear();
            to.vertices = from.vertices;
            to.normals = from.normals;
            to.tangents = from.tangents;
            to.uv = from.uv;
            to.uv2 = from.uv2;
            to.triangles = from.triangles;
            to.boneWeights = from.boneWeights;
            to.bindposes = from.bindposes;
            to.subMeshCount = from.subMeshCount;
            for (int i = 0; i < from.subMeshCount; i++)
            {
                to.SetIndices(from.GetIndices(i), from.GetTopology(i), i);
            }
            to.name = from.name;
            return to;
        }
        // {
        //     var copy = new Mesh();
        //     foreach (var property in typeof(Mesh).GetProperties())
        //     {
        //         if (property.GetSetMethod() != null && property.GetGetMethod() != null)
        //         {
        //             property.SetValue(copy, property.GetValue(mesh, null), null);
        //         }
        //     }
        //     return copy;
        // }

    }

}