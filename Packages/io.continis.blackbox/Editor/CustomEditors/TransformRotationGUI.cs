// Credits: https://gist.github.com/keenanwoodall/b189359e8498c244496212122ca1e8e0

using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace BlackBox.CustomEditors
{
    /// <summary>
    /// Abstracts the reflection required to use the internal TransformRotationGUI class.
    /// </summary>
    public class TransformRotationGUI
    {
        private readonly object _transformRotationGUI;
        private readonly MethodInfo _onEnable;
        private readonly MethodInfo _rotationField;

        public TransformRotationGUI ()
        {
            if (_transformRotationGUI == null)
            {
                Type type = Type.GetType ("UnityEditor.TransformRotationGUI,UnityEditor");

                _onEnable = type!.GetMethod ("OnEnable");
                _rotationField = type.GetMethod ("RotationField", new Type[] { });

                _transformRotationGUI = Activator.CreateInstance (type);
            }
        }

        public void Initialize (SerializedProperty property, GUIContent content)
        {
            _onEnable.Invoke (_transformRotationGUI, new object[] { property, content });
        }

        public void Draw ()
        {
            _rotationField.Invoke (_transformRotationGUI, null);
        }
    }
}