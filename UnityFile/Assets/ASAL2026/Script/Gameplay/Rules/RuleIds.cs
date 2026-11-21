using System;

namespace ASAL
{
    internal readonly struct RaceId : IEquatable<RaceId>
    {
        internal RaceId(string value) => Value = value;
        internal string Value { get; }
        public bool Equals(RaceId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is RaceId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(RaceId left, RaceId right) => left.Equals(right);
        public static bool operator !=(RaceId left, RaceId right) => !left.Equals(right);
        public override string ToString() => Value;
    }

    internal readonly struct ClassId : IEquatable<ClassId>
    {
        internal ClassId(string value) => Value = value;
        internal string Value { get; }
        public bool Equals(ClassId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ClassId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(ClassId left, ClassId right) => left.Equals(right);
        public static bool operator !=(ClassId left, ClassId right) => !left.Equals(right);
        public override string ToString() => Value;
    }

    internal readonly struct AbilityId : IEquatable<AbilityId>
    {
        internal AbilityId(string value) => Value = value;
        internal string Value { get; }
        internal static AbilityId Constitution => new("constitution");
        public bool Equals(AbilityId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is AbilityId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(AbilityId left, AbilityId right) => left.Equals(right);
        public static bool operator !=(AbilityId left, AbilityId right) => !left.Equals(right);
        public override string ToString() => Value;
    }

    internal readonly struct SkillId : IEquatable<SkillId>
    {
        internal SkillId(string value) => Value = value;
        internal string Value { get; }
        public bool Equals(SkillId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is SkillId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(SkillId left, SkillId right) => left.Equals(right);
        public static bool operator !=(SkillId left, SkillId right) => !left.Equals(right);
        public override string ToString() => Value;
    }

    internal readonly struct StatusId : IEquatable<StatusId>
    {
        internal StatusId(string value) => Value = value;
        internal string Value { get; }
        public bool Equals(StatusId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is StatusId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public static bool operator ==(StatusId left, StatusId right) => left.Equals(right);
        public static bool operator !=(StatusId left, StatusId right) => !left.Equals(right);
        public override string ToString() => Value;
    }
}
