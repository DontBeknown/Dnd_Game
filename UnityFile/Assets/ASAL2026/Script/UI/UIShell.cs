using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace ASAL
{
    public sealed class UIShell : MonoBehaviour
    {
        [SerializeField] private RectTransform screenRoot;
        [SerializeField] private InputSystemUIInputModule inputModule;

        internal RectTransform ScreenRoot => screenRoot;
        internal InputSystemUIInputModule InputModule => inputModule;

        internal static UIShell Resolve(GameObject root)
        {
            var eventSystems = root.GetComponentsInChildren<EventSystem>(true);
            if (eventSystems.Length != 1)
                throw new InvalidOperationException("UI shell must contain exactly one EventSystem.");

            var modules = root.GetComponentsInChildren<BaseInputModule>(true);
            var view = root.GetComponent<UIShell>();
            if (!view || !view.screenRoot || !view.screenRoot.IsChildOf(root.transform)
                || modules.Length != 1 || !view.inputModule || modules[0] != view.inputModule
                || view.inputModule.gameObject != eventSystems[0].gameObject)
            {
                throw new InvalidOperationException(
                    "Assign the screen root and the EventSystem's InputSystemUIInputModule on UIShell.");
            }

            return view;
        }
    }
}
