using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ASAL.Tests
{
    public sealed class Phase4CombatTests
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
        private object catalog;

        [OneTimeSetUp]
        public void LoadRules() => catalog = Get("RulesCatalog").GetMethod("Load", Static).Invoke(null, null);

        [Test]
        public void InitiativeOrdersHighestFirstAndBreaksTiesByTokenId()
        {
            var tokens = Deserialize("MatchSnapshot", @"{""Tokens"":[{""Id"":""b"",""Placed"":true,""Hp"":10},{""Id"":""a"",""Placed"":true,""Hp"":10},{""Id"":""c"",""Placed"":true,""Hp"":10}]}");
            var roll = new Queue(new[] { 10, 10, 20 });
            var encounter = Rules("StartEncounter").Invoke(null, new object[]
            {
                catalog, Field(tokens, "Tokens"), (Func<int, int>)(_ => (int)roll.Dequeue())
            });
            var order = (IList)Field(encounter, "TurnOrder");
            Assert.That(Field(order[0], "TokenId"), Is.EqualTo("c"));
            Assert.That(Field(order[1], "TokenId"), Is.EqualTo("a"));
            Assert.That(Field(encounter, "ActiveTokenId"), Is.EqualTo("c"));
            Assert.That(Field(order[0], "ActionsLeft"), Is.EqualTo(1));
            Rules("Advance").Invoke(null, new[] { catalog, encounter, Field(tokens, "Tokens") });
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
            var root = new GameObject();
            try
            {
                var manager = CreateCombatManager(root);
                manager.GetType().GetMethod("ResolvePlacement", Instance).Invoke(manager, new[] { snapshot, command, delta });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

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
                    ""Character"":{""SkillIds"":[""rage""]}}],
                ""Encounter"":{""Round"":1,""ActiveTokenId"":""hero"",""TurnOrder"":[
                    {""TokenId"":""hero"",""Hp"":10,""ActionsLeft"":1,""Statuses"":[]}]}
            }");
            var command = Deserialize("MatchCommand", @"{""Action"":4,""TokenId"":""hero"",""SkillId"":""rage""}");
            var root = new GameObject();
            try
            {
                var manager = CreateCombatManager(root);
                var resolve = manager.GetType().GetMethod("TryResolve", Instance);
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
            var target = Deserialize("Combatant", @"{""Hp"":8,""Statuses"":[{""StatusId"":""dodge"",""Turns"":1}]}");
            var change = Rules("ApplyHealth");
            Assert.That(change.Invoke(null, new[] { catalog, target, (object)(-4), 10, null }), Is.EqualTo(0));
            Assert.That(((IList)Field(target, "Statuses")).Count, Is.Zero);
            Assert.That(change.Invoke(null, new[] { catalog, target, (object)(-4), 10, null }), Is.EqualTo(-4));
            Assert.That(change.Invoke(null, new[] { catalog, target, (object)100, 10, null }), Is.EqualTo(6));
            Assert.That(Field(target, "Hp"), Is.EqualTo(10));
        }

        [Test]
        public void MultiShotsConsumesOneActionAndAllowsThreeDifferentTargets()
        {
            var snapshot = Deserialize("MatchSnapshot", @"{""Tokens"": [
                {""Id"":""a"",""OwnerActor"":2,""Placed"":true,""Hp"":10,
                    ""Character"":{""Level"":1,""MaxHp"":10,""Abilities"":[{""AbilityId"":""dexterity"",""Points"":10}],""SkillIds"":[""multi-shots""]}},
                {""Id"":""b"",""Placed"":true,""Hp"":10,""Character"":{""MaxHp"":10}},
                {""Id"":""c"",""Placed"":true,""Hp"":10,""Character"":{""MaxHp"":10}},
                {""Id"":""d"",""Placed"":true,""Hp"":10,""Character"":{""MaxHp"":10}},
                {""Id"":""e"",""Placed"":true,""Hp"":10,""Character"":{""MaxHp"":10}}
            ]}");
            var tokens = (IList)Field(snapshot, "Tokens");
            Func<int, int> roll = _ => 1;
            var encounter = Rules("StartEncounter").Invoke(null, new[] { catalog, tokens, roll });
            var turns = (IList)Field(encounter, "TurnOrder");
            var caster = turns[0];
            var skill = Definition("Skill", "multi-shots");
            Assert.That(skill, Is.Not.Null);
            var tryUse = Get("CombatSkills").GetMethod("TryUse", Static);
            var command = Deserialize("MatchCommand", "{}");
            var target = command.GetType().GetField("TargetId");
            var arguments = new[] { catalog, tokens, encounter, tokens[0], caster, command, skill, roll, null, null, null };

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
                    ""Character"":{""Level"":1,""MaxHp"":10,""Abilities"":[{""AbilityId"":""strength"",""Points"":16}],""SkillIds"":[""slash""]}},
                {""Id"":""b"",""Name"":""Enemy"",""Placed"":true,""Hp"":10,""Character"":{""MaxHp"":10}}
            ]}");
            var tokens = (IList)Field(snapshot, "Tokens");
            Func<int, int> roll = _ => 1;
            var encounter = Rules("StartEncounter").Invoke(null, new[] { catalog, tokens, roll });
            var caster = ((IList)Field(encounter, "TurnOrder"))[0];
            Rules("AddStatus").Invoke(null, new[] { caster, Definition("Status", "rage"), "a" });
            Assert.That(Rules("Modifier").Invoke(null, new[] { catalog, Field(tokens[0], "Character"), caster,
                CreateId("Ability", "strength") }), Is.EqualTo(3));

            var tryUse = Get("CombatSkills").GetMethod("TryUse", Static);
            var slash = Definition("Skill", "slash");
            var command = Deserialize("MatchCommand", @"{""TargetId"":""b""}");
            var arguments = new[] { catalog, tokens, encounter, tokens[0], caster, command, slash, roll, null, null, null };
            Assert.That(tryUse.Invoke(null, arguments), Is.True);
            Assert.That(arguments[9], Is.EqualTo("Hit: 5. Damage roll: 7."));
        }

        [TestCase("slime", 14, 3)]
        [TestCase("human", 14, 4)]
        [TestCase("elf", 16, 3)]
        public void FireballScalesDamageAndConsumesFocus(string race, int expectedHp, int expectedDice)
        {
            var snapshot = Deserialize("MatchSnapshot", @"{""Tokens"":[
                {""Id"":""a"",""OwnerActor"":2,""Placed"":true,""Hp"":20,
                    ""Character"":{""Level"":5,""MaxHp"":20,""Abilities"":[{""AbilityId"":""intelligence"",""Points"":10}]}},
                {""Id"":""b"",""Placed"":true,""Hp"":20,""Character"":{""MaxHp"":20,""RaceId"":""" + race + @"""}}
            ]}");
            var tokens = (IList)Field(snapshot, "Tokens");
            Func<int, int> roll = _ => 2;
            var encounter = Rules("StartEncounter").Invoke(null, new[] { catalog, tokens, roll });
            var turns = (IList)Field(encounter, "TurnOrder");
            var caster = turns[0];
            Rules("AddStatus").Invoke(null, new[] { caster, Definition("Status", "focus"), "a" });

            var tryUse = Get("CombatSkills").GetMethod("TryUse", Static);
            var skill = Definition("Skill", "fireball");
            var command = Deserialize("MatchCommand", @"{""TargetId"":""b""}");
            var arguments = new[] { catalog, tokens, encounter, tokens[0], caster, command, skill, roll, null, null, null };

            Assert.That(tryUse.Invoke(null, arguments), Is.True);
            Assert.That(Field(turns[1], "Hp"), Is.EqualTo(expectedHp));
            Assert.That(((IList)Field(arguments[10], "Faces")).Count, Is.EqualTo(expectedDice));
            Assert.That(((IList)Field(caster, "Statuses")).Count, Is.Zero);
        }

        [Test]
        public void HealCanRestoreAZeroHealthAllyWithoutConsumingFocus()
        {
            var snapshot = Deserialize("MatchSnapshot", @"{""Tokens"":[
                {""Id"":""a"",""OwnerActor"":2,""Placed"":true,""Hp"":20,
                    ""Character"":{""Level"":3,""MaxHp"":20,""Abilities"":[{""AbilityId"":""intelligence"",""Points"":12}]}},
                {""Id"":""b"",""OwnerActor"":3,""Placed"":true,""Hp"":0,""Character"":{""MaxHp"":20}}
            ]}");
            var tokens = (IList)Field(snapshot, "Tokens");
            Func<int, int> roll = _ => 2;
            var encounter = Rules("StartEncounter").Invoke(null, new[] { catalog, tokens, roll });
            var turns = (IList)Field(encounter, "TurnOrder");
            var caster = turns[0];
            Rules("AddStatus").Invoke(null, new[] { caster, Definition("Status", "focus"), "a" });

            var tryUse = Get("CombatSkills").GetMethod("TryUse", Static);
            var skill = Definition("Skill", "heal");
            var command = Deserialize("MatchCommand", @"{""TargetId"":""b""}");
            var arguments = new[] { catalog, tokens, encounter, tokens[0], caster, command, skill, roll, null, null, null };

            Assert.That(tryUse.Invoke(null, arguments), Is.True);
            Assert.That(Field(turns[1], "Hp"), Is.EqualTo(9));
            Assert.That(((IList)Field(caster, "Statuses")).Count, Is.EqualTo(1));
            Assert.That(arguments[9], Is.EqualTo("Healing roll: 3."));
        }

        [Test]
        public void MixedDiceFormulaScalesAndRollsEachTermInOrder()
        {
            var termType = Get("DiceTerm");
            var terms = Array.CreateInstance(termType, 2);
            terms.SetValue(Activator.CreateInstance(termType, Instance, null, new object[] { 6, 2 }, null), 0);
            terms.SetValue(Activator.CreateInstance(termType, Instance, null, new object[] { 4, 1 }, null), 1);
            var formula = Activator.CreateInstance(Get("DiceFormula"), Instance, null, new object[] { terms }, null);
            var actualSides = new List<int>();
            Func<int, int> roll = sides =>
            {
                actualSides.Add(sides);
                return sides;
            };
            var total = formula.GetType().GetMethod("Roll", Instance).Invoke(formula, new object[] { roll, 1 });

            Assert.That(actualSides, Is.EqualTo(new[] { 6, 6, 6, 4, 4 }));
            Assert.That(total, Is.EqualTo(26));
        }

        [Test]
        public void CircleSlashUsesOneRollForAllEnemiesInRange()
        {
            var snapshot = Deserialize("MatchSnapshot", @"{""Tokens"":[
                {""Id"":""a"",""OwnerActor"":2,""Placed"":true,""Hp"":20,""Character"":{""Level"":1,""MaxHp"":20}},
                {""Id"":""b"",""Placed"":true,""Hp"":20,""Character"":{""MaxHp"":20}},
                {""Id"":""c"",""Placed"":true,""Hp"":20,""Character"":{""MaxHp"":20}},
                {""Id"":""d"",""Placed"":true,""Hp"":20,""Position"":{""x"":2},""Character"":{""MaxHp"":20}}
            ]}");
            var tokens = (IList)Field(snapshot, "Tokens");
            Func<int, int> roll = _ => 1;
            var encounter = Rules("StartEncounter").Invoke(null, new[] { catalog, tokens, roll });
            var turns = (IList)Field(encounter, "TurnOrder");
            var arguments = new[] { catalog, tokens, encounter, tokens[0], turns[0], Deserialize("MatchCommand", "{}"),
                Definition("Skill", "circle-slash"), roll, null, null, null };

            Assert.That(Get("CombatSkills").GetMethod("TryUse", Static).Invoke(null, arguments), Is.True);
            Assert.That(Field(turns[1], "Hp"), Is.EqualTo(19));
            Assert.That(Field(turns[2], "Hp"), Is.EqualTo(19));
            Assert.That(Field(turns[3], "Hp"), Is.EqualTo(20));
            Assert.That(((IList)Field(arguments[10], "Faces")).Count, Is.EqualTo(2));
        }

        [Test]
        public void TauntRejectsAnInvalidTargetWithoutSpendingDiceOrActionAndExpiresOnSourceTurn()
        {
            var snapshot = Deserialize("MatchSnapshot", @"{""Tokens"":[
                {""Id"":""a"",""OwnerActor"":2,""Placed"":true,""Hp"":20,""Character"":{""Level"":1,""MaxHp"":20}},
                {""Id"":""b"",""Placed"":true,""Hp"":20,""Character"":{""MaxHp"":20}},
                {""Id"":""c"",""Placed"":true,""Hp"":20,""Character"":{""MaxHp"":20}}
            ]}");
            var tokens = (IList)Field(snapshot, "Tokens");
            var encounter = Rules("StartEncounter").Invoke(null, new[] { catalog, tokens, (Func<int, int>)(_ => 1) });
            var turns = (IList)Field(encounter, "TurnOrder");
            var caster = turns[0];
            Rules("AddStatus").Invoke(null, new[] { caster, Definition("Status", "roar"), "b" });
            var command = Deserialize("MatchCommand", @"{""TargetId"":""c""}");
            var skill = Definition("Skill", "slash");
            Func<int, int> roll = _ => throw new InvalidOperationException("Invalid actions must not roll.");
            var arguments = new[] { catalog, tokens, encounter, tokens[0], caster, command, skill, roll, null, null, null };

            Assert.That(Get("CombatSkills").GetMethod("TryUse", Static).Invoke(null, arguments), Is.False);
            Assert.That(Field(caster, "ActionsLeft"), Is.EqualTo(1));
            Assert.That(Field(turns[2], "Hp"), Is.EqualTo(20));
            Rules("Advance").Invoke(null, new[] { catalog, encounter, tokens });
            Assert.That(Field(encounter, "ActiveTokenId"), Is.EqualTo("b"));
            Assert.That(((IList)Field(caster, "Statuses")).Count, Is.Zero);
        }

        [Test]
        public void TurnUndeadTargetsOnlyZombiesAndBlocksOneTurn()
        {
            var snapshot = Deserialize("MatchSnapshot", @"{""Tokens"":[
                {""Id"":""a"",""OwnerActor"":2,""Placed"":true,""Hp"":20,""Character"":{""Level"":1}},
                {""Id"":""b"",""Placed"":true,""Hp"":20,""Character"":{""RaceId"":""zombie""}},
                {""Id"":""c"",""Placed"":true,""Hp"":20,""Character"":{""RaceId"":""orc""}}
            ]}");
            var tokens = (IList)Field(snapshot, "Tokens");
            var encounter = Rules("StartEncounter").Invoke(null, new[] { catalog, tokens, (Func<int, int>)(_ => 1) });
            var turns = (IList)Field(encounter, "TurnOrder");
            var skill = Definition("Skill", "turn-undead");
            Func<int, int> roll = _ => throw new InvalidOperationException("Turn Undead must not roll.");
            var arguments = new[] { catalog, tokens, encounter, tokens[0], turns[0], Deserialize("MatchCommand", "{}"),
                skill, roll, null, null, null };

            Assert.That(Get("CombatSkills").GetMethod("TryUse", Static).Invoke(null, arguments), Is.True);
            Assert.That(((IList)Field(turns[1], "Statuses")).Count, Is.EqualTo(1));
            Assert.That(((IList)Field(turns[2], "Statuses")).Count, Is.Zero);
            var advance = Rules("Advance");
            var turn = new[] { catalog, encounter, tokens };
            advance.Invoke(null, turn);
            Assert.That(Field(turns[1], "ActionsLeft"), Is.Zero);
            advance.Invoke(null, turn);
            advance.Invoke(null, turn);
            advance.Invoke(null, turn);
            Assert.That(Field(turns[1], "ActionsLeft"), Is.EqualTo(1));
            Assert.That(((IList)Field(turns[1], "Statuses")).Count, Is.Zero);
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
                var manager = CreateCombatManager(root);
                var resolve = manager.GetType().GetMethod("TryResolve", Instance);
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

        private Component CreateCombatManager(GameObject root)
        {
            var manager = root.AddComponent(Get("CombatManager"));
            manager.GetType().GetMethod("Initialize", Instance).Invoke(manager, new[] { catalog });
            return manager;
        }

        private object Definition(string type, string value) =>
            catalog.GetType().GetMethod(type, Instance).Invoke(catalog, new[] { CreateId(type, value) });

        private static object CreateId(string type, string value) =>
            Activator.CreateInstance(Get(type + "Id"), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new object[] { value }, null);

        private static MethodInfo Rules(string method) => Get("CombatRules").GetMethod(method, Static);
        private static Type Get(string name) => Type.GetType("ASAL." + name + ", Assembly-CSharp", true);
        private static object Deserialize(string type, string json) => JsonUtility.FromJson(json, Get(type));
        private static object Field(object item, string field) => item.GetType().GetField(field).GetValue(item);
    }
}
