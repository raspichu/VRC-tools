using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Avatars.Components;

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

        public void ApplyColliderChanges()
        {
            // Change the avatar colliders positions to object reference values
            if (avatarDescriptor == null)
            {
                Debug.LogWarning("[ApplyColliderChanges] Avatar Descriptor is not assigned.");
                return;
            }

            if (changeLeftIndex)
            {
                avatarDescriptor.collider_fingerIndexL.transform = leftIndex;
                avatarDescriptor.collider_fingerIndexL.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_fingerIndexL.isMirrored = false;
                avatarDescriptor.collider_fingerIndexL.position = Vector3.zero;
            }
            if (changeLeftMiddle)
            {
                avatarDescriptor.collider_fingerMiddleL.transform = leftMiddle;
                avatarDescriptor.collider_fingerMiddleL.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_fingerMiddleL.isMirrored = false;
                avatarDescriptor.collider_fingerMiddleL.position = Vector3.zero;
            }
            if (changeLeftRing)
            {
                avatarDescriptor.collider_fingerRingL.transform = leftRing;
                avatarDescriptor.collider_fingerRingL.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_fingerRingL.isMirrored = false;
                avatarDescriptor.collider_fingerRingL.position = Vector3.zero;
            }
            if (changeLeftPinky)
            {
                avatarDescriptor.collider_fingerLittleL.transform = leftPinky;
                avatarDescriptor.collider_fingerLittleL.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_fingerLittleL.isMirrored = false;
                avatarDescriptor.collider_fingerLittleL.position = Vector3.zero;
            }
            if (changeLeftHand)
            {
                avatarDescriptor.collider_handL.transform = leftHand;
                avatarDescriptor.collider_handL.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_handL.isMirrored = false;
                avatarDescriptor.collider_handL.position = Vector3.zero;
            }

            if (changeRightIndex)
            {
                avatarDescriptor.collider_fingerIndexR.transform = rightIndex;
                avatarDescriptor.collider_fingerIndexR.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_fingerIndexR.isMirrored = false;
                avatarDescriptor.collider_fingerIndexR.position = Vector3.zero;
            }
            if (changeRightMiddle)
            {
                avatarDescriptor.collider_fingerMiddleR.transform = rightMiddle;
                avatarDescriptor.collider_fingerMiddleR.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_fingerMiddleR.isMirrored = false;
                avatarDescriptor.collider_fingerMiddleR.position = Vector3.zero;
            }
            if (changeRightRing)
            {
                avatarDescriptor.collider_fingerRingR.transform = rightRing;
                avatarDescriptor.collider_fingerRingR.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_fingerRingR.isMirrored = false;
                avatarDescriptor.collider_fingerRingR.position = Vector3.zero;
            }
            if (changeRightPinky)
            {
                avatarDescriptor.collider_fingerLittleR.transform = rightPinky;
                avatarDescriptor.collider_fingerLittleR.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_fingerLittleR.isMirrored = false;
                avatarDescriptor.collider_fingerLittleR.position = Vector3.zero;
            }
            if (changeRightHand)
            {
                avatarDescriptor.collider_handR.transform = rightHand;
                avatarDescriptor.collider_handR.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_handR.isMirrored = false;
                avatarDescriptor.collider_handR.position = Vector3.zero;
            }

            if (changeHead)
            {
                avatarDescriptor.collider_head.transform = Head;
                avatarDescriptor.collider_head.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_head.isMirrored = false;
                avatarDescriptor.collider_head.position = Vector3.zero;
            }

            if (changeTorso)
            {
                avatarDescriptor.collider_torso.transform = Torso;
                avatarDescriptor.collider_torso.state = VRCAvatarDescriptor.ColliderConfig.State.Custom;
                avatarDescriptor.collider_torso.isMirrored = false;
                avatarDescriptor.collider_torso.position = Vector3.zero;
            }
        }

    }

}
