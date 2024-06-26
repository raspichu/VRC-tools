using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.modular_avatar.core;

namespace raspichu.vrc_tools.component
{
    [AddComponentMenu("Pichu/Parameter to MA")]
    public class ParameterToMA : MonoBehaviour
    {
        public VRCExpressionParameters expressionParameters;

        public void CreateParameters(bool reset = true)
        {
            if (expressionParameters == null)
            {
                Debug.LogWarning("No expression parameters assigned!");
                return;
            }


            ModularAvatarParameters map = GetComponent<ModularAvatarParameters>();
            if (map == null)
            {
                map = gameObject.AddComponent<ModularAvatarParameters>();
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

            // Delete this script after creating the parameters

            // Undo the creation of the parameters
            Undo.DestroyObjectImmediate(this);

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
