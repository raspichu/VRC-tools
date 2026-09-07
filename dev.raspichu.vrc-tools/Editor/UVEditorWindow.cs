using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace raspichu.vrc_tools.editor
{
    // Blender-style UV editor: pick a mesh/material, drag UV points around on the texture in
    // real time, then save the edited layout as a brand new Mesh asset (the original is never
    // touched). Also carries the "click mesh/texture -> red marker dot" picker from UV Mesh
    // Inspector, toggled separately so it doesn't fight point dragging over the same clicks.
    public class UVEditorWindow : EditorWindow
    {
        private struct UVEdgeIndices
        {
            public int A;
            public int B;
        }

        private class SubmeshData
        {
            public int[] Triangles; // vertex indices, flattened (length = TriCount * 3)
            public int TriCount;
            public Dictionary<long, List<int>> EdgeToTriangles;
        }

        private struct RaycastMeshHit
        {
            public Vector3 WorldPosition;
            public int SubmeshIndex;
            public Vector2 UV;
        }

        // Topology doesn't change between clicks on the same mesh, only vertex positions do
        // (via animation) and UVs (via our own edits), so this is built once per renderer.
        private class FullMeshCache
        {
            public int[] Triangles; // all submeshes concatenated, in submesh order
            public int[] SubmeshTriStart;
            public int[] SubmeshTriCount;
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

        private const float PointHitPixels = 7f;
        private const float PointDrawPixels = 3.5f;

        // Lazily-built, cached across every UV Editor window/session (until domain reload) - a
        // tiny soft-edged white circle, tinted per-point via GUI.color, so points render as round
        // dots at the same cost as a flat GUI.DrawTexture blit.
        private static Texture2D _dotTexture;
        private static Texture2D DotTexture
        {
            get
            {
                if (_dotTexture == null)
                {
                    const int size = 16;
                    _dotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    float radius = size * 0.5f;
                    var pixels = new Color32[size * size];
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - radius;
                        float dy = y + 0.5f - radius;
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        byte alpha = (byte)(Mathf.Clamp01(radius - dist) * 255);
                        pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                    }
                    _dotTexture.SetPixels32(pixels);
                    _dotTexture.Apply();
                }
                return _dotTexture;
            }
        }

        private Renderer targetRenderer;
        private int materialIndex;
        private bool dataDirty = true;
        private bool fullCacheDirty = true;

        private Mesh cachedBakedMesh; // reused scratch mesh for SkinnedMeshRenderer bakes
        private FullMeshCache fullMeshCache;
        private int cachedSubmeshIndex = -1;
        private SubmeshData submeshData;
        private List<UVEdgeIndices> allEdges = new List<UVEdgeIndices>();
        private Vector3[] allEdgeScreenPoints;
        private List<int> submeshVertexIndices = new List<int>(); // unique verts, drawn as handles
        private Texture2D mainTexture;
        private string mainTextureProperty;
        private string dataWarning;
        private bool showReadWriteFix;

        // Editable UV data - full mesh length, persists across material-slot switches, only reset
        // when the target mesh itself changes (or the user explicitly resets it). [SerializeField]
        // so Undo.RegisterCompleteObjectUndo (below) can actually snapshot/restore its contents -
        // a plain private field wouldn't be captured by Unity's serialization-based undo.
        [SerializeField]
        private Vector2[] workingUV;
        private int workingUVSourceId = -1;

        // Live preview: while a renderer is loaded, its actual mesh is swapped for a temporary
        // clone whose .uv we update on every edit, so the texture mapping updates in real time on
        // the mesh itself (Scene view included) instead of only in this window's 2D overlay. The
        // original mesh is restored the moment the renderer changes or this window closes.
        private Mesh tempPreviewMesh;
        private Mesh originalMesh;
        private Renderer swappedRenderer;

        private readonly HashSet<int> selectedVerts = new HashSet<int>();

        private bool isDraggingPoints;
        private Vector2 dragStartMouse;
        private Dictionary<int, Vector2> dragStartUVs;

        private bool isBoxSelecting;
        private Vector2 boxSelectStartMouse;
        private Vector2 boxSelectCurMouse;

        // Blender-style modal transform: press G/S/R with a selection to start, move the mouse
        // with no button held to preview it live, left-click/Enter confirms, right-click/Esc
        // cancels. During G or S, X/Y locks the move/scale to that axis only (press again to
        // unlock) - not meaningful for R, which only has the one rotation axis.
        private enum TransformMode
        {
            None,
            Move,
            Scale,
            Rotate,
        }

        private TransformMode transformMode = TransformMode.None;
        private char axisLock;
        private Vector2 transformStartMouse;
        private Vector2 transformPivotUV;
        private Dictionary<int, Vector2> transformStartUVs;

        // Point-marker picker, same idea as UV Mesh Inspector's "Pick In Scene": toggled on its
        // own so a click either edits points or drops the marker, never both at once.
        private bool pickModeActive;
        private bool hasHit;
        private Vector2 hitUV;
        private Vector3 hitWorldPosition;

        private bool showFullWire = true;
        private float zoom = 1f;
        private Vector2 pan = Vector2.zero;

        [MenuItem("Tools/Pichu/UV Editor", false, 23)]
        public static void ShowWindow()
        {
            var window = GetWindow<UVEditorWindow>("UV Editor");
            window.titleContent = new GUIContent(
                "UV Editor",
                EditorGUIUtility.IconContent("d_Texture Icon").image
            );
            window.minSize = new Vector2(380, 560);
            window.AssignRendererFromGameObject(Selection.activeGameObject);
        }

        [MenuItem("GameObject/Pichu/UV Editor", false, 23)]
        public static void ShowWindowContext(MenuCommand command)
        {
            var window = GetWindow<UVEditorWindow>("UV Editor");
            window.minSize = new Vector2(380, 560);
            window.AssignRendererFromGameObject(command.context as GameObject ?? Selection.activeGameObject);
            window.Focus();
        }

        [MenuItem("CONTEXT/SkinnedMeshRenderer/Open UV Editor")]
        private static void OpenFromSkinnedMeshRenderer(MenuCommand command) =>
            OpenWithRenderer(command.context as Renderer);

        [MenuItem("CONTEXT/MeshRenderer/Open UV Editor")]
        private static void OpenFromMeshRenderer(MenuCommand command) =>
            OpenWithRenderer(command.context as Renderer);

        private static void OpenWithRenderer(Renderer renderer)
        {
            if (renderer == null)
                return;
            var window = GetWindow<UVEditorWindow>("UV Editor");
            window.minSize = new Vector2(380, 560);
            window.SetTargetRenderer(renderer);
            window.Focus();
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            // wantsMouseMove is deliberately NOT enabled here: it makes Unity fire a MouseMove
            // event (and a full Layout+Repaint pass) on every pixel of mouse movement over the
            // whole window, all the time - that's what made panning/zooming feel sluggish here
            // vs UV Mesh Inspector. It's only turned on for the few seconds a G/S/R modal
            // transform is actually running (see BeginModalTransform/Confirm/CancelModalTransform).
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            RestoreSwappedRenderer();
            if (cachedBakedMesh != null)
            {
                DestroyImmediate(cachedBakedMesh);
                cachedBakedMesh = null;
            }
        }

        private void OnUndoRedoPerformed()
        {
            Repaint();
            SceneView.RepaintAll();
        }

        private void AssignRendererFromGameObject(GameObject go)
        {
            Renderer found = null;
            if (go != null)
            {
                found = go.GetComponent<SkinnedMeshRenderer>();
                if (found == null)
                    found = go.GetComponent<MeshRenderer>();
            }
            if (found != null)
                SetTargetRenderer(found);
        }

        private void SetTargetRenderer(Renderer renderer)
        {
            RestoreSwappedRenderer(); // undo any previous renderer's live-preview swap first

            targetRenderer = renderer;
            materialIndex = 0;
            hasHit = false;
            pickModeActive = false;
            selectedVerts.Clear();
            isDraggingPoints = false;
            isBoxSelecting = false;
            transformMode = TransformMode.None;
            wantsMouseMove = false;
            transformStartUVs = null;
            ResetView();
            fullCacheDirty = true;
            dataDirty = true;
            workingUV = null;
            workingUVSourceId = -1;
        }

        private void ResetView()
        {
            zoom = 1f;
            pan = Vector2.zero;
        }

        private Vector2 scrollPos;

        private void OnGUI()
        {
            // The window can open small enough that the buttons below the preview are cut off
            // with no visible hint they exist - wrap everything in a scroll view so it's always
            // reachable. try/finally guarantees EndScrollView runs even through the early
            // `return`s below (a mismatched Begin/End otherwise breaks IMGUI's layout for a frame).
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);
            try
            {
                DrawContent();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawContent()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("UV Editor", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Drag to move the selected points, drag empty space to box-select, Shift+click to add/remove, "
                    + "L selects the island under the cursor. Right-drag or middle-drag pans, scroll zooms. "
                    + "'Pick Point' drops a marker instead of editing.",
                MessageType.None
            );
            EditorGUILayout.Space(4);

            Renderer newRenderer = (Renderer)
                EditorGUILayout.ObjectField("Target Renderer", targetRenderer, typeof(Renderer), true);
            if (newRenderer != targetRenderer)
                SetTargetRenderer(newRenderer);

            if (targetRenderer == null)
            {
                EditorGUILayout.HelpBox(
                    "Drag a SkinnedMeshRenderer or MeshRenderer (or its GameObject) here, or right-click "
                        + "one and choose 'Open UV Editor'.",
                    MessageType.Info
                );
                return;
            }

            Material[] materials = targetRenderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                EditorGUILayout.HelpBox("This renderer has no materials assigned.", MessageType.Warning);
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
                selectedVerts.Clear();
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
                    EnableMeshReadWrite(GetSourceAssetMesh());
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
                    pickModeActive ? "Picking... (click mesh/texture)" : "Pick Point",
                    GUILayout.Height(24)
                )
            )
                pickModeActive = !pickModeActive;
            GUI.backgroundColor = Color.white;

            EditorGUI.BeginDisabledGroup(!hasHit);
            if (GUILayout.Button("Clear Marker", GUILayout.Width(90), GUILayout.Height(24)))
                hasHit = false;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            showFullWire = EditorGUILayout.ToggleLeft("Show UV wireframe", showFullWire);
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField($"Zoom: {zoom:F1}x", EditorStyles.miniLabel, GUILayout.Width(60));
            if (GUILayout.Button("Reset Zoom", GUILayout.Width(80)))
                ResetView();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"{selectedVerts.Count} point(s) selected", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(selectedVerts.Count == 0);
            if (GUILayout.Button("Deselect All", GUILayout.Width(90)))
                selectedVerts.Clear();
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            DrawPreview();

            if (hasHit)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(
                    $"Marker UV: ({hitUV.x:F3}, {hitUV.y:F3})",
                    EditorStyles.miniLabel
                );
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(selectedVerts.Count == 0);
            if (GUILayout.Button("Reset Selection", GUILayout.Height(24)))
                ResetSelection();
            EditorGUI.EndDisabledGroup();
            GUI.backgroundColor = new Color(0.4f, 0.85f, 0.5f);
            if (GUILayout.Button("Save Mesh As...", GUILayout.Height(24)))
                SaveMeshAs();
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPreview()
        {
            float size = Mathf.Min(position.width - 24f, 520f);
            if (size < 32f)
                return;

            EditorGUILayout.LabelField(
                "G to move   R to rotate   S to scale   L to select island",
                EditorStyles.centeredGreyMiniLabel
            );

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

            HandlePointInteraction(imageRect);

            if (Event.current.type != EventType.Repaint)
            {
                GUI.EndGroup();
                return;
            }

            Handles.color = new Color(1f, 1f, 1f, 0.15f);
            DrawRectOutline(imageRect);

            if (showFullWire && allEdges != null && allEdges.Count > 0 && workingUV != null)
            {
                if (allEdgeScreenPoints == null || allEdgeScreenPoints.Length != allEdges.Count * 2)
                    allEdgeScreenPoints = new Vector3[allEdges.Count * 2];

                for (int i = 0; i < allEdges.Count; i++)
                {
                    allEdgeScreenPoints[i * 2] = UVToScreen(workingUV[allEdges[i].A], imageRect);
                    allEdgeScreenPoints[i * 2 + 1] = UVToScreen(workingUV[allEdges[i].B], imageRect);
                }

                Handles.color = new Color(1f, 1f, 1f, 0.35f);
                Handles.DrawLines(allEdgeScreenPoints);
            }

            if (submeshVertexIndices != null && workingUV != null)
            {
                // A cached round alpha texture blitted via GUI.DrawTexture instead of
                // Handles.DrawSolidDisc - a mesh's worth of points redrawn every drag frame made
                // the antialiased Handles discs the actual bottleneck. A texture blit is the same
                // cheap cost as a flat rect, but still looks like a dot instead of a square.
                Color unselectedColor = new Color(0.2f, 0.9f, 1f, 0.9f);
                float unselectedSize = PointDrawPixels * 2f;
                float selectedSize = (PointDrawPixels + 1.5f) * 2f;
                Color prevColor = GUI.color;
                Texture2D dot = DotTexture;

                foreach (int vi in submeshVertexIndices)
                {
                    Vector3 p = UVToScreen(workingUV[vi], imageRect);
                    bool isSelected = selectedVerts.Contains(vi);
                    float pointSize = isSelected ? selectedSize : unselectedSize;
                    float half = pointSize * 0.5f;
                    GUI.color = isSelected ? Color.yellow : unselectedColor;
                    GUI.DrawTexture(new Rect(p.x - half, p.y - half, pointSize, pointSize), dot);
                }

                GUI.color = prevColor;
            }

            if (isBoxSelecting)
            {
                Rect sel = GetSelectionScreenRect();
                Vector3[] corners =
                {
                    new Vector3(sel.xMin, sel.yMin, 0),
                    new Vector3(sel.xMax, sel.yMin, 0),
                    new Vector3(sel.xMax, sel.yMax, 0),
                    new Vector3(sel.xMin, sel.yMax, 0),
                };
                Handles.DrawSolidRectangleWithOutline(
                    corners,
                    new Color(0.3f, 0.6f, 1f, 0.12f),
                    new Color(0.5f, 0.8f, 1f, 0.9f)
                );
            }

            if (hasHit)
            {
                Vector3 dot = UVToScreen(hitUV, imageRect);
                Handles.color = Color.black;
                Handles.DrawSolidDisc(dot, Vector3.forward, 5.5f);
                Handles.color = Color.red;
                Handles.DrawSolidDisc(dot, Vector3.forward, 4f);
            }

            if (transformMode != TransformMode.None)
            {
                string axisSuffix = axisLock != '\0' ? $" [{axisLock} only]" : "";
                string label =
                    transformMode == TransformMode.Move
                        ? "Move (G)" + axisSuffix + " - X/Y to lock axis, click/Enter to confirm, Esc to cancel"
                        : transformMode == TransformMode.Rotate
                            ? "Rotate (R) - click/Enter to confirm, Esc to cancel"
                            : "Scale (S)"
                                + axisSuffix
                                + " - X/Y to lock axis, click/Enter to confirm, Esc to cancel";
                GUI.Label(new Rect(6, 6, imageRect.width - 12f, 18), label, EditorStyles.whiteBoldLabel);
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
            else if (e.type == EventType.MouseDrag && (e.button == 1 || e.button == 2))
            {
                pan += e.delta;
                e.Use();
                Repaint();
            }
        }

        // Selecting/dragging UV points, or box-selecting a group of them. When pick mode is on,
        // clicks instead drop the red marker dot (mirrors UV Mesh Inspector's picker) - the two
        // never run on the same click.
        private void HandlePointInteraction(Rect imageRect)
        {
            Event e = Event.current;

            if (transformMode != TransformMode.None)
            {
                HandleModalTransform(imageRect, e);
                return;
            }

            if (pickModeActive)
            {
                HandleTextureClickForMarker(imageRect, e);
                return;
            }

            if (submeshVertexIndices == null || submeshVertexIndices.Count == 0 || workingUV == null)
                return;

            if (!isDraggingPoints && !isBoxSelecting && !imageRect.Contains(e.mousePosition))
                return;

            if (
                e.type == EventType.KeyDown
                && e.keyCode == KeyCode.L
                && !isDraggingPoints
                && !isBoxSelecting
            )
            {
                HashSet<int> islandVerts = FindIslandVertsAt(e.mousePosition, imageRect);
                if (islandVerts != null && islandVerts.Count > 0)
                {
                    if (e.shift)
                        selectedVerts.UnionWith(islandVerts);
                    else
                    {
                        selectedVerts.Clear();
                        selectedVerts.UnionWith(islandVerts);
                    }
                    Repaint();
                }
                e.Use();
                return;
            }

            if (
                e.type == EventType.KeyDown
                && !isDraggingPoints
                && !isBoxSelecting
                && selectedVerts.Count > 0
            )
            {
                if (e.keyCode == KeyCode.G)
                {
                    BeginModalTransform(TransformMode.Move, imageRect, e.mousePosition);
                    e.Use();
                    return;
                }
                if (e.keyCode == KeyCode.S)
                {
                    BeginModalTransform(TransformMode.Scale, imageRect, e.mousePosition);
                    e.Use();
                    return;
                }
                if (e.keyCode == KeyCode.R)
                {
                    BeginModalTransform(TransformMode.Rotate, imageRect, e.mousePosition);
                    e.Use();
                    return;
                }
            }

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button != 0)
                        return;

                    int hitVert = FindNearestPoint(e.mousePosition, imageRect);
                    if (hitVert >= 0)
                    {
                        if (e.shift)
                        {
                            if (!selectedVerts.Remove(hitVert))
                                selectedVerts.Add(hitVert);
                        }
                        else if (!selectedVerts.Contains(hitVert))
                        {
                            selectedVerts.Clear();
                            selectedVerts.Add(hitVert);
                        }

                        BeginDragSelected(e.mousePosition);
                    }
                    else
                    {
                        if (!e.shift)
                            selectedVerts.Clear();
                        isBoxSelecting = true;
                        boxSelectStartMouse = e.mousePosition;
                        boxSelectCurMouse = e.mousePosition;
                    }
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag:
                    if (e.button != 0)
                        return;
                    if (isDraggingPoints)
                    {
                        Vector2 screenDelta = e.mousePosition - dragStartMouse;
                        Vector2 uvDelta = new Vector2(
                            screenDelta.x / imageRect.width,
                            -screenDelta.y / imageRect.height
                        );
                        foreach (var kv in dragStartUVs)
                            workingUV[kv.Key] = kv.Value + uvDelta;
                        ApplyWorkingUVToPreviewMesh();
                        SceneView.RepaintAll();
                        e.Use();
                        Repaint();
                    }
                    else if (isBoxSelecting)
                    {
                        boxSelectCurMouse = e.mousePosition;
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    if (isDraggingPoints)
                    {
                        isDraggingPoints = false;
                        dragStartUVs = null;
                        e.Use();
                    }
                    else if (isBoxSelecting)
                    {
                        Rect sel = GetSelectionScreenRect();
                        foreach (int vi in submeshVertexIndices)
                        {
                            Vector3 sp = UVToScreen(workingUV[vi], imageRect);
                            if (sel.Contains(new Vector2(sp.x, sp.y)))
                                selectedVerts.Add(vi);
                        }
                        isBoxSelecting = false;
                        e.Use();
                    }
                    Repaint();
                    break;
            }
        }

        private int FindNearestPoint(Vector2 mousePos, Rect imageRect)
        {
            int best = -1;
            float bestDist = PointHitPixels;
            foreach (int vi in submeshVertexIndices)
            {
                Vector3 sp = UVToScreen(workingUV[vi], imageRect);
                float d = Vector2.Distance(mousePos, new Vector2(sp.x, sp.y));
                if (d < bestDist)
                {
                    bestDist = d;
                    best = vi;
                }
            }
            return best;
        }

        // Snapshots the whole editable UV array onto the Undo stack BEFORE the drag changes
        // anything, so one Ctrl+Z reverts the entire gesture (an accidental drag included) in a
        // single step, regardless of how many in-between positions were painted during it.
        private void BeginDragSelected(Vector2 mousePos)
        {
            Undo.RegisterCompleteObjectUndo(this, "Move UV Points");

            isDraggingPoints = true;
            dragStartMouse = mousePos;
            dragStartUVs = new Dictionary<int, Vector2>();
            foreach (int v in selectedVerts)
                dragStartUVs[v] = workingUV[v];
        }

        // ---------------------------------------------------------------
        // Modal transform (G / S, Blender-style)
        // ---------------------------------------------------------------

        private void BeginModalTransform(TransformMode mode, Rect imageRect, Vector2 mousePos)
        {
            string undoLabel =
                mode == TransformMode.Move ? "Move UV Points"
                : mode == TransformMode.Scale ? "Scale UV Points"
                : "Rotate UV Points";
            Undo.RegisterCompleteObjectUndo(this, undoLabel);

            transformMode = mode;
            wantsMouseMove = true; // only while the modal transform actually needs to track the mouse
            axisLock = '\0';
            transformStartMouse = mousePos;

            transformStartUVs = new Dictionary<int, Vector2>();
            Vector2 sum = Vector2.zero;
            foreach (int v in selectedVerts)
            {
                transformStartUVs[v] = workingUV[v];
                sum += workingUV[v];
            }
            transformPivotUV = sum / selectedVerts.Count;

            Repaint();
        }

        private void HandleModalTransform(Rect imageRect, Event e)
        {
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                ApplyModalTransform(imageRect, e.mousePosition);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.KeyDown)
            {
                TransformMode? switchTo =
                    e.keyCode == KeyCode.G ? TransformMode.Move
                    : e.keyCode == KeyCode.S ? TransformMode.Scale
                    : e.keyCode == KeyCode.R ? TransformMode.Rotate
                    : (TransformMode?)null;

                if (switchTo.HasValue && switchTo.Value != transformMode)
                {
                    // Undo whatever this transform already applied before switching to the new
                    // one, so they don't compound - matches Blender's G-then-S behavior.
                    if (transformStartUVs != null)
                    {
                        foreach (var kv in transformStartUVs)
                            workingUV[kv.Key] = kv.Value;
                        ApplyWorkingUVToPreviewMesh();
                        SceneView.RepaintAll();
                    }
                    BeginModalTransform(switchTo.Value, imageRect, e.mousePosition);
                    e.Use();
                    return;
                }

                bool axisLockable = transformMode == TransformMode.Move || transformMode == TransformMode.Scale;
                if (axisLockable && e.keyCode == KeyCode.X)
                {
                    axisLock = axisLock == 'X' ? '\0' : 'X';
                    ApplyModalTransform(imageRect, e.mousePosition);
                    e.Use();
                    Repaint();
                }
                else if (axisLockable && e.keyCode == KeyCode.Y)
                {
                    axisLock = axisLock == 'Y' ? '\0' : 'Y';
                    ApplyModalTransform(imageRect, e.mousePosition);
                    e.Use();
                    Repaint();
                }
                else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    ConfirmModalTransform();
                    e.Use();
                }
                else if (e.keyCode == KeyCode.Escape)
                {
                    CancelModalTransform();
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDown && e.button == 0)
            {
                ConfirmModalTransform();
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 1)
            {
                CancelModalTransform();
                e.Use();
            }
        }

        private void ApplyModalTransform(Rect imageRect, Vector2 currentMouse)
        {
            if (transformMode == TransformMode.Move)
            {
                Vector2 screenDelta = currentMouse - transformStartMouse;
                Vector2 uvDelta = new Vector2(
                    screenDelta.x / imageRect.width,
                    -screenDelta.y / imageRect.height
                );
                if (axisLock == 'X')
                    uvDelta.y = 0f;
                else if (axisLock == 'Y')
                    uvDelta.x = 0f;
                foreach (var kv in transformStartUVs)
                    workingUV[kv.Key] = kv.Value + uvDelta;
            }
            else if (transformMode == TransformMode.Scale)
            {
                // Distance from the pivot to the mouse, start vs current, drives the scale factor -
                // dragging away from the pivot grows the selection, dragging toward it shrinks it.
                Vector3 pivotScreen3 = UVToScreen(transformPivotUV, imageRect);
                Vector2 pivotScreen = new Vector2(pivotScreen3.x, pivotScreen3.y);
                float startDist = Vector2.Distance(transformStartMouse, pivotScreen);
                float curDist = Vector2.Distance(currentMouse, pivotScreen);
                float factor = startDist > 0.5f ? curDist / startDist : 1f;

                foreach (var kv in transformStartUVs)
                {
                    Vector2 offset = kv.Value - transformPivotUV;
                    Vector2 newOffset =
                        axisLock == 'X' ? new Vector2(offset.x * factor, offset.y)
                        : axisLock == 'Y' ? new Vector2(offset.x, offset.y * factor)
                        : offset * factor;
                    workingUV[kv.Key] = transformPivotUV + newOffset;
                }
            }
            else if (transformMode == TransformMode.Rotate)
            {
                // Angle from the pivot to the mouse, start vs current (in UV space, so it isn't
                // skewed by a non-square preview), drives how far the selection turns.
                Vector2 startUV = ScreenToUV(transformStartMouse, imageRect);
                Vector2 curUV = ScreenToUV(currentMouse, imageRect);
                float startAngle = Mathf.Atan2(startUV.y - transformPivotUV.y, startUV.x - transformPivotUV.x);
                float curAngle = Mathf.Atan2(curUV.y - transformPivotUV.y, curUV.x - transformPivotUV.x);
                float delta = curAngle - startAngle;
                float cos = Mathf.Cos(delta);
                float sin = Mathf.Sin(delta);

                foreach (var kv in transformStartUVs)
                {
                    Vector2 offset = kv.Value - transformPivotUV;
                    Vector2 rotated = new Vector2(
                        offset.x * cos - offset.y * sin,
                        offset.x * sin + offset.y * cos
                    );
                    workingUV[kv.Key] = transformPivotUV + rotated;
                }
            }

            ApplyWorkingUVToPreviewMesh();
            SceneView.RepaintAll();
        }

        private static Vector2 ScreenToUV(Vector2 screenPoint, Rect imageRect)
        {
            return new Vector2(
                (screenPoint.x - imageRect.x) / imageRect.width,
                1f - (screenPoint.y - imageRect.y) / imageRect.height
            );
        }

        private void ConfirmModalTransform()
        {
            transformMode = TransformMode.None;
            wantsMouseMove = false;
            transformStartUVs = null;
            axisLock = '\0';
            Repaint();
        }

        private void CancelModalTransform()
        {
            if (transformStartUVs != null)
            {
                foreach (var kv in transformStartUVs)
                    workingUV[kv.Key] = kv.Value;
                ApplyWorkingUVToPreviewMesh();
                SceneView.RepaintAll();
            }

            transformMode = TransformMode.None;
            wantsMouseMove = false;
            transformStartUVs = null;
            axisLock = '\0';
            Repaint();
        }

        // All vertices of the UV island under the cursor (flood fill by shared edges), or null if
        // the click didn't land on the current submesh at all.
        private HashSet<int> FindIslandVertsAt(Vector2 mousePos, Rect imageRect)
        {
            Vector2 uv = new Vector2(
                (mousePos.x - imageRect.x) / imageRect.width,
                1f - (mousePos.y - imageRect.y) / imageRect.height
            );

            int tri = FindTriangleContainingUV(uv);
            if (tri < 0)
            {
                int nearestVert = FindNearestPoint(mousePos, imageRect);
                if (nearestVert < 0)
                    return null;
                tri = FindAnyTriangleForVertex(nearestVert);
                if (tri < 0)
                    return null;
            }

            HashSet<int> islandTris = FloodFillIsland(submeshData, tri);
            var verts = new HashSet<int>();
            foreach (int t in islandTris)
            {
                verts.Add(submeshData.Triangles[t * 3 + 0]);
                verts.Add(submeshData.Triangles[t * 3 + 1]);
                verts.Add(submeshData.Triangles[t * 3 + 2]);
            }
            return verts;
        }

        private int FindTriangleContainingUV(Vector2 uv)
        {
            if (submeshData == null || workingUV == null)
                return -1;

            for (int t = 0; t < submeshData.TriCount; t++)
            {
                int ia = submeshData.Triangles[t * 3 + 0];
                int ib = submeshData.Triangles[t * 3 + 1];
                int ic = submeshData.Triangles[t * 3 + 2];

                if (TryGetBarycentric2D(uv, workingUV[ia], workingUV[ib], workingUV[ic], out _))
                    return t;
            }
            return -1;
        }

        private int FindAnyTriangleForVertex(int vertexIndex)
        {
            if (submeshData == null)
                return -1;

            for (int t = 0; t < submeshData.TriCount; t++)
            {
                if (
                    submeshData.Triangles[t * 3 + 0] == vertexIndex
                    || submeshData.Triangles[t * 3 + 1] == vertexIndex
                    || submeshData.Triangles[t * 3 + 2] == vertexIndex
                )
                    return t;
            }
            return -1;
        }

        // Unity duplicates vertices along UV seams, so two triangles sharing a vertex INDEX (not
        // just a position) are guaranteed to be UV-continuous - flood filling by shared edges
        // therefore gives exactly the UV island the starting triangle belongs to.
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

        private Rect GetSelectionScreenRect()
        {
            float xMin = Mathf.Min(boxSelectStartMouse.x, boxSelectCurMouse.x);
            float xMax = Mathf.Max(boxSelectStartMouse.x, boxSelectCurMouse.x);
            float yMin = Mathf.Min(boxSelectStartMouse.y, boxSelectCurMouse.y);
            float yMax = Mathf.Max(boxSelectStartMouse.y, boxSelectCurMouse.y);
            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private void HandleTextureClickForMarker(Rect imageRect, Event e)
        {
            if (e.type != EventType.MouseDown || e.button != 0)
                return;

            if (submeshData == null || !imageRect.Contains(e.mousePosition))
                return;

            Vector2 uv = new Vector2(
                (e.mousePosition.x - imageRect.x) / imageRect.width,
                1f - (e.mousePosition.y - imageRect.y) / imageRect.height
            );

            if (TryFindWorldPositionFromUV(uv, out Vector3 worldPos))
                ApplyHit(uv, worldPos);

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
                return new Rect(container.x, container.y + (container.height - h) * 0.5f, container.width, h);
            }
            else
            {
                float w = container.height * aspect;
                return new Rect(container.x + (container.width - w) * 0.5f, container.y, w, container.height);
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

            Mesh sourceMesh = GetSourceAssetMesh();
            if (sourceMesh == null)
            {
                fullMeshCache = null;
                submeshData = null;
                allEdges.Clear();
                submeshVertexIndices.Clear();
                return;
            }

            bool isSkinned = targetRenderer is SkinnedMeshRenderer;
            if (!isSkinned && !sourceMesh.isReadable)
            {
                dataWarning = "This mesh is not marked as Read/Write Enabled, so its UV data can't be edited.";
                showReadWriteFix = true;
                fullMeshCache = null;
                submeshData = null;
                allEdges.Clear();
                submeshVertexIndices.Clear();
                return;
            }

            int subCount = sourceMesh.subMeshCount;
            if (subCount == 0)
            {
                fullMeshCache = null;
                submeshData = null;
                allEdges.Clear();
                submeshVertexIndices.Clear();
                return;
            }

            Vector2[] originalUV = sourceMesh.uv;
            if (originalUV == null || originalUV.Length == 0)
            {
                dataWarning = "This mesh has no UV coordinates.";
                fullMeshCache = null;
                submeshData = null;
                allEdges.Clear();
                submeshVertexIndices.Clear();
                return;
            }

            if (workingUV == null || workingUVSourceId != sourceMesh.GetInstanceID())
            {
                workingUV = (Vector2[])originalUV.Clone();
                workingUVSourceId = sourceMesh.GetInstanceID();
                selectedVerts.Clear();
                SwapInTempMesh(targetRenderer, sourceMesh);
            }

            if (fullCacheDirty || fullMeshCache == null)
            {
                BuildFullMeshCache(sourceMesh, subCount);
                fullCacheDirty = false;
                cachedSubmeshIndex = -1;
            }

            int clampedIndex = Mathf.Clamp(materialIndex, 0, subCount - 1);

            if (submeshData == null || cachedSubmeshIndex != clampedIndex)
            {
                int[] subTriangles = SliceSubmeshTriangles(fullMeshCache, clampedIndex);
                submeshData = BuildSubmeshData(subTriangles);
                allEdges = GetAllEdgesIndices(submeshData);
                allEdgeScreenPoints = null;
                submeshVertexIndices = new HashSet<int>(subTriangles).OrderBy(i => i).ToList();
                cachedSubmeshIndex = clampedIndex;
            }

            Material[] materials = targetRenderer.sharedMaterials;
            if (materials != null && clampedIndex < materials.Length)
                mainTexture = FindMainTexture(materials[clampedIndex], out mainTextureProperty);
        }

        private void BuildFullMeshCache(Mesh mesh, int subCount)
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

        // The TRUE original asset mesh - never the runtime bake, and never the temp live-preview
        // mesh even while it's swapped onto the renderer, so skin weights/bindposes/blend shapes
        // stay intact and Save/EnableReadWrite always operate on the real asset.
        private Mesh GetSourceAssetMesh()
        {
            if (targetRenderer == null)
                return null;

            if (swappedRenderer == targetRenderer && originalMesh != null)
                return originalMesh;

            if (targetRenderer is SkinnedMeshRenderer skinned)
                return skinned.sharedMesh;
            if (targetRenderer.TryGetComponent(out MeshFilter filter))
                return filter.sharedMesh;
            return null;
        }

        // ---------------------------------------------------------------
        // Live preview mesh swap
        // ---------------------------------------------------------------

        // Swaps the renderer's mesh for a temporary clone so edits show up on the actual mesh (and
        // in the Scene view) in real time. Always restores whatever renderer/mesh it previously
        // swapped first, so at most one renderer is ever affected at a time.
        private void SwapInTempMesh(Renderer renderer, Mesh sourceMesh)
        {
            RestoreSwappedRenderer();

            if (renderer == null || sourceMesh == null)
                return;

            Mesh temp = Instantiate(sourceMesh);
            temp.name = sourceMesh.name + " (UV Editor Preview)";
            temp.hideFlags = HideFlags.HideAndDontSave;

            if (renderer is SkinnedMeshRenderer skinned)
            {
                originalMesh = skinned.sharedMesh;
                skinned.sharedMesh = temp;
            }
            else if (renderer.TryGetComponent(out MeshFilter filter))
            {
                originalMesh = filter.sharedMesh;
                filter.sharedMesh = temp;
            }
            else
            {
                DestroyImmediate(temp);
                return;
            }

            tempPreviewMesh = temp;
            swappedRenderer = renderer;
            ApplyWorkingUVToPreviewMesh();
        }

        // Puts the renderer back exactly how it was before this window touched it.
        private void RestoreSwappedRenderer()
        {
            if (swappedRenderer != null)
            {
                if (swappedRenderer is SkinnedMeshRenderer skinned)
                    skinned.sharedMesh = originalMesh;
                else if (swappedRenderer.TryGetComponent(out MeshFilter filter))
                    filter.sharedMesh = originalMesh;
            }

            if (tempPreviewMesh != null)
                DestroyImmediate(tempPreviewMesh);

            tempPreviewMesh = null;
            originalMesh = null;
            swappedRenderer = null;
        }

        private void ApplyWorkingUVToPreviewMesh()
        {
            if (tempPreviewMesh != null && workingUV != null)
                tempPreviewMesh.uv = workingUV;
        }

        // Baking always yields up-to-date vertex positions for the current pose regardless of the
        // source asset's Read/Write setting, so it's used for the Scene-view marker/raycast only.
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

        private static List<UVEdgeIndices> GetAllEdgesIndices(SubmeshData data)
        {
            var result = new List<UVEdgeIndices>(data.EdgeToTriangles.Count);
            foreach (long key in data.EdgeToTriangles.Keys)
            {
                int lo = (int)(key >> 32);
                int hi = (int)(key & 0xFFFFFFFF);
                result.Add(new UVEdgeIndices { A = lo, B = hi });
            }
            return result;
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
        // Save / reset
        // ---------------------------------------------------------------

        // Reverts only the currently selected points back to the original asset's UVs - everything
        // else stays as edited.
        private void ResetSelection()
        {
            Mesh sourceMesh = GetSourceAssetMesh();
            if (sourceMesh == null || workingUV == null || selectedVerts.Count == 0)
                return;

            Vector2[] originalUV = sourceMesh.uv;
            if (originalUV == null)
                return;

            Undo.RegisterCompleteObjectUndo(this, "Reset UV Selection");

            foreach (int v in selectedVerts)
            {
                if (v < originalUV.Length)
                    workingUV[v] = originalUV[v];
            }

            ApplyWorkingUVToPreviewMesh();
            SceneView.RepaintAll();
            Repaint();
        }

        // Never overwrites the original asset - Instantiate deep-clones the mesh (skin weights,
        // bindposes, blend shapes, all submeshes included), then only the UVs are swapped in.
        private void SaveMeshAs()
        {
            Mesh sourceMesh = GetSourceAssetMesh();
            if (sourceMesh == null || workingUV == null)
                return;

            string sourcePath = AssetDatabase.GetAssetPath(sourceMesh);
            string defaultFolder = !string.IsNullOrEmpty(sourcePath)
                ? Path.GetDirectoryName(sourcePath)
                : "Assets";
            string defaultName = sourceMesh.name + "_UV";

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Mesh As",
                defaultName,
                "asset",
                "Choose where to save the edited mesh",
                defaultFolder
            );
            if (string.IsNullOrEmpty(path))
                return;

            Mesh clone = Instantiate(sourceMesh);
            clone.uv = workingUV;
            clone.name = Path.GetFileNameWithoutExtension(path);

            AssetDatabase.CreateAsset(clone, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(clone);
            Debug.Log($"[UVEditor] Saved edited mesh to {path}");
        }

        // ---------------------------------------------------------------
        // Marker picker (Scene view + texture click -> red dot)
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
                ApplyHit(hit.UV, hit.WorldPosition);
            }

            e.Use();
        }

        private void ApplyHit(Vector2 uv, Vector3 worldPosition)
        {
            hitUV = uv;
            hitWorldPosition = worldPosition;
            hasHit = true;

            Repaint();
            SceneView.RepaintAll();
        }

        private bool TryRaycastRenderer(Renderer renderer, Ray ray, out RaycastMeshHit hit)
        {
            hit = default;

            if (fullMeshCache == null || workingUV == null)
                return false;

            if (!renderer.bounds.IntersectRay(ray))
                return false;

            Mesh mesh = GetCurrentMesh(renderer);
            if (mesh == null)
                return false;

            bool isSkinned = renderer is SkinnedMeshRenderer;
            if (!isSkinned && !mesh.isReadable)
                return false;

            Vector3[] verts = mesh.vertices;
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

                        Vector2 uva = ia < workingUV.Length ? workingUV[ia] : Vector2.zero;
                        Vector2 uvb = ib < workingUV.Length ? workingUV[ib] : Vector2.zero;
                        Vector2 uvc = ic < workingUV.Length ? workingUV[ic] : Vector2.zero;

                        hit.WorldPosition = ray.origin + ray.direction * dist;
                        hit.SubmeshIndex = sm;
                        hit.UV = uva * bary.x + uvb * bary.y + uvc * bary.z;
                    }
                }
            }

            return found;
        }

        // Reverse of TryRaycastRenderer: given a UV point (in the CURRENT, edited layout), find
        // which triangle of the displayed submesh contains it and interpolate the matching
        // world-space position.
        private bool TryFindWorldPositionFromUV(Vector2 uv, out Vector3 worldPosition)
        {
            worldPosition = default;

            if (submeshData == null || workingUV == null || targetRenderer == null)
                return false;

            Mesh mesh = GetCurrentMesh(targetRenderer);
            if (mesh == null)
                return false;

            Vector3[] verts = mesh.vertices;
            Matrix4x4 localToWorld = targetRenderer.transform.localToWorldMatrix;

            for (int t = 0; t < submeshData.TriCount; t++)
            {
                int ia = submeshData.Triangles[t * 3 + 0];
                int ib = submeshData.Triangles[t * 3 + 1];
                int ic = submeshData.Triangles[t * 3 + 2];

                if (TryGetBarycentric2D(uv, workingUV[ia], workingUV[ib], workingUV[ic], out Vector3 bary))
                {
                    Vector3 pa = verts[ia];
                    Vector3 pb = verts[ib];
                    Vector3 pc = verts[ic];
                    Vector3 localPos = pa * bary.x + pb * bary.y + pc * bary.z;

                    worldPosition = localToWorld.MultiplyPoint3x4(localPos);
                    return true;
                }
            }

            return false;
        }

        private static bool TryGetBarycentric2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 baryUVW)
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
