using UnityEditor;
using UnityEngine;

namespace OnlyWorlds.Sdk.Editor
{
    /// <summary>
    /// Draws <see cref="SerializableNullable{T}"/> so that unset reads as "--", never as 0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole point of the type is that "unset" and "zero" are different claims. A default
    /// inspector would draw both as <c>0</c> with a separate hidden bool, which reintroduces exactly
    /// the confusion the type prevents -- and this time in the place a human looks.
    /// </para>
    /// <para>
    /// Layout: a value field, then a "Set" toggle. Unticked greys the field and shows "--".
    /// Ticking it commits the current value; unticking clears to unset. Typing into an unset field
    /// sets it, because that is what a user typing a number means.
    /// </para>
    /// </remarks>
    [CustomPropertyDrawer(typeof(SerializableNullable<>))]
    public sealed class SerializableNullableDrawer : PropertyDrawer
    {
        private const float ToggleWidth = 42f;
        private const float Gap = 4f;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var valueProp = property.FindPropertyRelative("_value");
            var hasValueProp = property.FindPropertyRelative("_hasValue");

            if (valueProp == null || hasValueProp == null)
            {
                EditorGUI.LabelField(position, label.text, "Unsupported nullable type");
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            var content = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            var fieldRect = new Rect(content.x, content.y, content.width - ToggleWidth - Gap, content.height);
            var toggleRect = new Rect(content.xMax - ToggleWidth, content.y, ToggleWidth, content.height);

            var indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            if (hasValueProp.boolValue)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUI.PropertyField(fieldRect, valueProp, GUIContent.none);
                if (EditorGUI.EndChangeCheck())
                {
                    hasValueProp.boolValue = true;
                }
            }
            else
            {
                // Unset: a DELAYED field showing "--". Delayed is load-bearing -- a plain TextField
                // re-supplies "--" every frame, so the first keystroke would flip _hasValue, swap
                // to the other branch mid-edit, and destroy focus. Multi-digit and negative entry
                // would be impossible. DelayedTextField only reports on Enter/focus-loss, so the
                // user types a whole value before anything changes.
                EditorGUI.BeginChangeCheck();
                var typed = EditorGUI.DelayedTextField(fieldRect, "--");
                if (EditorGUI.EndChangeCheck() && typed != "--" && !string.IsNullOrWhiteSpace(typed))
                {
                    if (TryAssign(valueProp, typed))
                    {
                        hasValueProp.boolValue = true;
                    }
                }
            }

            EditorGUI.BeginChangeCheck();
            var set = GUI.Toggle(toggleRect, hasValueProp.boolValue, "Set", EditorStyles.miniButton);
            if (EditorGUI.EndChangeCheck())
            {
                hasValueProp.boolValue = set;
                if (!set)
                {
                    // Clearing resets the payload so a stale value cannot resurface if re-set.
                    // Type-agnostic: the drawer is registered open-generic, so T may be float,
                    // bool, an enum, or anything else satisfying the struct constraint. Writing
                    // intValue here would silently fail to clear for all of them.
                    ClearValue(valueProp);
                }
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        /// <summary>
        /// Resets the payload to its zero value, whatever the underlying serialized type is.
        /// </summary>
        private static void ClearValue(SerializedProperty p)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                    p.intValue = 0;
                    break;
                case SerializedPropertyType.Float:
                    p.floatValue = 0f;
                    break;
                case SerializedPropertyType.Boolean:
                    p.boolValue = false;
                    break;
                default:
                    // Unknown payload type: leave it rather than corrupt it. _hasValue is already
                    // false, so the value is unreachable through the public API either way.
                    break;
            }
        }

        /// <summary>
        /// Parses typed text into the property's actual type. Returns false on unparseable input,
        /// which leaves the field unset rather than inventing a value.
        /// </summary>
        private static bool TryAssign(SerializedProperty p, string text)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                    if (int.TryParse(text, out var i)) { p.intValue = i; return true; }
                    return false;
                case SerializedPropertyType.Float:
                    if (float.TryParse(text, out var f)) { p.floatValue = f; return true; }
                    return false;
                case SerializedPropertyType.Boolean:
                    if (bool.TryParse(text, out var b)) { p.boolValue = b; return true; }
                    return false;
                default:
                    return false;
            }
        }
    }
}
