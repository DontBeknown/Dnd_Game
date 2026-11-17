using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace ASAL
{
    public sealed class InputManager : MonoBehaviour
    {
        public static InputManager Instance { get; private set; }

        internal Vector2 EditorPointer => Mouse.current?.position.ReadValue() ?? Vector2.zero;
        internal bool EditorStrokeStarted => Mouse.current?.leftButton.wasPressedThisFrame == true;
        internal bool EditorStrokeEnded => Mouse.current?.leftButton.wasReleasedThisFrame == true;
        internal bool EditorCancelStroke => Mouse.current?.rightButton.wasPressedThisFrame == true;
        internal float EditorZoom => Mathf.Clamp(Mouse.current?.scroll.y.ReadValue() ?? 0, -1, 1);
        internal Vector2 EditorPan
        {
            get
            {
                var keyboard = Keyboard.current;
                if (keyboard == null)
                    return Vector2.zero;
                return new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0)).normalized;
            }
        }

        internal Vector2 BoardPointer => EditorPointer;
        internal bool BoardClick => EditorStrokeStarted;
        internal float BoardZoom => EditorZoom;
        internal Vector2 BoardPan => EditorPan;

        private readonly List<InputActionReference> actionReferences = new();
        private PlayerInput input;
        private InputSystemUIInputModule module;

        internal void Register()
        {
            if (Instance != null)
                throw new InvalidOperationException("Duplicate InputManager.");
            Instance = this;
        }

        internal void Unregister()
        {
            if (ReferenceEquals(Instance, this))
                Instance = null;
        }

        internal void Initialize(InputSystemUIInputModule inputModule)
        {
            input = new PlayerInput();
            module = inputModule;

            var ui = new InputActionMap("UI");
            var point = ui.AddAction("Point", InputActionType.PassThrough, "<Mouse>/position");
            var click = ui.AddAction("Click", InputActionType.PassThrough, "<Mouse>/leftButton");
            var rightClick = ui.AddAction("RightClick", InputActionType.PassThrough, "<Mouse>/rightButton");
            var middleClick = ui.AddAction("MiddleClick", InputActionType.PassThrough, "<Mouse>/middleButton");
            var scroll = ui.AddAction("ScrollWheel", InputActionType.PassThrough, "<Mouse>/scroll");
            var move = ui.AddAction("Navigate", InputActionType.PassThrough, expectedControlLayout: "Vector2");
            move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            var submit = ui.AddAction("Submit", InputActionType.Button, "<Keyboard>/enter");
            submit.AddBinding("<Keyboard>/numpadEnter");
            var cancel = ui.AddAction("Cancel", InputActionType.Button, "<Keyboard>/escape");
            input.asset.AddActionMap(ui);

            module.UnassignActions();
            module.actionsAsset = input.asset;
            module.point = Reference(point);
            module.leftClick = Reference(click);
            module.rightClick = Reference(rightClick);
            module.middleClick = Reference(middleClick);
            module.scrollWheel = Reference(scroll);
            module.move = Reference(move);
            module.submit = Reference(submit);
            module.cancel = Reference(cancel);
        }

        private InputActionReference Reference(InputAction action)
        {
            var reference = InputActionReference.Create(action);
            actionReferences.Add(reference);
            return reference;
        }

        internal void Shutdown()
        {
            if (module)
                module.UnassignActions();

            foreach (var reference in actionReferences)
            {
                if (Application.isPlaying)
                    Destroy(reference);
                else
                    DestroyImmediate(reference);
            }

            actionReferences.Clear();
            if (input != null)
            {
                if (Application.isPlaying)
                    input.Dispose();
                else
                    DestroyImmediate(input.asset);
            }

            input = null;
            module = null;
        }
    }
}
