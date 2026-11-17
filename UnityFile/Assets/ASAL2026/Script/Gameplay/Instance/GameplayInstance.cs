using System;
using System.Collections.Generic;
using UnityEngine;

namespace ASAL
{
    [DefaultExecutionOrder(-1000)]
    public sealed class GameplayInstance : MonoBehaviour
    {
        [SerializeField] private GameObject managerGroup;
        [SerializeField] private GameObject uiShell;

        private readonly Stack<Action> cleanup = new();
        private GameObject managers;
        private GameObject shell;

        private void Awake() => Boot();
        private void OnDestroy() => Shutdown();

        private void Boot()
        {
            try
            {
                managers = ResolveChild(managerGroup, nameof(managerGroup),
                    child => child.GetComponentInChildren<UIManager>(true));
                shell = ResolveChild(uiShell, nameof(uiShell), child => child.GetComponent<UIShell>());
                managers.SetActive(false);
                shell.SetActive(false);

                var input = RequireManager<InputManager>(managers);
                var ui = RequireManager<UIManager>(managers);
                var auth = RequireManager<AuthManager>(managers);
                var saves = RequireManager<SaveManager>(managers);
                var photon = RequireManager<PhotonManager>(managers);
                var rooms = RequireManager<RoomManager>(managers);
                var flow = RequireManager<GameFlowManager>(managers);

                Register(input.Register, input.Unregister);
                Register(ui.Register, ui.Unregister);
                Register(auth.Register, auth.Unregister);
                Register(saves.Register, saves.Unregister);
                Register(photon.Register, photon.Unregister);
                Register(rooms.Register, rooms.Unregister);
                Register(flow.Register, flow.Unregister);

                var shellView = UIShell.Resolve(shell);
                Initialize(() => input.Initialize(shellView.InputModule), input.Shutdown);
                Initialize(() => ui.Initialize(shellView.ScreenRoot), ui.Shutdown);
                Initialize(auth.Initialize, auth.Shutdown);
                saves.Initialize();
                cleanup.Push(photon.Shutdown);
                Initialize(rooms.Initialize, rooms.Shutdown);
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

        private GameObject ResolveChild(GameObject assigned, string fieldName, Func<GameObject, bool> matches)
        {
            if (assigned && assigned.transform.parent == transform && matches(assigned))
                return assigned;

            GameObject candidate = null;
            foreach (Transform child in transform)
            {
                if (!matches(child.gameObject))
                    continue;
                if (candidate)
                    throw new InvalidOperationException($"Assign {fieldName} on GameplayInstance. Multiple direct children match.");
                candidate = child.gameObject;
            }

            if (!candidate)
                throw new InvalidOperationException($"Assign {fieldName} on GameplayInstance. No matching direct child was found.");
            return candidate;
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

            if (managers)
                managers.SetActive(false);

            shell = null;
            managers = null;
        }
    }
}
