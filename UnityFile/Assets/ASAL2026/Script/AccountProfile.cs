namespace ASAL
{
    public sealed class AccountProfile
    {
        public AccountProfile(string uid, string displayName)
        {
            Uid = uid;
            DisplayName = displayName;
        }

        public string Uid { get; }
        public string DisplayName { get; }
    }
}
