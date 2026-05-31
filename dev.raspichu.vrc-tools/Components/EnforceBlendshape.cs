using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
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
            public bool applyAsDefault;
        }

        private void OnValidate()
        {
            if (skinnedMeshRenderer == null)
            {
                skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
            }
            Debug.Log("OnValidate");
#if UNITY_EDITOR
            Undo.RecordObject(this, "Modified Blendshape Selection");
            EditorUtility.SetDirty(this);
#endif
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

            // Track total accumulated vertex displacements applied as default
            int vertexCount = clonedMesh.vertexCount;
            Vector3[] totalDefaultDeltaVertices = new Vector3[vertexCount];
            bool hasAnyDefaultApplied = false;

            for (int i = 0; i < blendShapeSelections.Count; i++)
            {
                var selection = blendShapeSelections[i];
                int blendShapeIndex = originalMesh.GetBlendShapeIndex(selection.blendShapeName);

                if (blendShapeIndex == -1)
                {
                    Debug.LogError(
                        $"BlendShape '{selection.blendShapeName}' not found in the mesh."
                    );
                    continue;
                }

                if (!selection.isSelected)
                {
                    Debug.Log($"BlendShape '{selection.blendShapeName}' is not selected.");
                    continue;
                }

                float weight = renderer.GetBlendShapeWeight(blendShapeIndex);

                // FIXED: Process even if weight is 0. If weight is 0, we force it to 100% for the bake default behavior
                if (selection.applyAsDefault)
                {
                    float targetWeight = weight;
                    if (targetWeight == 0)
                    {
                        targetWeight = 100f; // Force 100% effect if user wants it as default base shape
                    }

                    // Get delta vertices, normals, tangents for the blendshape
                    Vector3[] deltaVertices = new Vector3[vertexCount];
                    Vector3[] deltaNormals = new Vector3[vertexCount];
                    Vector3[] deltaTangents = new Vector3[vertexCount];

                    clonedMesh.GetBlendShapeFrameVertices(
                        blendShapeIndex,
                        0,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents
                    );
                    ModifyMeshBlendShape(
                        renderer,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents,
                        targetWeight
                    );
                    renderer.SetBlendShapeWeight(blendShapeIndex, 0);

                    // Accumulate default displacements to correct other blendshapes later using the active baking weight
                    float factor = targetWeight / 100f;
                    for (int v = 0; v < vertexCount; v++)
                    {
                        totalDefaultDeltaVertices[v] += deltaVertices[v] * factor;
                    }
                    hasAnyDefaultApplied = true;
                }

                // ALWAYS mark as remove/ignore if it is enabled (isSelected == true), regardless of its weight
                blendshapesToRemove.Add(selection.blendShapeName);
            }

            // If defaults were applied, modify RemoveBlendShapes behavior or correct remaining frames here
            if (hasAnyDefaultApplied)
            {
                CorrectRemainingBlendShapeDeltas(
                    clonedMesh,
                    blendshapesToRemove,
                    totalDefaultDeltaVertices,
                    renderer // Pasamos el renderer
                );
            }

            // ALWAYS trigger removal to process renaming to [IGNORED]_ for all enabled selections
            RemoveBlendShapes(renderer, blendshapesToRemove);

            Debug.Log(
                "New blendshapes created and applied to the cloned mesh. Original blendshapes reset."
            );
        }

        // New helper method to subtract applied default deltas from remaining blendshapes to prevent stretching/spikes
        private void CorrectRemainingBlendShapeDeltas(
            Mesh mesh,
            List<string> blendShapesToRemove,
            Vector3[] totalDefaultDeltaVertices,
            SkinnedMeshRenderer renderer
        )
        {
            int blendShapeCount = mesh.blendShapeCount;
            int vertexCount = mesh.vertexCount;

            // Nuevo cache capaz de guardar dos frames por Blendshape si hace falta
            var correctedFrames =
                new List<(
                    string name,
                    float currentWeight,
                    bool needsMidFrame,
                    Vector3[] midDv,
                    Vector3[] midDn,
                    Vector3[] midDt,
                    Vector3[] endDv,
                    Vector3[] endDn,
                    Vector3[] endDt
                )>();

            for (int i = 0; i < blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);

                Vector3[] deltaVertices = new Vector3[vertexCount];
                Vector3[] deltaNormals = new Vector3[vertexCount];
                Vector3[] deltaTangents = new Vector3[vertexCount];
                mesh.GetBlendShapeFrameVertices(i, 0, deltaVertices, deltaNormals, deltaTangents);

                if (!blendShapesToRemove.Contains(name))
                {
                    float currentWeight = renderer.GetBlendShapeWeight(i);
                    float inverseFactor = 1.0f - (currentWeight / 100f);

                    // Si el peso está a medias (ej. 50%), necesitamos un frame intermedio (Checkpoint)
                    bool needsMidFrame = currentWeight > 0f && currentWeight < 100f;

                    Vector3[] midDv = null,
                        midDn = null,
                        midDt = null;
                    Vector3[] endDv = new Vector3[vertexCount];
                    Vector3[] endDn = new Vector3[vertexCount];
                    Vector3[] endDt = new Vector3[vertexCount];

                    // 1. HORNEAR EL ESTADO ACTUAL COMO FRAME INTERMEDIO (Ej: el 50%)
                    if (needsMidFrame)
                    {
                        midDv = new Vector3[vertexCount];
                        midDn = new Vector3[vertexCount];
                        midDt = new Vector3[vertexCount];

                        float midMultiplier = currentWeight / 100f;
                        for (int v = 0; v < vertexCount; v++)
                        {
                            // Congelamos el desplazamiento para que en Play Mode no varíe nada respecto a ahora
                            midDv[v] = deltaVertices[v] * midMultiplier;
                            midDn[v] = deltaNormals[v] * midMultiplier;
                            midDt[v] = deltaTangents[v] * midMultiplier;
                        }
                    }

                    // 2. APLICAR TU FÓRMULA EXACTA AL FRAME FINAL (100%)
                    for (int v = 0; v < vertexCount; v++)
                    {
                        endDv[v] =
                            deltaVertices[v] - (totalDefaultDeltaVertices[v] * inverseFactor);
                        endDn[v] = deltaNormals[v];
                        endDt[v] = deltaTangents[v];
                    }

                    correctedFrames.Add(
                        (
                            name,
                            currentWeight,
                            needsMidFrame,
                            midDv,
                            midDn,
                            midDt,
                            endDv,
                            endDn,
                            endDt
                        )
                    );
                }
                else
                {
                    // Si se va a borrar, lo pasamos intacto (RemoveBlendShapes lo inutilizará igualmente)
                    correctedFrames.Add(
                        (
                            name,
                            0f,
                            false,
                            null,
                            null,
                            null,
                            deltaVertices,
                            deltaNormals,
                            deltaTangents
                        )
                    );
                }
            }

            // Clear and rewrite frames
            mesh.ClearBlendShapes();
            foreach (var frame in correctedFrames)
            {
                // Si requería curva de compensación, añadimos el checkpoint en su % exacto
                if (frame.needsMidFrame)
                {
                    mesh.AddBlendShapeFrame(
                        frame.name,
                        frame.currentWeight,
                        frame.midDv,
                        frame.midDn,
                        frame.midDt
                    );
                }
                // Añadimos el objetivo final
                mesh.AddBlendShapeFrame(frame.name, 100f, frame.endDv, frame.endDn, frame.endDt);
            }
        }

        private void ModifyMeshBlendShape(
            SkinnedMeshRenderer renderer,
            Vector3[] deltaVertices,
            Vector3[] deltaNormals,
            Vector3[] deltaTangents,
            float weight
        )
        {
            Mesh mesh = renderer.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector4[] tangents = mesh.tangents;

            float factor = weight / 100f;

            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] += deltaVertices[i] * factor;
                if (normals.Length > 0)
                    normals[i] += deltaNormals[i] * factor;
                if (tangents.Length > 0)
                    tangents[i] += new Vector4(
                        deltaTangents[i].x * factor,
                        deltaTangents[i].y * factor,
                        deltaTangents[i].z * factor,
                        0
                    );
            }

            mesh.vertices = vertices;
            if (normals.Length > 0)
                mesh.normals = normals;
            if (tangents.Length > 0)
                mesh.tangents = tangents;
        }

        private void RemoveBlendShapes(
            SkinnedMeshRenderer renderer,
            List<string> blendShapesToRemove
        )
        {
            Mesh mesh = renderer.sharedMesh;

            var blendShapeCount = mesh.blendShapeCount;
            var blendShapesToKeep =
                new List<(
                    string name,
                    List<(
                        int index,
                        float frameWeight, // AHORA RECOGEMOS EL PESO REAL DEL FRAME (CRÍTICO)
                        Vector3[] deltaVertices,
                        Vector3[] deltaNormals,
                        Vector3[] deltaTangents,
                        float weight
                    )> frameData
                )>();

            for (int i = 0; i < blendShapeCount; i++)
            {
                string name = mesh.GetBlendShapeName(i);
                float weight = renderer.GetBlendShapeWeight(i);

                if (blendShapesToRemove.Contains(name))
                {
                    name = "[IGNORED]_" + name;
                    weight = 0f;
                }

                int frameCount = mesh.GetBlendShapeFrameCount(i);
                var frameData =
                    new List<(
                        int index,
                        float frameWeight,
                        Vector3[] deltaVertices,
                        Vector3[] deltaNormals,
                        Vector3[] deltaTangents,
                        float weight
                    )>();

                for (int frame = 0; frame < frameCount; frame++)
                {
                    Vector3[] deltaVertices = new Vector3[mesh.vertexCount];
                    Vector3[] deltaNormals = new Vector3[mesh.vertexCount];
                    Vector3[] deltaTangents = new Vector3[mesh.vertexCount];
                    mesh.GetBlendShapeFrameVertices(
                        i,
                        frame,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents
                    );

                    // FIXED: Rescatamos en qué % exacto se había guardado este frame para no aplastar la curva no lineal
                    float frameWeight = mesh.GetBlendShapeFrameWeight(i, frame);

                    frameData.Add(
                        (i, frameWeight, deltaVertices, deltaNormals, deltaTangents, weight)
                    );
                }

                blendShapesToKeep.Add((name, frameData));
            }

            mesh.ClearBlendShapes();

            foreach (var (name, frameData) in blendShapesToKeep)
            {
                foreach (
                    var (
                        index,
                        frameWeight,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents,
                        weight
                    ) in frameData
                )
                {
                    // Volcamos el frame respetando su porcentaje original en vez del 100f harcodeado
                    mesh.AddBlendShapeFrame(
                        name,
                        frameWeight,
                        deltaVertices,
                        deltaNormals,
                        deltaTangents
                    );
                    renderer.SetBlendShapeWeight(index, weight);
                }
            }
        }
    }
}
