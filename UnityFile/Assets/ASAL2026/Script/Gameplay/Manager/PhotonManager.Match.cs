using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace ASAL
{
    public sealed partial class PhotonManager
    {
        private const byte MATCH_EVENT = 43;
        private const string REVISION_KEY = "matchRevision";
        private const string PROPOSAL_KEY = "matchProposal";

        internal event Action<int, string, object[]> MatchReceived;
        internal int MatchRevision => Current?.CustomProperties[REVISION_KEY] is int revision ? revision : 0;
        internal string MatchProposal => Current?.CustomProperties[PROPOSAL_KEY] as string;

        internal bool CommitMatch(int revision, string proposal)
        {
            var phase = revision == 1 ? RoomPhase.Starting : RoomPhase.Playing;
            return Current.SetCustomProperties(new Hashtable
            {
                [REVISION_KEY] = revision, [PROPOSAL_KEY] = proposal, [PHASE_KEY] = (int)RoomPhase.Playing
            }, new Hashtable
            {
                [REVISION_KEY] = revision - 1, [PROPOSAL_KEY] = MatchProposal, [PHASE_KEY] = (int)phase
            });
        }

        internal bool CancelStart() => Current.SetCustomProperties(
            new Hashtable { [PHASE_KEY] = (int)RoomPhase.Setup },
            new Hashtable { [PHASE_KEY] = (int)RoomPhase.Starting, [REVISION_KEY] = 0 });

        internal bool SendMatch(int[] actors, params object[] payload) => actors.Length == 0
            || PhotonNetwork.RaiseEvent(MATCH_EVENT, payload,
                new RaiseEventOptions { TargetActors = actors }, SendOptions.SendReliable);
    }

}
