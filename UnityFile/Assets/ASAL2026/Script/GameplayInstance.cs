using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASAL
{
    public sealed class GameplayInstance : MonoBehaviour
    {
        [SerializeField] private GameObject managerGroupPrefab;
        [SerializeField] private GameObject uiShellPrefab;

        private readonly Stack<Action> cleanup = new();
        private GameObject managers;
        private GameObject shell;

        private void Awake() => Boot();
        private void OnDestroy() => Shutdown();

        private void Boot()
        {
            try
            {
                RequireInactive(managerGroupPrefab, nameof(managerGroupPrefab));
                RequireInactive(uiShellPrefab, nameof(uiShellPrefab));
                managers = Instantiate(managerGroupPrefab, transform);
                shell = Instantiate(uiShellPrefab, transform);

                var input = RequireManager<InputManager>(managers);
                var ui = RequireManager<UIManager>(managers);
                var auth = RequireManager<AuthManager>(managers);
                var saves = RequireManager<SaveManager>(managers);
                var flow = RequireManager<GameFlowManager>(managers);

                Register(input.Register, input.Unregister);
                Register(ui.Register, ui.Unregister);
                Register(auth.Register, auth.Unregister);
                Register(saves.Register, saves.Unregister);
                Register(flow.Register, flow.Unregister);

                var shellView = UIShell.Resolve(shell);
                Initialize(() => input.Initialize(shellView.InputModule), input.Shutdown);
                Initialize(() => ui.Initialize(shellView.ScreenRoot), ui.Shutdown);
                Initialize(auth.Initialize, auth.Shutdown);
                saves.Initialize();
                managers.SetActive(true);
                shell.SetActive(true);
                Initialize(flow.Begin, flow.Shutdown);
            }
            catch
            {
                Shutdown();
                throw;
            }
        }

        private static void RequireInactive(GameObject prefab, string fieldName)
        {
            if (!prefab || prefab.activeSelf)
                throw new InvalidOperationException($"{fieldName} must reference an inactive prefab root.");
        }

        private static T RequireManager<T>(GameObject root) where T : MonoBehaviour
        {
            var matches = root.GetComponentsInChildren<T>(true);
            if (matches.Length != 1)
                throw new InvalidOperationException($"Expected exactly one {typeof(T).Name}, found {matches.Length}.");

            return matches[0];
        }

        private void Register(Action register, Action unregister)
        {
            register();
            cleanup.Push(unregister);
        }

        private void Initialize(Action initialize, Action shutdown)
        {
            cleanup.Push(shutdown);
            initialize();
        }

        private void Shutdown()
        {
            if (shell)
                shell.SetActive(false);

            while (cleanup.TryPop(out var release))
            {
                try { release(); }
                catch (Exception error) { Debug.LogException(error, this); }
            }

            if (shell)
                Release(shell);
            if (managers)
                Release(managers);

            shell = null;
            managers = null;
        }

        internal static void Release(GameObject instance)
        {
            instance.SetActive(false);
            if (Application.isPlaying)
                Destroy(instance);
            else
                DestroyImmediate(instance);
        }
    }
}
