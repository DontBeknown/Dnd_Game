using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace ASAL
{
    public sealed partial class PhotonManager
    {
        private const string TokenPrefabId = "ASAL2026.PlayerToken";
        private readonly Dictionary<string, TokenView> tokenViews = new();

        internal event Action TokenViewsChanged;
        internal IReadOnlyDictionary<string, TokenView> TokenViews => tokenViews;

        internal static void RegisterTokenPrefab(TokenView prefab)
        {
            if (!prefab || !prefab.TryGetComponent<PhotonView>(out _) || PhotonNetwork.PrefabPool is not DefaultPool pool)
                throw new InvalidOperationException("The Photon token prefab requires a PhotonView and the default PUN pool.");

            pool.ResourceCache[TokenPrefabId] = prefab.gameObject;
        }

        internal bool SpawnToken(MatchToken token, Vector3 worldPosition)
        {
            var session = Session;
            if (!CanWriteToken(session) || !ValidToken(token) || !FiniteWorldPosition(worldPosition)
                || tokenViews.ContainsKey(token.Id))
                return false;

            var instance = PhotonNetwork.InstantiateRoomObject(TokenPrefabId, worldPosition, Quaternion.identity,
                data: new object[] { session, JsonUtility.ToJson(token) });
            if (!instance)
                return false;
            if (tokenViews.TryGetValue(token.Id, out var view) && view.gameObject == instance)
                return true;

            PhotonNetwork.Destroy(instance);
            return false;
        }

        internal void DespawnToken(string id)
        {
            if (id != null && CanWriteToken(Session) && tokenViews.TryGetValue(id, out var view) && view)
                PhotonNetwork.Destroy(view.gameObject);
        }

        internal bool TryReadTokenSpawn(PhotonMessageInfo info, out string session, out MatchToken token)
        {
            session = null;
            token = null;
            var view = info.photonView;
            if (!view || view.ViewID == 0 || !view.IsRoomView || !FiniteWorldPosition(view.transform.position)
                || view.InstantiationData is not { Length: 2 } data || data[0] is not string matchId
                || !CanReadToken(matchId, info.Sender) || data[1] is not string body)
                return false;

            try
            {
                token = JsonUtility.FromJson<MatchToken>(body);
            }
            catch (ArgumentException)
            {
                return false;
            }
            if (!ValidToken(token))
                return false;

            session = matchId;
            return true;
        }

        internal bool RegisterToken(TokenView view)
        {
            if (!tokenViews.TryAdd(view.TokenId, view))
                return false;
            TokenViewsChanged?.Invoke();
            return true;
        }

        internal void UnregisterToken(TokenView view)
        {
            if (!tokenViews.TryGetValue(view.TokenId, out var current) || current != view)
                return;
            tokenViews.Remove(view.TokenId);
            TokenViewsChanged?.Invoke();
        }

        internal void ClearTokenViews()
        {
            if (tokenViews.Count == 0)
                return;
            foreach (var view in tokenViews.Values)
                if (view)
                    view.SetBoardVisible(false);
            tokenViews.Clear();
            TokenViewsChanged?.Invoke();
        }

        internal bool CanReadToken(string session, Player sender) => session != null && Session == session
            && sender != null && !sender.IsInactive && RoomManager.Instance is { } room && room.Session == session
            && room.IsDmIdentity(sender.ActorNumber, sender.UserId);

        internal bool CanWriteToken(string session) => IsMaster && CanReadToken(session, PhotonNetwork.LocalPlayer);

        internal static bool FiniteWorldPosition(Vector3 position) =>
            float.IsFinite(position.x) && float.IsFinite(position.y) && float.IsFinite(position.z);

        internal static bool FiniteBoardPosition(Vector2 position) =>
            float.IsFinite(position.x) && float.IsFinite(position.y)
            && Mathf.Abs(position.x) <= 1000000f && Mathf.Abs(position.y) <= 1000000f;

        private static bool ValidToken(MatchToken token) => token != null && token.Placed
            && (token.OwnerActor == 0 ? Guid.TryParseExact(token.Id, "N", out _)
                : token.OwnerActor > 0 && token.Id == MatchSnapshot.PlayerTokenId(token.OwnerActor))
            && !string.IsNullOrWhiteSpace(token.RecordId) && FiniteBoardPosition(token.Position)
            && token.Character != null && token.Name == token.Character.Name
            && CharacterManager.Instance && CharacterManager.Instance.ValidProjection(token.Character)
            && token.Hp >= 0 && token.Hp <= token.Character.MaxHp;
    }
}
