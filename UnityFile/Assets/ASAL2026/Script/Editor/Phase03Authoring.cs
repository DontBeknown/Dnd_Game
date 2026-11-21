using System;
using System.IO;
using ASAL;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class Phase03Authoring
{
    private const string Root = "Assets/ASAL2026/Prefab/";
    private const string Maps = Root + "UI/MapEditor/MapEditor.prefab";
    private const string Lobby = Root + "UI/Lobby/Lobby.prefab";
    private const string Play = Root + "UI/Play/Play.prefab";
    private static Button button;
    private static TMP_FontAsset font;

    [MenuItem("Tools/ASAL/Author Phase 03 Prefabs")]
    public static void Build()
    {
        var maps = AssetDatabase.LoadAssetAtPath<MapEditorPanel>(Maps);
        if (!maps)
            maps = AssetDatabase.LoadAssetAtPath<GameObject>(Maps).GetComponent<MapEditorPanel>();
        button = Get<Button>(maps, "save");
        font = Get<TMP_Text>(maps, "status").font;

        var picker = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "UI/CharacterPicker/SelectCharacter.prefab")
            .GetComponent<CharacterEditorPanel>();
        var play = AssetDatabase.LoadAssetAtPath<GameObject>(Play)?.GetComponent<PlayPanel>();
        if (!play)
            throw new InvalidOperationException("The authored Play prefab is missing its PlayPanel.");
        Modify(Root + "Manager/Manager.prefab", root =>
        {
            if (root.GetComponentInChildren<ASAL.CharacterManager>(true))
                return;
            var manager = new GameObject("CharacterManager", typeof(ASAL.CharacterManager));
            manager.transform.SetParent(root.transform, false);
        });
        Modify(Root + "Manager/UIManager.prefab", root =>
        {
            var ui = root.GetComponent<ASAL.UIManager>();
            Set(ui, "characterPickerPrefab", picker);
            Set(ui, "playPrefab", play);
        });
        Modify(Lobby, root =>
        {
            var panel = root.GetComponent<LobbyPanel>();
            if (Get<Button>(panel, "manageCharacters"))
                return;
            var title = Get<GameObject>(panel, "titleView");
            var control = Button(title.transform, "ManageCharactersButton", "Characters", 36, -142, 205, 62);
            Set(panel, "manageCharacters", control);
        });
        AssetDatabase.SaveAssets();
        Debug.Log("ASAL Phase 03 prefabs authored.");
    }

    [MenuItem("Tools/ASAL/Run ASAL Edit Mode Tests")]
    public static void RunTests()
    {
        var api = ScriptableObject.CreateInstance<UnityEditor.TestTools.TestRunner.Api.TestRunnerApi>();
        api.RegisterCallbacks(new Results(), 0);
        api.Execute(new UnityEditor.TestTools.TestRunner.Api.ExecutionSettings(
            new UnityEditor.TestTools.TestRunner.Api.Filter
            {
                testMode = UnityEditor.TestTools.TestRunner.Api.TestMode.EditMode,
                groupNames = new[] { "ASAL.Tests" }
            }));
    }

    private sealed class Results : UnityEditor.TestTools.TestRunner.Api.ICallbacks
    {
        public void RunStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor tests)
        {
            File.WriteAllText("Library/asal-phase03-tests.txt", "Running");
        }

        public void TestStarted(UnityEditor.TestTools.TestRunner.Api.ITestAdaptor test) { }

        public void TestFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result)
        {
            if (!result.Test.IsSuite)
                File.AppendAllText("Library/asal-phase03-tests.txt",
                    $"\n{result.Test.FullName}: {result.ResultState} {result.Message}");
        }

        public void RunFinished(UnityEditor.TestTools.TestRunner.Api.ITestResultAdaptor result)
        {
            File.AppendAllText("Library/asal-phase03-tests.txt",
                $"\nPassed: {result.PassCount}. Failed: {result.FailCount}. Skipped: {result.SkipCount}.");
        }
    }

    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        Position(rect, parent, name, x, y, w, h);
        return rect;
    }

    private static void Position(RectTransform rect, Transform parent, string name, float x, float y, float w, float h)
    {
        rect.SetParent(parent, false);
        rect.name = name;
        rect.gameObject.layer = 5;
        rect.localScale = Vector3.one;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = new Vector2(w, h);
        rect.anchoredPosition = new Vector2(x, y);
        rect.gameObject.SetActive(true);
    }

    private static TMP_Text Text(Transform parent, string name, string label, float x, float y, float w, float h, float size)
    {
        var text = Rect(parent, name, x, y, w, h).gameObject.AddComponent<TextMeshProUGUI>();
        text.text = label;
        text.font = font;
        text.fontSize = size;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }

    private static Button Button(Transform parent, string name, string label, float x, float y, float w, float h)
    {
        var control = Object.Instantiate(button);
        Position((RectTransform)control.transform, parent, name, x, y, w, h);
        control.onClick = new Button.ButtonClickedEvent();
        var text = control.GetComponentInChildren<TMP_Text>();
        if (!text)
            text = Text(control.transform, "Label", label, 4, -2, w - 8, h - 4, 24);
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 14;
        text.fontSizeMax = 27;
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(5, 2);
        rect.offsetMax = new Vector2(-5, -2);
        return control;
    }

    private static void Modify(string path, Action<GameObject> edit)
    {
        var root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            edit(root);
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    private static T Get<T>(Component owner, string name) where T : Object =>
        (T)new SerializedObject(owner).FindProperty(name).objectReferenceValue;

    private static void Set(Component owner, string name, Object value)
    {
        var data = new SerializedObject(owner);
        data.FindProperty(name).objectReferenceValue = value;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

}
