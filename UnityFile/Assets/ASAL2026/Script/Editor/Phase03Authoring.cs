using System;
using System.IO;
using System.Linq;
using ASAL;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class Phase03Authoring
{
    private const string Root = "Assets/ASAL2026/Prefab/";
    private const string Maps = Root + "UI/MapEditor/MapEditor.prefab";
    private const string Lobby = Root + "UI/Lobby/Lobby.prefab";
    private const string Play = Root + "UI/Play/";
    private const string Records = Root + "UI/CharacterEditor/CharacterEditor.prefab";
    private static Button button;
    private static TMP_Dropdown dropdown;
    private static TMP_FontAsset font;

    [MenuItem("Tools/ASAL/Author Phase 03 Prefabs")]
    public static void Build()
    {
        var maps = AssetDatabase.LoadAssetAtPath<MapEditorPanel>(Maps);
        if (!maps)
            maps = AssetDatabase.LoadAssetAtPath<GameObject>(Maps).GetComponent<MapEditorPanel>();
        button = Get<Button>(maps, "save");
        dropdown = Get<TMP_Dropdown>(maps, "maps");
        font = Get<TMP_Text>(maps, "status").font;
        Directory.CreateDirectory(Play);
        Directory.CreateDirectory(Path.GetDirectoryName(Records));
        AssetDatabase.Refresh();

        var picker = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "UI/CharacterPicker/SelectCharacter.prefab")
            .GetComponent<CharacterEditorPanel>();
        var play = BuildPlay();
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

    private static PlayPanel BuildPlay()
    {
        foreach (var leftover in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
            if (leftover.name == "Token" && leftover.TryGetComponent<TokenView>(out _))
                Object.DestroyImmediate(leftover);
        var token = new GameObject("Token", typeof(SpriteRenderer));
        var view = token.AddComponent<TokenView>();
        var renderer = token.GetComponent<SpriteRenderer>();
        renderer.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
        if (!renderer.sprite)
            renderer.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/CharacterImages/Human/Human_Warrior_Circle.png");
        if (!renderer.sprite)
            throw new InvalidOperationException("Missing built-in token marker sprite.");
        renderer.drawMode = SpriteDrawMode.Sliced;
        renderer.size = Vector2.one * 0.72f;
        renderer.sortingOrder = 100;
        var text = new GameObject("Name", typeof(RectTransform), typeof(TextMeshPro)).GetComponent<TextMeshPro>();
        text.transform.SetParent(token.transform, false);
        text.transform.localPosition = new Vector3(0, 0.5f, 0);
        text.rectTransform.sizeDelta = new Vector2(4, 0.6f);
        text.font = font;
        text.fontSize = 2.6f;
        text.richText = false;
        text.alignment = TextAlignmentOptions.Center;
        text.GetComponent<MeshRenderer>().sortingOrder = 101;
        Set(view, "marker", renderer);
        Set(view, "label", text);
        token.SetActive(false);
        var tokenAsset = PrefabUtility.SaveAsPrefabAsset(token, Play + "Token.prefab").GetComponent<TokenView>();
        Object.DestroyImmediate(token);

        var legacy = EditorSceneManager.OpenScene("Assets/Scenes/GameSetUp.unity", UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            var root = Screen("Play");
            var panel = root.gameObject.AddComponent<PlayPanel>();
            var cameraSource = legacy.GetRootGameObjects().SelectMany(rootObject => rootObject.GetComponentsInChildren<Camera>(true))
                .First(item => item.name == "Main Camera");
            var world = new GameObject("PlayWorld").transform;
            world.SetParent(root, false);
            var camera = new GameObject("PlayCamera", typeof(Camera)).GetComponent<Camera>();
            camera.transform.SetParent(world, false);
            EditorUtility.CopySerialized(cameraSource, camera);
            camera.transform.localPosition = new Vector3(0, 0, -10);
            camera.transform.localRotation = Quaternion.identity;
            camera.orthographic = true;
            camera.orthographicSize = 9;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.13f);
            camera.depth = 10;
            var gridSource = legacy.GetRootGameObjects().SelectMany(rootObject => rootObject.GetComponentsInChildren<Grid>(true)).First();
            var grid = new GameObject("Grid", typeof(Grid)).GetComponent<Grid>();
            grid.transform.SetParent(world, false);
            EditorUtility.CopySerialized(gridSource, grid);
            var mapRoot = new GameObject("MapRoot").transform;
            mapRoot.SetParent(grid.transform, false);
            var tokenRoot = new GameObject("TokenRoot").transform;
            tokenRoot.SetParent(grid.transform, false);

            Set(panel, "playCamera", camera);
            Set(panel, "world", world);
            Set(panel, "grid", grid);
            Set(panel, "mapRoot", mapRoot);
            Set(panel, "tokenRoot", tokenRoot);
            Set(panel, "tokenPrefab", tokenAsset);

            var toolbar = Rect(root, "PlayToolbar", 24, -24, 990, 124);
            toolbar.gameObject.AddComponent<Image>().color = new Color(0.09f, 0.11f, 0.16f, 0.96f);
            Set(panel, "mapEditor", Button(toolbar, "MapMakerButton", "MapMaker", 16, -15, 147, 44));
            Set(panel, "enemies", Button(toolbar, "EnemyTemplatesButton", "Enemy templates", 174, -15, 188, 44));
            Set(panel, "leave", Button(toolbar, "LeaveButton", "Leave", 848, -15, 126, 44));
            Set(panel, "status", Text(toolbar, "StatusText", "", 16, -68, 958, 44, 21));
            Set(panel, "tokenChoices", Dropdown(toolbar, "TokenChoices", 375, -15, 455));
            return Save(root, Play + "Play.prefab").GetComponent<PlayPanel>();
        }
        finally
        {
            EditorSceneManager.CloseScene(legacy, true);
        }
    }

    private static RectTransform Screen(string name)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup)).GetComponent<RectTransform>();
        root.gameObject.layer = 5;
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.offsetMin = root.offsetMax = Vector2.zero;
        return root;
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

    private static TMP_Dropdown Dropdown(Transform parent, string name, float x, float y, float w)
    {
        var control = Object.Instantiate(dropdown);
        Position((RectTransform)control.transform, parent, name, x, y, w, 48);
        control.onValueChanged = new TMP_Dropdown.DropdownEvent();
        control.ClearOptions();
        return control;
    }

    private static GameObject Save(RectTransform root, string path)
    {
        root.gameObject.SetActive(false);
        var saved = PrefabUtility.SaveAsPrefabAsset(root.gameObject, path);
        Object.DestroyImmediate(root.gameObject);
        return saved;
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
