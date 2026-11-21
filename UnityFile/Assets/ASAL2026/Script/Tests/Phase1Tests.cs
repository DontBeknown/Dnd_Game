using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASAL.Tests
{
    public sealed class Phase1Tests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static readonly string[] Managers =
        {
            "InputManager", "UIManager", "AuthManager", "SaveManager",
            "PhotonManager", "RoomManager", "CharacterManager", "CombatManager",
            "DiceManager", "InventoryManager", "ChatManager", "GameFlowManager"
        };

        // Runtime scripts share Unity's predefined assembly with the generated PlayerInput.
        private static Type RuntimeType(string name) =>
            Type.GetType($"ASAL.{name}, Assembly-CSharp", throwOnError: true);

        [Test]
        public void MainStartsWithBothGameplayRootsAssignedAndInactive()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/ASAL2026/Scene/Main.unity");
            try
            {
                var type = RuntimeType("GameplayInstance");
                var host = Array.Find(scene.GetRootGameObjects(), root => root.GetComponent(type));
                Assert.That(host, Is.Not.Null);
                var instance = new SerializedObject(host.GetComponent(type));
                foreach (var field in new[] { "managerGroup", "uiShell" })
                {
                    var child = instance.FindProperty(field).objectReferenceValue as GameObject;
                    Assert.That(child, Is.Not.Null, field);
                    Assert.That(child.transform.parent, Is.EqualTo(host.transform), field);
                    Assert.That(child.activeSelf, Is.False, field);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [TestCase(0, "Expected exactly one AuthManager", false)]
        [TestCase(2, "Expected exactly one AuthManager", false)]
        [TestCase(1, "UI shell must contain exactly one EventSystem", false)]
        [TestCase(1, "UI shell must contain exactly one EventSystem", true)]
        public void FailedStartupClearsManagerRegistrations(int authCount, string expectedError, bool recoverRoots)
        {
            var host = new GameObject("Composition root");
            var managers = new GameObject("Managers");
            var shell = new GameObject("Shell");
            host.SetActive(false);
            managers.SetActive(recoverRoots);
            shell.SetActive(recoverRoots);
            managers.transform.SetParent(host.transform, false);
            shell.transform.SetParent(host.transform, false);

            try
            {
                shell.AddComponent(RuntimeType("UIShell"));
                foreach (var name in Managers)
                {
                    var count = name == "AuthManager" ? authCount : 1;
                    for (var i = 0; i < count; i++)
                        managers.AddComponent(RuntimeType(name));
                }

                var type = RuntimeType("GameplayInstance");
                var root = host.AddComponent(type);
                if (!recoverRoots)
                {
                    type.GetField("managerGroup", PrivateInstance).SetValue(root, managers);
                    type.GetField("uiShell", PrivateInstance).SetValue(root, shell);
                }

                var error = Assert.Throws<TargetInvocationException>(
                    () => type.GetMethod("Boot", PrivateInstance).Invoke(root, null));
                Assert.That(error.InnerException.Message, Does.Contain(expectedError));
                Assert.That(host.transform.childCount, Is.EqualTo(2));
                Assert.That(managers.activeSelf || shell.activeSelf, Is.False);
                foreach (var name in Managers)
                    Assert.That(RuntimeType(name).GetProperty("Instance").GetValue(null), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void InterruptedTabSwitchRestoresAuthoredVisuals()
        {
            var first = new GameObject("First tab", typeof(RectTransform), typeof(CanvasGroup));
            var second = new GameObject("Second tab", typeof(RectTransform));
            var type = Type.GetType("ASAL.TabTransition, Assembly-CSharp");
            try
            {
                Assert.That(type, Is.Not.Null);
                var transition = Activator.CreateInstance(type, nonPublic: true);
                var show = type.GetMethod("Show", PrivateInstance);
                var finish = type.GetMethod("Finish", PrivateInstance);
                var group = first.GetComponent<CanvasGroup>();
                group.alpha = 0.8f;
                first.transform.localScale = Vector3.one * 0.75f;
                second.SetActive(false);

                show.Invoke(transition, new object[] { first, null, null });
                show.Invoke(transition, new object[] { second, null, null });
                Assert.That(first.activeSelf, Is.True);
                Assert.That(group.interactable, Is.False);
                Assert.That(second.GetComponent<CanvasGroup>().alpha, Is.Zero);
                Assert.That(second.transform.localScale, Is.EqualTo(Vector3.one));

                finish.Invoke(transition, null);
                Assert.That(first.activeSelf, Is.False);
                Assert.That(second.activeSelf, Is.True);
                Assert.That(second.GetComponent<CanvasGroup>().interactable, Is.True);
                Assert.That(first.transform.localScale, Is.EqualTo(Vector3.one * 0.75f));
                Assert.That(group.alpha, Is.EqualTo(0.8f));
                Assert.That(group.interactable, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
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
