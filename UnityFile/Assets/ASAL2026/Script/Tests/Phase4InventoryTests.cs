using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;

namespace ASAL.Tests
{
    public sealed class Phase4InventoryTests
    {
        private const string Match = "match";
        private const string Item = "1";

        [TestCase(2L, -2, true, 0)]
        [TestCase(2L, -3, false, 0)]
        [TestCase(0L, 1, true, 1)]
        [TestCase(2147483647L, 1, false, 0)]
        [TestCase(1.1, 1, false, 0)]
        [TestCase(2L, 0, false, 0)]
        [TestCase("2", 1, false, 0)]
        public void QuantityChangesAreValidated(object current, int delta, bool expected, int quantity)
        {
            var arguments = new object[] { current, delta, 0 };
            Assert.That(Invoke("TryChangeInventoryQuantity", arguments), Is.EqualTo(expected));
            Assert.That(arguments[2], Is.EqualTo(quantity));
        }

        [Test]
        public void ReceiptReplaysOriginalQuantityAndRejectsConflictingPayloads()
        {
            var receipt = new Dictionary<string, object>
            {
                ["matchId"] = Match, ["itemId"] = Item, ["delta"] = 1L, ["quantity"] = 2L
            };
            var arguments = new object[] { receipt, Match, Item, 1, 0 };
            Assert.That(Invoke("TryReadInventoryReceipt", arguments), Is.True);
            Assert.That(arguments[4], Is.EqualTo(2));

            Assert.That(Invoke("TryReadInventoryReceipt", receipt, Match, Item, -1, 0), Is.False);
            Assert.That(Invoke("TryReadInventoryReceipt", receipt, "another-match", Item, 1, 0), Is.False);
            Assert.That(Invoke("TryReadInventoryReceipt", receipt, Match, "2", 1, 0), Is.False);
        }

        [TestCase("1", true)]
        [TestCase("2", true)]
        [TestCase("_asalInventoryOperations", false)]
        [TestCase("items/sword", false)]
        public void ReceiptKeyAndInvalidDatabasePathsAreReserved(string key, bool valid) =>
            Assert.That(Invoke("IsInventoryItemId", key), Is.EqualTo(valid));

        [Test]
        public void ReceiptMustComeFromOriginalRecipientActorAndAccount()
        {
            var operationType = Type.GetType("ASAL.InventoryOperation, Assembly-CSharp", true);
            var operation = Activator.CreateInstance(operationType, BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { "operation", 2, "account", Match, Item, 1 }, null);
            var matches = operationType.GetMethod("MatchesRecipient", BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(matches.Invoke(operation, new object[] { 2, "account" }), Is.True);
            Assert.That(matches.Invoke(operation, new object[] { 2, "other" }), Is.False);
            Assert.That(matches.Invoke(operation, new object[] { 3, "account" }), Is.False);
        }

        private static object Invoke(string name, params object[] arguments)
        {
            var save = Type.GetType("ASAL.SaveManager, Assembly-CSharp", true);
            var method = save.GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            return method.Invoke(null, arguments);
        }
    }
}
