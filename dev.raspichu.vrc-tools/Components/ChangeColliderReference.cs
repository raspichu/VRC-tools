using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Dynamics.Constraint.Components;
using VRC.SDKBase;

namespace raspichu.vrc_tools.component
{
    [AddComponentMenu("Pichu/Change collider reference")]
    public class ChangeColliderReference : MonoBehaviour, IEditorOnly
    {
        public VRCAvatarDescriptor avatarDescriptor;

        // References to the transforms for each finger and hand
        public Transform leftIndex;
        public Transform leftMiddle;
        public Transform leftRing;
        public Transform leftPinky;
        public Transform leftHand;

        public Transform rightIndex;
        public Transform rightMiddle;
        public Transform rightRing;
        public Transform rightPinky;
        public Transform rightHand;

        public Transform Head;
        public Transform Torso;

        // Checkbox states
        public bool changeLeftIndex = false;
        public bool changeLeftMiddle = false;
        public bool changeLeftRing = false;
        public bool changeLeftPinky = false;
        public bool changeLeftHand = false;

        public bool changeRightIndex = false;
        public bool changeRightMiddle = false;
        public bool changeRightRing = false;
        public bool changeRightPinky = false;
        public bool changeRightHand = false;

        public bool changeHead = false;
        public bool changeTorso = false;

        public bool constraintZeroToAuto = false;

        public void ApplyColliderChanges()
        {
            // Change the avatar colliders positions to object reference values
            if (avatarDescriptor == null)
            {
                Debug.LogWarning("[ApplyColliderChanges] Avatar Descriptor is not assigned.");
                return;
            }

            // Left hand
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

            // Right hand
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

            // Head & torso
            ApplyColliderChange(changeHead, ref avatarDescriptor.collider_head, Head);
            ApplyColliderChange(changeTorso, ref avatarDescriptor.collider_torso, Torso);
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
            {
                Debug.Log(
                    "[ApplyColliderChange] Setting collider position from constraint source: "
                        + target.name
                );
                var constraint = target.GetComponent<VRCParentConstraint>();
                if (constraint != null && constraint.Sources.Count > 0)
                {
                    var source = constraint.Sources[0].SourceTransform;
                    Debug.Log("[ApplyColliderChange] Found constraint source: " + source.name);
                    if (source != null)
                    {
                        Debug.Log(
                            "[ApplyColliderChange] Setting source position to config position before:"
                                + source.position
                                + " to "
                                + colliderConfig.position
                        );
                        Vector3 worldPos = colliderConfig.transform.TransformPoint(
                            colliderConfig.position
                        );
                        source.position = worldPos;
                        // source.rotation = colliderConfig.transform.rotation;
                        // source.rotation =
                        //     colliderConfig.transform.rotation * colliderConfig.rotation;
                        Debug.Log("[ApplyColliderChange] New source position: " + source.position);
                    }
                }
            }
            colliderConfig.transform = target;
            colliderConfig.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
            colliderConfig.isMirrored = false;

            colliderConfig.position = Vector3.zero;
        }
    }
}