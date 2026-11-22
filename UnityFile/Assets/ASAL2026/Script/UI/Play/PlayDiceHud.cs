using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ASAL.PlayHudControls;

namespace ASAL
{
    public sealed class PlayDiceHud : MonoBehaviour
    {
        [SerializeField] private GameObject dicePanel, diceRollPanel, diceButtonRow, dieRow;
        [SerializeField] private Button diceList;
        [SerializeField] private TMP_Text rollTotal;
        [SerializeField] private Toggle privateRoll;

        private readonly HashSet<string> seenRolls = new(StringComparer.Ordinal);
        private Coroutine rollAnimation;
        private bool hasSnapshot;

        internal bool IsRolling { get; private set; }

        internal void Initialize(PlayHud hud, RoomManager room)
        {
            dicePanel.SetActive(false);
            diceRollPanel.SetActive(false);
            diceButtonRow.SetActive(false);
            dieRow.SetActive(false);
            diceList.onClick.AddListener(() => hud.ToggleModal(dicePanel, diceList));
            foreach (var sides in CharacterManager.Instance.Catalog.DiceSides)
            {
                var row = AddRow(diceButtonRow);
                SetText(row.GetComponentInChildren<TMP_Text>(true), "D" + sides);
                row.GetComponentInChildren<Button>(true).onClick.AddListener(() =>
                {
                    DiceManager.Instance.Roll(new[] { sides }, room.IsDm && privateRoll.isOn);
                    hud.CloseModal();
                });
            }

            DiceManager.Instance.Rolled += AnimateRoll;
        }

        internal void Release()
        {
            if (DiceManager.Instance)
                DiceManager.Instance.Rolled -= AnimateRoll;
        }

        private void OnDisable()
        {
            if (rollAnimation != null)
                StopCoroutine(rollAnimation);
            rollAnimation = null;
            IsRolling = false;
            if (diceRollPanel)
                diceRollPanel.SetActive(false);
        }

        internal void RefreshRole(bool isDm) => privateRoll.gameObject.SetActive(isDm);

        internal void Apply(MatchSnapshot next)
        {
            var initial = !hasSnapshot;
            hasSnapshot = true;
            var log = next.PublicLog ?? Array.Empty<MatchLogEntry>();
            MatchRoll latest = null;
            foreach (var entry in log)
                if (entry.Roll is MatchRoll roll && seenRolls.Add(entry.Id))
                    latest = roll;
            seenRolls.IntersectWith(log.Select(entry => entry.Id));
            if (!initial && latest != null)
                AnimateRoll(latest);
        }

        private void AnimateRoll(MatchRoll roll)
        {
            if (!isActiveAndEnabled || roll?.Faces is not { Length: > 0 } || roll.Sides?.Length != roll.Faces.Length)
                return;
            if (rollAnimation != null)
                StopCoroutine(rollAnimation);
            IsRolling = true;
            rollAnimation = StartCoroutine(ShowRoll(roll));
        }

        private IEnumerator ShowRoll(MatchRoll roll)
        {
            ClearRows(dieRow);
            diceRollPanel.SetActive(true);
            SetText(rollTotal, "");
            var faces = new TMP_Text[roll.Faces.Length];
            var die = (RectTransform)dieRow.transform;
            var content = (RectTransform)die.parent;
            var layout = content.GetComponent<HorizontalLayoutGroup>();
            var width = faces.Length * die.rect.width + (faces.Length - 1) * layout.spacing + Mathf.Max(100, layout.padding.horizontal);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            var available = ((RectTransform)transform).rect.width - 48;
            content.localScale = Vector3.one * Mathf.Clamp(available / width, 0.1f, 1f);
            for (var i = 0; i < faces.Length; i++)
            {
                var row = AddRow(dieRow);
                SetText(Text(row, "DiceNameText"), "D" + roll.Sides[i]);
                faces[i] = Text(row, "Side");
            }

            for (var frame = 0; frame < 6; frame++)
            {
                for (var i = 0; i < faces.Length; i++)
                    SetText(faces[i], (1 + (frame + i) % roll.Sides[i]).ToString());
                yield return new WaitForSecondsRealtime(0.08f);
            }

            for (var i = 0; i < faces.Length; i++)
                SetText(faces[i], roll.Faces[i].ToString());
            SetText(rollTotal, "Total: " + roll.Total);
            IsRolling = false;
            yield return new WaitForSecondsRealtime(1.8f);
            diceRollPanel.SetActive(false);
            rollAnimation = null;
        }
    }
}
