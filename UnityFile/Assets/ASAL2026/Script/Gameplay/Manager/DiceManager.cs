using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace ASAL
{
    [Serializable]
    internal sealed class MatchRoll
    {
        public string Id;
        public int[] Sides;
        public int[] Faces;
        public int Total;
    }

    [Serializable]
    internal sealed class MatchLogEntry
    {
        public string Id;
        public string Text;
        public MatchRoll Roll;
    }

    public sealed class DiceManager : MonoBehaviour
    {
        private HashSet<int> supported;
        public static DiceManager Instance { get; private set; }
        internal event Action<MatchRoll> Rolled;

        internal void Register()
        {
            if (Instance)
                throw new InvalidOperationException("Duplicate DiceManager.");
            Instance = this;
        }

        internal void Unregister()
        {
            if (Instance == this)
                Instance = null;
        }

        internal void Initialize() => supported = Resources.LoadAll<Dice>("Dices").Select(dice => dice.diceType).ToHashSet();

        internal void Shutdown() => Rolled = null;

        internal void Roll(int[] sides, bool isPrivate)
        {
            var room = RoomManager.Instance;
            if (room?.Match?.Committed == null || !room.CanLeave || !Allows(sides))
                return;
            if (!isPrivate)
            {
                room.Match.Submit(new MatchCommand { Action = MatchAction.RollDice, Dice = sides });
                return;
            }
            if (!room.IsDm)
                return;

            var roll = Resolve(Guid.NewGuid().ToString("N"), sides);
            ReportPrivate(Describe("DM", roll), roll);
        }

        internal bool Allows(int[] sides) => sides is { Length: > 0 and <= 20 } && sides.All(supported.Contains);

        internal static int RollFace(int sides) => Random.Range(1, sides + 1);

        internal static MatchRoll Resolve(string id, int[] sides)
        {
            var faces = sides.Select(RollFace).ToArray();
            return MakeRoll(id, sides, faces);
        }

        internal static MatchRoll MakeRoll(string id, int[] sides, int[] faces) => new()
        {
            Id = id, Sides = sides, Faces = faces, Total = faces.Sum()
        };

        internal static string Describe(string name, MatchRoll roll) =>
            $"{name} rolled {string.Join(" + ", roll.Sides.Select(sides => $"d{sides}"))} and got {roll.Total}.";

        internal void ReportPrivate(string text, MatchRoll roll)
        {
            ChatManager.Instance?.AddPrivate(text);
            if (roll != null)
                Rolled?.Invoke(roll);
        }
    }
}
