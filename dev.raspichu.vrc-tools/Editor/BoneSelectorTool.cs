using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Overlays;
using UnityEngine.UIElements;
#endif

namespace raspichu.vrc_tools.editor
{
    // Runs alongside whatever transform tool (Move/Rotate/Scale/etc.) is active,
    // unlike EditorTool which is exclusive and gets replaced when switching tools.
    [InitializeOnLoad]
    public static class BoneSelectorTool
    {
        private const string MasterEnabledKey = "Pichu_BoneToolsEnabled";
        private const string EnabledKey = "Pichu_BonePickerEnabled";
        private const string MirrorKey = "Pichu_BonePickerMirror";
        private const float HoverPixelThreshold = 10f;
        private const float JointSizeFactor = 0.1f;
#if UNITY_2021_2_OR_NEWER
        public const string OverlayId = "pichu-bone-tools-overlay";
#endif

        // Left/right name markers checked (in order) when looking for a mirrored bone.
        private static readonly (string left, string right)[] MirrorPatterns =
        {
            ("_L", "_R"),
            ("_l", "_r"),
            (".L", ".R"),
            (".l", ".r"),
            ("-L", "-R"),
            ("-l", "-r"),
            ("Left", "Right"),
            ("left", "right"),
            ("LEFT", "RIGHT"),
        };

        // Sticky: only updated when the selection includes at least one SkinnedMeshRenderer.
        // Selecting anything else (a bone, the Armature root, empty space, ...) leaves this
        // untouched, so the bone overlay only changes when you actually pick a different mesh.
        private static List<SkinnedMeshRenderer> lastRenderers = new List<SkinnedMeshRenderer>();

        // Per-bone live-mirror tracking state, so every selected bone (not just the single
        // "active" one) gets its own mirror counterpart driven while it's selected.
        private class MirrorTrackState
        {
            public Transform mirrorBone;
            public Transform root; // avatar root; its local X axis is assumed to be left/right
            public Quaternion startRotation; // source bone's rotation (root space) when tracking began
            public Quaternion mirrorStartRotation; // mirror bone's rotation (root space) at that moment
            public Quaternion lastRotation; // source bone's rotation (world space), to detect changes
            public Vector3 startScale;
            public Vector3 mirrorStartScale;
            public Vector3 lastScale;
            public Vector3 startPosition; // root space
            public Vector3 mirrorStartPosition; // root space
            public Vector3 lastPosition; // world space, to detect changes
            public bool wasChanging; // was the bone still moving as of last frame?
        }

        private static readonly Dictionary<Transform, MirrorTrackState> trackedMirrorStates = new();

        // Raised whenever MasterEnabled/Enabled/MirrorEnabled change, from any source (menu
        // items, the overlay panel, the fallback floating buttons), so every UI surface can
        // stay in sync without polling.
        public static event System.Action StateChanged;

        // Master switch: when off, nothing from this tool runs or shows at all.
        // Uses EditorUserSettings (per-project) rather than EditorPrefs (shared machine-wide
        // across every Unity project) so enabling this here doesn't leak into other projects.
        public static bool MasterEnabled
        {
            get => EditorUserSettings.GetConfigValue(MasterEnabledKey) == "1";
            set
            {
                EditorUserSettings.SetConfigValue(MasterEnabledKey, value ? "1" : "0");
                StateChanged?.Invoke();
            }
        }

        public static bool Enabled
        {
            get => EditorUserSettings.GetConfigValue(EnabledKey) == "1";
            set
            {
                EditorUserSettings.SetConfigValue(EnabledKey, value ? "1" : "0");
                StateChanged?.Invoke();
            }
        }

        public static bool MirrorEnabled
        {
            get => EditorUserSettings.GetConfigValue(MirrorKey) == "1";
            set
            {
                EditorUserSettings.SetConfigValue(MirrorKey, value ? "1" : "0");
                StateChanged?.Invoke();
            }
        }

        static BoneSelectorTool()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        [MenuItem("Tools/Pichu/Options/Enable Bone Tools")]
        private static void ToggleMasterEnabled()
        {
            MasterEnabled = !MasterEnabled;
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/Pichu/Options/Enable Bone Tools", true)]
        private static bool ToggleMasterEnabledValidate()
        {
            Menu.SetChecked("Tools/Pichu/Options/Enable Bone Tools", MasterEnabled);
            return true;
        }

        [MenuItem("Tools/Pichu/Bone Tools/Bone Picker")]
        private static void ToggleEnabled()
        {
            Enabled = !Enabled;
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/Pichu/Bone Tools/Bone Picker", true)]
        private static bool ToggleEnabledValidate()
        {
            Menu.SetChecked("Tools/Pichu/Bone Tools/Bone Picker", Enabled);
            return true;
        }

        [MenuItem("Tools/Pichu/Bone Tools/Mirror Bone")]
        private static void ToggleMirror()
        {
            MirrorEnabled = !MirrorEnabled;
        }

        [MenuItem("Tools/Pichu/Bone Tools/Mirror Bone", true)]
        private static bool ToggleMirrorValidate()
        {
            Menu.SetChecked("Tools/Pichu/Bone Tools/Mirror Bone", MirrorEnabled);
            return true;
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
#if UNITY_2021_2_OR_NEWER
            // Keep the overlay's visibility in sync with the master switch, in case it was
            // toggled from the menu while the overlay is already on screen.
            if (sceneView.TryGetOverlay(OverlayId, out Overlay overlay))
                overlay.displayed = MasterEnabled;
#endif

            if (!MasterEnabled)
                return;

#if !UNITY_2021_2_OR_NEWER
            DrawToggleButtons(sceneView);
#endif

            HandleMirror();

            if (!Enabled)
                return;

            List<SkinnedMeshRenderer> renderers = GetTargetRenderers();
            if (renderers == null || renderers.Count == 0)
                return;

            // Aggregated across every selected mesh, so multiple meshes can be inspected at once.
            var boneSet = new HashSet<Transform>();
            var weightedBones = new HashSet<Transform>();
            var visibleBones = new HashSet<Transform>();
            // Bones belonging to a renderer whose weights couldn't be read - treated like they
            // have weight (not grayed out) since we simply don't know either way.
            var unknownWeightBones = new HashSet<Transform>();

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                if (renderer == null)
                    continue;

                Transform[] rendererBones = renderer.bones;
                if (rendererBones == null || rendererBones.Length == 0)
                    continue;

                // Includes leaf transforms (e.g. "_end" tip markers) that Unity leaves out of
                // SkinnedMeshRenderer.bones because they carry no weight themselves.
                Transform[] extendedBones = GetExtendedBones(renderer, rendererBones);
                var rendererBoneSet = new HashSet<Transform>(extendedBones);

                HashSet<Transform> rendererWeightedBones = GetWeightedBones(
                    renderer,
                    rendererBones,
                    out bool weightsKnown
                );

                // Fixed for as long as the same mesh stays selected - clicking between its bones
                // does not narrow the view any further.
                HashSet<Transform> rendererVisibleBones = GetVisibleBones(
                    renderer,
                    rendererBoneSet,
                    rendererWeightedBones,
                    weightsKnown
                );

                boneSet.UnionWith(rendererBoneSet);
                weightedBones.UnionWith(rendererWeightedBones);
                visibleBones.UnionWith(rendererVisibleBones);

                if (!weightsKnown)
                    unknownWeightBones.UnionWith(rendererBoneSet);
            }

            if (boneSet.Count == 0)
                return;

            // Mirror counterparts for every currently selected bone (not just the single one
            // being live-mirrored), so multi-select shows all of them highlighted too.
            HashSet<Transform> mirrorHighlightBones = MirrorEnabled ? GetMirrorHighlightBones(boneSet) : null;
            if (mirrorHighlightBones != null)
            {
                foreach (Transform mirrorBone in mirrorHighlightBones)
                    AddAncestorChain(mirrorBone, boneSet, visibleBones);
            }

            Transform[] bones = new Transform[boneSet.Count];
            boneSet.CopyTo(bones);

            Event e = Event.current;

            // Ctrl/Cmd+click is handled as a direct hit-test, bypassing the cooperative
            // nearestControl arbitration below entirely - Unity reserves Ctrl for the active
            // gizmo (e.g. snapping while dragging), which was swallowing the modifier before it
            // ever reached our per-bone controls, so a plain click-to-select-multiple never fired.
            if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && (e.control || e.command))
            {
                Transform closest = null;
                float closestDist = HoverPixelThreshold;
                foreach (Transform bone in bones)
                {
                    if (bone == null || bone.parent == null || !boneSet.Contains(bone.parent))
                        continue;
                    if (!visibleBones.Contains(bone))
                        continue;

                    float dist = HandleUtility.DistanceToLine(bone.parent.position, bone.position);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = bone;
                    }
                }

                if (closest != null)
                {
                    var selected = new List<Object>(Selection.objects);
                    if (selected.Contains(closest.gameObject))
                        selected.Remove(closest.gameObject);
                    else
                        selected.Add(closest.gameObject);
                    Selection.objects = selected.ToArray();
                    e.Use();
                    return;
                }
            }

            // Registering each bone as a proper Handles "control" (instead of unconditionally
            // consuming MouseDown ourselves) lets Unity's own nearest-control system decide who
            // wins when a transform gizmo handle overlaps a bone - otherwise we'd always steal
            // the click even when the user meant to grab the rotate/move/scale gizmo.
            foreach (Transform bone in bones)
            {
                if (bone == null || bone.parent == null || !boneSet.Contains(bone.parent))
                    continue;
                if (!visibleBones.Contains(bone))
                    continue;

                int controlId = GUIUtility.GetControlID(FocusType.Passive);
                Vector3 p1 = bone.parent.position;
                Vector3 p2 = bone.position;
                float dist = HandleUtility.DistanceToLine(p1, p2);

                if (e.type == EventType.Layout)
                {
                    HandleUtility.AddControl(controlId, dist);
                    continue;
                }

                bool isNearestAndClose = dist < HoverPixelThreshold && HandleUtility.nearestControl == controlId;

                if (e.type == EventType.Repaint)
                {
                    // Highlights every selected bone, not just the "active" one, so multi-select
                    // (Ctrl+click) shows all of them as selected.
                    bool isSelected = System.Array.IndexOf(Selection.gameObjects, bone.gameObject) >= 0;
                    bool isMirrorTarget = mirrorHighlightBones != null && mirrorHighlightBones.Contains(bone);
                    bool hasWeight = unknownWeightBones.Contains(bone) || weightedBones.Contains(bone);

                    Handles.color = isSelected
                        ? Color.yellow
                        : isMirrorTarget
                            ? new Color(1f, 0.5f, 0.1f, 0.95f)
                            : isNearestAndClose
                                ? Color.white
                                : hasWeight
                                    ? new Color(0.2f, 0.9f, 1f, 0.9f)
                                    : new Color(0.55f, 0.55f, 0.55f, 0.7f);

                    Handles.DrawLine(p1, p2);

                    float jointSize = HandleUtility.GetHandleSize(p2) * JointSizeFactor;
                    Handles.SphereHandleCap(0, p2, Quaternion.identity, jointSize, EventType.Repaint);
                }
                else if (e.type == EventType.MouseDown && e.button == 0 && !e.alt && isNearestAndClose)
                {
                    // Ctrl/Cmd+click is already handled (and consumed) by the bypass above -
                    // this only ever runs for a plain click now.
                    GUIUtility.hotControl = controlId;
                    Selection.activeGameObject = bone.gameObject;
                    e.Use();
                }
            }

            if (e.type == EventType.MouseMove)
                sceneView.Repaint();
        }

        // ---------------------------------------------------------------
        // Mirror
        // ---------------------------------------------------------------

        private static void HandleMirror()
        {
            if (!MirrorEnabled)
            {
                trackedMirrorStates.Clear();
                return;
            }

            Transform[] selected = Selection.transforms;
            var selectedSet = new HashSet<Transform>(selected);

            // Drop tracking for anything that's no longer selected.
            if (trackedMirrorStates.Count > 0)
            {
                var toRemove = new List<Transform>();
                foreach (Transform tracked in trackedMirrorStates.Keys)
                {
                    if (!selectedSet.Contains(tracked))
                        toRemove.Add(tracked);
                }
                foreach (Transform tracked in toRemove)
                    trackedMirrorStates.Remove(tracked);
            }

            // Start tracking any newly selected bone from its current pose, so only pose changes
            // made WHILE selected get mirrored (not the pre-existing pose itself).
            foreach (Transform current in selected)
            {
                if (trackedMirrorStates.ContainsKey(current))
                    continue;

                var state = new MirrorTrackState();
                RebaselineMirrorTrackState(current, state, FindMirroredBone(current));
                trackedMirrorStates[current] = state;
            }

            // Apply mirrored deltas for every tracked bone whose pose changed since last frame.
            foreach (KeyValuePair<Transform, MirrorTrackState> entry in trackedMirrorStates)
            {
                Transform current = entry.Key;
                MirrorTrackState state = entry.Value;
                if (current == null)
                    continue;

                bool isChanging =
                    current.rotation != state.lastRotation
                    || current.position != state.lastPosition
                    || current.localScale != state.lastScale;

                // A bone kept selected through a rename (of itself or its counterpart) can change
                // which mirror target its name resolves to without ever being deselected, so the
                // cached mirrorBone would otherwise go stale until a fresh reselect re-ran
                // FindMirroredBone. Re-resolve only once, right as a move starts (the bone was
                // stationary last frame and just began changing) - not on every idle tick, and not
                // on every single frame of an ongoing drag.
                if (isChanging && !state.wasChanging)
                {
                    Transform freshMirrorBone = FindMirroredBone(current);
                    if (freshMirrorBone != state.mirrorBone)
                    {
                        RebaselineMirrorTrackState(current, state, freshMirrorBone);
                        state.wasChanging = true;
                        continue; // re-baselined this frame; mirror starting next frame
                    }
                }
                state.wasChanging = isChanging;

                if (state.mirrorBone == null)
                    continue;

                if (current.rotation != state.lastRotation)
                {
                    // Compute the rotation change in the avatar root's space, mirror it across
                    // the root's local X axis (assumed to be the character's left/right axis),
                    // and apply it on top of the mirror bone's own starting rotation.
                    Quaternion currentRootSpace = WorldToRootSpace(state.root, current.rotation);
                    Quaternion delta = currentRootSpace * Quaternion.Inverse(state.startRotation);
                    Quaternion mirroredDelta = MirrorRotationAcrossX(delta);
                    Quaternion newMirrorRootSpace = mirroredDelta * state.mirrorStartRotation;

                    Undo.RecordObject(state.mirrorBone, "Mirror Bone Rotation");
                    state.mirrorBone.rotation = state.root.rotation * newMirrorRootSpace;
                    state.lastRotation = current.rotation;
                    SyncMirrorPartnerLastValues(state.mirrorBone);
                }

                if (current.position != state.lastPosition)
                {
                    // Positions are ordinary (polar) vectors, so mirroring them across the root's
                    // local X plane is a plain reflection: negate X, keep Y and Z.
                    Vector3 currentRootSpace = WorldToRootSpacePosition(state.root, current.position);
                    Vector3 delta = currentRootSpace - state.startPosition;
                    Vector3 mirroredDelta = new Vector3(-delta.x, delta.y, delta.z);
                    Vector3 newMirrorRootSpace = state.mirrorStartPosition + mirroredDelta;

                    Undo.RecordObject(state.mirrorBone, "Mirror Bone Position");
                    state.mirrorBone.position =
                        state.root.position + state.root.rotation * newMirrorRootSpace;
                    state.lastPosition = current.position;
                    SyncMirrorPartnerLastValues(state.mirrorBone);
                }

                if (current.localScale != state.lastScale)
                {
                    Vector3 s = current.localScale;
                    Vector3 start = state.startScale;
                    Vector3 scaleDelta = new Vector3(
                        start.x != 0f ? s.x / start.x : 1f,
                        start.y != 0f ? s.y / start.y : 1f,
                        start.z != 0f ? s.z / start.z : 1f
                    );

                    Undo.RecordObject(state.mirrorBone, "Mirror Bone Scale");
                    state.mirrorBone.localScale = Vector3.Scale(state.mirrorStartScale, scaleDelta);
                    state.lastScale = current.localScale;
                    SyncMirrorPartnerLastValues(state.mirrorBone);
                }
            }
        }

        // Re-points a tracked bone's mirror target and re-baselines all the start/last pose
        // values around the CURRENT pose of both bones - the same thing that happens when a bone
        // is first selected, just triggered by the mirror target changing underneath an already-
        // tracked bone instead of by a fresh selection.
        private static void RebaselineMirrorTrackState(
            Transform current,
            MirrorTrackState state,
            Transform newMirrorBone
        )
        {
            state.mirrorBone = newMirrorBone;
            state.root = current.root;
            state.startRotation = WorldToRootSpace(state.root, current.rotation);
            state.startScale = current.localScale;
            state.mirrorStartRotation =
                newMirrorBone != null
                    ? WorldToRootSpace(state.root, newMirrorBone.rotation)
                    : Quaternion.identity;
            state.mirrorStartScale = newMirrorBone != null ? newMirrorBone.localScale : Vector3.one;
            state.startPosition = WorldToRootSpacePosition(state.root, current.position);
            state.mirrorStartPosition =
                newMirrorBone != null
                    ? WorldToRootSpacePosition(state.root, newMirrorBone.position)
                    : Vector3.zero;
            state.lastRotation = current.rotation;
            state.lastScale = current.localScale;
            state.lastPosition = current.position;
        }

        // If the bone we just wrote a mirrored pose into is ALSO independently selected/tracked
        // (e.g. both sides of a symmetric pair are selected at once), stamp its own "last" values
        // so it isn't mistaken for a fresh user-driven edit and mirrored right back, which would
        // fight the write that just happened.
        private static void SyncMirrorPartnerLastValues(Transform mirrorBone)
        {
            if (trackedMirrorStates.TryGetValue(mirrorBone, out MirrorTrackState partnerState))
            {
                partnerState.lastRotation = mirrorBone.rotation;
                partnerState.lastPosition = mirrorBone.position;
                partnerState.lastScale = mirrorBone.localScale;
            }
        }

        // Expresses a world rotation as if the root itself had no rotation of its own - i.e. the
        // rotation relative to the root's own axes, regardless of the bone's own local axes.
        private static Quaternion WorldToRootSpace(Transform root, Quaternion worldRotation)
        {
            return Quaternion.Inverse(root.rotation) * worldRotation;
        }

        // Expresses a world position as a vector relative to the root's own position and axes.
        private static Vector3 WorldToRootSpacePosition(Transform root, Vector3 worldPosition)
        {
            return Quaternion.Inverse(root.rotation) * (worldPosition - root.position);
        }

        // Reflects a rotation across the root's local X=0 plane. A rotation's axis is an axial
        // vector, which under reflection picks up an extra sign flip compared to a normal vector -
        // the net effect is that the axis's X stays, and Y/Z negate (angle unchanged). In quaternion
        // terms that's simply negating the y and z components.
        private static Quaternion MirrorRotationAcrossX(Quaternion q)
        {
            return new Quaternion(q.x, -q.y, -q.z, q.w);
        }

        private static Transform FindMirroredBone(Transform bone)
        {
            Transform root = bone.root;
            if (root == bone)
                return null;

            var segments = new List<string>();
            Transform cur = bone;
            while (cur != null && cur != root)
            {
                segments.Insert(0, MirrorName(cur.name));
                cur = cur.parent;
            }

            string mirroredPath = string.Join("/", segments);
            Transform found = root.Find(mirroredPath);
            return found != bone ? found : null;
        }

        private static string MirrorName(string name)
        {
            foreach (var (left, right) in MirrorPatterns)
            {
                int li = name.IndexOf(left, System.StringComparison.Ordinal);
                if (li >= 0)
                    return name.Substring(0, li) + right + name.Substring(li + left.Length);

                int ri = name.IndexOf(right, System.StringComparison.Ordinal);
                if (ri >= 0)
                    return name.Substring(0, ri) + left + name.Substring(ri + right.Length);
            }

            // Fallback: a bare trailing "L"/"R" with no separator (e.g. "handL"/"handR"), as long
            // as the character right before it isn't itself uppercase - that guards against
            // all-caps names/abbreviations ending in L or R (e.g. "HAIR", "FLOOR") being treated
            // as mirror suffixes.
            if (name.Length > 0)
            {
                char lastChar = name[name.Length - 1];
                if (
                    (lastChar == 'L' || lastChar == 'R')
                    && (name.Length == 1 || !char.IsUpper(name[name.Length - 2]))
                )
                {
                    char mirroredChar = lastChar == 'L' ? 'R' : 'L';
                    return name.Substring(0, name.Length - 1) + mirroredChar;
                }
            }

            return name;
        }

        // ---------------------------------------------------------------

#if !UNITY_2021_2_OR_NEWER
        // Named "Pichu Tools" (not "Bone Picker") since more sections may be added here later,
        // each under its own subtitle label.
        private static void DrawToggleButtons(SceneView sceneView)
        {
            Handles.BeginGUI();

            Rect titleRect = new Rect(sceneView.position.width - 116, 4, 108, 16);
            GUI.Label(titleRect, "Pichu Tools", EditorStyles.boldLabel);

            Rect subtitleRect = new Rect(sceneView.position.width - 116, 20, 108, 14);
            GUI.Label(subtitleRect, "Bone Tools", EditorStyles.miniLabel);

            Rect rect = new Rect(sceneView.position.width - 116, 36, 108, 22);
            GUIContent content = new GUIContent(
                " Bone Picker",
                EditorGUIUtility.IconContent("d_SkinnedMeshRenderer Icon").image
            );
            bool newEnabled = GUI.Toggle(rect, Enabled, content, EditorStyles.miniButton);
            if (newEnabled != Enabled)
                Enabled = newEnabled;

            Rect mirrorRect = new Rect(sceneView.position.width - 116, 60, 108, 20);
            bool newMirror = GUI.Toggle(mirrorRect, MirrorEnabled, " Mirror Bone", EditorStyles.miniButton);
            if (newMirror != MirrorEnabled)
                MirrorEnabled = newMirror;

            Handles.EndGUI();
        }
#endif

        // Only updates lastRenderers when the current selection actually contains at least one
        // SkinnedMeshRenderer (supports multi-selecting several meshes at once). Selecting a bone,
        // the Armature root, empty space, or anything else without one of those leaves the
        // previously shown mesh set completely untouched.
        private static List<SkinnedMeshRenderer> GetTargetRenderers()
        {
            GameObject[] selected = Selection.gameObjects;
            List<SkinnedMeshRenderer> meshesInSelection = null;

            foreach (GameObject go in selected)
            {
                SkinnedMeshRenderer smr = go.GetComponent<SkinnedMeshRenderer>();
                if (smr == null)
                    continue;

                meshesInSelection ??= new List<SkinnedMeshRenderer>();
                meshesInSelection.Add(smr);
            }

            if (meshesInSelection != null)
                lastRenderers = meshesInSelection;

            return lastRenderers;
        }

        // Extends the renderer's own bone list with leaf transforms Unity leaves out because
        // they carry no weight (e.g. "_end" tip markers) - so chain tips can still be shown.
        // Cached per renderer (keyed by renderer, not a single slot) since several renderers can
        // be processed within the same frame when multiple meshes are selected at once.
        private static readonly Dictionary<SkinnedMeshRenderer, Transform[]> extendedBonesCache = new();

        private static Transform[] GetExtendedBones(SkinnedMeshRenderer renderer, Transform[] rendererBones)
        {
            if (extendedBonesCache.TryGetValue(renderer, out Transform[] cached))
                return cached;

            var boneSet = new HashSet<Transform>(rendererBones);
            var result = new List<Transform>(rendererBones);

            foreach (Transform bone in rendererBones)
            {
                if (bone == null)
                    continue;

                for (int i = 0; i < bone.childCount; i++)
                {
                    Transform child = bone.GetChild(i);
                    if (boneSet.Contains(child) || child.childCount > 0 || HasNonTransformComponent(child))
                        continue;

                    result.Add(child);
                    boneSet.Add(child);
                }
            }

            Transform[] extended = result.ToArray();
            extendedBonesCache[renderer] = extended;
            return extended;
        }

        private static bool HasNonTransformComponent(Transform t)
        {
            Component[] components = t.GetComponents<Component>();
            foreach (Component c in components)
            {
                if (!(c is Transform))
                    return true;
            }
            return false;
        }

        // The visible set is fixed for as long as the same mesh is selected: every weighted
        // bone, its full ancestor chain (grayed out if unweighted), and any unweighted "tip"
        // bone hanging off the end of an otherwise-relevant chain. Cached per renderer since
        // several renderers can be processed within the same frame with multi-selection.
        private static readonly Dictionary<SkinnedMeshRenderer, HashSet<Transform>> visibleBonesCache = new();

        private static HashSet<Transform> GetVisibleBones(
            SkinnedMeshRenderer renderer,
            HashSet<Transform> boneSet,
            HashSet<Transform> weightedBones,
            bool weightsKnown
        )
        {
            if (visibleBonesCache.TryGetValue(renderer, out HashSet<Transform> cached))
                return cached;

            if (!weightsKnown)
            {
                // Can't tell what's relevant without weight data - show everything rather than
                // risk hiding bones the user actually needs.
                visibleBonesCache[renderer] = boneSet;
                return boneSet;
            }

            var visible = new HashSet<Transform>();

            foreach (Transform bone in weightedBones)
                AddAncestorChain(bone, boneSet, visible);

            foreach (Transform bone in boneSet)
            {
                if (bone == null || weightedBones.Contains(bone))
                    continue;
                if (bone.parent == null || !visible.Contains(bone.parent))
                    continue;
                if (IsLeaf(bone, boneSet))
                    visible.Add(bone);
            }

            visibleBonesCache[renderer] = visible;
            return visible;
        }

        private static bool IsLeaf(Transform bone, HashSet<Transform> boneSet)
        {
            for (int i = 0; i < bone.childCount; i++)
            {
                if (boneSet.Contains(bone.GetChild(i)))
                    return false;
            }
            return true;
        }

        private static void AddAncestorChain(Transform bone, HashSet<Transform> boneSet, HashSet<Transform> visible)
        {
            Transform cur = bone;
            while (cur != null && boneSet.Contains(cur))
            {
                visible.Add(cur);
                cur = cur.parent;
            }
        }

        // Mirror counterparts for every currently selected bone, so the orange mirror highlight
        // shows up for all of them when multiple bones are selected at once (not just the single
        // bone HandleMirror() is actively live-mirroring).
        private static HashSet<Transform> GetMirrorHighlightBones(HashSet<Transform> boneSet)
        {
            var result = new HashSet<Transform>();
            foreach (GameObject go in Selection.gameObjects)
            {
                Transform selected = go.transform;
                if (!boneSet.Contains(selected))
                    continue;
                Transform mirrored = FindMirroredBone(selected);
                if (mirrored != null && boneSet.Contains(mirrored))
                    result.Add(mirrored);
            }
            return result;
        }

        // ---------------------------------------------------------------
        // Bone weights (grays out purely structural/parent bones with no vertex weight)
        // ---------------------------------------------------------------

        private static readonly Dictionary<SkinnedMeshRenderer, HashSet<Transform>> weightedBonesCache = new();
        private static readonly Dictionary<SkinnedMeshRenderer, bool> weightedBonesKnownCache = new();

        private static HashSet<Transform> GetWeightedBones(
            SkinnedMeshRenderer renderer,
            Transform[] bones,
            out bool weightsKnown
        )
        {
            if (weightedBonesCache.TryGetValue(renderer, out HashSet<Transform> cached))
            {
                weightsKnown = weightedBonesKnownCache[renderer];
                return cached;
            }

            var weighted = new HashSet<Transform>();
            bool known = false;

            Mesh mesh = renderer.sharedMesh;
            if (mesh != null && mesh.isReadable)
            {
                BoneWeight[] boneWeights = mesh.boneWeights;
                if (boneWeights != null && boneWeights.Length > 0)
                {
                    known = true;
                    var weightedIndices = new HashSet<int>();
                    foreach (BoneWeight bw in boneWeights)
                    {
                        if (bw.weight0 > 0f)
                            weightedIndices.Add(bw.boneIndex0);
                        if (bw.weight1 > 0f)
                            weightedIndices.Add(bw.boneIndex1);
                        if (bw.weight2 > 0f)
                            weightedIndices.Add(bw.boneIndex2);
                        if (bw.weight3 > 0f)
                            weightedIndices.Add(bw.boneIndex3);
                    }

                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (bones[i] != null && weightedIndices.Contains(i))
                            weighted.Add(bones[i]);
                    }
                }
            }

            weightedBonesCache[renderer] = weighted;
            weightedBonesKnownCache[renderer] = known;
            weightsKnown = known;
            return weighted;
        }
    }

#if UNITY_2021_2_OR_NEWER
    // Named "Pichu Tools" (not "Bone Picker") since more sections may be added here later,
    // each under its own subtitle label.
    [Overlay(typeof(SceneView), BoneSelectorTool.OverlayId, "Pichu Tools", true)]
    public class BonePickerOverlay : Overlay
    {
        public override VisualElement CreatePanelContent()
        {
            var root = new VisualElement();

            var boneSectionLabel = new Label("Bone Tools")
            {
                style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 2, marginBottom = 2 },
            };
            root.Add(boneSectionLabel);

            var enableToggle = new Toggle("Bone Picker") { value = BoneSelectorTool.Enabled };
            enableToggle.RegisterValueChangedCallback(evt =>
            {
                BoneSelectorTool.Enabled = evt.newValue;
                SceneView.RepaintAll();
            });
            root.Add(enableToggle);

            var mirrorToggle = new Toggle("Mirror Bone") { value = BoneSelectorTool.MirrorEnabled };
            mirrorToggle.RegisterValueChangedCallback(evt => BoneSelectorTool.MirrorEnabled = evt.newValue);
            root.Add(mirrorToggle);

            // Keep in sync with changes made elsewhere (e.g. the Tools/Pichu menu items),
            // triggered only when something actually changes, not on a timer.
            void RefreshToggles()
            {
                enableToggle.SetValueWithoutNotify(BoneSelectorTool.Enabled);
                mirrorToggle.SetValueWithoutNotify(BoneSelectorTool.MirrorEnabled);
            }

            BoneSelectorTool.StateChanged += RefreshToggles;
            root.RegisterCallback<DetachFromPanelEvent>(_ => BoneSelectorTool.StateChanged -= RefreshToggles);

            return root;
        }
    }
#endif
}
