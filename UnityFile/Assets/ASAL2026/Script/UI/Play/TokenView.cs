using Photon.Pun;
using TMPro;
using UnityEngine;

namespace ASAL
{
    public sealed class TokenView : MonoBehaviour, IPunInstantiateMagicCallback, IPunObservable
    {
        private static readonly Color PlayerOutline = new(0.3f, 0.9f, 0.38f);
        private static readonly Color EnemyOutline = new(0.95f, 0.29f, 0.25f);
        private static readonly Color SelectedOutline = new(1f, 0.86f, 0.23f);

        [SerializeField] private SpriteRenderer outline;
        [SerializeField] private SpriteRenderer marker;
        [SerializeField] private TextMeshPro label;
        [SerializeField] private TextMeshPro hpLabel;
        [SerializeField] private CircleCollider2D footprint;

        private bool enemy;
        private bool selected;
        private bool preview;
        private string tokenSession;

        internal MatchToken Token { get; private set; }
        internal string TokenId => Token?.Id;
        internal int OwnerActor => Token?.OwnerActor ?? 0;
        internal Vector2 BoardPosition => Token?.Position ?? Vector2.zero;
        internal CircleCollider2D Footprint => footprint;

        public void OnPhotonInstantiate(PhotonMessageInfo info)
        {
            var photon = PhotonManager.Instance;
            if (!photon || !photon.TryReadTokenSpawn(info, out var session, out var token))
            {
                gameObject.SetActive(false);
                return;
            }

            Bind(token, transform.position);
            SetBoardVisible(false);
            tokenSession = session;
            if (!photon.RegisterToken(this))
                gameObject.SetActive(false);
        }

        public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
        {
            var photon = PhotonManager.Instance;
            if (!photon || tokenSession == null)
                return;

            var world = transform.position;
            var board = BoardPosition;
            if (stream.IsWriting)
            {
                if (!photon.CanWriteToken(tokenSession))
                    return;
                stream.Serialize(ref world);
                stream.Serialize(ref board);
            }
            else if (photon.CanReadToken(tokenSession, info.Sender))
            {
                stream.Serialize(ref world);
                stream.Serialize(ref board);
                if (PhotonManager.FiniteWorldPosition(world) && PhotonManager.FiniteBoardPosition(board))
                    SetBoardPosition(world, board);
            }
        }

        private void OnDisable()
        {
            if (tokenSession == null)
                return;
            tokenSession = null;
            PhotonManager.Instance?.UnregisterToken(this);
        }

        internal void Bind(MatchToken token, Vector3 position)
        {
            Token = token;
            SetBoardPosition(position, token.Position);
            enemy = token.OwnerActor == 0;
            label.text = token.Name;
            UpdateHealth(token.Hp);
            RefreshOutline();
        }

        internal void SetBoardPosition(Vector3 worldPosition, Vector2 boardPosition)
        {
            transform.position = worldPosition;
            Token.Position = boardPosition;
        }

        internal void UpdateHealth(int hp)
        {
            Token.Hp = hp;
            hpLabel.text = Token.Character == null ? string.Empty : $"HP {hp} / {Token.Character.MaxHp}";
        }

        internal void SetBoardVisible(bool visible)
        {
            outline.forceRenderingOff = !visible;
            marker.forceRenderingOff = !visible;
            label.renderer.forceRenderingOff = !visible;
            hpLabel.renderer.forceRenderingOff = !visible;
        }

        internal void SetSelected(bool value)
        {
            selected = value;
            RefreshOutline();
        }

        internal void SetPreview(bool value)
        {
            preview = value;
            footprint.enabled = !value;
            float alpha = value ? 0.5f : 1f;
            marker.color = WithAlpha(marker.color, alpha);
            label.alpha = alpha;
            hpLabel.alpha = alpha;
            RefreshOutline();
        }

        private void RefreshOutline()
        {
            var color = selected ? SelectedOutline : enemy ? EnemyOutline : PlayerOutline;
            outline.color = WithAlpha(color, preview ? 0.5f : 1f);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }
    }
}

