using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ASAL
{
    internal sealed class TabTransition
    {
        private const float FadeDuration = 0.5f;
        private GameObject current;
        private Sequence animation;

        internal GameObject Current => current;
        internal bool IsPlaying => animation != null;
        internal void Finish() => animation?.Complete(withCallbacks: true);

        internal void Show(GameObject next, Action prepare = null, Action retirePrevious = null)
        {
            if (current == next)
            {
                next.SetActive(true);
                return;
            }

            Finish();
            var previous = current;
            current = next;
            if (!previous || !previous.activeInHierarchy)
            {
                if (previous)
                    previous.SetActive(false);
                retirePrevious?.Invoke();
                next.SetActive(true);
                prepare?.Invoke();
                return;
            }

            var outgoing = new Visual(previous);
            var incoming = new Visual(next);
            var events = EventSystem.current;
            if (events && events.currentSelectedGameObject
                && events.currentSelectedGameObject.transform.IsChildOf(previous.transform))
                events.SetSelectedGameObject(null);

            incoming.Group.alpha = 0f;
            next.SetActive(true);
            prepare?.Invoke();

            animation = DOTween.Sequence().SetUpdate(true)
                .Append(outgoing.Group.DOFade(0f, FadeDuration))
                .Join(incoming.Group.DOFade(incoming.Alpha, FadeDuration))
                .OnComplete(() =>
                {
                    animation = null;
                    previous.SetActive(false);
                    outgoing.Restore();
                    incoming.Restore();
                    retirePrevious?.Invoke();
                });
        }

        private sealed class Visual
        {
            private readonly bool interactable;
            private readonly bool blocksRaycasts;

            internal readonly CanvasGroup Group;
            internal readonly float Alpha;

            internal Visual(GameObject panel)
            {
                Group = panel.TryGetComponent<CanvasGroup>(out var group) ? group : panel.AddComponent<CanvasGroup>();
                Alpha = Group.alpha;
                interactable = Group.interactable;
                blocksRaycasts = Group.blocksRaycasts;
                Group.interactable = false;
                Group.blocksRaycasts = false;
            }

            internal void Restore()
            {
                Group.alpha = Alpha;
                Group.interactable = interactable;
                Group.blocksRaycasts = blocksRaycasts;
            }
        }
    }
}
