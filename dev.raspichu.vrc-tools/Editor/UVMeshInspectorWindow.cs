using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace raspichu.vrc_tools.editor
{
    public class UVMeshInspectorWindow : EditorWindow
    {
        private struct UVEdge
        {
            public Vector2 A;
            public Vector2 B;

            public UVEdge(Vector2 a, Vector2 b)
            {
                A = a;
                B = b;
            }
        }

        private class SubmeshData
        {
            public int[] Triangles; // vertex indices, flattened (length = TriCount * 3)
            public int TriCount;
            public Dictionary<long, List<int>> EdgeToTriangles; // packed edge -> local triangle indices
        }

        private struct RaycastMeshHit
        {
            public Vector3 WorldPosition;
            public int SubmeshIndex;
            public int LocalTriangleIndex;
            public Vector2 UV;
        }

        // Topology (triangles/UV) doesn't change between clicks on the same mesh, only vertex
        // positions do (via animation), so this is built once per renderer instead of per click.
        private class FullMeshCache
        {
            public int[] Triangles; // all submeshes concatenated, in submesh order
            public int[] SubmeshTriStart; // starting local-triangle index per submesh
            public int[] SubmeshTriCount;
            public Vector2[] UV;
        }

        private static readonly string[] MainTexturePropertyCandidates =
        {
            "_MainTex",
            "_BaseMap",
            "_BaseColorMap",
            "_Albedo",
            "_AlbedoMap",
            "_Diffuse",
            "_BaseTex",
        };

        private Renderer targetRenderer;
        private int materialIndex;
        private bool dataDirty = true;
        private bool fullCacheDirty = true;

        private Mesh cachedBakedMesh; // reused scratch mesh for SkinnedMeshRenderer bakes
        private FullMeshCache fullMeshCache;
        private int cachedSubmeshIndex = -1; // avoids rebuilding the edge map on every click
        private SubmeshData submeshData;
        private List<UVEdge> allEdges = new List<UVEdge>();
        private Vector3[] allEdgeScreenPoints; // reused buffer for batched line drawing
        private Texture2D mainTexture;
        private string mainTextureProperty;
        private string dataWarning;
        private bool showReadWriteFix;

        private bool pickModeActive;
        private bool showFullWire = true;

        private float zoom = 1f;
        private Vector2 pan = Vector2.zero;

        private bool hasHit;
        private Vector2 hitUV;
        private Vector3 hitWorldPosition;
        private List<UVEdge> islandBoundary = new List<UVEdge>();
        private int islandTriangleCount;

        [MenuItem("Tools/Pichu/UV Mesh Inspector", false, 22)]
        public static void ShowWindow()
        {
            var window = GetWindow<UVMeshInspectorWindow>("UV Mesh Inspector");
            window.titleContent = new GUIContent(
                "UV Mesh Inspector",
                EditorGUIUtility.IconContent("d_Texture Icon").image
            );
            window.minSize = new Vector2(360, 480);
            window.AssignRenderer(Selection.activeGameObject);
        }

        [MenuItem("GameObject/Pichu/UV Mesh Inspector", false, 22)]
        public static void ShowWindowContext(MenuCommand command)
        {
            var window = GetWindow<UVMeshInspectorWindow>("UV Mesh Inspector");
            window.minSize = new Vector2(360, 480);
            window.AssignRenderer(command.context as GameObject ?? Selection.activeGameObject);
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            if (cachedBakedMesh != null)
            {
                DestroyImmediate(cachedBakedMesh);
                cachedBakedMesh = null;
            }
        }

        private void AssignRenderer(GameObject go)
        {
            Renderer found = null;
            if (go != null)
            {
                found = go.GetComponent<SkinnedMeshRenderer>();
                if (found == null)
                    found = go.GetComponent<MeshRenderer>();
            }

            if (found == null)
                return;

            targetRenderer = found;
            materialIndex = 0;
            ClearHit();
            ResetView();
            fullCacheDirty = true;
            dataDirty = true;
        }

        private void ClearHit()
        {
            hasHit = false;
            islandBoundary.Clear();
            islandTriangleCount = 0;
        }

        private void ResetView()
        {
            zoom = 1f;
            pan = Vector2.zero;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("UV Mesh Inspector", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Assign a mesh, pick a material slot, enable 'Pick In Scene' and click the mesh in the Scene view or the texture.",
                MessageType.None
            );
            EditorGUILayout.Space(4);

            Renderer newRenderer = (Renderer)
                EditorGUILayout.ObjectField(
                    "Target Renderer",
                    targetRenderer,
                    typeof(Renderer),
                    true
                );
            if (newRenderer != targetRenderer)
            {
                targetRenderer = newRenderer;
                materialIndex = 0;
                ClearHit();
                ResetView();
                fullCacheDirty = true;
                dataDirty = true;
            }

            if (targetRenderer == null)
            {
                EditorGUILayout.HelpBox(
                    "Drag a SkinnedMeshRenderer or MeshRenderer (or its GameObject) here.",
                    MessageType.Info
                );
                return;
            }

            Material[] materials = targetRenderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "This renderer has no materials assigned.",
                    MessageType.Warning
                );
                return;
            }

            string[] matNames = materials
                .Select((m, i) => $"{i}: {(m != null ? m.name : "<null>")}")
                .ToArray();
            int clampedMatIndex = Mathf.Clamp(materialIndex, 0, materials.Length - 1);
            int newMatIndex = EditorGUILayout.Popup("Material Slot", clampedMatIndex, matNames);
            if (newMatIndex != materialIndex)
            {
                materialIndex = newMatIndex;
                ClearHit();
                dataDirty = true;
            }

            if (dataDirty)
            {
                RefreshData();
                dataDirty = false;
            }

            if (!string.IsNullOrEmpty(dataWarning))
            {
                EditorGUILayout.HelpBox(dataWarning, MessageType.Warning);
                if (showReadWriteFix && GUILayout.Button("Enable Read/Write on model"))
                {
                    EnableMeshReadWrite(GetCurrentMesh(targetRenderer));
                    dataDirty = true;
                }
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField(
                mainTexture != null
                    ? $"Texture: {mainTexture.name}  ({mainTextureProperty})"
                    : "No texture found on this material.",
                EditorStyles.miniLabel
            );

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = pickModeActive ? new Color(0.4f, 0.85f, 1f) : Color.white;
            if (
                GUILayout.Button(
                    pickModeActive ? "Picking... (click mesh in Scene)" : "Pick In Scene",
                    GUILayout.Height(24)
                )
            )
            {
                pickModeActive = !pickModeActive;
            }
            GUI.backgroundColor = Color.white;

            EditorGUI.BeginDisabledGroup(!hasHit);
            if (GUILayout.Button("Clear", GUILayout.Width(60), GUILayout.Height(24)))
            {
                ClearHit();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            showFullWire = EditorGUILayout.ToggleLeft("Show full UV wireframe", showFullWire);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Zoom: {zoom:F1}x", EditorStyles.miniLabel, GUILayout.Width(60));
            if (GUILayout.Button("Reset Zoom", GUILayout.Width(80)))
            {
                ResetView();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.BeginDisabledGroup(submeshData == null);
            if (GUILayout.Button("Save UV as...", GUILayout.Height(20)))
            {
                SaveUVMap();
            }
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(6);
            DrawPreview();

            if (hasHit)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(
                    $"UV: ({hitUV.x:F3}, {hitUV.y:F3})   Island: {islandTriangleCount} tris",
                    EditorStyles.miniLabel
                );
            }
        }

        private void DrawPreview()
        {
            float size = Mathf.Min(position.width - 24f, 480f);
            if (size < 32f)
                return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect boxRect = GUILayoutUtility.GetRect(size, size, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUI.DrawRect(boxRect, new Color(0.12f, 0.12f, 0.12f));

            HandleZoomAndPan(boxRect);

            GUI.BeginGroup(boxRect);
            Rect localRect = new Rect(0, 0, boxRect.width, boxRect.height);

            Rect fitted = mainTexture != null
                ? FitRect(localRect, (float)mainTexture.width / mainTexture.height)
                : localRect;

            Rect imageRect = new Rect(0, 0, fitted.width * zoom, fitted.height * zoom)
            {
                center = localRect.center + pan,
            };

            if (mainTexture != null)
                GUI.DrawTexture(imageRect, mainTexture, ScaleMode.StretchToFill);

            HandleTextureClick(imageRect);

            if (Event.current.type != EventType.Repaint)
            {
                GUI.EndGroup();
                return;
            }

            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            DrawRectOutline(imageRect);

            if (showFullWire && allEdges != null && allEdges.Count > 0)
            {
                if (allEdgeScreenPoints == null || allEdgeScreenPoints.Length != allEdges.Count * 2)
                    allEdgeScreenPoints = new Vector3[allEdges.Count * 2];

                for (int i = 0; i < allEdges.Count; i++)
                {
                    allEdgeScreenPoints[i * 2] = UVToScreen(allEdges[i].A, imageRect);
                    allEdgeScreenPoints[i * 2 + 1] = UVToScreen(allEdges[i].B, imageRect);
                }

                Handles.color = new Color(1f, 1f, 1f, 0.35f);
                Handles.DrawLines(allEdgeScreenPoints);
            }

            if (hasHit && islandBoundary != null)
            {
                Handles.color = new Color(1f, 0.9f, 0.1f, 0.95f);
                foreach (var edge in islandBoundary)
                {
                    Handles.DrawAAPolyLine(
                        3f,
                        UVToScreen(edge.A, imageRect),
                        UVToScreen(edge.B, imageRect)
                    );
                }
            }

            if (hasHit)
            {
                Vector3 dot = UVToScreen(hitUV, imageRect);
                Handles.color = Color.black;
                Handles.DrawSolidDisc(dot, Vector3.forward, 5.5f);
                Handles.color = Color.red;
                Handles.DrawSolidDisc(dot, Vector3.forward, 4f);
            }

            GUI.EndGroup();
        }

        private void HandleZoomAndPan(Rect boxRect)
        {
            Event e = Event.current;
            if (!boxRect.Contains(e.mousePosition))
                return;

            if (e.type == EventType.ScrollWheel)
            {
                float oldZoom = zoom;
                zoom = Mathf.Clamp(zoom * Mathf.Exp(-e.delta.y * 0.05f), 0.2f, 8f);

                Vector2 mouseLocal = e.mousePosition - boxRect.center;
                pan = mouseLocal - (mouseLocal - pan) * (zoom / oldZoom);

                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseDrag && e.button == 1)
            {
                pan += e.delta;
                e.Use();
                Repaint();
            }
        }

        private void HandleTextureClick(Rect imageRect)
        {
            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0)
                return;

            if (submeshData == null || !imageRect.Contains(e.mousePosition))
                return;

            Vector2 uv = new Vector2(
                (e.mousePosition.x - imageRect.x) / imageRect.width,
                1f - (e.mousePosition.y - imageRect.y) / imageRect.height
            );

            if (TryFindWorldPositionFromUV(uv, out int localTriangleIndex, out Vector3 worldPos))
            {
                ApplyHit(localTriangleIndex, uv, worldPos);
            }

            e.Use();
        }

        private static void DrawRectOutline(Rect r)
        {
            Vector3 tl = new Vector3(r.xMin, r.yMin, 0);
            Vector3 tr = new Vector3(r.xMax, r.yMin, 0);
            Vector3 br = new Vector3(r.xMax, r.yMax, 0);
            Vector3 bl = new Vector3(r.xMin, r.yMax, 0);
            Handles.DrawLine(tl, tr);
            Handles.DrawLine(tr, br);
            Handles.DrawLine(br, bl);
            Handles.DrawLine(bl, tl);
        }

        private static Rect FitRect(Rect container, float aspect)
        {
            if (aspect <= 0f)
                return container;

            float containerAspect = container.width / container.height;
            if (aspect > containerAspect)
            {
                float h = container.width / aspect;
                return new Rect(
                    container.x,
                    container.y + (container.height - h) * 0.5f,
                    container.width,
                    h
                );
            }
            else
            {
                float w = container.height * aspect;
                return new Rect(
                    container.x + (container.width - w) * 0.5f,
                    container.y,
                    w,
                    container.height
                );
            }
        }

        private static Vector3 UVToScreen(Vector2 uv, Rect rect)
        {
            return new Vector3(rect.x + uv.x * rect.width, rect.y + (1f - uv.y) * rect.height, 0f);
        }

        // ---------------------------------------------------------------
        // Data building
        // ---------------------------------------------------------------

        private void RefreshData()
        {
            mainTexture = null;
            mainTextureProperty = null;
            dataWarning = null;
            showReadWriteFix = false;

            Mesh mesh = GetCurrentMesh(targetRenderer);
            if (mesh == null)
            {
                fullMeshCache = null;
                submeshData = null;
                allEdges.Clear();
                return;
            }

            bool isSkinned = targetRenderer is SkinnedMeshRenderer;
            if (!isSkinned && !mesh.isReadable)
            {
                dataWarning =
                    "This mesh is not marked as Read/Write Enabled, so its UV data can't be read.";
                showReadWriteFix = true;
                fullMeshCache = null;
                submeshData = null;
                allEdges.Clear();
                return;
            }

            int subCount = mesh.subMeshCount;
            if (subCount == 0)
            {
                fullMeshCache = null;
                submeshData = null;
                allEdges.Clear();
                return;
            }

            Vector2[] uvArray = mesh.uv;
            if (uvArray == null || uvArray.Length == 0)
            {
                dataWarning = "This mesh has no UV coordinates.";
                fullMeshCache = null;
                submeshData = null;
                allEdges.Clear();
                return;
            }

            if (fullCacheDirty || fullMeshCache == null)
            {
                BuildFullMeshCache(mesh, subCount, uvArray);
                fullCacheDirty = false;
                cachedSubmeshIndex = -1; // force a submesh rebuild below, topology may have changed
            }

            int clampedIndex = Mathf.Clamp(materialIndex, 0, subCount - 1);

            // Rebuilding the edge map is the expensive part on large meshes, so skip it when
            // re-clicking within the same submesh/material (the common case).
            if (submeshData == null || cachedSubmeshIndex != clampedIndex)
            {
                int[] subTriangles = SliceSubmeshTriangles(fullMeshCache, clampedIndex);
                submeshData = BuildSubmeshData(subTriangles);
                allEdges = GetAllEdges(submeshData, uvArray);
                allEdgeScreenPoints = null; // resize lazily in DrawPreview
                cachedSubmeshIndex = clampedIndex;
            }

            Material[] materials = targetRenderer.sharedMaterials;
            if (materials != null && clampedIndex < materials.Length)
            {
                mainTexture = FindMainTexture(materials[clampedIndex], out mainTextureProperty);
            }
        }

        private void BuildFullMeshCache(Mesh mesh, int subCount, Vector2[] uv)
        {
            var starts = new int[subCount];
            var counts = new int[subCount];
            var allTris = new List<int>();

            for (int sm = 0; sm < subCount; sm++)
            {
                int[] tris = mesh.GetTriangles(sm);
                starts[sm] = allTris.Count / 3;
                counts[sm] = tris.Length / 3;
                allTris.AddRange(tris);
            }

            fullMeshCache = new FullMeshCache
            {
                Triangles = allTris.ToArray(),
                SubmeshTriStart = starts,
                SubmeshTriCount = counts,
                UV = uv,
            };
        }

        private static int[] SliceSubmeshTriangles(FullMeshCache cache, int submeshIndex)
        {
            int start = cache.SubmeshTriStart[submeshIndex] * 3;
            int count = cache.SubmeshTriCount[submeshIndex] * 3;
            int[] slice = new int[count];
            System.Array.Copy(cache.Triangles, start, slice, 0, count);
            return slice;
        }

        // Baking (SkinnedMeshRenderer) always yields a readable, up-to-date mesh regardless
        // of the source asset's Read/Write setting, so we use it for both topology and raycasting.
        private Mesh GetCurrentMesh(Renderer renderer)
        {
            if (renderer == null)
                return null;

            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null)
                    return null;
                if (cachedBakedMesh == null)
                    cachedBakedMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                skinned.BakeMesh(cachedBakedMesh);
                return cachedBakedMesh;
            }

            if (renderer.TryGetComponent(out MeshFilter filter))
                return filter.sharedMesh;

            return null;
        }

        private static SubmeshData BuildSubmeshData(int[] triangles)
        {
            var data = new SubmeshData { Triangles = triangles };
            data.TriCount = data.Triangles.Length / 3;
            data.EdgeToTriangles = new Dictionary<long, List<int>>();

            for (int t = 0; t < data.TriCount; t++)
            {
                int a = data.Triangles[t * 3 + 0];
                int b = data.Triangles[t * 3 + 1];
                int c = data.Triangles[t * 3 + 2];

                AddEdge(data.EdgeToTriangles, a, b, t);
                AddEdge(data.EdgeToTriangles, b, c, t);
                AddEdge(data.EdgeToTriangles, c, a, t);
            }

            return data;
        }

        private static void AddEdge(Dictionary<long, List<int>> map, int a, int b, int triIndex)
        {
            long key = PackEdge(a, b);
            if (!map.TryGetValue(key, out var list))
            {
                list = new List<int>();
                map[key] = list;
            }
            list.Add(triIndex);
        }

        private static long PackEdge(int a, int b)
        {
            int lo = Mathf.Min(a, b);
            int hi = Mathf.Max(a, b);
            return ((long)lo << 32) | (uint)hi;
        }

        private static List<UVEdge> GetAllEdges(SubmeshData data, Vector2[] uv)
        {
            var result = new List<UVEdge>(data.EdgeToTriangles.Count);
            foreach (long key in data.EdgeToTriangles.Keys)
            {
                int lo = (int)(key >> 32);
                int hi = (int)(key & 0xFFFFFFFF);
                result.Add(new UVEdge(uv[lo], uv[hi]));
            }
            return result;
        }

        // Unity duplicates vertices along UV seams, so two triangles sharing a vertex INDEX
        // (not just a position) are guaranteed to be UV-continuous. Flood filling by shared
        // edges therefore gives exactly the UV island the clicked triangle belongs to.
        private static HashSet<int> FloodFillIsland(SubmeshData data, int startTri)
        {
            var visited = new HashSet<int> { startTri };
            var stack = new Stack<int>();
            stack.Push(startTri);

            while (stack.Count > 0)
            {
                int tri = stack.Pop();
                int a = data.Triangles[tri * 3 + 0];
                int b = data.Triangles[tri * 3 + 1];
                int c = data.Triangles[tri * 3 + 2];

                VisitNeighbors(data, a, b, tri, visited, stack);
                VisitNeighbors(data, b, c, tri, visited, stack);
                VisitNeighbors(data, c, a, tri, visited, stack);
            }

            return visited;
        }

        private static void VisitNeighbors(
            SubmeshData data,
            int v0,
            int v1,
            int selfTri,
            HashSet<int> visited,
            Stack<int> stack
        )
        {
            if (!data.EdgeToTriangles.TryGetValue(PackEdge(v0, v1), out var triList))
                return;

            foreach (int other in triList)
            {
                if (other == selfTri)
                    continue;
                if (visited.Add(other))
                    stack.Push(other);
            }
        }

        private static List<UVEdge> GetIslandBoundaryEdges(
            SubmeshData data,
            HashSet<int> island,
            Vector2[] uv
        )
        {
            var boundary = new List<UVEdge>();
            foreach (int tri in island)
            {
                int a = data.Triangles[tri * 3 + 0];
                int b = data.Triangles[tri * 3 + 1];
                int c = data.Triangles[tri * 3 + 2];

                AddIfBoundary(data, a, b, uv, boundary);
                AddIfBoundary(data, b, c, uv, boundary);
                AddIfBoundary(data, c, a, uv, boundary);
            }
            return boundary;
        }

        private static void AddIfBoundary(
            SubmeshData data,
            int v0,
            int v1,
            Vector2[] uv,
            List<UVEdge> boundary
        )
        {
            if (
                data.EdgeToTriangles.TryGetValue(PackEdge(v0, v1), out var triList)
                && triList.Count == 1
            )
            {
                boundary.Add(new UVEdge(uv[v0], uv[v1]));
            }
        }

        private static Texture2D FindMainTexture(Material mat, out string usedProperty)
        {
            usedProperty = null;
            if (mat == null)
                return null;

            foreach (string propName in MainTexturePropertyCandidates)
            {
                if (mat.HasProperty(propName) && mat.GetTexture(propName) is Texture2D tex)
                {
                    usedProperty = propName;
                    return tex;
                }
            }

            Shader shader = mat.shader;
            if (shader == null)
                return null;

            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) != ShaderUtil.ShaderPropertyType.TexEnv)
                    continue;

                string propName = ShaderUtil.GetPropertyName(shader, i);
                if (mat.GetTexture(propName) is Texture2D tex)
                {
                    usedProperty = propName;
                    return tex;
                }
            }

            return null;
        }

        private static void EnableMeshReadWrite(Mesh mesh)
        {
            if (mesh == null)
                return;

            string path = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(path))
                return;

            if (AssetImporter.GetAtPath(path) is ModelImporter importer && !importer.isReadable)
            {
                importer.isReadable = true;
                importer.SaveAndReimport();
            }
        }

        // ---------------------------------------------------------------
        // UV export
        // ---------------------------------------------------------------

        private void SaveUVMap()
        {
            if (submeshData == null)
                return;

            int width = mainTexture != null ? mainTexture.width : 1024;
            int height = mainTexture != null ? mainTexture.height : 1024;

            Color32[] pixels = new Color32[width * height];
            Color32 clear = new Color32(0, 0, 0, 0);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = clear;

            Color32 wireColor = new Color32(255, 255, 255, 255);
            foreach (UVEdge edge in allEdges)
                DrawLineOnPixels(pixels, width, height, edge.A, edge.B, wireColor);

            if (hasHit && islandBoundary != null)
            {
                Color32 islandColor = new Color32(255, 230, 25, 255);
                foreach (UVEdge edge in islandBoundary)
                    DrawLineOnPixels(pixels, width, height, edge.A, edge.B, islandColor);
            }

            if (hasHit)
            {
                float radius = Mathf.Clamp(Mathf.Min(width, height) * 0.0015f, 1f, 3f);
                DrawFilledCircleOnPixels(pixels, width, height, hitUV, radius + 1f, new Color32(0, 0, 0, 255));
                DrawFilledCircleOnPixels(pixels, width, height, hitUV, radius, new Color32(255, 0, 0, 255));
            }

            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.SetPixels32(pixels);
            tex.Apply();

            string defaultName =
                (targetRenderer != null ? targetRenderer.gameObject.name : "Mesh") + "_UV";
            string path = EditorUtility.SaveFilePanel(
                "Save UV Map",
                Application.dataPath,
                defaultName,
                "png"
            );

            if (!string.IsNullOrEmpty(path))
            {
                File.WriteAllBytes(path, tex.EncodeToPNG());
                if (path.Replace("\\", "/").StartsWith(Application.dataPath))
                    AssetDatabase.Refresh();
                Debug.Log($"[UVMeshInspector] UV map saved to {path}");
            }

            DestroyImmediate(tex);
        }

        private static void DrawLineOnPixels(
            Color32[] pixels,
            int width,
            int height,
            Vector2 uvA,
            Vector2 uvB,
            Color32 color
        )
        {
            int x0 = Mathf.RoundToInt(uvA.x * (width - 1));
            int y0 = Mathf.RoundToInt(uvA.y * (height - 1));
            int x1 = Mathf.RoundToInt(uvB.x * (width - 1));
            int y1 = Mathf.RoundToInt(uvB.y * (height - 1));

            int dx = Mathf.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                SetPixelSafe(pixels, width, height, x0, y0, color);
                if (x0 == x1 && y0 == y1)
                    break;
                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static void SetPixelSafe(
            Color32[] pixels,
            int width,
            int height,
            int x,
            int y,
            Color32 color
        )
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
                return;
            pixels[y * width + x] = color;
        }

        private static void DrawFilledCircleOnPixels(
            Color32[] pixels,
            int width,
            int height,
            Vector2 uv,
            float radius,
            Color32 color
        )
        {
            int cx = Mathf.RoundToInt(uv.x * (width - 1));
            int cy = Mathf.RoundToInt(uv.y * (height - 1));
            int r = Mathf.CeilToInt(radius);
            float rSq = radius * radius;

            for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                if (x * x + y * y <= rSq)
                    SetPixelSafe(pixels, width, height, cx + x, cy + y, color);
            }
        }

        // ---------------------------------------------------------------
        // Scene view picking
        // ---------------------------------------------------------------

        private void OnSceneGUI(SceneView sceneView)
        {
            if (targetRenderer == null)
                return;

            if (hasHit)
            {
                Handles.color = new Color(1f, 0.2f, 0.2f, 0.9f);
                Handles.SphereHandleCap(
                    0,
                    hitWorldPosition,
                    Quaternion.identity,
                    HandleUtility.GetHandleSize(hitWorldPosition) * 0.12f,
                    EventType.Repaint
                );
            }

            if (!pickModeActive)
                return;

            Event e = Event.current;
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt)
                return;

            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (TryRaycastRenderer(targetRenderer, ray, out RaycastMeshHit hit))
            {
                materialIndex = hit.SubmeshIndex;
                RefreshData();
                dataDirty = false;

                ApplyHit(hit.LocalTriangleIndex, hit.UV, hit.WorldPosition);
            }

            e.Use();
        }

        // Shared by both directions: picking a point on the mesh (Scene view -> UV) and
        // picking a point on the UV texture (texture -> mesh position).
        private void ApplyHit(int localTriangleIndex, Vector2 uv, Vector3 worldPosition)
        {
            if (submeshData != null && fullMeshCache != null)
            {
                HashSet<int> island = FloodFillIsland(submeshData, localTriangleIndex);
                islandBoundary = GetIslandBoundaryEdges(submeshData, island, fullMeshCache.UV);
                islandTriangleCount = island.Count;
            }

            hitUV = uv;
            hitWorldPosition = worldPosition;
            hasHit = true;

            Repaint();
            SceneView.RepaintAll();
        }

        private bool TryRaycastRenderer(Renderer renderer, Ray ray, out RaycastMeshHit hit)
        {
            hit = default;

            if (fullMeshCache == null)
                return false;

            // Cheap early-out before scanning every triangle.
            if (!renderer.bounds.IntersectRay(ray))
                return false;

            Mesh mesh = GetCurrentMesh(renderer);
            if (mesh == null)
                return false;

            bool isSkinned = renderer is SkinnedMeshRenderer;
            if (!isSkinned && !mesh.isReadable)
                return false;

            // Only positions need to be fetched fresh each click; topology/UV come from the cache.
            Vector3[] verts = mesh.vertices;
            Vector2[] uv = fullMeshCache.UV;
            int[] tris = fullMeshCache.Triangles;
            Matrix4x4 localToWorld = renderer.transform.localToWorldMatrix;

            float closest = float.MaxValue;
            bool found = false;

            int subCount = fullMeshCache.SubmeshTriStart.Length;
            for (int sm = 0; sm < subCount; sm++)
            {
                int startTri = fullMeshCache.SubmeshTriStart[sm];
                int triCount = fullMeshCache.SubmeshTriCount[sm];

                for (int t = 0; t < triCount; t++)
                {
                    int gi = (startTri + t) * 3;
                    int ia = tris[gi + 0];
                    int ib = tris[gi + 1];
                    int ic = tris[gi + 2];

                    Vector3 wa = localToWorld.MultiplyPoint3x4(verts[ia]);
                    Vector3 wb = localToWorld.MultiplyPoint3x4(verts[ib]);
                    Vector3 wc = localToWorld.MultiplyPoint3x4(verts[ic]);

                    if (
                        RayIntersectsTriangle(ray, wa, wb, wc, out float dist, out Vector3 bary)
                        && dist < closest
                    )
                    {
                        closest = dist;
                        found = true;

                        Vector2 uva = uv != null && uv.Length > ia ? uv[ia] : Vector2.zero;
                        Vector2 uvb = uv != null && uv.Length > ib ? uv[ib] : Vector2.zero;
                        Vector2 uvc = uv != null && uv.Length > ic ? uv[ic] : Vector2.zero;

                        hit.WorldPosition = ray.origin + ray.direction * dist;
                        hit.SubmeshIndex = sm;
                        hit.LocalTriangleIndex = t;
                        hit.UV = uva * bary.x + uvb * bary.y + uvc * bary.z;
                    }
                }
            }

            return found;
        }

        // Reverse of TryRaycastRenderer: given a UV point, find which triangle of the currently
        // displayed submesh contains it and interpolate the matching world-space position.
        private bool TryFindWorldPositionFromUV(
            Vector2 uv,
            out int localTriangleIndex,
            out Vector3 worldPosition
        )
        {
            localTriangleIndex = -1;
            worldPosition = default;

            if (submeshData == null || fullMeshCache == null || targetRenderer == null)
                return false;

            Mesh mesh = GetCurrentMesh(targetRenderer);
            if (mesh == null)
                return false;

            Vector3[] verts = mesh.vertices;
            Vector2[] meshUV = fullMeshCache.UV;
            Matrix4x4 localToWorld = targetRenderer.transform.localToWorldMatrix;

            for (int t = 0; t < submeshData.TriCount; t++)
            {
                int ia = submeshData.Triangles[t * 3 + 0];
                int ib = submeshData.Triangles[t * 3 + 1];
                int ic = submeshData.Triangles[t * 3 + 2];

                if (
                    TryGetBarycentric2D(uv, meshUV[ia], meshUV[ib], meshUV[ic], out Vector3 bary)
                )
                {
                    Vector3 pa = verts[ia];
                    Vector3 pb = verts[ib];
                    Vector3 pc = verts[ic];
                    Vector3 localPos = pa * bary.x + pb * bary.y + pc * bary.z;

                    localTriangleIndex = t;
                    worldPosition = localToWorld.MultiplyPoint3x4(localPos);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetBarycentric2D(
            Vector2 p,
            Vector2 a,
            Vector2 b,
            Vector2 c,
            out Vector3 baryUVW
        )
        {
            baryUVW = Vector3.zero;

            Vector2 v0 = b - a;
            Vector2 v1 = c - a;
            Vector2 v2 = p - a;

            float den = v0.x * v1.y - v1.x * v0.y;
            if (Mathf.Abs(den) < 1e-12f)
                return false;

            float invDen = 1f / den;
            float v = (v2.x * v1.y - v1.x * v2.y) * invDen;
            float w = (v0.x * v2.y - v2.x * v0.y) * invDen;
            float u = 1f - v - w;

            const float epsilon = -0.001f; // small tolerance for points right on an edge
            if (u < epsilon || v < epsilon || w < epsilon)
                return false;

            baryUVW = new Vector3(u, v, w);
            return true;
        }

        private static bool RayIntersectsTriangle(
            Ray ray,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            out float distance,
            out Vector3 baryUVW
        )
        {
            distance = 0f;
            baryUVW = Vector3.zero;

            Vector3 edge1 = b - a;
            Vector3 edge2 = c - a;
            Vector3 h = Vector3.Cross(ray.direction, edge2);
            float det = Vector3.Dot(edge1, h);

            if (Mathf.Abs(det) < 1e-8f)
                return false;

            float invDet = 1f / det;
            Vector3 s = ray.origin - a;
            float u = Vector3.Dot(s, h) * invDet;
            if (u < 0f || u > 1f)
                return false;

            Vector3 q = Vector3.Cross(s, edge1);
            float v = Vector3.Dot(ray.direction, q) * invDet;
            if (v < 0f || u + v > 1f)
                return false;

            float t = Vector3.Dot(edge2, q) * invDet;
            if (t < 0f)
                return false;

            distance = t;
            baryUVW = new Vector3(1f - u - v, u, v);
            return true;
        }
    }
}
