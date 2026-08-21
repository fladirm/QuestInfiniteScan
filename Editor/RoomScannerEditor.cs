using System;
using System.Linq;
using Genesis.RoomScan.UI;
using Meta.XR;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Genesis.RoomScan.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="RoomScanner"/> that shows attached modules
    /// and provides an "Add Module" dropdown for optional features.
    /// </summary>
    [CustomEditor(typeof(RoomScanner))]
    public class RoomScannerEditor : UnityEditor.Editor
    {
        static readonly (string label, Type type, Type[] extraDeps)[] ModuleOptions =
        {
            ("Passthrough Camera", typeof(PassthroughCameraProvider),
                new[] { typeof(PassthroughCameraAccess) }),
            ("Input Handler", typeof(RoomScanInputHandler), null),
            ("Debug Overlays", typeof(CameraDebugOverlay),
                new[] { typeof(DepthDebugOverlay) }),
        };

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);

            var scanner = (RoomScanner)target;
            var modules = scanner.GetComponents<IRoomScanModule>();

            if (modules.Length == 0)
            {
                EditorGUILayout.HelpBox("No optional modules attached.", MessageType.Info);
            }
            else
            {
                foreach (var m in modules)
                {
                    if (m is RoomAnchorManager)
                        continue;
                    EditorGUILayout.LabelField($"  \u2022 {m.ModuleName}", EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.Space(4);
            if (EditorGUILayout.DropdownButton(new GUIContent("Add Module\u2026"), FocusType.Keyboard))
                ShowModuleMenu(scanner);
        }

        void ShowModuleMenu(RoomScanner scanner)
        {
            var menu = new GenericMenu();

            foreach (var (label, type, extraDeps) in ModuleOptions)
            {
                bool alreadyAttached = scanner.GetComponent(type) != null;
                if (alreadyAttached)
                {
                    menu.AddDisabledItem(new GUIContent($"{label} (attached)"));
                }
                else
                {
                    menu.AddItem(new GUIContent(label), false, () =>
                    {
                        Undo.RegisterCompleteObjectUndo(scanner.gameObject, $"Add {label}");
                        var added = Undo.AddComponent(scanner.gameObject, type);
                        if (extraDeps != null)
                        {
                            foreach (var dep in extraDeps)
                            {
                                if (scanner.GetComponent(dep) == null)
                                {
                                    var depComp = Undo.AddComponent(scanner.gameObject, dep);
                                    RoomScanSetupWizard.WireComponent(depComp);
                                }
                            }
                        }

                        RoomScanSetupWizard.WireComponent(added);

                        EditorUtility.SetDirty(scanner.gameObject);
                    });
                }
            }

#if HAS_AI_INFERENCE
            var aiDetType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Type.EmptyTypes; } })
                .FirstOrDefault(t => t.Name == "ObjectDetectionModule" && typeof(IRoomScanModule).IsAssignableFrom(t));

            if (aiDetType != null)
            {
                bool hasAI = scanner.GetComponent(aiDetType) != null;
                if (hasAI)
                    menu.AddDisabledItem(new GUIContent("AI Object Detection (attached)"));
                else
                    menu.AddItem(new GUIContent("AI Object Detection"), false, () =>
                    {
                        Undo.RegisterCompleteObjectUndo(scanner.gameObject, "Add AI Object Detection");
                        RoomScanSetupWizard.SetupAIDetectionModule(scanner.gameObject);
                        EditorUtility.SetDirty(scanner.gameObject);
                    });
            }
#endif

            // Debug Menu — lives on a child GameObject with UIDocument
            bool hasDebugMenu = scanner.GetComponentInChildren<DebugMenuController>(true) != null;
            if (hasDebugMenu)
                menu.AddDisabledItem(new GUIContent("Debug Menu (attached)"));
            else
                menu.AddItem(new GUIContent("Debug Menu"), false, () =>
                {
                    Undo.RegisterCompleteObjectUndo(scanner.gameObject, "Add Debug Menu");

                    var debugGo = new GameObject("DebugMenu");
                    debugGo.transform.SetParent(scanner.transform);
                    Undo.RegisterCreatedObjectUndo(debugGo, "Create DebugMenu");

                    Undo.AddComponent<UIDocument>(debugGo);
                    Undo.AddComponent<DebugMenuController>(debugGo);

                    RoomScanSetupWizard.EnsureDebugMenuAssets();
                    RoomScanSetupWizard.EnsureVRInput();

                    EditorUtility.SetDirty(scanner.gameObject);
                });

            menu.ShowAsContext();
        }

    }
}
