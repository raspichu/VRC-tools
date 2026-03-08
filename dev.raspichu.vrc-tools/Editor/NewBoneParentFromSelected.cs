using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace raspichu.vrc_tools.editor
{
    public static class NewBoneParentFromSelected
    {
        private const string ToolsMenuPath = "Tools/Pichu/New Bone Parent From Selected";
        private const string ContextMenuPath = "GameObject/Pichu/New Bone Parent From Selected";

        [MenuItem(ToolsMenuPath)]
        private static void ExecuteFromTools()
        {
            ExecuteCore();
        }

        [MenuItem(ContextMenuPath, false, 10)]
        private static void ExecuteFromContext(MenuCommand command)
        {
            if (!ShouldExecuteForContextCommand(command))
            {
                return;
            }

            ExecuteCore();
        }

        [MenuItem(ToolsMenuPath, true)]
        [MenuItem(ContextMenuPath, true)]
        private static bool ValidateExecute()
        {
            return TryValidateSelection(Selection.transforms, out _);
        }

        private static void ExecuteCore()
        {
            Transform[] selected = Selection.transforms;
            if (!TryValidateSelection(selected, out string error))
            {
                EditorUtility.DisplayDialog("New Bone Parent From Selected", error, "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("New Bone Parent From Selected");
            int undoGroup = Undo.GetCurrentGroup();

            Transform boneGroup = CreateBoneGroup(selected[0]);
            List<Transform> selectedRoots = GetSelectionRoots(selected);

            foreach (Transform root in selectedRoots)
            {
                CloneHierarchy(root, boneGroup, true);
            }

            bool copiedPhysBone = CopyFirstPhysBoneAndRemoveAllFromSelection(selected, boneGroup);
            int proxiesAssigned = AssignBoneProxies(selected, boneGroup);

            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeTransform = boneGroup;

            Debug.Log(
                $"[NewBoneParentFromSelected] Created '{boneGroup.name}', clonedRoots={selectedRoots.Count}, " +
                $"proxiesAssigned={proxiesAssigned}, physBoneCopied={copiedPhysBone}."
            );
        }

        private static bool TryValidateSelection(Transform[] selected, out string error)
        {
            if (selected == null || selected.Length == 0)
            {
                error = "Select one or more bones in the scene hierarchy.";
                return false;
            }

            foreach (Transform t in selected)
            {
                if (t == null || EditorUtility.IsPersistent(t))
                {
                    error = "Selection must contain only scene objects.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private static Transform CreateBoneGroup(Transform firstSelected)
        {
            Transform parent = firstSelected.parent;
            GameObject group = new GameObject(GetUniqueNameAtLevel(parent, "Bone_Group"));
            Undo.RegisterCreatedObjectUndo(group, "Create Bone Group");

            Transform t = group.transform;
            if (parent != null)
            {
                t.SetParent(parent, false);
                t.position = parent.position;
                t.rotation = parent.rotation;
                t.SetSiblingIndex(firstSelected.GetSiblingIndex());
            }
            else
            {
                t.position = firstSelected.position;
                t.rotation = firstSelected.rotation;
            }

            t.localScale = Vector3.one;
            return t;
        }

        private static List<Transform> GetSelectionRoots(IEnumerable<Transform> selected)
        {
            HashSet<Transform> selectedSet = new HashSet<Transform>(selected);
            List<Transform> roots = new List<Transform>();

            foreach (Transform t in selectedSet)
            {
                bool hasSelectedAncestor = false;
                Transform current = t.parent;
                while (current != null)
                {
                    if (selectedSet.Contains(current))
                    {
                        hasSelectedAncestor = true;
                        break;
                    }

                    current = current.parent;
                }

                if (!hasSelectedAncestor)
                {
                    roots.Add(t);
                }
            }

            return roots;
        }

        private static void CloneHierarchy(Transform source, Transform parent, bool worldSpace)
        {
            GameObject clone = new GameObject(ToFakeBoneName(source.name));
            Undo.RegisterCreatedObjectUndo(clone, "Create Bone Clone");

            Transform cloneTransform = clone.transform;
            cloneTransform.SetParent(parent, worldSpace);

            if (worldSpace)
            {
                cloneTransform.position = source.position;
                cloneTransform.rotation = source.rotation;
                cloneTransform.localScale = source.lossyScale;
            }
            else
            {
                cloneTransform.localPosition = source.localPosition;
                cloneTransform.localRotation = source.localRotation;
                cloneTransform.localScale = source.localScale;
            }

            clone.tag = "EditorOnly";

            for (int i = 0; i < source.childCount; i++)
            {
                CloneHierarchy(source.GetChild(i), cloneTransform, false);
            }
        }

        private static bool CopyFirstPhysBoneAndRemoveAllFromSelection(IEnumerable<Transform> selected, Transform target)
        {
            VRCPhysBone source = null;

            foreach (Transform t in selected)
            {
                if (t == null)
                {
                    continue;
                }

                VRCPhysBone candidate = t.GetComponent<VRCPhysBone>();
                if (source == null && candidate != null)
                {
                    source = candidate;
                }
            }

            if (source != null)
            {
                VRCPhysBone copy = Undo.AddComponent<VRCPhysBone>(target.gameObject);
                EditorUtility.CopySerialized(source, copy);
                EditorUtility.SetDirty(copy);
            }

            foreach (Transform t in selected)
            {
                if (t == null)
                {
                    continue;
                }

                VRCPhysBone[] physBones = t.GetComponents<VRCPhysBone>();
                foreach (VRCPhysBone physBone in physBones)
                {
                    Undo.DestroyObjectImmediate(physBone);
                }
            }

            return source != null;
        }

        private static int AssignBoneProxies(IEnumerable<Transform> selected, Transform target)
        {
            Type boneProxyType = FindTypeInLoadedAssemblies("nadena.dev.modular_avatar.core.ModularAvatarBoneProxy");
            if (boneProxyType == null)
            {
                return 0;
            }

            int assigned = 0;
            string subPath = GetSubPathFromAvatarRootToTarget(target);
            HashSet<Transform> unique = new HashSet<Transform>(selected);

            foreach (Transform t in unique)
            {
                Component proxy = t.GetComponent(boneProxyType) ?? Undo.AddComponent(t.gameObject, boneProxyType);

                SerializedObject so = new SerializedObject(proxy);
                bool targetOk = SetObjectReference(so, target, "target") |
                                SetObjectReference(so, target.gameObject, "targetObject");
                bool pathOk = SetString(so, subPath, "subPath", "path", "relativePath", "m_SubPath");

                if (targetOk || pathOk)
                {
                    so.ApplyModifiedProperties();
                    EditorUtility.SetDirty(proxy);
                    assigned++;
                }
            }

            return assigned;
        }

        private static Type FindTypeInLoadedAssemblies(string fullTypeName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = asm.GetType(fullTypeName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private static bool SetObjectReference(SerializedObject serialized, UnityEngine.Object value, params string[] names)
        {
            bool changed = false;
            foreach (string name in names)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                {
                    continue;
                }

                if (property.objectReferenceValue != value)
                {
                    property.objectReferenceValue = value;
                    changed = true;
                }
            }

            return changed;
        }

        private static bool SetString(SerializedObject serialized, string value, params string[] names)
        {
            bool changed = false;
            foreach (string name in names)
            {
                SerializedProperty property = serialized.FindProperty(name);
                if (property == null || property.propertyType != SerializedPropertyType.String)
                {
                    continue;
                }

                if (property.stringValue != value)
                {
                    property.stringValue = value;
                    changed = true;
                }
            }

            return changed;
        }

        private static string GetSubPathFromAvatarRootToTarget(Transform target)
        {
            if (target == null)
            {
                return string.Empty;
            }

            Transform avatarRoot = GetAvatarRootTransform(target);
            return AnimationUtility.CalculateTransformPath(target, avatarRoot);
        }

        private static Transform GetAvatarRootTransform(Transform target)
        {
            var avatarDescriptor = CommonEditor.GetVRCAvatarDescriptors(target.gameObject);
            return avatarDescriptor != null ? avatarDescriptor.transform : target.root;
        }

        private static string ToFakeBoneName(string sourceName)
        {
            return sourceName.StartsWith("Fake_", StringComparison.Ordinal) ? sourceName : "Fake_" + sourceName;
        }

        private static string GetUniqueNameAtLevel(Transform parent, string baseName)
        {
            bool Exists(string name)
            {
                if (parent == null)
                {
                    UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        if (root.name == name)
                        {
                            return true;
                        }
                    }

                    return false;
                }

                return parent.Find(name) != null;
            }

            if (!Exists(baseName))
            {
                return baseName;
            }

            int index = 1;
            while (Exists(baseName + "_" + index))
            {
                index++;
            }

            return baseName + "_" + index;
        }

        private static bool ShouldExecuteForContextCommand(MenuCommand command)
        {
            if (command == null || !(command.context is GameObject contextGameObject))
            {
                return true;
            }

            GameObject active = Selection.activeGameObject;
            return active == null || contextGameObject == active;
        }
    }
}
