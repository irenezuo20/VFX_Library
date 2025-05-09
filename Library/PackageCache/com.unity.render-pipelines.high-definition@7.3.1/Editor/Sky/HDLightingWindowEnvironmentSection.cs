using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.HighDefinition;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEditor.SceneManagement;
using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEditor.Rendering.HighDefinition
{
    [ScriptableRenderPipelineExtension(typeof(HDRenderPipelineAsset))]
    class HDLightingWindowEnvironmentSectionEditor : LightingWindowEnvironmentSection
    {
        class Styles
        {
            public static GUIStyle headerStyle;
            static Styles()
            {
                headerStyle = new GUIStyle(EditorStyles.foldoutHeader);
                headerStyle.fontStyle = FontStyle.Bold;
                headerStyle.fontSize = 12;
                headerStyle.margin = new RectOffset(17, 0, 0, 0);
                headerStyle.padding = new RectOffset(16, 1, 0, 0);
                headerStyle.fixedHeight = 21;
            }
        }

        class SerializedStaticLightingSky
        {
            SerializedObject serializedObject;
            public SerializedProperty skyUniqueID;

            public VolumeProfile volumeProfile
            {
                get => (serializedObject.targetObject as StaticLightingSky).profile;
                set => (serializedObject.targetObject as StaticLightingSky).profile = value;
            }

            public SerializedStaticLightingSky(StaticLightingSky staticLightingSky)
            {
                serializedObject = new SerializedObject(staticLightingSky);
                skyUniqueID = serializedObject.FindProperty("m_StaticLightingSkyUniqueID");
            }

            public void Apply() => serializedObject.ApplyModifiedProperties();

            public void Update() => serializedObject.Update();

            public bool valid
                => serializedObject != null
                && !serializedObject.Equals(null)
                && serializedObject.targetObject != null
                && !serializedObject.targetObject.Equals(null);
        }

        SerializedStaticLightingSky m_SerializedActiveSceneLightingSky;

        List<GUIContent> m_SkyClassNames = null;
        List<int> m_SkyUniqueIDs = null;

        const string k_ToggleValueKey = "HDRP:LightingWindowEnvironemntSection:Header";
        bool m_ToggleValue = true;
        bool toggleValue
        {
            get => m_ToggleValue;
            set
            {
                m_ToggleValue = value;
                EditorPrefs.SetBool(k_ToggleValueKey, value);
            }
        }

        public override void OnEnable()
        {
            m_SerializedActiveSceneLightingSky = new SerializedStaticLightingSky(GetStaticLightingSkyForScene(EditorSceneManager.GetActiveScene()));

            if (EditorPrefs.HasKey(k_ToggleValueKey))
                m_ToggleValue = EditorPrefs.GetBool(k_ToggleValueKey);

            EditorSceneManager.activeSceneChanged += OnActiveSceneChange;
        }

        public override void OnDisable()
            => EditorSceneManager.activeSceneChanged -= OnActiveSceneChange;

        void OnActiveSceneChange(Scene current, Scene next)
            => m_SerializedActiveSceneLightingSky = new SerializedStaticLightingSky(GetStaticLightingSkyForScene(next));

        StaticLightingSky GetStaticLightingSkyForScene(Scene scene)
        {
            StaticLightingSky result = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                result = go.GetComponent<StaticLightingSky>();
                if (result != null)
                    break;
            }

            //Perhaps it is an old scene. Search everywhere
            if (result == null)
            {
                var candidates = GameObject.FindObjectsOfType<StaticLightingSky>().Where(sls => sls.gameObject.scene == scene);
                if (candidates.Count() > 0)
                    result = candidates.First();
            }

            //Perhaps it not exist yet
            if (result == null)
            {
                var go = new GameObject("StaticLightingSky", new[] { typeof(StaticLightingSky) });
                go.hideFlags = HideFlags.HideInHierarchy;
                result = go.GetComponent<StaticLightingSky>();
            }

            return result;
        }

        public override void OnInspectorGUI()
        {
            WorkarroundWhileActiveSceneChangedHookIsNotCalled();
            m_SerializedActiveSceneLightingSky.Update();

            //Volume can have changed. Check available sky
            UpdateSkyIntPopupData();

            EditorGUI.BeginChangeCheck();
            DrawGUI();
            if (EditorGUI.EndChangeCheck())
            {
                m_SerializedActiveSceneLightingSky.Apply();
                var hdrp = HDRenderPipeline.currentPipeline;
                if (hdrp != null)
                {
                    hdrp.RequestStaticSkyUpdate();
                    SceneView.RepaintAll();
                }
            }
        }

        void DrawGUI()
        {
            Rect mainSeparator = EditorGUILayout.GetControlRect(GUILayout.Height(1));
            mainSeparator.xMin -= 3;
            mainSeparator.xMax += 4;
            EditorGUI.DrawRect(mainSeparator, EditorGUIUtility.isProSkin
                ? new Color32(26, 26, 26, 255)
                : new Color32(127, 127, 127, 255));

            Rect line = EditorGUILayout.GetControlRect();
            line.xMin -= 3;
            line.xMax += 4;
            line.y -= 2;
            line.yMax += 4;

            toggleValue = EditorGUI.Foldout(line, toggleValue, EditorGUIUtility.TrTextContent("Environment (HDRP)", "Sky lighting environment for active Scene"), Styles.headerStyle);

            EditorGUI.DrawRect(line, EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.03f)
                : new Color(1f, 1f, 1f, 0.2f));

            if (m_ToggleValue)
            {
                Rect separator = EditorGUILayout.GetControlRect(GUILayout.Height(1));
                separator.xMin -= 3;
                separator.xMax += 4;
                separator.y -= 1;
                EditorGUI.DrawRect(separator, EditorGUIUtility.isProSkin
                    ? new Color32(48, 48, 48, 255)
                    : new Color32(186, 186, 186, 255));

                ++EditorGUI.indentLevel;

                //cannot use SerializeProperty due to logic in the property
                var profile = m_SerializedActiveSceneLightingSky.volumeProfile;
                var newProfile = EditorGUILayout.ObjectField(EditorGUIUtility.TrTextContent("Profile"), profile, typeof(VolumeProfile), allowSceneObjects: false) as VolumeProfile;
                if (profile != newProfile)
                    m_SerializedActiveSceneLightingSky.volumeProfile = newProfile;

                using (new EditorGUI.DisabledScope(m_SkyClassNames.Count == 1)) // Only "None"
                {
                    EditorGUILayout.IntPopup(m_SerializedActiveSceneLightingSky.skyUniqueID, m_SkyClassNames.ToArray(), m_SkyUniqueIDs.ToArray(), EditorGUIUtility.TrTextContent("Static Lighting Sky", "Specify which kind of sky you want to use for static ambient in the referenced profile for active scene."));
                }

                --EditorGUI.indentLevel;
            }
        }

        void UpdateSkyIntPopupData()
        {
            if (m_SkyClassNames == null)
            {
                m_SkyClassNames = new List<GUIContent>();
                m_SkyUniqueIDs = new List<int>();
            }

            // We always reinit because the content can change depending on the volume and we are not always notified when this happens (like for undo/redo for example)
            m_SkyClassNames.Clear();
            m_SkyUniqueIDs.Clear();

            // Add special "None" case.
            m_SkyClassNames.Add(new GUIContent("None"));
            m_SkyUniqueIDs.Add(0);

            VolumeProfile profile = m_SerializedActiveSceneLightingSky.volumeProfile;
            if (profile != null)
            {
                var skyTypesDict = SkyManager.skyTypesDict;

                foreach (KeyValuePair<int, Type> kvp in skyTypesDict)
                {
                    if (profile.Has(kvp.Value))
                    {
                        m_SkyClassNames.Add(new GUIContent(kvp.Value.Name.ToString()));
                        m_SkyUniqueIDs.Add(kvp.Key);
                    }
                }
            }
        }

        Scene m_ActiveScene;
        void WorkarroundWhileActiveSceneChangedHookIsNotCalled()
        {
            Scene currentActiveScene = EditorSceneManager.GetActiveScene();

            if (m_ActiveScene != currentActiveScene
                || !(m_SerializedActiveSceneLightingSky?.valid ?? false))
            {
                m_SerializedActiveSceneLightingSky = new SerializedStaticLightingSky(GetStaticLightingSkyForScene(currentActiveScene));
                m_ActiveScene = currentActiveScene;
            }
        }
    }
}
