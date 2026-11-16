using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASAL.Tests
{
    public sealed class Phase1Tests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly string[] Managers =
        {
            "InputManager", "UIManager", "AuthManager", "SaveManager", "GameFlowManager"
        };

        // Runtime scripts share Unity's predefined assembly with the generated PlayerInput.
        private static Type RuntimeType(string name) =>
            Type.GetType($"ASAL.{name}, Assembly-CSharp", throwOnError: true);

        [TestCase(0, "Expected exactly one AuthManager")]
        [TestCase(2, "Expected exactly one AuthManager")]
        [TestCase(1, "UI shell must contain exactly one EventSystem")]
        public void FailedStartupClearsManagerRegistrations(int authCount, string expectedError)
        {
            var host = new GameObject("Composition root");
            var managers = new GameObject("Managers");
            var shell = new GameObject("Shell");
            host.SetActive(false);
            managers.SetActive(false);
            shell.SetActive(false);

            try
            {
                foreach (var name in Managers)
                {
                    var count = name == "AuthManager" ? authCount : 1;
                    for (var i = 0; i < count; i++)
                        managers.AddComponent(RuntimeType(name));
                }

                var type = RuntimeType("GameplayInstance");
                var root = host.AddComponent(type);
                type.GetField("managerGroupPrefab", PrivateInstance).SetValue(root, managers);
                type.GetField("uiShellPrefab", PrivateInstance).SetValue(root, shell);

                var error = Assert.Throws<TargetInvocationException>(
                    () => type.GetMethod("Boot", PrivateInstance).Invoke(root, null));
                Assert.That(error.InnerException.Message, Does.Contain(expectedError));
                Assert.That(host.transform.childCount, Is.Zero);
                foreach (var name in Managers)
                    Assert.That(RuntimeType(name).GetProperty("Instance").GetValue(null), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(managers);
                Object.DestroyImmediate(shell);
            }
        }

        [Test]
        public void SignOutDiscardsAnOlderProfileCompletion()
        {
            var host = new GameObject("Auth");
            try
            {
                var type = RuntimeType("AuthManager");
                var auth = host.AddComponent(type);
                var generation = type.GetField("generation", PrivateInstance).GetValue(auth);
                var profile = Activator.CreateInstance(RuntimeType("AccountProfile"), "uid", "Name");
                var account = type.GetProperty("CurrentAccount");
                var publish = type.GetMethod("PublishAccount", PrivateInstance);

                publish.Invoke(auth, new[] { generation, profile });
                Assert.That(account.GetValue(auth), Is.SameAs(profile));

                type.GetMethod("SignOut").Invoke(auth, null);
                publish.Invoke(auth, new[] { generation, profile });

                Assert.That(account.GetValue(auth), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
