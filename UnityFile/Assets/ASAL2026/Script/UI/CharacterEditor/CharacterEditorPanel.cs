using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ASAL
{
    public sealed class CharacterEditorPanel : MonoBehaviour
    {
        [SerializeField] private CharacterFormLayout layout;
        [SerializeField] private TMP_Text status;
        [SerializeField] private TMP_Dropdown race;
        [SerializeField] private TMP_Dropdown characterClass;
        [SerializeField] private TMP_InputField recordName;
        [SerializeField] private Button back;
        [SerializeField] private RawImage portrait;

        private CharacterDraft[] entries = Array.Empty<CharacterDraft>();
        private CharacterDraft draft;
        private string existingId;
        private AccountProfile account;
        private string session;
        private RoomPhase phase;
        private bool enemy;
        private bool busy;
        private bool confirmDelete;
        private bool dirty;
        private Func<SelectedCharacter, bool> choose;
        private Func<SelectedCharacter, bool> updateSelection;
        private Texture defaultPortrait;
        private CharacterManager Characters => CharacterManager.Instance;
        private TMP_Dropdown Records => layout.Records;
        private bool IsAssignedCharacter => choose != null && existingId != null
            && existingId == RoomManager.Instance.SelectedCharacterId;
        internal RoomPhase OpeningPhase => phase;
        internal CharacterDraft EditableDraft => !busy && Current && draft?.RepairError == null ? draft : null;
        private bool Current => this && account != null && ReferenceEquals(account, AuthManager.Instance?.CurrentAccount)
            && RoomManager.Instance && session == RoomManager.Instance.Session && phase == RoomManager.Instance.Phase;

        internal void Open(AccountProfile owner, bool editingEnemies, Action close, string selectedId = null,
            Func<SelectedCharacter, bool> selectForRoom = null, Func<SelectedCharacter, bool> updateRoomSelection = null)
        {
            account = owner;
            session = RoomManager.Instance.Session;
            phase = RoomManager.Instance.Phase;
            enemy = editingEnemies;
            choose = selectForRoom;
            updateSelection = updateRoomSelection;
            defaultPortrait = portrait.texture;
            layout.Bind(this, enemy);

            status.richText = false;
            recordName.richText = false;
            recordName.textComponent.richText = false;
            race.ClearOptions();
            race.AddOptions(Characters.Races.Select(item => item.Label).ToList());
            characterClass.ClearOptions();
            characterClass.AddOptions(Characters.Classes.Select(item => item.Label).ToList());
            foreach (var dropdown in new[] { Records, race, characterClass })
            {
                dropdown.captionText.richText = false;
                dropdown.itemText.richText = false;
            }
            Records.onValueChanged.AddListener(ChangeRecord);
            race.onValueChanged.AddListener(_ => ChangeCatalog());
            characterClass.onValueChanged.AddListener(_ => ChangeCatalog());

            layout.SaveButton.onClick.AddListener(Save);
            layout.DeleteButton.onClick.AddListener(Delete);
            back.onClick.AddListener(() => close());
            if (layout.UsesTemplateDraft)
                recordName.onValueChanged.AddListener(_ => MarkDirty());
            RenderIndex(null);
            LoadDraft(0);
            Refresh(selectedId);
            back.Select();
        }

        internal void Release() => account = null;

        internal void ResetDraft() => LoadDraft(Records.value);

        private void ChangeRecord(int index)
        {
            if (busy || !Current)
                return;
            if (layout.UsesTemplateDraft && dirty)
            {
                Records.SetValueWithoutNotify(Array.FindIndex(entries, item => item.RecordId == existingId) + 1);
                Records.RefreshShownValue();
                status.text = "Save your changes or use Reset before opening another record.";
                return;
            }
            LoadDraft(index);
        }

        private void LoadDraft(int index)
        {
            confirmDelete = false;
            dirty = false;
            existingId = index == 0 ? null : entries[index - 1].RecordId;
            draft = index == 0
                ? (layout.UsesTemplateDraft ? Characters.NewRoomDraft(enemy) : Characters.NewDraft())
                : entries[index - 1].Copy();
            if (index == 0 && !layout.UsesTemplateDraft)
                Characters.SetCatalog(draft, Characters.Races[0], Characters.Classes[0]);

            recordName.SetTextWithoutNotify(draft.Name ?? string.Empty);
            layout.LoadDraft(draft);
            SyncCatalogControls();
            RenderDraft();
            status.text = draft.RepairError ?? string.Empty;
            Controls();
        }

        private void ChangeCatalog()
        {
            if (EditableDraft == null)
                return;
            var nextRace = Characters.Races[race.value];
            if (!layout.PrepareCatalogChange(draft) || !Characters.CanChangeRace(draft, nextRace))
            {
                SyncCatalogControls();
                status.text = "Adjust your ability scores before changing to this race.";
                return;
            }
            Characters.SetCatalog(draft, nextRace, Characters.Classes[characterClass.value]);
            RenderDraft();
            MarkDirty();
        }

        private void SyncCatalogControls()
        {
            race.SetValueWithoutNotify(Math.Max(0, Array.FindIndex(Characters.Races.ToArray(), item => item.Id == draft.RaceId)));
            race.RefreshShownValue();
            characterClass.SetValueWithoutNotify(Math.Max(0, Array.FindIndex(Characters.Classes.ToArray(), item => item.Id == draft.ClassId)));
            characterClass.RefreshShownValue();
        }

        private void RenderDraft()
        {
            layout.Render(draft);
            var texture = draft.RepairError == null ? Characters.Catalog.Portrait(draft.RaceId, draft.ClassId) : null;
            portrait.texture = texture ? texture : defaultPortrait;
        }

        internal void UpdateDerivedStats()
        {
            draft.Hp = CharacterManager.CalculateHp(draft, Characters.Classes[characterClass.value].BaseHp);
            RenderDraft();
            MarkDirty();
        }

        private void MarkDirty()
        {
            dirty = true;
            confirmDelete = false;
            status.text = choose != null && existingId != null ? "Save your changes or use Reset before selecting." : string.Empty;
            Controls();
        }

        internal void ChooseSaved()
        {
            if (!Current || busy || dirty || existingId == null || choose == null)
                return;
            if (Characters.Project(draft) == null)
                status.text = draft.RepairError ?? "This saved character needs a valid level, HP, and ability scores before it can be selected.";
            else if (!choose(new SelectedCharacter(existingId, draft.Name)))
                status.text = "Could not select the character. Try again.";
        }

        private void Refresh(string preferredId = null) => Run("Loading saved records…", async () =>
        {
            var result = await Characters.LoadAsync(account, enemy);
            if (!Current)
                return;
            entries = result;
            RenderIndex(preferredId ?? existingId);
            if (preferredId != null || existingId != null && Records.value == 0)
                LoadDraft(Records.value);
            status.text = entries.Length == 0
                ? (enemy ? "No saved enemies. Edit the draft, then use Create Enemy." : "No saved characters. Edit the draft, then create a character.")
                : draft.RepairError ?? string.Empty;
        }, enemy ? "Could not load monster templates. Check database access or reopen to retry." : "Could not load saved characters. Reopen to retry.");

        internal void Save()
        {
            if (EditableDraft == null)
                return;
            if (!layout.ReadForSave(draft))
            {
                status.text = "Check the level, HP, and ability scores.";
                return;
            }

            bool isNew = existingId == null;
            draft.Name = recordName.text;
            Run("Saving…", async () =>
            {
                await Characters.SaveAsync(account, enemy, draft);
                if (!Current)
                    return;
                entries = entries.Where(item => item.RecordId != draft.RecordId).Append(draft)
                    .OrderBy(item => item.Name, StringComparer.CurrentCulture)
                    .ThenBy(item => item.RecordId, StringComparer.Ordinal).ToArray();
                existingId = draft.RecordId;
                RenderIndex(existingId);
                LoadDraft(Records.value);
                status.text = "Saved.";
                if (!isNew && updateSelection != null)
                {
                    var selection = new SelectedCharacter(existingId, draft.Name);
                    if (!updateSelection(selection))
                        status.text = "Saved. Use Select to update the room.";
                }
            });
        }

        private void Delete()
        {
            if (EditableDraft == null || existingId == null || IsAssignedCharacter)
                return;
            if (!confirmDelete)
            {
                confirmDelete = true;
                status.text = "Press Delete again to remove the selected record.";
                return;
            }

            var id = existingId;
            Run("Deleting…", async () =>
            {
                await Characters.DeleteAsync(account, enemy, id);
                if (!Current)
                    return;
                entries = entries.Where(item => item.RecordId != id).ToArray();
                RenderIndex(null);
                LoadDraft(0);
                status.text = "Record deleted.";
            });
        }

        private void RenderIndex(string selected)
        {
            Records.ClearOptions();
            var newLabel = layout.UsesTemplateDraft ? (enemy ? "Create New Monster" : "Create New Character") : "New character";
            var labels = new List<string> { newLabel };
            if (layout.UsesTemplateDraft)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal) { [newLabel] = 1 };
                foreach (var entry in entries)
                {
                    var name = entry.Name;
                    counts.TryGetValue(name, out var previous);
                    counts[name] = previous + 1;
                    labels.Add(previous == 0 ? name : $"{name} ({previous + 1})");
                }
            }
            else
            {
                labels.AddRange(entries.Select(item => item.Name));
            }
            Records.AddOptions(labels);
            Records.SetValueWithoutNotify(Array.FindIndex(entries, item => item.RecordId == selected) + 1);
            Records.RefreshShownValue();
        }

        private async void Run(string message, Func<Task> operation, string failureMessage = "Operation failed. Try again.")
        {
            if (busy || !Current)
                return;
            var eventSystem = EventSystem.current;
            var focused = eventSystem ? eventSystem.currentSelectedGameObject : null;
            busy = true;
            confirmDelete = false;
            status.text = message;
            Controls();
            try { await operation(); }
            catch (Exception error)
            {
                if (Current)
                    status.text = error is FormatException or InvalidOperationException ? error.Message : failureMessage;
            }
            finally
            {
                busy = false;
                if (Current)
                {
                    Controls();
                    if (eventSystem && focused
                        && (!eventSystem.currentSelectedGameObject || eventSystem.currentSelectedGameObject == focused))
                    {
                        if (focused.activeInHierarchy && focused.TryGetComponent<Selectable>(out var control)
                            && control.IsInteractable())
                            control.Select();
                        else
                            Records.Select();
                    }
                }
            }
        }

        private void Controls()
        {
            bool hasRecord = existingId != null;
            bool canEdit = !busy && draft.RepairError == null;
            Records.interactable = !busy;
            race.interactable = characterClass.interactable = recordName.interactable = canEdit;
            layout.SaveButton.interactable = canEdit;
            layout.DeleteButton.interactable = canEdit && hasRecord && !IsAssignedCharacter;
            layout.SetControls(draft, !canEdit, hasRecord, choose != null, dirty);
        }
    }
}
