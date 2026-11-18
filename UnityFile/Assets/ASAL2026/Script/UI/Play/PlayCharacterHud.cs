using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static ASAL.PlayHudControls;

namespace ASAL
{
    public sealed class PlayCharacterHud : MonoBehaviour
    {
        [SerializeField] private GameObject characterPanel, skillPanel, hpPanel, raceRow;
        [SerializeField] private Button closeCharacter, closeSkill, changeHp, removeToken;
        [SerializeField] private TMP_InputField hpChange;
        [SerializeField] private TMP_Text nameText, classText, raceText, levelText, hpText, actionText, skillTitle, skillDetail;
        [SerializeField] private RawImage portrait;
        [SerializeField] private RawImage[] skillIcons;
        [SerializeField] private TMP_Text[] abilityPoints, abilityModifiers, skillNames, hitDice, damageDice;
        [SerializeField] private Button[] checkAbility, showSkill, castSkill;

        private static readonly string[] AbilitySlots = { "Strength", "Dexterity", "Constitution", "Intelligence" };

        private MatchSnapshot snapshot;
        private RoomManager room;
        private PlayPanel play;
        private PlayHud hud;
        private string inspectedToken;
        private string pendingSkill;
        private Button skillReturnFocus;

        internal bool IsTargeting => pendingSkill != null;

        internal void Initialize(PlayHud owner, PlayPanel panel, RoomManager rooms)
        {
            hud = owner;
            play = panel;
            room = rooms;
            characterPanel.SetActive(false);
            skillPanel.SetActive(false);
            raceRow.SetActive(false);
            closeCharacter.onClick.AddListener(hud.CloseModal);
            closeSkill.onClick.AddListener(CloseSkill);
            changeHp.onClick.AddListener(ChangeHp);
            removeToken.onClick.AddListener(() => CombatManager.Instance.RemoveToken(inspectedToken));
            for (var i = 0; i < checkAbility.Length; i++)
            {
                var slot = i;
                checkAbility[i].onClick.AddListener(() =>
                {
                    var ability = AbilityAt(Inspected()?.Character, slot);
                    if (ability != null)
                        CombatManager.Instance.RollAbility(inspectedToken, ability.Name);
                });
            }
            for (var i = 0; i < showSkill.Length; i++)
            {
                var slot = i;
                showSkill[i].onClick.AddListener(() => ShowSkill(slot));
                castSkill[i].onClick.AddListener(() => CastSkill(slot));
            }
        }

        private void ChangeHp()
        {
            if (!int.TryParse(hpChange.text, out var amount) || amount == 0)
                return;

            CombatManager.Instance.ChangeHp(inspectedToken, amount);
            hpChange.text = string.Empty;
        }

        internal void Apply(MatchSnapshot next)
        {
            snapshot = next;
            if (!characterPanel.activeSelf)
                return;

            if (Inspected() is MatchToken token)
                RenderCharacter(token);
            else
                hud.CloseModal();
        }

        private void ClearInspection()
        {
            skillPanel.SetActive(false);
            skillReturnFocus = null;
            pendingSkill = null;
            inspectedToken = null;
        }

        internal bool TryCloseSkill()
        {
            if (!skillPanel.activeSelf)
                return false;

            CloseSkill();
            return true;
        }

        internal bool Inspect(MatchToken token)
        {
            if (token?.Character == null || (token.OwnerActor == 0 && !room.IsDm))
                return false;
            hud.OpenModal(characterPanel, null, ClearInspection);
            inspectedToken = token.Id;
            RenderCharacter(token);
            closeCharacter.Select();
            return true;
        }

        internal void AcceptTarget(MatchToken target)
        {
            if (pendingSkill == null || target == null)
                return;
            CombatManager.Instance.UseSkill(inspectedToken, target.Id, pendingSkill);
            pendingSkill = null;
            characterPanel.SetActive(true);
            closeCharacter.Select();
            play.ShowNotice(null);
        }

        private MatchToken FindToken(string id) => snapshot?.Tokens.FirstOrDefault(token => token.Id == id);
        private MatchToken Inspected() => FindToken(inspectedToken);

        private void RenderCharacter(MatchToken token)
        {
            var character = token.Character;
            if (character == null)
                return;
            SetText(nameText, "Name: " + token.Name);
            SetText(classText, "Class: " + character.Class);
            SetText(raceText, "Race: " + character.Race);
            SetText(levelText, "Level: " + character.Level);
            SetText(hpText, "HP: " + token.Hp + " / " + character.MaxHp);
            var participant = snapshot.Encounter?.TurnOrder.FirstOrDefault(item => item.TokenId == token.Id);
            SetText(actionText, participant == null ? "—" : participant.ActionsLeft + " action / " + participant.MoveLeft + " move");
            var race = character.Race;
            portrait.texture = Resources.Load<Texture>($"CharacterImages/{race}/{race}_{character.Class}_Circle") ??
                Resources.Load<Texture>($"CharacterImages/{race}/{race}_Circle");
            hpPanel.SetActive(room.IsDm);
            removeToken.gameObject.SetActive(room.IsDm);
            var controlsToken = room.IsDm || token.OwnerActor == room.LocalActor;
            for (var i = 0; i < checkAbility.Length; i++)
            {
                var ability = AbilityAt(character, i);
                SetText(abilityPoints[i], ability?.Points.ToString() ?? "—");
                SetText(abilityModifiers[i], ability == null ? "—" :
                    CombatRules.Modifier(character, participant, ability.Name).ToString("+0;-0;0"));
                checkAbility[i].gameObject.SetActive(controlsToken && ability != null);
            }

            var skills = ActiveSkills(token);
            for (var i = 0; i < showSkill.Length; i++)
            {
                var info = i < skills.Length ? skills[i] : null;
                SetText(skillNames[i], info ? info.skillName : "—");
                skillIcons[i].texture = info ? Resources.Load<Texture>("SkillImages/" + info.skillName) : null;
                skillIcons[i].enabled = skillIcons[i].texture;
                SetText(hitDice[i], info ? info.GetHitDiceData() : "—");
                SetText(damageDice[i], info ? info.GetDmgDiceData() : "—");
                showSkill[i].interactable = info;
                castSkill[i].gameObject.SetActive(controlsToken && info);
                castSkill[i].interactable = info && CombatManager.Instance.CanUseSkill(token, info.skillName);
            }
            ClearRows(raceRow);
            foreach (var name in character.Skills ?? Array.Empty<string>())
            {
                var info = CombatManager.Instance.Skill(name);
                if (!info || info.skillType != SkillType.RacePassive)
                    continue;
                var row = AddRow(raceRow);
                SetText(Text(row, "RaceSkillNameText"), info.skillName);
                SetText(Text(row, "RaceSkillNameDetail"), info.detail);
            }
        }

        private static Skill[] ActiveSkills(MatchToken token) =>
            (token?.Character?.Skills ?? Array.Empty<string>()).Select(name => CombatManager.Instance.Skill(name))
                .Where(skill => skill && skill.skillType != SkillType.RacePassive).ToArray();

        private static MatchAbility AbilityAt(MatchCharacter character, int slot) =>
            character?.Abilities?.FirstOrDefault(ability => ability.Name == AbilitySlots[slot]);

        private void ShowSkill(int slot)
        {
            var skills = ActiveSkills(Inspected());
            if (slot >= skills.Length)
                return;
            SetText(skillTitle, skills[slot].skillName);
            SetText(skillDetail, skills[slot].detail);
            skillReturnFocus = showSkill[slot];
            skillPanel.SetActive(true);
            closeSkill.Select();
        }

        private void CloseSkill()
        {
            skillPanel.SetActive(false);
            if (skillReturnFocus)
                skillReturnFocus.Select();
            skillReturnFocus = null;
        }

        private void CastSkill(int slot)
        {
            var skills = ActiveSkills(Inspected());
            if (slot >= skills.Length)
                return;
            var skill = skills[slot].skillName;
            if (!CombatManager.Instance.NeedsTarget(skill))
            {
                CombatManager.Instance.UseSkill(inspectedToken, null, skill);
                return;
            }
            pendingSkill = skill;
            characterPanel.SetActive(false);
            play.ShowNotice("Select a target on the map, or right-click to cancel.");
        }

        internal void CancelTarget()
        {
            if (!IsTargeting)
                return;
            pendingSkill = null;
            characterPanel.SetActive(true);
            closeCharacter.Select();
            play.ShowNotice(null);
        }
    }
}
