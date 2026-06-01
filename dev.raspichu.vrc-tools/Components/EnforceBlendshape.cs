using System.Collections.Generic;
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
                skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
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
                Debug.LogError("No mesh.");
                return;
            }

            int vCount = originalMesh.vertexCount;

            // ── Correction Knee (the adjustment knob) ────────────────────
            // Above this % of the maximum movement of each shape, the default
            // is undone COMPLETELY (like the external). Below, smooth ramp.
            //   ~1.0 = pure proportional (smooth, but leaves residual straighten)
            //   ~0.3 = like the external inside, smooth edge (recommended)
            //   ~0.0 = binary (exact, but can cut)
            const float correctionKnee = 0.3f;

            List<int> sourceIdx = new List<int>();
            HashSet<string> sourceNames = new HashSet<string>();
            HashSet<string> ignoredNames = new HashSet<string>();
            Dictionary<string, float> ignoredWeights = new Dictionary<string, float>(); // Store original weights for ignored shapes

            foreach (var sel in blendShapeSelections)
            {
                if (!sel.isSelected)
                    continue;

                int idx = originalMesh.GetBlendShapeIndex(sel.blendShapeName);
                if (idx == -1)
                {
                    Debug.LogError(
                        $"BlendShape '{sel.blendShapeName}' not found (do Undo before retrying)."
                    );
                    continue;
                }

                if (sel.applyAsDefault)
                {
                    sourceIdx.Add(idx);
                    sourceNames.Add(sel.blendShapeName);
                }
                else
                {
                    // Selected, but NOT default. Store its name and original weight.
                    ignoredNames.Add(sel.blendShapeName);
                    ignoredWeights[sel.blendShapeName] = renderer.GetBlendShapeWeight(idx);
                }
            }

            if (sourceIdx.Count == 0 && ignoredNames.Count == 0)
            {
                Debug.LogError("EnforceBlendshape: mark at least one shape to process.");
                return;
            }

            Mesh src = Object.Instantiate(originalMesh);

            // offset (+) = position of the default that is baked into the base.
            Vector3[] offV = new Vector3[vCount],
                offN = new Vector3[vCount],
                offT = new Vector3[vCount];
            Vector3[] tV = new Vector3[vCount],
                tN = new Vector3[vCount],
                tT = new Vector3[vCount];

            // Only sourceIdx affects geometry (ignoredNames do not correct the rest)
            foreach (int idx in sourceIdx)
            {
                float w = renderer.GetBlendShapeWeight(idx);
                src.GetBlendShapeFrameVertices(idx, 0, tV, tN, tT);
                float f = w / 100f;
                for (int v = 0; v < vCount; v++)
                {
                    offV[v] += tV[v] * f;
                    offN[v] += tN[v] * f;
                    offT[v] += tT[v] * f;
                }
            }

            float maxOff = 0f;
            for (int v = 0; v < vCount; v++)
                maxOff = Mathf.Max(maxOff, offV[v].magnitude);
            if (maxOff < 1e-6f && sourceIdx.Count > 0)
                Debug.LogWarning(
                    "EnforceBlendshape: the source is at weight 0; set it to its default value before executing."
                );

            // dst = geometry + offset baked into the base (permanent default).
            Mesh dst = new Mesh { name = src.name + "_Enforced" };
            CopyGeometry(src, dst);
            Vector3[] dV = dst.vertices,
                dN = dst.normals;
            Vector4[] dT = dst.tangents;
            bool hasN = dN != null && dN.Length == vCount,
                hasT = dT != null && dT.Length == vCount;
            for (int v = 0; v < vCount; v++)
            {
                dV[v] += offV[v];
                if (hasN)
                    dN[v] += offN[v];
                if (hasT)
                {
                    dT[v].x += offT[v].x;
                    dT[v].y += offT[v].y;
                    dT[v].z += offT[v].z;
                }
            }
            dst.vertices = dV;
            if (hasN)
                dst.normals = dN;
            if (hasT)
                dst.tangents = dT;
            dst.RecalculateBounds();

            // Rebuild blendshapes.
            for (int i = 0; i < src.blendShapeCount; i++)
            {
                string name = src.GetBlendShapeName(i);
                bool isSource = sourceNames.Contains(name);
                bool isIgnored = ignoredNames.Contains(name);

                float cw = renderer.GetBlendShapeWeight(i); // Current Weight of this blendshape
                bool insertedCwFrame = false;

                int frames = src.GetBlendShapeFrameCount(i);
                for (int fr = 0; fr < frames; fr++)
                {
                    float fw = src.GetBlendShapeFrameWeight(i, fr);
                    Vector3[] v = new Vector3[vCount],
                        n = new Vector3[vCount],
                        t = new Vector3[vCount];
                    src.GetBlendShapeFrameVertices(i, fr, v, n, t);

                    if (!isSource && !isIgnored)
                    {
                        // 1. Insert a pure keyframe at the current weight to preserve the 0 to CW trajectory.
                        // Must be inserted BEFORE the first frame that exceeds it to maintain ascending weight order.
                        if (!insertedCwFrame && cw > 0.001f && cw < fw)
                        {
                            float ratio = cw / fw;
                            Vector3[] pV = new Vector3[vCount],
                                pN = new Vector3[vCount],
                                pT = new Vector3[vCount];
                            for (int j = 0; j < vCount; j++)
                            {
                                pV[j] = v[j] * ratio;
                                pN[j] = n[j] * ratio;
                                pT[j] = t[j] * ratio;
                            }
                            dst.AddBlendShapeFrame(name, cw, pV, pN, pT);
                            insertedCwFrame = true;
                        }

                        // 2. Apply mathematical correction ONLY if this frame represents the "remaining" portion (> cw)
                        if (fw > cw + 0.001f)
                        {
                            float maxDelta = 0f;
                            for (int j = 0; j < vCount; j++)
                            {
                                float m = v[j].sqrMagnitude;
                                if (m > maxDelta)
                                    maxDelta = m;
                            }
                            maxDelta = Mathf.Sqrt(maxDelta);

                            if (maxDelta > 1e-6f)
                            {
                                float inv = 1f / (Mathf.Max(correctionKnee, 1e-4f) * maxDelta);
                                for (int j = 0; j < vCount; j++)
                                {
                                    float a = Mathf.Clamp01(v[j].magnitude * inv);
                                    v[j] -= offV[j] * a;
                                    n[j] -= offN[j] * a;
                                    t[j] -= offT[j] * a;
                                }
                            }
                        }
                    }

                    // If it is Source or Ignored, add prefix
                    string finalName = (isSource || isIgnored) ? "[IGNORED]_" + name : name;
                    dst.AddBlendShapeFrame(finalName, fw, v, n, t);
                }
            }

#if UNITY_EDITOR
            Undo.RecordObject(renderer, "Enforce Blendshape");
#endif
            renderer.sharedMesh = dst;

            // Set final weights depending on their specific source/ignored status
            for (int i = 0; i < dst.blendShapeCount; i++)
            {
                string bsName = dst.GetBlendShapeName(i);
                if (bsName.StartsWith("[IGNORED]_"))
                {
                    string originalName = bsName.Substring(10); // Remove the "[IGNORED]_" prefix (10 characters)

                    if (ignoredNames.Contains(originalName))
                    {
                        // If it was just 'Enabled' but not 'Default', restore its previous weight
                        renderer.SetBlendShapeWeight(i, ignoredWeights[originalName]);
                    }
                    else
                    {
                        // If it was a 'Default' source, set its weight to 0 because it is now baked
                        renderer.SetBlendShapeWeight(i, 0f);
                    }
                }
            }

            Debug.Log("EnforceBlendshape: Done (in memory).");
        }

        private void CopyGeometry(Mesh src, Mesh dst)
        {
            dst.vertices = src.vertices;
            dst.normals = src.normals;
            dst.tangents = src.tangents;
            dst.uv = src.uv;
            dst.uv2 = src.uv2;
            dst.colors32 = src.colors32;
            dst.boneWeights = src.boneWeights;
            dst.bindposes = src.bindposes;
            dst.subMeshCount = src.subMeshCount;
            for (int i = 0; i < src.subMeshCount; i++)
                dst.SetTriangles(src.GetTriangles(i), i);
            dst.RecalculateBounds();
        }
    }
}
