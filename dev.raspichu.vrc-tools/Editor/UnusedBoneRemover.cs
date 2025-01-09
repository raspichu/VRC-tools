using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Dynamics.PhysBone.Components;

namespace raspichu.vrc_tools.editor
{
    public class UnusedBoneRemover : Editor
    {
        [MenuItem("Tools/Unused Bone Remover")]
        public static void RemoveUnusedBonesMenu()
        {
            RemoveSelectedUnusedBones();
        }

        [MenuItem("GameObject/Unused Bone Remover", false, 0)]
        public static void RemoveUnusedBonesHierarchy()
        {
            RemoveSelectedUnusedBones();
        }

        /// <summary>
        /// Removes unused bones from the selected object.
        /// </summary>
        private static void RemoveSelectedUnusedBones()
        {
            GameObject selectedObject = Selection.activeGameObject;

            if (selectedObject == null)
            {
                Debug.Log("No object selected.");
                return;
            }

            // Duplicate the object, keeping prefab in case it is one
            EditorWindow.focusedWindow.SendEvent(EditorGUIUtility.CommandEvent("Duplicate"));

            // Get the newly duplicated object (it should be selected automatically)
            GameObject newObjectCopy = Selection.activeGameObject;

            // Hide the copy
            newObjectCopy.SetActive(false);

            // Rename the duplicate to indicate it is the original
            newObjectCopy.name = selectedObject.name + "_original";

            // Register undo for the creation of the duplicate object
            Undo.RegisterCreatedObjectUndo(newObjectCopy, "Create Cleaned Object");

            // Get the sibling index of the selected object
            int siblingIndex = selectedObject.transform.GetSiblingIndex();

            // Explicitly record hierarchy changes
            Transform parentTransform = selectedObject.transform.parent;
            Undo.RegisterCompleteObjectUndo(parentTransform, "Adjust Sibling Index");

            // Set sibling indices to ensure proper order
            newObjectCopy.transform.SetSiblingIndex(siblingIndex);
            selectedObject.transform.SetSiblingIndex(siblingIndex + 1);

            // Save Undo for changes to the selected object
            Undo.RecordObject(selectedObject, "Remove Unused Bones");

            // Show the cleaned object and rename it
            selectedObject.SetActive(true);
            selectedObject.name = selectedObject.name + "_cleaned";
            Selection.activeObject = selectedObject;

            // Perform the operation to remove unused bones
            RemoveUnusedBonesFromObject(selectedObject);
        }


        /// <summary>
        /// Removes unused bones from a GameObject.
        /// </summary>
        /// <param name="gameObject">The GameObject to remove unused bones from.</param>
        public static void RemoveUnusedBonesFromObject(GameObject gameObject)
        {
            // ---- Starting the process ---- // 

            // This process is 3 steps:
            // 1. Get all the bones used by the SkinnedMeshRenderers
            // 2. Get all the bones used by the VRCPhysBone colliders
            // 3. Remove the bones that are not used

            // Create a set to store the bones that are used
            HashSet<Transform> usedBones = new HashSet<Transform>();

            // #### Step 1 #### //
            // Iterate all the SkinnedMeshRenderers in the copy and add the bones and skin mesh to the set
            AddSkinMeshToHash(gameObject, usedBones);

            // #### Step 2 #### //
            // Iterate all the VRCPhysBone colliders in the copy and add the bones to the set and remove the unused colliders
            AddPhysbonesCollidersToHash(gameObject, usedBones);

            // #### Step 3 #### //
            // Remove the bones that are not in the set from the copy
            RemoveUnusedBonesList(gameObject, usedBones);

            // ---- Ending the process ---- //
        }

        private static void CopyOverridesAndHierarchy(GameObject original, GameObject duplicate)
        {
            // Copy property modifications
            PrefabUtility.SetPropertyModifications(duplicate, PrefabUtility.GetPropertyModifications(original));

            // Sync child objects, including deletions
            for (int i = 0; i < original.transform.childCount; i++)
            {
                Transform originalChild = original.transform.GetChild(i);
                Transform duplicateChild = duplicate.transform.Find(originalChild.name);

                if (duplicateChild == null)
                {
                    // If the child is missing, clone it manually
                    GameObject clonedChild = Object.Instantiate(originalChild.gameObject, duplicate.transform);
                    clonedChild.name = originalChild.name;
                    CopyOverridesAndHierarchy(originalChild.gameObject, clonedChild);
                }
                else
                {
                    // Recursively copy hierarchy and overrides for existing children
                    CopyOverridesAndHierarchy(originalChild.gameObject, duplicateChild.gameObject);
                }
            }

            // Remove extra children in the duplicate that don't exist in the original
            for (int i = duplicate.transform.childCount - 1; i >= 0; i--)
            {
                Transform duplicateChild = duplicate.transform.GetChild(i);
                if (!original.transform.Find(duplicateChild.name))
                {
                    Object.DestroyImmediate(duplicateChild.gameObject);
                }
            }
        }


        /// <summary>
        /// Adds a bone to the hash set of used bones recursively.
        /// </summary>
        /// <param name="bone">The bone to add.</param>
        /// <param name="usedBones">The hash set of used bones.</param>
        /// <param name="childs">If true, the function will add the bone children to the hash set.</param>
        private static void AddBoneToHash(Transform bone, HashSet<Transform> usedBones, bool childs = false)
        {
            if (bone == null) return;
            if (usedBones.Contains(bone) && !childs) return;
            usedBones.Add(bone);
            AddBoneToHash(bone.parent, usedBones, false);

            if (childs)
            {
                for (int i = 0; i < bone.childCount; i++)
                {
                    AddBoneToHash(bone.GetChild(i), usedBones, childs);
                }
            }
        }


        /// <summary>
        /// Adds the bones used by the SkinnedMeshRenderers in the specified GameObject to the given HashSet of used bones.
        /// </summary>
        /// <param name="gameObject">The GameObject containing the SkinnedMeshRenderers.</param>
        /// <param name="usedBones">The HashSet of used bones to add the bones to.</param>
        private static void AddSkinMeshToHash(GameObject gameObject, HashSet<Transform> usedBones)
        {
            // Get all the SkinnedMeshRenderers in the copy
            SkinnedMeshRenderer[] skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();

            // Iterate over all the SkinnedMeshRenderers to get the bones used by them
            foreach (SkinnedMeshRenderer skinnedMesh in skinnedMeshRenderers)
            {
                BoneWeight[] boneWithWeights = skinnedMesh.sharedMesh.boneWeights;

                // Add the skin mesh itself to prevent it from being removed
                AddBoneToHash(skinnedMesh.transform, usedBones, false);

                // Add the bones used by the skin mesh and their parents (4 bones per vertex)
                foreach (var boneWithWeight in boneWithWeights)
                {
                    AddBoneToHash(skinnedMesh.bones[boneWithWeight.boneIndex0], usedBones, false);
                    AddBoneToHash(skinnedMesh.bones[boneWithWeight.boneIndex1], usedBones, false);
                    AddBoneToHash(skinnedMesh.bones[boneWithWeight.boneIndex2], usedBones, false);
                    AddBoneToHash(skinnedMesh.bones[boneWithWeight.boneIndex3], usedBones, false);
                }
            }
        }


        /// <summary>
        /// Adds the bones used by the VRCPhysBone colliders and their parents that are already in the set.
        /// Removes the colliders that are not used.
        /// </summary>
        /// <param name="gameObject">The GameObject to process.</param>
        /// <param name="usedBones">The HashSet of used bones.</param>
        private static void AddPhysbonesCollidersToHash(GameObject gameObject, HashSet<Transform> usedBones)
        {
            Debug.Log("Checking: " + gameObject.name);
            // Add the bones used by the VRCPhysBone colliders and their parents that are already in the set
            VRCPhysBone[] physBones = gameObject.GetComponentsInChildren<VRCPhysBone>();
            HashSet<Transform> usedColliders = new HashSet<Transform>();
            foreach (var physBone in physBones)
            {
                Debug.Log("Checking bone: " + physBone.transform.name);

                if (physBone.rootTransform != null)
                {
                    if (!usedBones.Contains(physBone.rootTransform)) continue;
                    AddBoneToHash(physBone.rootTransform, usedBones, true);
                } else {
                    if (!usedBones.Contains(physBone.transform)) continue;
                }
                AddBoneToHash(physBone.transform, usedBones, true);

                // Add the colliders bone and its parents
                foreach (var collider in physBone.colliders)
                {
                    if (collider == null) continue;
                    if (collider.rootTransform != null){
                        AddBoneToHash(collider.rootTransform, usedBones, false);
                    }
                    AddBoneToHash(collider.transform, usedBones, false);
                    usedColliders.Add(collider.transform);
                }

            }

            // Remove the colliders that are not used
            VRCPhysBoneCollider[] allColliders = gameObject.GetComponentsInChildren<VRCPhysBoneCollider>(true);
            foreach (var collider in allColliders)
            {
                if (!usedColliders.Contains(collider.transform))
                {
                    DestroyImmediate(collider);
                }
            }
        }


        /// <summary>
        /// Removes unused bones from a GameObject and its children recursively.
        /// </summary>
        /// <param name="gameObject">The GameObject to remove unused bones from.</param>
        /// <param name="usedBones">A HashSet of Transform objects representing the used bones.</param>
        private static void RemoveUnusedBonesList(GameObject gameObject, HashSet<Transform> usedBones)
        {
            Transform rootBone = gameObject.transform;
            for (int i = rootBone.childCount - 1; i >= 0; i--)
            {
                Transform child = rootBone.GetChild(i);
                if (!usedBones.Contains(child))
                {
                    Debug.Log("Removing bone: " + child.name);
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
                else
                {
                    RemoveUnusedBonesList(child.gameObject, usedBones);
                }

            }
        }
    }
}
