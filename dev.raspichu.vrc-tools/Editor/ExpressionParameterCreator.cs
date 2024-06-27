#if MA_EXISTS
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using VRC.SDK3.Avatars.ScriptableObjects;

using raspichu.vrc_tools.component;

namespace raspichu.vrc_tools.editor
{
    [CustomEditor(typeof(ParameterToMA))]
    public class ExpressionParameterCreator : Editor
    {

        private VRCExpressionParameters expressionParameters;

        public override void OnInspectorGUI()
        {
            Debug.Log("ExpressionParameterCreator");
            ParameterToMA parameterToMA = (ParameterToMA)target;

            EditorGUILayout.LabelField("VRC Expression Parameters", EditorStyles.boldLabel);
            expressionParameters = (VRCExpressionParameters)EditorGUILayout.ObjectField(
                "Expression Parameters", expressionParameters, typeof(VRCExpressionParameters), true);

            // If there's already a ModularAvatarParameters
            bool isAlreadyCreated = parameterToMA.GetComponent<ModularAvatarParameters>() != null;

            // If there's already a ModularAvatarParameters, add a button to "Add Parameters"
            if (isAlreadyCreated)
            {
                if (GUILayout.Button("Clear and create"))
                {
                    CreateParameters(parameterToMA, false);
                }
                if (GUILayout.Button("Add Parameters to existing"))
                {
                    CreateParameters(parameterToMA, false);
                }
            }
            else
            {
                if (GUILayout.Button("Create"))
                {
                    CreateParameters(parameterToMA, true);
                }
            }

            EditorUtility.SetDirty(target);
        }

        private void CreateParameters(ParameterToMA parameterToMA, bool reset)
        {
            if (expressionParameters == null)
            {
                Debug.LogWarning("No expression parameters assigned!");
                return;
            }

            ModularAvatarParameters map = parameterToMA.GetComponent<ModularAvatarParameters>();
            if (map == null)
            {
                map = parameterToMA.gameObject.AddComponent<ModularAvatarParameters>();
                Debug.Log("ModularAvatarParameters component created and added to the GameObject.");
                Undo.RegisterCreatedObjectUndo(map, "Create Modular Avatar Parameters");
            }
            else
            {
                Undo.RecordObject(map, "Update Modular Avatar Parameters");
                if (reset)
                {
                    map.parameters.Clear();
                }
            }

            foreach (var parameter in expressionParameters.parameters)
            {
                ParameterConfig config = new ParameterConfig
                {
                    nameOrPrefix = parameter.name,
                    remapTo = "", // You can set a remap value if needed
                    internalParameter = false,
                    isPrefix = false,
                    syncType = parameter.networkSynced ? GetSyncType(parameter.valueType) : ParameterSyncType.NotSynced,
                    localOnly = false,
                    defaultValue = parameter.defaultValue,
                    saved = parameter.saved,
                    hasExplicitDefaultValue = true, // Adjust based on your logic
                };
                map.parameters.Add(config);
            }

            // Undo the creation of the parameters
            Undo.DestroyObjectImmediate(parameterToMA);

            Debug.Log("Parameters created successfully!");
        }

        private ParameterSyncType GetSyncType(VRCExpressionParameters.ValueType valueType)
        {
            switch (valueType)
            {
                case VRCExpressionParameters.ValueType.Int:
                    return ParameterSyncType.Int;
                case VRCExpressionParameters.ValueType.Float:
                    return ParameterSyncType.Float;
                case VRCExpressionParameters.ValueType.Bool:
                    return ParameterSyncType.Bool;
                default:
                    return ParameterSyncType.NotSynced;
            }
        }
    }

}
#endif