using UnityEditor;
using UnityEngine;

namespace RaidIntoDarkness.Integrations.Inventory.Editor
{
    public static class ItemIconDefaults
    {
        public const float LIGHT_INTENSITY = 40f;
        public const float SLOT_LIGHT_INTENSITY = 40f;
        public const bool USE_OUTLINE = false;

        public static readonly Vector3 SIDE_VIEW_ANGLES = new Vector3(0f, -90f, 0f);

        public static void Apply(SerializedObject definition, Vector3 cameraAngles)
        {
            definition.FindProperty("_iconOverride").objectReferenceValue = null;
            definition.FindProperty("_icon._generateIcon").boolValue = true;
            definition.FindProperty("_icon._useOutline").boolValue = USE_OUTLINE;
            definition.FindProperty("_icon._useCustomSlotView").boolValue = false;
            definition.FindProperty("_icon._defaultView._cameraEulerAngles").vector3Value = cameraAngles;
            definition.FindProperty("_icon._defaultView._lightIntensity").floatValue = LIGHT_INTENSITY;
            definition.FindProperty("_icon._slotView._cameraEulerAngles").vector3Value = cameraAngles;
            definition.FindProperty("_icon._slotView._lightIntensity").floatValue = SLOT_LIGHT_INTENSITY;
        }
    }
}
