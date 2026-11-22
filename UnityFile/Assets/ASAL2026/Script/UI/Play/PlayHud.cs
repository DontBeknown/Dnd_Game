using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class PlayHud : MonoBehaviour
    {
        [SerializeField] private Button exit;
        [SerializeField] private PlayMapHud maps;
        [SerializeField] private PlayTurnHud turns;
        [SerializeField] private PlayCharacterHud characters;
        [SerializeField] private PlayDiceHud dice;
        [SerializeField] private PlayInventoryHud inventory;
        [SerializeField] private PlayChatHud chat;

        private RoomManager room;
        private PlayPanel play;
        private GameObject modal;
        private Button returnFocus;
        private Action onModalClosed;
        private bool initialized;

        internal bool BlocksBoard => modal && !IsTargeting;
        internal bool IsTargeting => characters.IsTargeting;
        internal bool IsDiceRolling => dice.IsRolling;

        internal void Initialize(PlayPanel panel, RoomManager rooms)
        {
            play = panel;
            room = rooms;
            initialized = true;
            foreach (var text in GetComponentsInChildren<TMP_Text>(true))
                text.richText = false;

            exit.onClick.AddListener(room.Leave);
            maps.Initialize(this, play, room);
            turns.Initialize(room);
            characters.Initialize(this, play, room);
            dice.Initialize(this, room);
            inventory.Initialize(this);
            chat.Initialize(this);
            RefreshRoom();
        }

        internal void Release()
        {
            if (!initialized)
                return;

            initialized = false;
            chat.Release();
            inventory.Release();
            dice.Release();
        }

        private void OnDestroy() => Release();

        internal void RefreshRoom()
        {
            var isDm = room.IsDm;
            exit.interactable = room.CanLeave;
            maps.RefreshRole(isDm);
            turns.RefreshRole(isDm);
            dice.RefreshRole(isDm);
            inventory.RefreshRoom(room, isDm);
        }

        internal void Apply(MatchSnapshot next)
        {
            turns.Apply(next);
            characters.Apply(next);
            dice.Apply(next);
        }

        internal void SetMaps(IEnumerable<string> names, int selection, bool busy) => maps.SetMaps(names, selection, busy);
        internal void SetEnemies(IEnumerable<CharacterDraft> templates) => maps.SetEnemies(templates);
        internal bool Inspect(MatchToken token) => characters.Inspect(token);
        internal void AcceptTarget(MatchToken target) => characters.AcceptTarget(target);
        internal void CancelTarget() => characters.CancelTarget();

        internal void ToggleModal(GameObject target, Button focus, Action onOpen = null, bool selectFirstControl = true)
        {
            if (modal == target)
            {
                CloseModal();
                return;
            }

            OpenModal(target, focus);
            onOpen?.Invoke();
            if (selectFirstControl)
                target.GetComponentsInChildren<Selectable>().FirstOrDefault(control => control.IsInteractable())?.Select();
        }

        internal void OpenModal(GameObject target, Button focus, Action onClose = null)
        {
            CloseModal();
            modal = target;
            returnFocus = focus;
            onModalClosed = onClose;
            modal.SetActive(true);
        }

        internal void CloseModal()
        {
            if (modal)
                modal.SetActive(false);

            var onClose = onModalClosed;
            modal = null;
            onModalClosed = null;
            onClose?.Invoke();

            if (returnFocus && returnFocus.isActiveAndEnabled)
                returnFocus.Select();
            returnFocus = null;
            if (play)
                play.ShowNotice(null);
        }

        internal void Dismiss()
        {
            if (!characters.TryCloseSkill())
                CloseModal();
        }
    }
}
