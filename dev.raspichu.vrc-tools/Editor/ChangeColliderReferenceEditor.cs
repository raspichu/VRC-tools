using UnityEditor;
using UnityEngine;
using raspichu.vrc_tools.component;
using VRC.SDK3.Avatars.Components;

namespace raspichu.vrc_tools.editor
{
    [CustomEditor(typeof(ChangeColliderReference))]
    public class ChangeColliderReferenceEditor : Editor
    {

        // Avatar reference
        private SerializedProperty avatarDescriptorProp;


        // Checkbox properties
        private SerializedProperty changeLeftIndexProp;
        private SerializedProperty changeLeftMiddleProp;
        private SerializedProperty changeLeftRingProp;
        private SerializedProperty changeLeftPinkyProp;
        private SerializedProperty changeLeftHandProp;

        private SerializedProperty changeRightIndexProp;
        private SerializedProperty changeRightMiddleProp;
        private SerializedProperty changeRightRingProp;
        private SerializedProperty changeRightPinkyProp;
        private SerializedProperty changeRightHandProp;


        // References to the transforms for each finger and hand
        private SerializedProperty leftIndexProp;
        private SerializedProperty leftMiddleProp;
        private SerializedProperty leftRingProp;
        private SerializedProperty leftPinkyProp;
        private SerializedProperty leftHandProp;

        private SerializedProperty rightIndexProp;
        private SerializedProperty rightMiddleProp;
        private SerializedProperty rightRingProp;
        private SerializedProperty rightPinkyProp;
        private SerializedProperty rightHandProp;

        void OnEnable()
        {
            if (target == null)
            {
                return;
            }

            avatarDescriptorProp = serializedObject.FindProperty("avatarDescriptor");

            // Initialize checkbox properties
            changeLeftIndexProp = serializedObject.FindProperty("changeLeftIndex");
            changeLeftMiddleProp = serializedObject.FindProperty("changeLeftMiddle");
            changeLeftRingProp = serializedObject.FindProperty("changeLeftRing");
            changeLeftPinkyProp = serializedObject.FindProperty("changeLeftPinky");
            changeLeftHandProp = serializedObject.FindProperty("changeLeftHand");

            changeRightIndexProp = serializedObject.FindProperty("changeRightIndex");
            changeRightMiddleProp = serializedObject.FindProperty("changeRightMiddle");
            changeRightRingProp = serializedObject.FindProperty("changeRightRing");
            changeRightPinkyProp = serializedObject.FindProperty("changeRightPinky");
            changeRightHandProp = serializedObject.FindProperty("changeRightHand");

            // Initialize transform properties
            leftIndexProp = serializedObject.FindProperty("leftIndex");
            leftMiddleProp = serializedObject.FindProperty("leftMiddle");
            leftRingProp = serializedObject.FindProperty("leftRing");
            leftPinkyProp = serializedObject.FindProperty("leftPinky");
            leftHandProp = serializedObject.FindProperty("leftHand");

            rightIndexProp = serializedObject.FindProperty("rightIndex");
            rightMiddleProp = serializedObject.FindProperty("rightMiddle");
            rightRingProp = serializedObject.FindProperty("rightRing");
            rightPinkyProp = serializedObject.FindProperty("rightPinky");
            rightHandProp = serializedObject.FindProperty("rightHand");

            FindAndSetProperties();

        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Experimental
            EditorGUILayout.HelpBox("This script is experimental and may not work as expected", MessageType.Warning);


            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Left Hand", EditorStyles.boldLabel);
            changeLeftHandProp.boolValue = EditorGUILayout.Toggle("Left Hand", changeLeftHandProp.boolValue);
            if (changeLeftHandProp.boolValue)
                EditorGUILayout.PropertyField(leftHandProp);
            changeLeftIndexProp.boolValue = EditorGUILayout.Toggle("Left Index", changeLeftIndexProp.boolValue);
            if (changeLeftIndexProp.boolValue)
                EditorGUILayout.PropertyField(leftIndexProp);

            changeLeftMiddleProp.boolValue = EditorGUILayout.Toggle("Left Middle", changeLeftMiddleProp.boolValue);
            if (changeLeftMiddleProp.boolValue)
                EditorGUILayout.PropertyField(leftMiddleProp);

            changeLeftRingProp.boolValue = EditorGUILayout.Toggle("Left Ring", changeLeftRingProp.boolValue);
            if (changeLeftRingProp.boolValue)
                EditorGUILayout.PropertyField(leftRingProp);

            changeLeftPinkyProp.boolValue = EditorGUILayout.Toggle("Left Pinky", changeLeftPinkyProp.boolValue);
            if (changeLeftPinkyProp.boolValue)
                EditorGUILayout.PropertyField(leftPinkyProp);


            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Right Hand", EditorStyles.boldLabel);
            changeRightHandProp.boolValue = EditorGUILayout.Toggle("Right Hand", changeRightHandProp.boolValue);
            if (changeRightHandProp.boolValue)
                EditorGUILayout.PropertyField(rightHandProp);
            changeRightIndexProp.boolValue = EditorGUILayout.Toggle("Right Index", changeRightIndexProp.boolValue);
            if (changeRightIndexProp.boolValue)
                EditorGUILayout.PropertyField(rightIndexProp);

            changeRightMiddleProp.boolValue = EditorGUILayout.Toggle("Right Middle", changeRightMiddleProp.boolValue);
            if (changeRightMiddleProp.boolValue)
                EditorGUILayout.PropertyField(rightMiddleProp);

            changeRightRingProp.boolValue = EditorGUILayout.Toggle("Right Ring", changeRightRingProp.boolValue);
            if (changeRightRingProp.boolValue)
                EditorGUILayout.PropertyField(rightRingProp);

            changeRightPinkyProp.boolValue = EditorGUILayout.Toggle("Right Pinky", changeRightPinkyProp.boolValue);
            if (changeRightPinkyProp.boolValue)
                EditorGUILayout.PropertyField(rightPinkyProp);


            serializedObject.ApplyModifiedProperties();
        }

        private void FindAndSetProperties()
        {
            // Ensure avatarDescriptor is assigned

            if (!avatarDescriptorProp.objectReferenceValue)
            {
                ChangeColliderReference changeColliderReference = (ChangeColliderReference)target;
                avatarDescriptorProp.objectReferenceValue = CommonEditor.GetVRCAvatarDescriptors(changeColliderReference.gameObject);
            }

            VRCAvatarDescriptor avatarDescriptor = avatarDescriptorProp.objectReferenceValue as VRCAvatarDescriptor;


            if (!leftIndexProp.objectReferenceValue) leftIndexProp.objectReferenceValue = avatarDescriptor.collider_fingerIndexL.transform;
            if (!leftMiddleProp.objectReferenceValue) leftMiddleProp.objectReferenceValue = avatarDescriptor.collider_fingerMiddleL.transform;
            if (!leftRingProp.objectReferenceValue) leftRingProp.objectReferenceValue = avatarDescriptor.collider_fingerRingL.transform;
            if (!leftPinkyProp.objectReferenceValue) leftPinkyProp.objectReferenceValue = avatarDescriptor.collider_fingerLittleL.transform;
            if (!leftHandProp.objectReferenceValue) leftHandProp.objectReferenceValue = avatarDescriptor.collider_handL.transform;

            if (!rightIndexProp.objectReferenceValue) rightIndexProp.objectReferenceValue = avatarDescriptor.collider_fingerIndexR.transform;
            if (!rightMiddleProp.objectReferenceValue) rightMiddleProp.objectReferenceValue = avatarDescriptor.collider_fingerMiddleR.transform;
            if (!rightRingProp.objectReferenceValue) rightRingProp.objectReferenceValue = avatarDescriptor.collider_fingerRingR.transform;
            if (!rightPinkyProp.objectReferenceValue) rightPinkyProp.objectReferenceValue = avatarDescriptor.collider_fingerLittleL.transform;
            if (!rightHandProp.objectReferenceValue) rightHandProp.objectReferenceValue = avatarDescriptor.collider_handR.transform;

            // Apply modified properties to update changes
            serializedObject.ApplyModifiedProperties();
        }



    }
}
