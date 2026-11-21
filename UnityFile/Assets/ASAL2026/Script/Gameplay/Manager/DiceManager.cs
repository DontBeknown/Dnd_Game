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

    internal readonly struct DiceTerm
    {
        internal DiceTerm(int sides, int count)
        {
            Sides = sides;
            Count = count;
        }

        internal int Sides { get; }
        internal int Count { get; }
    }

    internal sealed class DiceFormula
    {
        private readonly DiceTerm[] terms;

        internal DiceFormula(IEnumerable<DiceTerm> terms) => this.terms = terms.ToArray();

        internal string DisplayText => terms.Length == 0 ? "-"
            : string.Join("+ ", terms.Select(term => $"{term.Count}D{term.Sides}"));

        internal int Roll(Func<int, int> roll, int extraPerTerm)
        {
            int total = 0;
            foreach (var term in terms)
            {
                for (int i = 0; i < term.Count + extraPerTerm; i++)
                    total += roll(term.Sides);
            }
            return total;
        }
    }

    internal sealed class DiceRoll
    {
        private readonly Func<int, int> roll;
        private readonly List<int> sides = new();
        private readonly List<int> faces = new();

        internal DiceRoll(Func<int, int> roll) => this.roll = roll;
        internal int Roll(DiceFormula formula, int extraPerTerm = 0) => formula.Roll(RollDie, extraPerTerm);
        internal MatchRoll ToResult(string id) => faces.Count == 0 ? null
            : DiceManager.MakeRoll(id, sides.ToArray(), faces.ToArray());

        private int RollDie(int sideCount)
        {
            var face = roll(sideCount);
            sides.Add(sideCount);
            faces.Add(face);
            return face;
        }
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

        internal void Initialize(RulesCatalog catalog) => supported = catalog.DiceSides.ToHashSet();

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

        internal bool Supports(int sides) => supported.Contains(sides);

        internal bool Allows(int[] sides) => sides is { Length: > 0 and <= 20 } && sides.All(Supports);

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
