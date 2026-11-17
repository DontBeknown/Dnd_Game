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
