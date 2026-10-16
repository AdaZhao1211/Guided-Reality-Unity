using System.IO;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace GuidedReality.Editor
{
    public sealed class ConnectionSettings : EditorWindow
    {
        const string PathName="Assets/StreamingAssets/guidedreality.local.json";
        RuntimeSettings settings;
        [MenuItem("GuidedReality/Connection Settings")]
        public static void Open() => GetWindow<ConnectionSettings>("GuidedReality Connections");
        void OnEnable() {
            settings=File.Exists(PathName)?JsonConvert.DeserializeObject<RuntimeSettings>(File.ReadAllText(PathName)):new RuntimeSettings();
            settings ??= new RuntimeSettings();
        }
        void OnGUI() {
            EditorGUILayout.LabelField("Headset → Gemini → SAM-2",EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Gemini runs directly from the headset. This prototype stores credentials in a git-ignored local file included in your development build. Use your own restricted development key; do not distribute this APK with credentials.",MessageType.Info);
            settings.geminiApiKey=EditorGUILayout.PasswordField("Gemini API key",settings.geminiApiKey);
            settings.geminiModel=EditorGUILayout.TextField("Gemini model",settings.geminiModel);
            settings.samEndpoint=EditorGUILayout.TextField("SAM endpoint",settings.samEndpoint);
            settings.samToken=EditorGUILayout.PasswordField("SAM bearer token",settings.samToken);
            settings.depthGridColumns=EditorGUILayout.IntSlider("Depth sample columns",settings.depthGridColumns,81,161);
            settings.depthGridRows=EditorGUILayout.IntSlider("Depth sample rows",settings.depthGridRows,61,121);
            EditorGUILayout.HelpBox("81 x 61 is the default captured depth grid. Higher values add depth queries and capture time; they do not improve the sensor's distance accuracy. Rebuild after saving.",MessageType.Info);
            if(GUILayout.Button("Save local configuration")) {
                Directory.CreateDirectory("Assets/StreamingAssets");
                File.WriteAllText(PathName,JsonConvert.SerializeObject(settings,Formatting.Indented));
                AssetDatabase.Refresh(); ShowNotification(new GUIContent("Saved. Rebuild to update headset configuration."));
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Direct connection: enter your SAM server's private network address.");
            EditorGUILayout.LabelField("USB tunnel: http://127.0.0.1:8765");
        }
    }
}
