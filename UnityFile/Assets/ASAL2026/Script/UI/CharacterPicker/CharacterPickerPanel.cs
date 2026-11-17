using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class CharacterPickerPanel : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown choices;
        [SerializeField] private TMP_Text preview;
        [SerializeField] private TMP_Text status;
        [SerializeField] private RawImage portrait;
        [SerializeField] private Button confirm;
        [SerializeField] private Button back;

        private CharacterData[] records = Array.Empty<CharacterData>();

        internal async void Open(AccountProfile account, string selectedId, Action<SelectedCharacter> select, Action close)
        {
            var defaultPortrait = portrait.texture;
            choices.ClearOptions();
            choices.interactable = false;
            confirm.interactable = false;
            back.onClick.AddListener(() => close());
            back.Select();
            preview.text = string.Empty;
            status.text = "Loading saved characters…";

            try
            {
                records = await SaveManager.Instance.LoadCharactersAsync(account.Uid);
            }
            catch (Exception)
            {
                if (Current(account))
                    status.text = "Could not load saved characters. Go back and try again.";
                return;
            }

            if (!Current(account))
                return;
            if (records.Length == 0)
            {
                status.text = "This account has no saved characters.";
                return;
            }

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            var labels = records.Select(record => CountedLabel(record.characterName, counts)).ToList();
            var selectedIndex = Array.FindIndex(records, record => record.firebaseKey == selectedId);
            choices.AddOptions(labels);
            choices.SetValueWithoutNotify(Math.Max(0, selectedIndex));
            choices.RefreshShownValue();
            choices.interactable = true;
            confirm.interactable = true;
            status.text = string.Empty;
            choices.onValueChanged.AddListener(ShowPreview);
            confirm.onClick.AddListener(ConfirmSelection);
            ShowPreview(choices.value);
            choices.Select();

            void ShowPreview(int index)
            {
                var record = records[index];
                preview.text = $"{record.characterName}\n{record.raceName}  {record.characterClassName}\nLevel {record.level}  •  HP {record.HP}";
                var texture = Resources.Load<Texture>(
                    $"CharacterImages/{record.raceName}/{record.raceName}_{record.characterClassName}");
                portrait.texture = texture ? texture : defaultPortrait;
            }

            void ConfirmSelection()
            {
                if (!Current(account))
                    return;
                var record = records[choices.value];
                select(new SelectedCharacter(record.firebaseKey, record.characterName));
            }
        }

        private static string CountedLabel(string name, Dictionary<string, int> counts)
        {
            counts.TryGetValue(name, out var previous);
            counts[name] = previous + 1;
            return previous == 0 ? name : $"{name} ({previous + 1})";
        }

        private bool Current(AccountProfile account) =>
            this && isActiveAndEnabled && ReferenceEquals(AuthManager.Instance?.CurrentAccount, account);
    }
}
