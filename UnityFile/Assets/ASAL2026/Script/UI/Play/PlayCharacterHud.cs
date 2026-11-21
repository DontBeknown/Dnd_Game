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
        [SerializeField] private TMP_Text[] abilityLabels, abilityPoints, abilityModifiers, skillNames, hitDice, damageDice;
        [SerializeField] private Button[] checkAbility, showSkill, castSkill;
        [SerializeField] private Button nextSkillPage;
        [SerializeField] private TMP_Text skillPageLabel;

        private static readonly AbilityId[] AbilitySlots =
            { new AbilityId("strength"), new AbilityId("dexterity"), AbilityId.Constitution, new AbilityId("intelligence") };

        private MatchSnapshot snapshot;
        private RoomManager room;
        private PlayPanel play;
        private PlayHud hud;
        private string inspectedToken;
        private SkillId? pendingSkill;
        private Button skillReturnFocus;
        private int skillPage;

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
            nextSkillPage.onClick.AddListener(AdvanceSkillPage);
            changeHp.onClick.AddListener(ChangeHp);
            removeToken.onClick.AddListener(() => CombatManager.Instance.RemoveToken(inspectedToken));
            for (var i = 0; i < checkAbility.Length; i++)
            {
                var slot = i;
                checkAbility[i].onClick.AddListener(() =>
                {
                    var token = Inspected();
                    if (room.IsDm || token == null || token.OwnerActor != room.LocalActor)
                        return;
                    var ability = AbilityAt(token.Character, slot);
                    if (ability != null)
                        CombatManager.Instance.RollAbility(inspectedToken, new AbilityId(ability.AbilityId));
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
            play.SetSelectedToken(null);
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
            if (token?.Character == null || !PlayPanel.CanSelectToken(token.OwnerActor, room.LocalActor, room.IsDm))
                return false;
            hud.OpenModal(characterPanel, null, ClearInspection);
            inspectedToken = token.Id;
            skillPage = 0;
            RenderCharacter(token);
            closeCharacter.Select();
            return true;
        }

        internal void AcceptTarget(MatchToken target)
        {
            if (pendingSkill == null || target == null)
                return;
            CombatManager.Instance.UseSkill(inspectedToken, target.Id, pendingSkill.Value);
            pendingSkill = null;
            characterPanel.SetActive(true);
            closeCharacter.Select();
            play.ShowNotice(null);
        }

        private MatchToken FindToken(string id) => snapshot?.Tokens.FirstOrDefault(token => token.Id == id);
        private MatchToken Inspected() => FindToken(inspectedToken);

        internal static bool CanCastTokenSkill(int ownerActor, int localActor, bool isDm) =>
            isDm ? ownerActor == 0 : localActor > 0 && ownerActor == localActor;

        private void RenderCharacter(MatchToken token)
        {
            var character = token.Character;
            if (character == null)
                return;
            SetText(nameText, "Name: " + token.Name);
            var catalog = CharacterManager.Instance.Catalog;
            SetText(classText, "Class: " + catalog.Class(new ClassId(character.ClassId))?.Label);
            SetText(raceText, "Race: " + catalog.Race(new RaceId(character.RaceId))?.Label);
            SetText(levelText, "Level: " + character.Level);
            SetText(hpText, "HP: " + token.Hp + " / " + character.MaxHp);
            var participant = snapshot.Encounter?.TurnOrder.FirstOrDefault(item => item.TokenId == token.Id);
            SetText(actionText, participant == null ? "—" : participant.ActionsLeft + " action");
            var tokenArt = catalog.TokenArt(new RaceId(character.RaceId), new ClassId(character.ClassId));
            portrait.texture = tokenArt ? tokenArt.texture : null;
            hpPanel.SetActive(room.IsDm);
            removeToken.gameObject.SetActive(room.IsDm);
            var controlsToken = !room.IsDm && token.OwnerActor == room.LocalActor;
            for (var i = 0; i < checkAbility.Length; i++)
            {
                var ability = AbilityAt(character, i);
                SetText(abilityLabels[i], catalog.Ability(AbilitySlots[i])?.Label ?? "—");
                SetText(abilityPoints[i], ability?.Points.ToString() ?? "—");
                SetText(abilityModifiers[i], ability == null ? "—" :
                    CombatRules.Modifier(catalog, character, participant, new AbilityId(ability.AbilityId)).ToString("+0;-0;0"));
                checkAbility[i].gameObject.SetActive(controlsToken && ability != null);
            }

            RenderSkills(token, ActiveSkills(token));
            ClearRows(raceRow);
            foreach (var id in character.SkillIds ?? Array.Empty<string>())
            {
                var info = CombatManager.Instance.Skill(new SkillId(id));
                if (info?.IsPassive != true)
                    continue;
                var row = AddRow(raceRow);
                SetText(Text(row, "RaceSkillNameText"), info.Label);
                SetText(Text(row, "RaceSkillNameDetail"), info.Detail);
            }
        }

        private void RenderSkills(MatchToken token, SkillDefinition[] skills)
        {
            var pageCount = Math.Max(1, (skills.Length + showSkill.Length - 1) / showSkill.Length);
            skillPage = Math.Min(skillPage, pageCount - 1);
            nextSkillPage.gameObject.SetActive(pageCount > 1);
            SetText(skillPageLabel, skillPage + 1 == pageCount
                ? $"First skills ({skillPage + 1}/{pageCount})" : $"Next skills ({skillPage + 1}/{pageCount})");
            var canCast = CanCastTokenSkill(token.OwnerActor, room.LocalActor, room.IsDm);
            for (var i = 0; i < showSkill.Length; i++)
            {
                var info = SkillAt(skills, i);
                SetText(skillNames[i], info?.Label ?? "—");
                skillIcons[i].texture = info?.Icon;
                skillIcons[i].enabled = skillIcons[i].texture;
                SetText(hitDice[i], info?.HitDiceText ?? "—");
                SetText(damageDice[i], info?.DamageDiceText ?? "—");
                showSkill[i].interactable = info != null;
                castSkill[i].gameObject.SetActive(canCast && info != null);
                castSkill[i].interactable = canCast && CombatManager.Instance.CanUseSkill(token, info);
            }
        }

        private void AdvanceSkillPage()
        {
            if (Inspected() is not { } token)
                return;
            var skills = ActiveSkills(token);
            skillPage = (skillPage + 1) * showSkill.Length < skills.Length ? skillPage + 1 : 0;
            RenderSkills(token, skills);
        }

        private SkillDefinition SkillAt(SkillDefinition[] skills, int slot)
        {
            var index = skillPage * showSkill.Length + slot;
            return index < skills.Length ? skills[index] : null;
        }

        private static SkillDefinition[] ActiveSkills(MatchToken token) =>
            (token?.Character?.SkillIds ?? Array.Empty<string>()).Select(id => CombatManager.Instance.Skill(new SkillId(id)))
                .Where(skill => skill != null && !skill.IsPassive).ToArray();

        private static MatchAbility AbilityAt(MatchCharacter character, int slot) =>
            character?.Abilities?.FirstOrDefault(ability => ability.AbilityId == AbilitySlots[slot].Value);

        private void ShowSkill(int slot)
        {
            var skill = SkillAt(ActiveSkills(Inspected()), slot);
            if (skill == null)
                return;
            SetText(skillTitle, skill.Label);
            SetText(skillDetail, skill.Detail);
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
            var token = Inspected();
            if (token == null || !CanCastTokenSkill(token.OwnerActor, room.LocalActor, room.IsDm))
                return;

            var skill = SkillAt(ActiveSkills(token), slot);
            if (!CombatManager.Instance.CanUseSkill(token, skill))
                return;
            if (!skill.Targeting.NeedsTarget)
            {
                CombatManager.Instance.UseSkill(inspectedToken, null, skill.Id);
                return;
            }
            pendingSkill = skill.Id;
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
