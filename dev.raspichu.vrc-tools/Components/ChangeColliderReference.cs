using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace raspichu.vrc_tools.component
{
    [AddComponentMenu("Pichu/Change collider reference")]
    public class ChangeColliderReference : MonoBehaviour, IEditorOnly
    {
        public VRCAvatarDescriptor avatarDescriptor;

        [Header("Left Hand")]
        public Transform leftIndex;
        public Transform leftMiddle;
        public Transform leftRing;
        public Transform leftPinky;
        public Transform leftHand;

        [Header("Right Hand")]
        public Transform rightIndex;
        public Transform rightMiddle;
        public Transform rightRing;
        public Transform rightPinky;
        public Transform rightHand;

        [Header("Body")]
        public Transform Head;
        public Transform Torso;

        [Header("Selection Toggles")]
        public bool changeLeftIndex,
            changeLeftMiddle,
            changeLeftRing,
            changeLeftPinky,
            changeLeftHand;
        public bool changeRightIndex,
            changeRightMiddle,
            changeRightRing,
            changeRightPinky,
            changeRightHand;
        public bool changeHead,
            changeTorso;

        [Header("Settings")]
        public bool constraintZeroToAuto = false;

        public void ApplyColliderChanges()
        {
            if (avatarDescriptor == null)
            {
                Debug.LogWarning("[ApplyColliderChanges] Avatar Descriptor is not assigned.");
                return;
            }

#if UNITY_EDITOR
            Undo.RecordObject(avatarDescriptor, "Apply Collider Changes");
#endif

            ApplyColliderChange(
                changeLeftIndex,
                ref avatarDescriptor.collider_fingerIndexL,
                leftIndex
            );
            ApplyColliderChange(
                changeLeftMiddle,
                ref avatarDescriptor.collider_fingerMiddleL,
                leftMiddle
            );
            ApplyColliderChange(
                changeLeftRing,
                ref avatarDescriptor.collider_fingerRingL,
                leftRing
            );
            ApplyColliderChange(
                changeLeftPinky,
                ref avatarDescriptor.collider_fingerLittleL,
                leftPinky
            );
            ApplyColliderChange(changeLeftHand, ref avatarDescriptor.collider_handL, leftHand);

            ApplyColliderChange(
                changeRightIndex,
                ref avatarDescriptor.collider_fingerIndexR,
                rightIndex
            );
            ApplyColliderChange(
                changeRightMiddle,
                ref avatarDescriptor.collider_fingerMiddleR,
                rightMiddle
            );
            ApplyColliderChange(
                changeRightRing,
                ref avatarDescriptor.collider_fingerRingR,
                rightRing
            );
            ApplyColliderChange(
                changeRightPinky,
                ref avatarDescriptor.collider_fingerLittleR,
                rightPinky
            );
            ApplyColliderChange(changeRightHand, ref avatarDescriptor.collider_handR, rightHand);

            ApplyColliderChange(changeHead, ref avatarDescriptor.collider_head, Head);
            ApplyColliderChange(changeTorso, ref avatarDescriptor.collider_torso, Torso);

#if UNITY_EDITOR
            EditorUtility.SetDirty(avatarDescriptor);
#endif
        }

        public void AlignConstraintSourcesToColliders()
        {
            if (avatarDescriptor == null)
                return;

            if (changeLeftIndex)
                AlignConstraintSource(leftIndex, avatarDescriptor.collider_fingerIndexL);
            if (changeLeftMiddle)
                AlignConstraintSource(leftMiddle, avatarDescriptor.collider_fingerMiddleL);
            if (changeLeftRing)
                AlignConstraintSource(leftRing, avatarDescriptor.collider_fingerRingL);
            if (changeLeftPinky)
                AlignConstraintSource(leftPinky, avatarDescriptor.collider_fingerLittleL);
            if (changeLeftHand)
                AlignConstraintSource(leftHand, avatarDescriptor.collider_handL);

            if (changeRightIndex)
                AlignConstraintSource(rightIndex, avatarDescriptor.collider_fingerIndexR);
            if (changeRightMiddle)
                AlignConstraintSource(rightMiddle, avatarDescriptor.collider_fingerMiddleR);
            if (changeRightRing)
                AlignConstraintSource(rightRing, avatarDescriptor.collider_fingerRingR);
            if (changeRightPinky)
                AlignConstraintSource(rightPinky, avatarDescriptor.collider_fingerLittleR);
            if (changeRightHand)
                AlignConstraintSource(rightHand, avatarDescriptor.collider_handR);

            if (changeHead)
                AlignConstraintSource(Head, avatarDescriptor.collider_head);
            if (changeTorso)
                AlignConstraintSource(Torso, avatarDescriptor.collider_torso);
        }

        private void ApplyColliderChange(
            bool shouldChange,
            ref VRCAvatarDescriptor.ColliderConfig colliderConfig,
            Transform target
        )
        {
            if (!shouldChange || target == null)
                return;

            if (constraintZeroToAuto)
                AlignConstraintSource(target, colliderConfig);

            colliderConfig.transform = target;
            colliderConfig.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
            colliderConfig.isMirrored = false;
            colliderConfig.position = Vector3.zero;
        }

        private void AlignConstraintSource(
            Transform target,
            VRCAvatarDescriptor.ColliderConfig colliderConfig
        )
        {
            if (target == null)
                return;

            VRCParentConstraint constraint = target.GetComponent<VRCParentConstraint>();
            if (constraint == null)
                constraint = target.GetComponentInChildren<VRCParentConstraint>();
            if (constraint == null)
                constraint = target.GetComponentInParent<VRCParentConstraint>();

            if (constraint == null || constraint.Sources.Count == 0)
            {
                Debug.LogWarning(
                    $"[Align] No VRCParentConstraint or Sources found for {target.name}"
                );
                return;
            }

            Transform source = constraint.Sources[0].SourceTransform;
            if (source == null)
                return;

            Transform refTransform =
                colliderConfig.transform != null ? colliderConfig.transform : target;
            Vector3 targetWorldPos = refTransform.TransformPoint(colliderConfig.position);
            Quaternion targetWorldRot = refTransform.rotation * colliderConfig.rotation;

#if UNITY_EDITOR
            Debug.Log(
                $"[Align] Aligning '{source.name}' to '{refTransform.name}' at world position {targetWorldPos} and rotation {targetWorldRot.eulerAngles}"
            );
            Undo.RecordObject(source, "Align Constraint Source");
            source.position = targetWorldPos;
            source.rotation = targetWorldRot;
            EditorUtility.SetDirty(source);
#else
            source.position = targetWorldPos;
            source.rotation = targetWorldRot;
#endif
            Debug.Log(
                $"[Align] Source '{source.name}' aligned to collider position at '{refTransform.name}'"
            );
        }
    }
}
