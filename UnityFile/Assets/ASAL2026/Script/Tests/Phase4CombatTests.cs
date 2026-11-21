using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ASAL.Tests
{
    public sealed class Phase4CombatTests
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;

        [Test]
        public void InitiativeOrdersHighestFirstAndBreaksTiesByTokenId()
        {
            var tokens = Deserialize("MatchSnapshot", @"{""Tokens"":[{""Id"":""b"",""Placed"":true,""Hp"":10},{""Id"":""a"",""Placed"":true,""Hp"":10},{""Id"":""c"",""Placed"":true,""Hp"":10}]}");
            var roll = new Queue(new[] { 10, 10, 20 });
            var encounter = Rules("StartEncounter").Invoke(null, new object[]
            {
                Field(tokens, "Tokens"), (Func<int, int>)(_ => (int)roll.Dequeue())
            });
            var order = (IList)Field(encounter, "TurnOrder");
            Assert.That(Field(order[0], "TokenId"), Is.EqualTo("c"));
            Assert.That(Field(order[1], "TokenId"), Is.EqualTo("a"));
            Assert.That(Field(encounter, "ActiveTokenId"), Is.EqualTo("c"));
            Assert.That(Field(order[0], "ActionsLeft"), Is.EqualTo(1));
            Rules("Advance").Invoke(null, new[] { encounter, Field(tokens, "Tokens") });
            Assert.That(Field(encounter, "ActiveTokenId"), Is.EqualTo("a"));
            Assert.That(Field(order[0], "ActionsLeft"), Is.Zero);
        }

        [Test]
        public void TurnAuthorityRequiresActiveTokenAndActualController()
        {
            var encounter = Deserialize("CombatEncounter", @"{""ActiveTokenId"":""hero"",""TurnOrder"":[{""TokenId"":""hero"",""Hp"":10}]}");
            var token = Deserialize("MatchToken", @"{""Id"":""hero"",""OwnerActor"":2,""Placed"":true}");
            var canAct = Rules("CanAct");
            Assert.That(canAct.Invoke(null, new[] { encounter, token, (object)2, false }), Is.True);
            Assert.That(canAct.Invoke(null, new[] { encounter, token, (object)3, false }), Is.False);
            Assert.That(canAct.Invoke(null, new[] { encounter, token, (object)1, true }), Is.False);
            token.GetType().GetField("Id").SetValue(token, "other");
            Assert.That(canAct.Invoke(null, new[] { encounter, token, (object)2, false }), Is.False);
        }

        [Test]
        public void NewMonsterJoinsAnExistingEncounterWithoutChangingTheActiveTurn()
        {
            var snapshot = Deserialize("MatchSnapshot", @"{
                ""Tokens"":[{""Id"":""hero"",""OwnerActor"":2,""Placed"":true,""Hp"":10}],
                ""Encounter"":{""Round"":1,""ActiveTokenId"":""hero"",""TurnOrder"":[
                    {""TokenId"":""hero"",""Hp"":10,""ActionsLeft"":1}]}
            }");
            var command = Deserialize("MatchCommand", @"{""CommandId"":""monster-placement"",""Action"":0}");
            var delta = Activator.CreateInstance(Get("MatchDelta"));
            delta.GetType().GetField("Token").SetValue(delta,
                Deserialize("MatchToken", @"{""Id"":""monster"",""Name"":""Monster"",""Hp"":8,""Placed"":true}"));
            Get("CombatManager").GetMethod("ResolvePlacement", Static).Invoke(null, new[] { snapshot, command, delta });

            Assert.That(Field(delta, "EncounterChanged"), Is.True);
            var encounter = Field(delta, "Encounter");
            Assert.That(Field(encounter, "ActiveTokenId"), Is.EqualTo("hero"));
            var monster = encounter.GetType().GetMethod("Find", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(encounter, new object[] { "monster" });
            Assert.That(Field(monster, "Hp"), Is.EqualTo(8));
            Assert.That(Field(monster, "Initiative"), Is.InRange(1, 20));
        }

        [TestCase(0.75f, true)]
        [TestCase(0.875f, false)]
        public void SkillRangeUsesContinuousManhattanDistance(float targetX, bool inRange)
        {
            var caster = Deserialize("MatchToken", @"{""Position"":{""x"":0.25,""y"":0.25}}");
            var target = Deserialize("MatchToken", "{}");
            target.GetType().GetField("Position").SetValue(target, new Vector2(targetX, 0.75f));
            Assert.That(Rules("InRange").Invoke(null, new[] { caster, target, (object)1 }), Is.EqualTo(inRange));
        }

        [Test]
        public void DungeonMasterCannotCastAsAnActivePlayerToken()
        {
            var snapshot = Deserialize("MatchSnapshot", @"{
                ""Tokens"":[{""Id"":""hero"",""OwnerActor"":2,""Placed"":true,""Hp"":10,
                    ""Character"":{""Skills"":[""Rage""]}}],
                ""Encounter"":{""Round"":1,""ActiveTokenId"":""hero"",""TurnOrder"":[
                    {""TokenId"":""hero"",""Hp"":10,""ActionsLeft"":1,""Statuses"":[]}]}
            }");
            var command = Deserialize("MatchCommand", @"{""Action"":4,""TokenId"":""hero"",""Skill"":""Rage""}");
            var root = new GameObject();
            try
            {
                var manager = root.AddComponent(Get("CombatManager"));
                var resolve = manager.GetType().GetMethod("TryResolve", BindingFlags.Instance | BindingFlags.NonPublic);
                manager.GetType().GetMethod("Initialize", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(manager, null);
                Assert.That(resolve.Invoke(manager, new[] { snapshot, command, (object)1, true, null, null }), Is.False);
                Assert.That(resolve.Invoke(manager, new[] { snapshot, command, (object)2, false, null, null }), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DodgeIsConsumedBeforeDamageAndHealingClampsToMaxHp()
        {
            var target = Deserialize("Combatant", @"{""Hp"":8,""Statuses"":[{""Name"":""Warrior’s sense"",""Turns"":1}]}");
            var change = Rules("ApplyHealth");
            Assert.That(change.Invoke(null, new[] { target, (object)(-4), 10, true }), Is.EqualTo(0));
            Assert.That(((IList)Field(target, "Statuses")).Count, Is.Zero);
            Assert.That(change.Invoke(null, new[] { target, (object)(-4), 10, true }), Is.EqualTo(-4));
            Assert.That(change.Invoke(null, new[] { target, (object)100, 10, false }), Is.EqualTo(6));
            Assert.That(Field(target, "Hp"), Is.EqualTo(10));
        }

        [Test]
        public void MultiShotsConsumesOneActionAndAllowsThreeDifferentTargets()
        {
            var snapshot = Deserialize("MatchSnapshot", @"{""Tokens"": [
                {""Id"":""a"",""OwnerActor"":2,""Placed"":true,""Hp"":10,
                    ""Character"":{""Level"":1,""MaxHp"":10,""Abilities"":[{""Name"":""Dexterity"",""Points"":10}],""Skills"":[""Multi shots""]}},
                {""Id"":""b"",""Placed"":true,""Hp"":10,""Character"":{""MaxHp"":10}},
                {""Id"":""c"",""Placed"":true,""Hp"":10,""Character"":{""MaxHp"":10}},
                {""Id"":""d"",""Placed"":true,""Hp"":10,""Character"":{""MaxHp"":10}},
                {""Id"":""e"",""Placed"":true,""Hp"":10,""Character"":{""MaxHp"":10}}
            ]}");
            var tokens = (IList)Field(snapshot, "Tokens");
            Func<int, int> roll = _ => 1;
            var encounter = Rules("StartEncounter").Invoke(null, new[] { tokens, (object)roll });
            var turns = (IList)Field(encounter, "TurnOrder");
            var caster = turns[0];
            var skill = Resources.Load("Skills/Multi shots", Type.GetType("Skill, Assembly-CSharp", true));
            Assert.That(skill, Is.Not.Null);
            var tryUse = Get("CombatSkills").GetMethod("TryUse", Static);
            var command = Deserialize("MatchCommand", "{}");
            var target = command.GetType().GetField("TargetId");
            var arguments = new[] { tokens, encounter, tokens[0], caster, command, skill, null, roll, null, null, null };

            target.SetValue(command, "b");
            Assert.That(tryUse.Invoke(null, arguments), Is.True);
            Assert.That(Field(caster, "ActionsLeft"), Is.Zero);
            Assert.That(tryUse.Invoke(null, arguments), Is.False);
            target.SetValue(command, "c");
            Assert.That(tryUse.Invoke(null, arguments), Is.True);
            target.SetValue(command, "d");
            Assert.That(tryUse.Invoke(null, arguments), Is.True);
            target.SetValue(command, "e");
            Assert.That(tryUse.Invoke(null, arguments), Is.False);
            Assert.That(Field(turns[4], "Hp"), Is.EqualTo(10));
        }

        [Test]
        public void RageAddsHitDieAndDamageWithoutChangingStrengthChecks()
        {
            var snapshot = Deserialize("MatchSnapshot", @"{""Tokens"":[
                {""Id"":""a"",""Name"":""Warrior"",""OwnerActor"":2,""Placed"":true,""Hp"":10,
                    ""Character"":{""Level"":1,""MaxHp"":10,""Abilities"":[{""Name"":""Strength"",""Points"":16}],""Skills"":[""Slash""]}},
                {""Id"":""b"",""Name"":""Enemy"",""Placed"":true,""Hp"":10,""Character"":{""MaxHp"":10}}
            ]}");
            var tokens = (IList)Field(snapshot, "Tokens");
            Func<int, int> roll = _ => 1;
            var encounter = Rules("StartEncounter").Invoke(null, new[] { tokens, (object)roll });
            var caster = ((IList)Field(encounter, "TurnOrder"))[0];
            Rules("AddStatus").Invoke(null, new[] { caster, (object)"Rage", 2, "a" });
            Assert.That(Rules("Modifier").Invoke(null, new[] { Field(tokens[0], "Character"), caster, "Strength" }), Is.EqualTo(3));

            var tryUse = Get("CombatSkills").GetMethod("TryUse", Static);
            var slash = Resources.Load("Skills/Slash", Type.GetType("Skill, Assembly-CSharp", true));
            var command = Deserialize("MatchCommand", @"{""TargetId"":""b""}");
            var arguments = new[] { tokens, encounter, tokens[0], caster, command, slash, null, roll, null, null, null };
            Assert.That(tryUse.Invoke(null, arguments), Is.True);
            Assert.That(arguments[9], Is.EqualTo("Hit: 5. Damage roll: 7."));
        }

        [Test]
        public void DungeonMasterHealthChangePreservesBoardPositionAndProducesPublicLog()
        {
            var snapshot = Deserialize("MatchSnapshot", @"{""Tokens"":[{""Id"":""hero"",""Name"":""Hero"",""OwnerActor"":2,
                ""Placed"":true,""Position"":{""x"":1.25,""y"":2.5},""Hp"":10,""Character"":{""MaxHp"":10}}]}");
            var command = Deserialize("MatchCommand", @"{""CommandId"":""hp-event"",""Action"":5,""TokenId"":""hero"",""Amount"":-4}");
            var root = new GameObject();
            try
            {
                var manager = root.AddComponent(Get("CombatManager"));
                var resolve = manager.GetType().GetMethod("TryResolve", BindingFlags.Instance | BindingFlags.NonPublic);
                var arguments = new[] { snapshot, command, (object)1, true, null, null };
                Assert.That(resolve.Invoke(manager, arguments), Is.True);
                var changed = Field(arguments[4], "Token");
                Assert.That(Field(changed, "Hp"), Is.EqualTo(6));
                Assert.That(Field(changed, "Position"), Is.EqualTo(new Vector2(1.25f, 2.5f)));
                Assert.That(Field(Field(arguments[4], "Log"), "Id"), Is.EqualTo("hp-event"));
                Assert.That(arguments[5], Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CombatMessageContractCarriesHealthEncounterAndPublicLog()
        {
            var session = Get("MatchSession");
            var update = session.GetNestedType("CombatUpdate", BindingFlags.NonPublic);
            var health = session.GetNestedType("HealthChange", BindingFlags.NonPublic);
            Assert.That(update.GetFields().Select(field => field.Name), Is.EquivalentTo(new[]
                { "Revision", "Health", "EncounterChanged", "Encounter", "Log" }));
            Assert.That(update.GetField("Health").FieldType, Is.EqualTo(health.MakeArrayType()));
            Assert.That(health.GetFields().Select(field => field.Name), Is.EquivalentTo(new[] { "Id", "Hp" }));
        }

        private static MethodInfo Rules(string method) => Get("CombatRules").GetMethod(method, Static);
        private static Type Get(string name) => Type.GetType("ASAL." + name + ", Assembly-CSharp", true);
        private static object Deserialize(string type, string json) => JsonUtility.FromJson(json, Get(type));
        private static object Field(object item, string field) => item.GetType().GetField(field).GetValue(item);
    }
}
