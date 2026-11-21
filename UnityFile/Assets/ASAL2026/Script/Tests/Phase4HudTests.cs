using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ASAL.Tests
{
    public sealed class Phase4HudTests
    {
        [Test]
        public void PlayUsesAuthoredHudAndSharedEventSystem()
        {
            var play = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ASAL2026/Prefab/UI/Play/Play.prefab");
            Assert.That(play, Is.Not.Null);
            var playType = Type.GetType("ASAL.PlayPanel, Assembly-CSharp", true);
            var hudProperty = new SerializedObject(play.GetComponent(playType)).FindProperty("hud");
            Assert.That(hudProperty, Is.Not.Null);
            var hud = hudProperty.objectReferenceValue;
            Assert.That(hud, Is.Not.Null);
            Assert.That(play.GetComponentsInChildren<Canvas>(true), Is.Empty);
            Assert.That(play.GetComponentsInChildren<EventSystem>(true), Is.Empty);
            var hierarchy = play.GetComponentsInChildren<Transform>(true);
            var names = hierarchy.Select(item => item.name).ToArray();
            foreach (var name in new[] { "PlayWorld", "GameplayUI", "ChatBoxUI", "EndTurnButton", "InventoryPanel", "DiceRollingUI" })
                Assert.That(names, Does.Contain(name));
            foreach (var item in hierarchy)
                Assert.That(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(item.gameObject), Is.Zero, item.name);

            AssertBindings(hud);
            var root = ((Component)hud).gameObject;
            foreach (var name in new[] { "PlayMapHud", "PlayTurnHud", "PlayCharacterHud", "PlayDiceHud", "PlayInventoryHud", "PlayChatHud" })
            {
                var type = Type.GetType($"ASAL.{name}, Assembly-CSharp");
                Assert.That(type, Is.Not.Null, name);
                var panels = root.GetComponents(type);
                Assert.That(panels, Has.Length.EqualTo(1), name);
                AssertBindings(panels[0]);
            }
        }

        private static void AssertBindings(UnityEngine.Object panel)
        {
            foreach (var binding in panel.GetType().GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                         .Where(field => field.IsDefined(typeof(SerializeField), false)))
            {
                if (binding.GetValue(panel) is UnityEngine.Object[] components)
                {
                    Assert.That(components, Has.Length.EqualTo(4), binding.Name);
                    Assert.That(components.All(component => component), Is.True, binding.Name);
                }
                else
                    Assert.That(binding.GetValue(panel) is UnityEngine.Object reference && reference, Is.True, binding.Name);
            }
        }

        [Test]
        public void PlayUsesAuthoredBoardTokenAtBoardFootprintSize()
        {
            var play = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ASAL2026/Prefab/UI/Play/Play.prefab");
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ASAL2026/Prefab/Player/Player.prefab");
            var panelType = Type.GetType("ASAL.PlayPanel, Assembly-CSharp", true);
            var viewType = Type.GetType("ASAL.TokenView, Assembly-CSharp", true);
            var tokenView = player.GetComponent(viewType);
            var bindings = new SerializedObject(play.GetComponent(panelType));
            Assert.That(tokenView, Is.Not.Null);
            Assert.That(bindings.FindProperty("tokenPrefab").objectReferenceValue, Is.EqualTo(tokenView));
            Assert.That(play.GetComponentsInChildren<Transform>(true).All(item => item.name != "TokenChoices"), Is.True);
            AssertBindings(tokenView);

            var photonView = player.GetComponents<Component>()
                .SingleOrDefault(item => item && item.GetType().FullName == "Photon.Pun.PhotonView");
            Assert.That(photonView, Is.Not.Null);
            var network = new SerializedObject(photonView);
            Assert.That(network.FindProperty("OwnershipTransfer").enumValueIndex, Is.Zero);
            Assert.That(network.FindProperty("Synchronization").enumValueIndex, Is.Not.Zero);
            var observed = network.FindProperty("ObservedComponents");
            Assert.That(observed.arraySize, Is.EqualTo(1));
            Assert.That(observed.GetArrayElementAtIndex(0).objectReferenceValue, Is.EqualTo(tokenView));

            var grid = (Grid)bindings.FindProperty("grid").objectReferenceValue;
            float stride = (grid.GetCellCenterLocal(Vector3Int.right) - grid.GetCellCenterLocal(Vector3Int.zero)).magnitude;
            var collider = player.GetComponent<CircleCollider2D>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.radius, Is.GreaterThan(0).And.LessThanOrEqualTo(stride * 0.5f));
            var outline = (SpriteRenderer)new SerializedObject(tokenView).FindProperty("outline").objectReferenceValue;
            Assert.That(outline.sprite.bounds.size.x * outline.transform.localScale.x,
                Is.EqualTo(collider.radius * 2).Within(0.001f));
        }

        [Test]
        public void BoardPlacementUsesWallCollidersAndAllowsAnEmptyBoard()
        {
            const BindingFlags methods = BindingFlags.Instance | BindingFlags.NonPublic;
            var play = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ASAL2026/Prefab/UI/Play/Play.prefab");
            var instance = UnityEngine.Object.Instantiate(play);
            try
            {
                var playType = Type.GetType("ASAL.PlayPanel, Assembly-CSharp", true);
                var panel = instance.GetComponent(playType);
                var prepare = playType.GetMethod("PrepareBoard", methods);
                var isOpen = playType.GetMethod("IsOpen", methods);
                var position = new Vector2(2, 0);
                prepare.Invoke(panel, new object[] { null, 0 });
                Assert.That(isOpen.Invoke(panel, new object[] { position, null }), Is.True);

                prepare.Invoke(panel, new[] { SingleWall(prepare), (object)1 });
                Assert.That(isOpen.Invoke(panel, new object[] { position, null }), Is.False);
                Assert.That(isOpen.Invoke(panel, new object[] { Vector2.zero, null }), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void MovingTokenStopsBeforeARealWallAndUpdatesItsPosition()
        {
            const BindingFlags methods = BindingFlags.Instance | BindingFlags.NonPublic;
            var board = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ASAL2026/Prefab/UI/Play/Play.prefab"));
            var player = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ASAL2026/Prefab/Player/Player.prefab"));
            var network = new GameObject("Board test token registry");
            network.SetActive(false);
            var photonType = Type.GetType("ASAL.PhotonManager, Assembly-CSharp", true);
            var photon = network.AddComponent(photonType);
            try
            {
                photonType.GetMethod("Register", methods).Invoke(photon, null);
                var playType = Type.GetType("ASAL.PlayPanel, Assembly-CSharp", true);
                var panel = board.GetComponent(playType);
                var prepare = playType.GetMethod("PrepareBoard", methods);
                prepare.Invoke(panel, new[] { SingleWall(prepare), (object)1 });

                var viewType = Type.GetType("ASAL.TokenView, Assembly-CSharp", true);
                var view = player.GetComponent(viewType);
                var bind = viewType.GetMethod("Bind", methods);
                var token = JsonUtility.FromJson(@"{""Id"":""player-2"",""OwnerActor"":2,""Name"":""Hero""}",
                    bind.GetParameters()[0].ParameterType);
                var origin = playType.GetMethod("ToWorld", methods).Invoke(panel, new object[] { Vector2.zero });
                bind.Invoke(view, new[] { token, origin });
                Assert.That(photonType.GetMethod("RegisterToken", methods).Invoke(photon, new[] { view }), Is.True);

                var move = playType.GetMethod("TryMoveToken", methods);
                var blocked = new object[] { "player-2", new Vector2(4, 0), null };
                Assert.That(move.Invoke(panel, blocked), Is.True);
                Assert.That(((Vector2)blocked[2]).x, Is.GreaterThan(0).And.LessThan(1.5f));
                Assert.That(viewType.GetProperty("BoardPosition", methods).GetValue(view), Is.EqualTo(blocked[2]));

                var slide = new object[] { "player-2", new Vector2(4, 0.6f), null };
                Assert.That(move.Invoke(panel, slide), Is.True);
                Assert.That(((Vector2)slide[2]).y, Is.EqualTo(0.6f).Within(0.01f));
                Assert.That(((Vector2)slide[2]).x, Is.LessThan(1.5f));

                var clear = new object[] { "player-2", new Vector2(0, 2), null };
                Assert.That(move.Invoke(panel, clear), Is.True);
                Assert.That(clear[2], Is.EqualTo(clear[1]));
            }
            finally
            {
                photonType.GetMethod("Unregister", methods).Invoke(photon, null);
                UnityEngine.Object.DestroyImmediate(network);
                UnityEngine.Object.DestroyImmediate(player);
                UnityEngine.Object.DestroyImmediate(board);
            }
        }

        private static object SingleWall(MethodInfo prepare) =>
            JsonUtility.FromJson(@"{""tilemapDataList"":[{""key"":""Tilemap_Wall"",""tiles"": [
                {""guidForBuildable"":""StoneWall"",""position"":{""x"":2,""y"":0}}]}]}",
                prepare.GetParameters()[0].ParameterType);

        [Test]
        public void RemotePositionSettlesOnTheLastPhotonSample()
        {
            const BindingFlags methods = BindingFlags.Instance | BindingFlags.NonPublic;
            var player = UnityEngine.Object.Instantiate(
                AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ASAL2026/Prefab/Player/Player.prefab"));
            try
            {
                var viewType = Type.GetType("ASAL.TokenView, Assembly-CSharp", true);
                var view = player.GetComponent(viewType);
                var bind = viewType.GetMethod("Bind", methods);
                var token = JsonUtility.FromJson(@"{""Id"":""player-2"",""OwnerActor"":2,""Name"":""Hero""}",
                    bind.GetParameters()[0].ParameterType);
                bind.Invoke(view, new[] { token, (object)Vector3.zero });

                var target = new Vector3(2, 1, 0);
                var boardPosition = new Vector2(1, 2);
                viewType.GetMethod("SetReplicatedPosition", methods).Invoke(view, new object[] { target, boardPosition });
                Assert.That(player.transform.position, Is.EqualTo(Vector3.zero));
                Assert.That(viewType.GetProperty("BoardPosition", methods).GetValue(view), Is.EqualTo(boardPosition));

                float period = (float)viewType.GetField("interpolationRemaining", methods).GetValue(view);
                var advance = viewType.GetMethod("AdvanceRemotePosition", methods);
                advance.Invoke(view, new object[] { period / 2 });
                Assert.That(player.transform.position, Is.EqualTo(target * 0.5f));
                advance.Invoke(view, new object[] { period });
                Assert.That(player.transform.position, Is.EqualTo(target));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void TokenPreviewIsTranslucentAndHasNoActiveCollider()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ASAL2026/Prefab/Player/Player.prefab");
            var ghost = UnityEngine.Object.Instantiate(player);
            try
            {
                var viewType = Type.GetType("ASAL.TokenView, Assembly-CSharp", true);
                var view = ghost.GetComponent(viewType);
                var setPreview = viewType.GetMethod("SetPreview", BindingFlags.NonPublic | BindingFlags.Instance);
                var collider = ghost.GetComponent<CircleCollider2D>();
                var marker = (SpriteRenderer)new SerializedObject(view).FindProperty("marker").objectReferenceValue;

                setPreview.Invoke(view, new object[] { true });
                Assert.That(collider.enabled, Is.False);
                Assert.That(marker.color.a, Is.LessThan(1f));
                setPreview.Invoke(view, new object[] { false });
                Assert.That(collider.enabled, Is.True);
                Assert.That(marker.color.a, Is.EqualTo(1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(ghost);
            }
        }

        [Test]
        public void ManagerPrefabWiresEachPhase4OwnerOnce()
        {
            var managers = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ASAL2026/Prefab/Manager/Manager.prefab");
            Assert.That(managers.activeSelf, Is.False);
            foreach (var name in new[] { "CombatManager", "DiceManager", "InventoryManager", "ChatManager" })
            {
                var type = Type.GetType($"ASAL.{name}, Assembly-CSharp", true);
                Assert.That(managers.GetComponentsInChildren(type, true), Has.Length.EqualTo(1), name);
            }
        }

        [TestCase(2, 2, false, true, true)]
        [TestCase(3, 2, false, false, false)]
        [TestCase(0, 2, false, false, false)]
        [TestCase(3, 1, true, true, false)]
        [TestCase(0, 1, true, true, true)]
        public void BoardSelectionAndSkillControlsRespectOwnership(int owner, int actor, bool dm, bool selects, bool casts)
        {
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var play = Type.GetType("ASAL.PlayPanel, Assembly-CSharp", true);
            var characterHud = Type.GetType("ASAL.PlayCharacterHud, Assembly-CSharp", true);
            var arguments = new object[] { owner, actor, dm };

            Assert.That(play.GetMethod("CanSelectToken", flags)?.Invoke(null, arguments), Is.EqualTo(selects));
            Assert.That(characterHud.GetMethod("CanCastTokenSkill", flags)?.Invoke(null, arguments), Is.EqualTo(casts));
        }

        [TestCase("  <b>Hello</b>  ", "<b>Hello</b>")]
        [TestCase("   ", null)]
        public void ChatPreservesLiteralUserText(string input, string expected)
        {
            var chat = Type.GetType("ASAL.ChatManager, Assembly-CSharp", true);
            var normalize = chat.GetMethod("Normalize", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(normalize.Invoke(null, new object[] { input }), Is.EqualTo(expected));
        }
    }
}
