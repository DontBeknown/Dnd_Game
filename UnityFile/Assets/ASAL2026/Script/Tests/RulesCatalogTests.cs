using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASAL.Tests
{
    public sealed class RulesCatalogTests
    {
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic;

        [Test]
        public void LegacyAliasAndArtworkSurviveRenamingTheDisplayLabel()
        {
            using var fixture = new CatalogFixture();
            var asset = fixture.Asset;
            SetString(fixture.Find("races", "human"), "label", "Renamed Human");
            SetString(fixture.Find("classes", "wizard"), "label", "Renamed Wizard");
            SetString(fixture.Find("abilities", "intelligence"), "label", "Renamed Intelligence");
            SetString(fixture.Find("skills", "fireball"), "label", "Renamed Fireball");
            SetString(fixture.Find("skills", "roar"), "label", "Renamed Roar");
            using var fireball = new SerializedObject(fixture.Find("skills", "fireball"));
            fireball.FindProperty("range").intValue = 7;
            fireball.ApplyModifiedPropertiesWithoutUndo();

            var catalog = Call(asset, "Build");
            var definition = Call(catalog, "LegacyRace", "Human");
            var classDefinition = Call(catalog, "LegacyClass", "Wizard");
            var skillDefinition = Call(catalog, "LegacySkill", "Fireball");
            Assert.That(Property(definition, "Label"), Is.EqualTo("Renamed Human"));
            Assert.That(Property(classDefinition, "Label"), Is.EqualTo("Renamed Wizard"));
            Assert.That(Property(Call(catalog, "LegacyAbility", "Intelligence"), "Label"), Is.EqualTo("Renamed Intelligence"));
            Assert.That(Property(skillDefinition, "Label"), Is.EqualTo("Renamed Fireball"));
            Assert.That(Property(Property(skillDefinition, "Targeting"), "Range"), Is.EqualTo(7));
            Assert.That(Property(skillDefinition, "Icon"), Is.Not.Null);
            Assert.That((IEnumerable)Property(classDefinition, "Skills"), Does.Contain(Property(skillDefinition, "Id")));
            Assert.That(Call(catalog, "LegacyRace", "Renamed Human"), Is.Null);
            var statusIdType = Type.GetType("ASAL.StatusId, Assembly-CSharp", true);
            var statusId = Activator.CreateInstance(statusIdType, Members | BindingFlags.Instance,
                binder: null, args: new object[] { "roar" }, culture: null);
            Assert.That(Property(Call(catalog, "Status", statusId), "Label"), Is.EqualTo("Renamed Roar"));
            var classId = Property(classDefinition, "Id");
            Assert.That(Call(catalog, "Portrait", Property(definition, "Id"), classId), Is.Not.Null);
            Assert.That(Call(catalog, "TokenArt", Property(definition, "Id"), classId), Is.Not.Null);
        }

        [Test]
        public void AuthoredSkillWithExistingEffectCanBeGrantedAndResolved()
        {
            using var fixture = new CatalogFixture();
            var replacement = Object.Instantiate(fixture.Find("skills", "fireball"));
            try
            {
                using var entry = new SerializedObject(replacement);
                entry.FindProperty("id").stringValue = "ember";
                entry.FindProperty("label").stringValue = "Ember";
                entry.FindProperty("legacyName").stringValue = "Ember";
                entry.FindProperty("aliases").arraySize = 0;
                var targetMode = entry.FindProperty("targetMode");
                targetMode.enumValueIndex = Array.IndexOf(targetMode.enumNames, "SelectedEnemy");
                entry.FindProperty("range").intValue = 10;
                entry.FindProperty("requiredRace").objectReferenceValue = null;
                entry.FindProperty("actionCost").intValue = 1;
                entry.FindProperty("maxDistinctTargets").intValue = 1;
                var effects = entry.FindProperty("effects");
                effects.arraySize = 1;
                var damage = effects.GetArrayElementAtIndex(0);
                var kind = damage.FindPropertyRelative("kind");
                kind.enumValueIndex = Array.IndexOf(kind.enumNames, "Damage");
                damage.FindPropertyRelative("ability").objectReferenceValue = null;
                damage.FindPropertyRelative("hitDice").arraySize = 0;
                damage.FindPropertyRelative("criticalRaces").arraySize = 0;
                var dice = damage.FindPropertyRelative("amountDice");
                dice.arraySize = 1;
                var term = dice.GetArrayElementAtIndex(0);
                term.FindPropertyRelative("die").objectReferenceValue = Resources.Load("Dices/D6");
                term.FindPropertyRelative("count").intValue = 1;
                entry.ApplyModifiedPropertiesWithoutUndo();
                AddReference(fixture.Asset, "skills", replacement);
                AddReference(fixture.Find("classes", "wizard"), "grants", replacement);

                var catalog = Call(fixture.Asset, "Build");
                var definition = Call(catalog, "LegacySkill", "Ember");
                Assert.That(Property(Property(definition, "Id"), "Value"), Is.EqualTo("ember"));
                Assert.That(Property(definition, "Effects"), Has.Count.EqualTo(1));
                var wizardSkills = (IEnumerable)Property(Call(catalog, "LegacyClass", "Wizard"), "Skills");
                Assert.That(wizardSkills.Cast<object>().Last(), Is.EqualTo(Property(definition, "Id")));

                const BindingFlags staticMethods = BindingFlags.Static | BindingFlags.NonPublic;
                var snapshot = JsonUtility.FromJson(
                    @"{""Tokens"":[{""Id"":""caster"",""Name"":""Caster"",""OwnerActor"":2,""Placed"":true,""Hp"":20,
                        ""Character"":{""Level"":1,""MaxHp"":20}},{""Id"":""target"",""Placed"":true,""Hp"":20,
                        ""Character"":{""MaxHp"":20}}]}", Type.GetType("ASAL.MatchSnapshot, Assembly-CSharp", true));
                var tokens = (IList)snapshot.GetType().GetField("Tokens").GetValue(snapshot);
                var roll = (Func<int, int>)(_ => 2);
                var rules = Type.GetType("ASAL.CombatRules, Assembly-CSharp", true);
                var encounter = rules.GetMethod("StartEncounter", staticMethods).Invoke(null, new[] { catalog, tokens, roll });
                var turnOrder = (IList)encounter.GetType().GetField("TurnOrder").GetValue(encounter);
                var command = JsonUtility.FromJson(@"{""TargetId"":""target"",""SkillId"":""ember""}",
                    Type.GetType("ASAL.MatchCommand, Assembly-CSharp", true));
                var resolve = Type.GetType("ASAL.CombatSkills, Assembly-CSharp", true).GetMethod("TryUse", staticMethods);
                var result = new[] { catalog, tokens, encounter, tokens[0], turnOrder[0], command, definition, roll, null, null, null };
                Assert.That(resolve.Invoke(null, result), Is.True);
                Assert.That(turnOrder[1].GetType().GetField("Hp").GetValue(turnOrder[1]), Is.EqualTo(18));
                Assert.That(result[8] as string, Does.Contain("Caster used Ember."));
            }
            finally
            {
                Object.DestroyImmediate(replacement);
            }
        }

        [Test]
        public void ShippedActiveSkillsHaveAuthoredEffectsAndTargeting()
        {
            var catalog = Load();
            var fireball = Call(catalog, "LegacySkill", "Fireball");
            Assert.That(Property(fireball, "IsPassive"), Is.False);
            Assert.That(Property(fireball, "Effects"), Has.Count.EqualTo(1));
            Assert.That(Property(Property(fireball, "Targeting"), "Range"), Is.EqualTo(10));

            var passive = Call(catalog, "LegacySkill", "Versatile");
            Assert.That(Property(passive, "IsPassive"), Is.True);
            Assert.That(Property(passive, "Effects"), Is.Empty);
        }

        [TestCase("id", "elf")]
        [TestCase("legacyName", "Elf")]
        public void CatalogRejectsDuplicateRaceIdentity(string field, string value)
        {
            using var fixture = new CatalogFixture();
            SetString(fixture.Find("races", "half-beast"), field, value);

            var error = Assert.Throws<TargetInvocationException>(() => Call(fixture.Asset, "Build"));
            Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
        }

        private static object Load() => Type.GetType("ASAL.RulesCatalog, Assembly-CSharp", true)
            .GetMethod("Load", Members | BindingFlags.Static).Invoke(null, null);

        private static object Call(object instance, string name, params object[] arguments) =>
            instance.GetType().GetMethod(name, Members | BindingFlags.Instance).Invoke(instance, arguments);

        private static object Property(object instance, string name) =>
            instance.GetType().GetProperty(name, Members | BindingFlags.Instance).GetValue(instance);

        private static void SetString(Object asset, string field, string value)
        {
            using var serialized = new SerializedObject(asset);
            serialized.FindProperty(field).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddReference(Object asset, string field, Object reference)
        {
            using var serialized = new SerializedObject(asset);
            var array = serialized.FindProperty(field);
            array.GetArrayElementAtIndex(array.arraySize++).objectReferenceValue = reference;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class CatalogFixture : IDisposable
        {
            private readonly Dictionary<Object, Object> clones = new();

            internal CatalogFixture()
            {
                var catalog = Resources.Load<ScriptableObject>("Rules/MainRules");
                using var catalogProperties = new SerializedObject(catalog);
                foreach (var field in new[] { "races", "classes", "abilities", "skills", "statuses" })
                {
                    var entries = catalogProperties.FindProperty(field);
                    for (var i = 0; i < entries.arraySize; i++)
                    {
                        var asset = entries.GetArrayElementAtIndex(i).objectReferenceValue;
                        clones.Add(asset, Object.Instantiate(asset));
                    }
                }
                Asset = Object.Instantiate(catalog);
                foreach (var asset in clones.Values.Append(Asset))
                {
                    using var serialized = new SerializedObject(asset);
                    var field = serialized.GetIterator();
                    while (field.Next(true))
                        if (field.propertyType == SerializedPropertyType.ObjectReference && field.objectReferenceValue
                            && clones.TryGetValue(field.objectReferenceValue, out var replacement))
                            field.objectReferenceValue = replacement;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            internal Object Asset { get; }

            internal Object Find(string collection, string id)
            {
                using var serialized = new SerializedObject(Asset);
                var entries = serialized.FindProperty(collection);
                return Enumerable.Range(0, entries.arraySize)
                    .Select(index => entries.GetArrayElementAtIndex(index).objectReferenceValue)
                    .Single(asset =>
                    {
                        using var entry = new SerializedObject(asset);
                        return entry.FindProperty("id").stringValue == id;
                    });
            }

            public void Dispose()
            {
                Object.DestroyImmediate(Asset);
                foreach (var clone in clones.Values)
                    Object.DestroyImmediate(clone);
            }
        }
    }
}
