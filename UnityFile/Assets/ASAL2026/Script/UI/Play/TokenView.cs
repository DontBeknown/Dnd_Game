using TMPro;
using UnityEngine;

namespace ASAL
{
    public sealed class TokenView : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer marker;
        [SerializeField] private TextMeshPro label;

        internal void Bind(MatchToken token, Vector3 position)
        {
            transform.position = position;
            marker.color = token.OwnerActor == 0 ? new Color(0.8f, 0.29f, 0.23f) : new Color(0.24f, 0.68f, 0.92f);
            label.text = token.Name;
        }
    }
}

