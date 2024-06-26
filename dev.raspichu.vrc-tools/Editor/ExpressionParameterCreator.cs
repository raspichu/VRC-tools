using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using nadena.dev.modular_avatar.core;

[CustomEditor(typeof(ParameterToMA))]
public class ExpressionParameterCreator : Editor
{
    public override void OnInspectorGUI()
    {
        ParameterToMA parameterToMA = (ParameterToMA)target;

        EditorGUILayout.LabelField("VRC Expression Parameters", EditorStyles.boldLabel);
        parameterToMA.expressionParameters = (VRCExpressionParameters)EditorGUILayout.ObjectField(
            "Expression Parameters", parameterToMA.expressionParameters, typeof(VRCExpressionParameters), true);


        // If there's already a ModularAvatarParameters, add a button to "Add Parameters"
        if (parameterToMA.GetComponent<ModularAvatarParameters>() != null)
        {
            if (GUILayout.Button("Clear and create"))
            {
                parameterToMA.CreateParameters(false);
            }
            if (GUILayout.Button("Add Parameters to existing"))
            {
                parameterToMA.CreateParameters(false);
            }
        }
        else
        {
            if (GUILayout.Button("Create"))
            {
                parameterToMA.CreateParameters(true);
            }
        }

        EditorUtility.SetDirty(target);
    }
}