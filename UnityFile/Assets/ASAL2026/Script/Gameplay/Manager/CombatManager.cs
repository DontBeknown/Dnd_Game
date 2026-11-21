using System;
using System.Linq;
using UnityEngine;

namespace ASAL
{
    public sealed class CombatManager : MonoBehaviour
    {
        private Skill[] skills;
        public static CombatManager Instance { get; private set; }

        internal void Register()
        {
            if (Instance)
                throw new InvalidOperationException("Duplicate CombatManager.");
            Instance = this;
        }

        internal void Unregister()
        {
            if (Instance == this)
                Instance = null;
        }

        internal void Initialize() => skills = Resources.LoadAll<Skill>("Skills");
        internal void Shutdown() => skills = null;

        internal Skill Skill(string name) => Array.Find(skills, skill => skill.skillName == name);
        internal bool NeedsTarget(string name) => Skill(name)?.skillType == SkillType.Damage
            && name is not ("Rage" or "Circle slash");

        internal bool CanAct(MatchToken token)
        {
            var room = RoomManager.Instance;
            return room?.CanLeave == true && CombatRules.CanAct(room.Match?.Committed?.Encounter, token, room.LocalActor, room.IsDm);
        }

        internal bool CanUseSkill(MatchToken token, string name) => CanAct(token)
            && CombatSkills.HasAction(RoomManager.Instance.Match.Committed.Encounter.Find(token.Id), name);

        internal void ToggleEncounter() => Submit(MatchAction.ToggleEncounter);
        internal void EndTurn(string tokenId) => Submit(MatchAction.EndTurn, tokenId);
        internal void UseSkill(string tokenId, string targetId, string skillName) => Submit(MatchAction.UseSkill, tokenId, targetId, skillName);
        internal void ChangeHp(string tokenId, int amount) => Submit(MatchAction.ChangeHp, tokenId, amount: amount);
        internal void RemoveToken(string tokenId) => Submit(MatchAction.RemoveToken, tokenId);
        internal void RollAbility(string tokenId, string ability)
        {
            var room = RoomManager.Instance;
            if (room?.Match?.Committed is not { } snapshot || !room.CanLeave)
                return;
            if (!room.IsDm)
            {
                Submit(MatchAction.RollAbility, tokenId, ability: ability);
                return;
            }
            var token = Array.Find(snapshot.Tokens, candidate => candidate.Id == tokenId && candidate.Placed);
            var entry = AbilityRoll(snapshot, token, ability, Guid.NewGuid().ToString("N"));
            if (entry != null)
                DiceManager.Instance.ReportPrivate(entry.Text, entry.Roll);
        }

        private static void Submit(MatchAction action, string tokenId = null, string targetId = null, string skill = null,
            int amount = 0, string ability = null) => RoomManager.Instance?.Match?.Submit(new MatchCommand
        {
            Action = action, TokenId = tokenId, TargetId = targetId, Skill = skill, Amount = amount, Ability = ability
        });

        internal bool TryResolve(MatchSnapshot current, MatchCommand command, int actor, bool dm, out MatchDelta delta,
            out MatchLogEntry privateReport)
        {
            delta = new MatchDelta();
            privateReport = null;
            if (command.Action == MatchAction.RollDice)
            {
                if (!DiceManager.Instance.Allows(command.Dice))
                    return false;
                var roll = DiceManager.Resolve(command.CommandId, command.Dice);
                var name = dm ? "DM" : Array.Find(current.Roster, member => member.Actor == actor)?.Name ?? "Player";
                delta.Log = Entry(command, DiceManager.Describe(name, roll), roll);
                return true;
            }
            if (command.Action == MatchAction.ToggleEncounter)
                return dm && Toggle(current, command, delta);

            var token = Array.Find(current.Tokens, item => item.Id == command.TokenId);
            if (token?.Placed != true || token.Character == null)
                return false;
            if (command.Action == MatchAction.RemoveToken)
                return dm && Remove(current, command, delta, token);
            if (command.Action == MatchAction.ChangeHp)
                return dm && ChangeHealth(current, command, delta, token);
            if (command.Action == MatchAction.RollAbility)
            {
                if (dm || token.OwnerActor != actor)
                    return false;
                delta.Log = AbilityRoll(current, token, command.Ability, command.CommandId);
                return delta.Log != null;
            }

            if (current.Encounter?.ActiveTokenId != token.Id)
                return false;
            bool controlsTurn = CombatRules.CanAct(current.Encounter, token, actor, dm);
            if (command.Action == MatchAction.EndTurn)
            {
                if (!dm && !controlsTurn)
                    return false;
                var encounter = CopyEncounter(current, delta);
                CombatRules.Advance(encounter, current.Tokens);
                delta.Log = Entry(command, $"{token.Name} ended their turn.");
                return true;
            }
            if (command.Action != MatchAction.UseSkill || !controlsTurn
                || !token.Character.Skills.Contains(command.Skill) || Skill(command.Skill) is not { } skill)
                return false;

            var next = CopyEncounter(current, delta);
            var combatant = next.Find(token.Id);
            if (!CombatSkills.TryUse(current.Tokens, next, token, combatant, command, skill, Skill("Focus"), DiceManager.RollFace,
                out var summary, out var report, out var rollResult))
                return false;
            SyncHealth(current, delta);
            if (dm && rollResult != null)
            {
                privateReport = Entry(command, summary + " " + report, rollResult);
                delta.Log = Entry(command, summary);
            }
            else
                delta.Log = Entry(command, report == null ? summary : summary + " " + report, rollResult);
            return true;
        }

        internal static void ResolvePlacement(MatchSnapshot current, MatchCommand command, MatchDelta delta)
        {
            if (current.Encounter == null)
                return;

            var encounter = CopyEncounter(current, delta);
            var added = delta.Token;
            var initiative = DiceManager.RollFace(20);
            encounter.TurnOrder = encounter.TurnOrder.Append(new Combatant
                { TokenId = added.Id, Initiative = initiative, Hp = added.Hp })
                .OrderByDescending(entry => entry.Initiative).ThenBy(entry => entry.TokenId, StringComparer.Ordinal).ToArray();
            delta.Log = Entry(command, $"{added.Name} joined combat with initiative {initiative}.",
                DiceManager.MakeRoll(command.CommandId, new[] { 20 }, new[] { initiative }));
            if (encounter.ActiveTokenId == null)
                CombatRules.Advance(encounter, current.Tokens.Where(candidate => candidate.Id != added.Id).Append(added).ToArray());
        }

        private static CombatEncounter CopyEncounter(MatchSnapshot current, MatchDelta delta)
        {
            delta.EncounterChanged = current.Encounter != null;
            delta.Encounter = current.Encounter?.Copy();
            return delta.Encounter;
        }

        private static bool Toggle(MatchSnapshot current, MatchCommand command, MatchDelta delta)
        {
            delta.EncounterChanged = true;
            if (current.Encounter != null)
            {
                delta.Log = Entry(command, "Combat ended.");
                return true;
            }
            delta.Encounter = CombatRules.StartEncounter(current.Tokens, DiceManager.RollFace);
            if (delta.Encounter == null)
                return false;
            var order = delta.Encounter.TurnOrder;
            var results = order.Select(entry => $"{Array.Find(current.Tokens, token => token.Id == entry.TokenId).Name} {entry.Initiative}");
            delta.Log = Entry(command, "Initiative: " + string.Join(", ", results), DiceManager.MakeRoll(command.CommandId,
                order.Select(_ => 20).ToArray(), order.Select(entry => entry.Initiative).ToArray()));
            return true;
        }

        private static bool ChangeHealth(MatchSnapshot current, MatchCommand command, MatchDelta delta, MatchToken token)
        {
            var amount = command.Amount;
            var hp = CombatRules.ClampHp(token.Hp, amount, token.Character.MaxHp);
            if (hp == token.Hp)
                return false;
            var changed = token.At(token.Position);
            changed.Hp = hp;
            delta.Token = changed;
            var encounter = CopyEncounter(current, delta);
            var combatant = encounter?.Find(token.Id);
            if (combatant != null)
            {
                combatant.Hp = hp;
                if (hp == 0 && encounter.ActiveTokenId == token.Id || hp > 0 && encounter.ActiveTokenId == null)
                    CombatRules.Advance(encounter, current.Tokens);
            }
            delta.Log = Entry(command, $"{token.Name} now has {hp}/{token.Character.MaxHp} HP.");
            return true;
        }

        private static bool Remove(MatchSnapshot current, MatchCommand command, MatchDelta delta, MatchToken token)
        {
            delta.RemovedTokenId = token.Id;
            var encounter = CopyEncounter(current, delta);
            if (encounter != null)
            {
                var remaining = current.Tokens.Where(item => item.Id != token.Id).ToArray();
                if (encounter.ActiveTokenId == token.Id)
                    CombatRules.Advance(encounter, remaining);
                encounter.TurnOrder = encounter.TurnOrder.Where(item => item.TokenId != token.Id).ToArray();
                foreach (var entry in encounter.TurnOrder)
                    entry.Statuses = entry.Statuses.Where(status => status.SourceId != token.Id || status.Name != "Roar").ToArray();
            }
            delta.Log = Entry(command, $"{token.Name} was removed from the board.");
            return true;
        }

        private static MatchLogEntry AbilityRoll(MatchSnapshot current, MatchToken token, string ability, string id)
        {
            if (token?.Character?.Abilities.Any(score => score.Name == ability) != true)
                return null;
            var roll = DiceManager.Resolve(id, new[] { 20 });
            var modifier = CombatRules.Modifier(token.Character, current.Encounter?.Find(token.Id), ability);
            return new MatchLogEntry { Id = id, Roll = roll,
                Text = $"{token.Name} rolled {roll.Total + (long)modifier} for {ability} ({roll.Total} {modifier:+#;-#;+0})." };
        }

        private static void SyncHealth(MatchSnapshot current, MatchDelta delta)
        {
            delta.ChangedTokens = current.Tokens.Where(token => delta.Encounter.Find(token.Id) is { } entry && entry.Hp != token.Hp)
                .Select(token =>
                {
                    var changed = token.At(token.Position);
                    changed.Hp = delta.Encounter.Find(token.Id).Hp;
                    return changed;
                }).ToArray();
        }

        private static MatchLogEntry Entry(MatchCommand command, string text, MatchRoll roll = null) =>
            new() { Id = command.CommandId, Text = text, Roll = roll };
    }
}
