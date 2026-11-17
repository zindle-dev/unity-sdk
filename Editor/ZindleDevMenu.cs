#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace ZindleDevSDK.Editor
{
    static class ZindleDevMenu
    {
        [MenuItem("Tools/Zindle Dev/Create Config Asset")]
        static void CreateConfig()
        {
            var existing = FindExistingConfig();
            if (existing != null)
            {
                Selection.activeObject = existing;
                EditorGUIUtility.PingObject(existing);
                Debug.Log($"[Zindle Dev] A config asset already exists at {AssetDatabase.GetAssetPath(existing)}. Selected it instead of creating a duplicate.", existing);
                return;
            }

            var cfg = ScriptableObject.CreateInstance<ZindleDevConfig>();
            const string path = "Assets/ZindleDevConfig.asset";
            AssetDatabase.CreateAsset(cfg, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = cfg;
            EditorGUIUtility.PingObject(cfg);
            Debug.Log("[Zindle Dev] Created config asset. Set your apiKey and endpoint in the Inspector.", cfg);
        }

        internal static ZindleDevConfig FindExistingConfig()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:ZindleDevConfig"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var cfg = AssetDatabase.LoadAssetAtPath<ZindleDevConfig>(path);
                if (cfg != null)
                    return cfg;
            }
            return null;
        }
    }
}
#endif
