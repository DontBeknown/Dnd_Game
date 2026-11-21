using System;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;

namespace ASAL
{
    public sealed partial class PhotonManager
    {
        private const byte MATCH_EVENT = 43;
        private const string INITIAL_MAP_KEY = "initialMapRevision";

        internal event Action<int, string, object[]> MatchReceived;
        internal bool Playing => Current?.CustomProperties[PHASE_KEY] is int phase && phase == (int)RoomPhase.Playing;
        internal int InitialMapRevision => Current?.CustomProperties[INITIAL_MAP_KEY] is int revision ? revision : 0;

        internal bool PublishPlay(int initialMapRevision) => Current.SetCustomProperties(new Hashtable
            { [INITIAL_MAP_KEY] = initialMapRevision, [PHASE_KEY] = (int)RoomPhase.Playing },
            new Hashtable { [PHASE_KEY] = (int)RoomPhase.Starting });

        internal bool CancelStart() => Current.SetCustomProperties(
            new Hashtable { [PHASE_KEY] = (int)RoomPhase.Setup },
            new Hashtable { [PHASE_KEY] = (int)RoomPhase.Starting });

        internal bool SendMatch(int[] actors, params object[] payload) => actors.Length == 0
            || PhotonNetwork.RaiseEvent(MATCH_EVENT, payload,
                new RaiseEventOptions { TargetActors = actors }, SendOptions.SendReliable);

        internal void SendDrag(int actor, object[] payload) => PhotonNetwork.RaiseEvent(MATCH_EVENT, payload,
            new RaiseEventOptions { TargetActors = new[] { actor } }, SendOptions.SendUnreliable);
    }

}
