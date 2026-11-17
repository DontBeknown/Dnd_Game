using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASAL.Tests
{
    public sealed class Phase3Tests
    {
        private static MethodInfo Find(string method) =>
            Type.GetType("ASAL.SaveManager, Assembly-CSharp", true)
                .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic);

        [Test]
        public void CharacterPickerBindsBothRolesAndSharedControls()
        {
            const string path = "Assets/ASAL2026/Prefab/UI/CharacterPicker/SelectCharacter.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var panel = prefab.GetComponent(Type.GetType("ASAL.CharacterEditorPanel, Assembly-CSharp", true));
            var serialized = new SerializedObject(panel);
            var layoutReference = serialized.FindProperty("layout");
            Assert.That(layoutReference, Is.Not.Null);
            var layout = layoutReference.objectReferenceValue;
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.GetType().Name, Is.EqualTo("CharacterPickerLayout"));
            Assert.That(((Component)layout).gameObject, Is.EqualTo(prefab));
            var layoutBindings = new SerializedObject(layout);
            foreach (var binding in new[]
            {
                ("records", "CharacterSelectionDropdown"), ("monsterRecords", "EnemySelectionDropdown"),
                ("create", "CreateCharacterButton"), ("createMonster", "CreateEnemyButton"),
                ("save", "EditCharacterButton"), ("saveMonster", "EditEnemyButton"),
                ("delete", "DeleteCharacterButton"), ("deleteMonster", "DeleteEnemyButton"),
                ("confirmSelection", "SelectCharacterButton"), ("back", "BackToRoomButton"),
                ("status", "StatusText"), ("race", "DropdownRace"), ("characterClass", "DropdownClass"),
                ("recordName", "InputName"), ("portrait", "CharacterImage"), ("hpValue", "HP"),
                ("remainingPoints", "AbilityPoint"), ("levelValue", "TextLevel"),
                ("reset", "ButtonResetAbilityPoint"), ("increaseLevel", "IncreaseButton"),
                ("decreaseLevel", "DecreaseButton")
            })
            {
                var property = serialized.FindProperty(binding.Item1) ?? layoutBindings.FindProperty(binding.Item1);
                Assert.That(property, Is.Not.Null, binding.Item1);
                var control = property.objectReferenceValue;
                Assert.That(control, Is.Not.Null, binding.Item1);
                Assert.That(control.name, Is.EqualTo(binding.Item2), binding.Item1);
                if (binding.Item1 is "confirmSelection" or "back")
                {
                    var button = new SerializedObject(control);
                    Assert.That(button.FindProperty("m_OnClick.m_PersistentCalls.m_Calls").arraySize, Is.Zero);
                }
            }

            var abilities = new[] { "Con", "Dex", "Int", "Str" };
            foreach (var (field, prefix, suffix) in new[]
            {
                ("scoreValues", "Text", "Point"), ("modifierValues", "Text", "Mod"),
                ("increaseScores", "Increase", "Button"), ("decreaseScores", "Decrease", "Button")
            })
            {
                var array = layoutBindings.FindProperty(field);
                Assert.That(array, Is.Not.Null, field);
                Assert.That(array.arraySize, Is.EqualTo(abilities.Length), field);
                for (int index = 0; index < abilities.Length; index++)
                {
                    var control = array.GetArrayElementAtIndex(index).objectReferenceValue;
                    Assert.That(control, Is.Not.Null, field);
                    Assert.That(control.name, Is.EqualTo(prefix + abilities[index] + suffix), field);
                }
            }
        }

        [Test]
        public void CharacterEditorBindsTypedForm()
        {
            const string path = "Assets/ASAL2026/Prefab/UI/CharacterEditor/CharacterEditor.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var panel = prefab.GetComponent(Type.GetType("ASAL.CharacterEditorPanel, Assembly-CSharp", true));
            var property = new SerializedObject(panel).FindProperty("layout");
            Assert.That(property, Is.Not.Null);
            var layout = property.objectReferenceValue;
            Assert.That(layout, Is.Not.Null);
            Assert.That(layout.GetType().Name, Is.EqualTo("CharacterEditorLayout"));
            Assert.That(((Component)layout).gameObject, Is.EqualTo(prefab));

            var bindings = new SerializedObject(layout);
            foreach (var field in new[] { "records", "save", "delete", "level", "hp" })
                Assert.That(bindings.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            foreach (var field in new[] { "scores", "abilityLabels" })
            {
                var array = bindings.FindProperty(field);
                Assert.That(array.arraySize, Is.EqualTo(4), field);
                for (int index = 0; index < array.arraySize; index++)
                    Assert.That(array.GetArrayElementAtIndex(index).objectReferenceValue, Is.Not.Null, field);
            }
        }

        [Test]
        public void CharacterAbilityPoolAndDerivedStatsUseLegacyRules()
        {
            var manager = Type.GetType("ASAL.CharacterManager, Assembly-CSharp", true);
            const BindingFlags methods = BindingFlags.Static | BindingFlags.NonPublic;
            var draft = Find("ReadCharacter").Invoke(null, new object[]
            {
                "record", @"{""characterName"":""Aria"",""level"":2,""abilityPoint"":1,""abilityScorePoints"":[{""abilityScoreName"":""Constitution"",""abilityScorePoint"":11,""ablityModifierBonus"":1}]}"
            });
            var score = ((IList)draft.GetType().GetField("abilityScorePoints").GetValue(draft))[0];
            var adjust = manager.GetMethod("TryAdjustAbility", methods);
            var modifier = manager.GetMethod("AbilityModifier", methods);
            var hp = manager.GetMethod("CalculateHp", methods);
            Assert.That(adjust.Invoke(null, new[] { draft, score, (object)1 }), Is.True);
            Assert.That(adjust.Invoke(null, new[] { draft, score, (object)1 }), Is.False);
            Assert.That(modifier.Invoke(null, new[] { score }), Is.EqualTo(2));
            Assert.That(hp.Invoke(null, new[] { draft, (object)8 }), Is.EqualTo(20));
            Assert.That(adjust.Invoke(null, new[] { draft, score, (object)(-1) }), Is.True);
            Assert.That(draft.GetType().GetField("abilityPoint").GetValue(draft), Is.EqualTo(1));
            Assert.That(modifier.Invoke(null, new[] { score }), Is.EqualTo(1));
            Assert.That(hp.Invoke(null, new[] { draft, (object)8 }), Is.EqualTo(18));
        }

        [Test]
        public void RoomDraftReadsLegacyTemplateWithoutMutatingIt()
        {
            var managerType = Type.GetType("ASAL.CharacterManager, Assembly-CSharp", true);
            const BindingFlags methods = BindingFlags.Instance | BindingFlags.NonPublic;
            var root = new GameObject("Character draft test");
            try
            {
                var manager = root.AddComponent(managerType);
                managerType.GetMethod("Initialize", methods).Invoke(manager, null);
                var newDraft = managerType.GetMethod("NewRoomDraft", methods);
                var first = newDraft.Invoke(manager, null);
                var recordType = first.GetType();
                var abilityPoints = recordType.GetField("abilityPoint");
                Assert.That(abilityPoints.GetValue(first), Is.EqualTo(20));
                Assert.That(recordType.GetField("level").GetValue(first), Is.EqualTo(10));
                Assert.That(recordType.GetField("HP").GetValue(first), Is.GreaterThan(0));
                Assert.That(recordType.GetProperty("firebaseKey").GetValue(first), Is.Null);

                var abilityScores = recordType.GetField("abilityScorePoints");
                ((IList)abilityScores.GetValue(first)).Clear();
                abilityPoints.SetValue(first, 0);
                var reset = newDraft.Invoke(manager, null);
                Assert.That(abilityPoints.GetValue(reset), Is.EqualTo(20));
                Assert.That(((IList)abilityScores.GetValue(reset)).Count, Is.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SameDisplayNameKeepsDistinctRecordIds()
        {
            var convert = Find("ReadCharacter");
            Assert.That(convert, Is.Not.Null);
            const string json = "{\"characterName\":\"Aria\"}";
            var first = convert.Invoke(null, new object[] { "first", json });
            var second = convert.Invoke(null, new object[] { "second", json });
            var id = first.GetType().GetProperty("firebaseKey");
            Assert.That(id.GetValue(first), Is.EqualTo("first"));
            Assert.That(id.GetValue(second), Is.EqualTo("second"));
        }

        [Test]
        public void CharacterWriteUsesLegacyFieldsWithoutEmbeddingRecordId()
        {
            var record = Find("ReadCharacter").Invoke(null, new object[]
            {
                "record-id", @"{""characterName"":""Aria"",""HP"":12,""abilityScorePoints"":[{""abilityScoreName"":""Strength"",""abilityScorePoint"":13,""ablityModifierBonus"":2}]}"
            });
            var write = Find("CharacterUpdate");
            Assert.That(write, Is.Not.Null);
            var fields = (IDictionary)write.Invoke(null, new[] { record });
            Assert.That(fields["HP"], Is.EqualTo(12));
            Assert.That(fields.Contains("firebaseKey"), Is.False);
            var ability = (IDictionary)((IList)fields["abilityScorePoints"])[0];
            Assert.That(ability["ablityModifierBonus"], Is.EqualTo(2));
        }

        [Test]
        public void WalkabilityAndMovementRespectOwnershipAndRevision()
        {
            var type = Type.GetType("ASAL.MatchSnapshot, Assembly-CSharp", true);
            var snapshot = JsonUtility.FromJson(@"{
                ""MatchId"":""room"",""Revision"":3,""Roster"":[],
                ""Tokens"":[{""Id"":""player-2"",""OwnerActor"":2,""Placed"":true}],
                ""Map"":{""tilemapDataList"":[{""key"":""Tilemap_Floor"",""tiles"":[{""position"":{""x"":1}},
                  {""position"":{""x"":2}}]},{""key"":""Tilemap_Wall"",""tiles"":[{""position"":{""x"":2}}]}]}}", type);
            var payload = @"{""CommandId"":""12345678901234567890123456789012"",""MatchId"":""room"",
                ""ExpectedRevision"":3,""Action"":1,""TokenId"":""player-2"",""Cell"":{""x"":1}}";
            var command = JsonUtility.FromJson(payload, Type.GetType("ASAL.MatchCommand, Assembly-CSharp", true));
            var apply = type.GetMethod("TryApply", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(apply.Invoke(snapshot, new[] { command, (object)2, false, null }), Is.True);
            Assert.That(apply.Invoke(snapshot, new[] { command, (object)3, false, null }), Is.False);
            Assert.That(apply.Invoke(snapshot, new[] { command, (object)1, true, null }), Is.True);
            command.GetType().GetField("ExpectedRevision").SetValue(command, 2);
            Assert.That(apply.Invoke(snapshot, new[] { command, (object)2, false, null }), Is.False);
            command.GetType().GetField("ExpectedRevision").SetValue(command, 3);
            command.GetType().GetField("Cell").SetValue(command, new Vector3Int(2, 0, 0));
            Assert.That(apply.Invoke(snapshot, new[] { command, (object)2, false, null }), Is.False);
        }

        [Test]
        public void TransferCompletesOnlyAfterAllUniqueChunksArrive()
        {
            var session = Type.GetType("ASAL.MatchSession, Assembly-CSharp", true);
            var type = session.GetNestedType("Transfer", BindingFlags.NonPublic);
            var transfer = Activator.CreateInstance(type, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { 2, 3 }, null);
            var add = type.GetMethod("Add", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(add.Invoke(transfer, new object[] { 2, new byte[] { 2 } }), Is.False);
            Assert.That(add.Invoke(transfer, new object[] { 0, new byte[] { 0 } }), Is.False);
            Assert.That(add.Invoke(transfer, new object[] { 0, new byte[] { 9 } }), Is.False);
            Assert.That(add.Invoke(transfer, new object[] { 1, new byte[] { 1 } }), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void AppliesOnlyAfterCommitAndMatchingSnapshot(bool commitFirst)
        {
            var snapshotType = Type.GetType("ASAL.MatchSnapshot, Assembly-CSharp", true);
            var snapshot = JsonUtility.FromJson(@"{""Revision"":2}", snapshotType);
            var dictionaryType = typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(typeof(string), snapshotType);
            var staged = (IDictionary)Activator.CreateInstance(dictionaryType);
            var session = Type.GetType("ASAL.MatchSession, Assembly-CSharp", true);
            var find = session.GetMethod("FindCommitted", BindingFlags.Static | BindingFlags.NonPublic);
            if (!commitFirst)
                staged["proposal"] = snapshot;
            Assert.That(find.Invoke(null, new object[] { staged, 1, commitFirst ? 2 : 1, "proposal" }), Is.Null);
            staged["proposal"] = snapshot;
            Assert.That(find.Invoke(null, new object[] { staged, 1, 2, "other" }), Is.Null);
            Assert.That(find.Invoke(null, new object[] { staged, 1, 2, "proposal" }), Is.SameAs(snapshot));
            Assert.That(find.Invoke(null, new object[] { staged, 2, 2, "proposal" }), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MapUpdateUsesBothLegacyPaths(bool delete)
        {
            const string fixture = "{\"tilemapDataList\":[{\"key\":\"Tilemap_Floor\",\"tiles\":["
                + "{\"guidForBuildable\":\"Grass\",\"position\":{\"x\":-2,\"y\":3,\"z\":0}}]}]}";
            var parse = Find("ReadMapBody");
            Assert.That(parse, Is.Not.Null);
            var body = parse.Invoke(null, new object[] { fixture });
            var update = (IDictionary)Find("MapUpdate").Invoke(null, new[] { "map-id", delete ? null : "Map", body });
            Assert.That(update.Count, Is.EqualTo(2));
            var data = update["map-id"] as IDictionary;
            if (delete)
            {
                Assert.That(data, Is.Null);
                Assert.That(update["keys/map-id"], Is.Null);
                return;
            }
            var layers = (IList)data["tilemapDataList"];
            var layer = (IDictionary)layers[0];
            var tile = (IDictionary)((IList)layer["tiles"])[0];
            Assert.That(layer["key"], Is.EqualTo("Tilemap_Floor"));
            Assert.That(tile["guidForBuildable"], Is.EqualTo("Grass"));
            Assert.That(((IDictionary)tile["position"])["x"], Is.EqualTo(-2));
            Assert.That(((IDictionary)update["keys/map-id"])["name"], Is.EqualTo("Map"));
        }

        [Test]
        public void InvalidMapDoesNotReplaceExistingDraft()
        {
            const string path = "Assets/ASAL2026/Prefab/UI/MapEditor/MapEditor.prefab";
            var editor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            try
            {
                const BindingFlags methods = BindingFlags.Instance | BindingFlags.NonPublic;
                var type = Type.GetType("ASAL.MapManager, Assembly-CSharp", true);
                var draft = editor.GetComponent(type);
                type.GetMethod("Initialize", methods).Invoke(draft, null);
                var replace = type.GetMethod("Replace", methods);
                var capture = type.GetMethod("Capture", methods);
                const string good = "{\"tilemapDataList\":[{\"key\":\"Tilemap_Floor\",\"tiles\":["
                    + "{\"guidForBuildable\":\"Grass\",\"position\":{\"x\":-2,\"y\":3,\"z\":0}}]}]}";
                var parse = Find("ReadMapBody");
                replace.Invoke(draft, new[] { "original", parse.Invoke(null, new object[] { good }) });
                var before = JsonUtility.ToJson(capture.Invoke(draft, null));
                StringAssert.Contains("\"guidForBuildable\":\"Grass\"", before);

                var bad = parse.Invoke(null, new object[] { good.Replace("Grass", "unknown") });
                var error = Assert.Throws<TargetInvocationException>(() => replace.Invoke(draft, new[] { "bad", bad }));
                Assert.That(error.InnerException, Is.TypeOf<FormatException>());
                Assert.That(JsonUtility.ToJson(capture.Invoke(draft, null)), Is.EqualTo(before));
                Assert.That(type.GetProperty("DraftId", methods).GetValue(draft), Is.EqualTo("original"));
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }
    }
}
