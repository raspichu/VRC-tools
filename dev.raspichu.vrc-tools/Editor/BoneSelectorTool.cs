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

        private static SkinnedMeshRenderer lastRenderer;

        private static Transform trackedBone;
        private static Transform trackedMirrorBone;
        private static Transform trackedRoot; // avatar root; its local X axis is assumed to be left/right
        private static Quaternion trackedStartRotation; // source bone's rotation (root space) when tracking began
        private static Quaternion trackedMirrorStartRotation; // mirror bone's rotation (root space) at that moment
        private static Quaternion trackedLastRotation; // source bone's rotation (world space), to detect changes
        private static Vector3 trackedStartScale;
        private static Vector3 trackedMirrorStartScale;
        private static Vector3 trackedLastScale;
        private static Vector3 trackedStartPosition; // root space
        private static Vector3 trackedMirrorStartPosition; // root space
        private static Vector3 trackedLastPosition; // world space, to detect changes

        // Raised whenever MasterEnabled/Enabled/MirrorEnabled change, from any source (menu
        // items, the overlay panel, the fallback floating buttons), so every UI surface can
        // stay in sync without polling.
        public static event System.Action StateChanged;

        // Master switch: when off, nothing from this tool runs or shows at all.
        public static bool MasterEnabled
        {
            get => EditorPrefs.GetBool(MasterEnabledKey, false);
            set
            {
                EditorPrefs.SetBool(MasterEnabledKey, value);
                StateChanged?.Invoke();
            }
        }

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, false);
            set
            {
                EditorPrefs.SetBool(EnabledKey, value);
                StateChanged?.Invoke();
            }
        }

        public static bool MirrorEnabled
        {
            get => EditorPrefs.GetBool(MirrorKey, false);
            set
            {
                EditorPrefs.SetBool(MirrorKey, value);
                StateChanged?.Invoke();
            }
        }

        static BoneSelectorTool()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        [MenuItem("Tools/Pichu/Options/Bone Tools")]
        private static void ToggleMasterEnabled()
        {
            MasterEnabled = !MasterEnabled;
            SceneView.RepaintAll();
        }

        [MenuItem("Tools/Pichu/Options/Bone Tools", true)]
        private static bool ToggleMasterEnabledValidate()
        {
            Menu.SetChecked("Tools/Pichu/Options/Bone Tools", MasterEnabled);
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

            SkinnedMeshRenderer renderer = GetTargetRenderer();
            if (renderer == null)
                return;

            Transform[] rendererBones = renderer.bones;
            if (rendererBones == null || rendererBones.Length == 0)
                return;

            // Includes leaf transforms (e.g. "_end" tip markers) that Unity leaves out of
            // SkinnedMeshRenderer.bones because they carry no weight themselves.
            Transform[] bones = GetExtendedBones(renderer, rendererBones);
            var boneSet = new HashSet<Transform>(bones);

            HashSet<Transform> weightedBones = GetWeightedBones(renderer, rendererBones, out bool weightsKnown);

            // Fixed for as long as the same mesh stays selected - clicking between its bones
            // does not narrow the view any further.
            HashSet<Transform> visibleBones = GetVisibleBones(renderer, boneSet, weightedBones, weightsKnown);

            if (MirrorEnabled && trackedMirrorBone != null && boneSet.Contains(trackedMirrorBone))
                AddAncestorChain(trackedMirrorBone, boneSet, visibleBones);

            Event e = Event.current;

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
                    bool isSelected = Selection.activeTransform == bone;
                    bool isMirrorTarget = MirrorEnabled && bone == trackedMirrorBone;
                    bool hasWeight = !weightsKnown || weightedBones.Contains(bone);

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
                return;

            Transform current = Selection.activeTransform;
            if (current == null)
            {
                trackedBone = null;
                trackedMirrorBone = null;
                return;
            }

            if (current != trackedBone)
            {
                trackedBone = current;
                trackedMirrorBone = FindMirroredBone(current);
                trackedRoot = current.root;
                // Rotation is tracked in the avatar root's local space (not the bone's own local
                // space), since the two sides' bones don't necessarily share mirrored local axes.
                trackedStartRotation = WorldToRootSpace(trackedRoot, current.rotation);
                trackedStartScale = current.localScale;
                trackedMirrorStartRotation =
                    trackedMirrorBone != null
                        ? WorldToRootSpace(trackedRoot, trackedMirrorBone.rotation)
                        : Quaternion.identity;
                trackedMirrorStartScale =
                    trackedMirrorBone != null ? trackedMirrorBone.localScale : Vector3.one;
                trackedStartPosition = WorldToRootSpacePosition(trackedRoot, current.position);
                trackedMirrorStartPosition =
                    trackedMirrorBone != null
                        ? WorldToRootSpacePosition(trackedRoot, trackedMirrorBone.position)
                        : Vector3.zero;
                trackedLastRotation = current.rotation;
                trackedLastScale = current.localScale;
                trackedLastPosition = current.position;
                return;
            }

            if (trackedMirrorBone == null)
                return;

            if (current.rotation != trackedLastRotation)
            {
                // Compute the rotation change in the avatar root's space, mirror it across the
                // root's local X axis (assumed to be the character's left/right axis), and apply
                // it on top of the mirror bone's own starting rotation.
                Quaternion currentRootSpace = WorldToRootSpace(trackedRoot, current.rotation);
                Quaternion delta = currentRootSpace * Quaternion.Inverse(trackedStartRotation);
                Quaternion mirroredDelta = MirrorRotationAcrossX(delta);
                Quaternion newMirrorRootSpace = mirroredDelta * trackedMirrorStartRotation;

                Undo.RecordObject(trackedMirrorBone, "Mirror Bone Rotation");
                trackedMirrorBone.rotation = trackedRoot.rotation * newMirrorRootSpace;
                trackedLastRotation = current.rotation;
            }

            if (current.position != trackedLastPosition)
            {
                // Positions are ordinary (polar) vectors, so mirroring them across the root's
                // local X plane is a plain reflection: negate X, keep Y and Z.
                Vector3 currentRootSpace = WorldToRootSpacePosition(trackedRoot, current.position);
                Vector3 delta = currentRootSpace - trackedStartPosition;
                Vector3 mirroredDelta = new Vector3(-delta.x, delta.y, delta.z);
                Vector3 newMirrorRootSpace = trackedMirrorStartPosition + mirroredDelta;

                Undo.RecordObject(trackedMirrorBone, "Mirror Bone Position");
                trackedMirrorBone.position =
                    trackedRoot.position + trackedRoot.rotation * newMirrorRootSpace;
                trackedLastPosition = current.position;
            }

            if (current.localScale != trackedLastScale)
            {
                Vector3 s = current.localScale;
                Vector3 start = trackedStartScale;
                Vector3 scaleDelta = new Vector3(
                    start.x != 0f ? s.x / start.x : 1f,
                    start.y != 0f ? s.y / start.y : 1f,
                    start.z != 0f ? s.z / start.z : 1f
                );

                Undo.RecordObject(trackedMirrorBone, "Mirror Bone Scale");
                trackedMirrorBone.localScale = Vector3.Scale(trackedMirrorStartScale, scaleDelta);
                trackedLastScale = current.localScale;
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

        private static SkinnedMeshRenderer GetTargetRenderer()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null)
                return lastRenderer = null;

            SkinnedMeshRenderer direct = go.GetComponent<SkinnedMeshRenderer>();
            if (direct != null)
                return lastRenderer = direct;

            if (lastRenderer != null && lastRenderer.bones != null)
            {
                // Check the extended list too, since leaf "tip" bones (e.g. "_end" markers) are
                // not part of SkinnedMeshRenderer.bones itself but are still shown/selectable.
                Transform[] extended = GetExtendedBones(lastRenderer, lastRenderer.bones);
                if (System.Array.IndexOf(extended, go.transform) >= 0)
                    return lastRenderer;
            }

            return lastRenderer = null;
        }

        // Extends the renderer's own bone list with leaf transforms Unity leaves out because
        // they carry no weight (e.g. "_end" tip markers) - so chain tips can still be shown.
        // Cached per renderer since it depends only on the mesh, not on any selection.
        private static SkinnedMeshRenderer extendedBonesRenderer;
        private static Transform[] extendedBonesCache;

        private static Transform[] GetExtendedBones(SkinnedMeshRenderer renderer, Transform[] rendererBones)
        {
            if (extendedBonesRenderer == renderer && extendedBonesCache != null)
                return extendedBonesCache;

            extendedBonesRenderer = renderer;

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

            extendedBonesCache = result.ToArray();
            return extendedBonesCache;
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
        // bone hanging off the end of an otherwise-relevant chain.
        private static SkinnedMeshRenderer visibleBonesRenderer;
        private static HashSet<Transform> visibleBonesCache;

        private static HashSet<Transform> GetVisibleBones(
            SkinnedMeshRenderer renderer,
            HashSet<Transform> boneSet,
            HashSet<Transform> weightedBones,
            bool weightsKnown
        )
        {
            if (visibleBonesRenderer == renderer && visibleBonesCache != null)
                return visibleBonesCache;

            visibleBonesRenderer = renderer;

            if (!weightsKnown)
            {
                // Can't tell what's relevant without weight data - show everything rather than
                // risk hiding bones the user actually needs.
                visibleBonesCache = boneSet;
                return visibleBonesCache;
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

            visibleBonesCache = visible;
            return visibleBonesCache;
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

        // ---------------------------------------------------------------
        // Bone weights (grays out purely structural/parent bones with no vertex weight)
        // ---------------------------------------------------------------

        private static SkinnedMeshRenderer weightedBonesRenderer;
        private static HashSet<Transform> weightedBonesCache;
        private static bool weightedBonesKnown;

        private static HashSet<Transform> GetWeightedBones(
            SkinnedMeshRenderer renderer,
            Transform[] bones,
            out bool weightsKnown
        )
        {
            if (weightedBonesRenderer == renderer && weightedBonesCache != null)
            {
                weightsKnown = weightedBonesKnown;
                return weightedBonesCache;
            }

            weightedBonesRenderer = renderer;
            weightedBonesCache = new HashSet<Transform>();
            weightedBonesKnown = false;

            Mesh mesh = renderer.sharedMesh;
            if (mesh != null && mesh.isReadable)
            {
                BoneWeight[] boneWeights = mesh.boneWeights;
                if (boneWeights != null && boneWeights.Length > 0)
                {
                    weightedBonesKnown = true;
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
                            weightedBonesCache.Add(bones[i]);
                    }
                }
            }

            weightsKnown = weightedBonesKnown;
            return weightedBonesCache;
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
