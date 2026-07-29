using UnityEditor;
using UnityEngine;

namespace VoxelLabs.UltimateThumbnails.Lib
{
    public static class Utils
    {
        public static int GetEditorVersionIdentifier()
        {
#if UNITY_6000_5_OR_NEWER
            return 6000_5;
#elif UNITY_2022_2_OR_NEWER
            return 2022_2;
#elif UNITY_2021_3_OR_NEWER
                return 2021_3;
#elif UNITY_2021_1_OR_NEWER
                return 2021_1;
#elif UNITY_2018_4_OR_NEWER
                return 2018_4;
#endif
        }

        public static bool GetVerbosity()
        {
#if ULTIMATE_PREVIEW_VERBOSE
            return true;
#else
            return false;
#endif
        }

        public static object GetSelectionIds()
        {
#if UNITY_6000_5_OR_NEWER
            return Selection.entityIds;

#else
            return Selection.instanceIDs;
#endif
        }
        
        public static void SetSelectionIds(object ids)
        {
#if UNITY_6000_5_OR_NEWER
            Selection.entityIds = ids as EntityId[];

#else
            Selection.instanceIDs = ids as int[];
#endif
        }

        public static void ClearSelection()
        {
#if UNITY_6000_5_OR_NEWER
            Selection.entityIds = new EntityId[0];

#else
            Selection.instanceIDs = new int[0];
#endif
        }
        
        public static int GetIconSize(IconSize iconSize)
        {
            // Texture size should be multiple of 4 else the generated icon can be blurry
            switch (iconSize)
            {
                case IconSize.X64:
                    return 64;
                case IconSize.X128:
                    return 128;
                case IconSize.X256:
                    return 256;
                case IconSize.X512:
                    return 512;
                default:
                    return 128;
            }
        }
    }
}