using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace ASAL
{
    public sealed partial class PlayPanel
    {
        private const float AttackLeadInSeconds = 0.75f;
        private const float WindupSeconds = 0.12f;
        private const float StrikeSeconds = 0.09f;
        private const float RecoverySeconds = 0.18f;

        private MatchSession attackSession;
        private AttackFeedback pendingAttack;
        private int pendingAttackRevision;
        private AttackAnimation attackAnimation;
        private Sequence attackTween;

        private void BindAttackFeedback(MatchSession session)
        {
            attackSession = session;
            if (attackSession != null)
                attackSession.AttackResolved += QueueAttackFeedback;
        }

        private void UnbindAttackFeedback()
        {
            if (attackSession != null)
                attackSession.AttackResolved -= QueueAttackFeedback;
            attackSession = null;
        }

        private void QueueAttackFeedback(AttackFeedback feedback, int revision)
        {
            if (!BoardVisible || snapshot == null || !PhotonManager.Instance)
                return;

            ClearAttackFeedback();
            pendingAttack = feedback;
            pendingAttackRevision = revision;
        }

        private void UpdateAttackFeedback()
        {
            if ((pendingAttack != null && !room.CanLeave)
                || (attackAnimation != null && !CanAnimateAttack(attackAnimation)))
            {
                ClearAttackFeedback();
                return;
            }

            // The snapshot starts any public dice roll for this attack.
            if (pendingAttack == null || snapshot.Revision < pendingAttackRevision || (hud && hud.IsDiceRolling))
                return;

            var feedback = pendingAttack;
            pendingAttack = null;
            PlayAttackFeedback(feedback);
        }

        private void PlayAttackFeedback(AttackFeedback feedback)
        {
            if (!PhotonManager.Instance)
                return;

            var views = PhotonManager.Instance.TokenViews;
            if (!views.TryGetValue(feedback.AttackerId, out var attacker) || !attacker)
                return;

            var tileSize = (ToWorld(Vector2.right) - ToWorld(Vector2.zero)).magnitude;
            var animation = new AttackAnimation
            {
                Attacker = attacker,
                TileSize = tileSize,
                CameraStrength = Controls(attacker) ? 0.0025f : 0f
            };

            foreach (var outcome in feedback.Targets)
            {
                if (string.IsNullOrEmpty(outcome?.TokenId) || !views.TryGetValue(outcome.TokenId, out var target)
                    || !target || target == attacker)
                    continue;

                // The resolver orders targets by distance, breaking ties by token ID.
                if (!animation.PrimaryTarget)
                {
                    animation.PrimaryTarget = target;
                    animation.Direction = AttackDirection(attacker, target);
                }
                if (outcome.Damage > 0)
                {
                    float intensity = (outcome.Damage - 1) / 19f;
                    animation.Reactions.Add(new DefenderReaction(target, Vector2.zero,
                        tileSize * Mathf.Lerp(0.10f, 0.25f, intensity)));
                    if (Controls(target))
                        animation.CameraStrength = Mathf.Max(animation.CameraStrength, Mathf.Lerp(0.006f, 0.014f, intensity));
                }
                else if (outcome.Dodged)
                {
                    Vector2 sidestep = Vector2.Perpendicular(AttackDirection(attacker, target)) * (tileSize * 0.5f);
                    animation.Reactions.Add(new DefenderReaction(target, sidestep, 0f));
                }
            }

            if (!CanAnimateAttack(animation))
                return;

            attackAnimation = animation;
            int impactFrame = -1;
            attackTween = DOTween.Sequence().SetUpdate(UpdateType.Late, true).SetAutoKill(true)
                .AppendInterval(AttackLeadInSeconds)
                .Append(DOVirtual.Float(0f, 1f, WindupSeconds, progress => RenderAttack(animation, progress, 0f, 0f))
                    .SetEase(Ease.OutQuad))
                .Append(DOVirtual.Float(0f, 1f, StrikeSeconds, progress => RenderAttack(animation, 1f, progress, 0f))
                    .SetEase(Ease.Linear))
                .AppendCallback(() =>
                {
                    impactFrame = Time.frameCount;
                    RenderAttack(animation, 1f, 1f, 0f);
                })
                .Append(DOVirtual.Float(0f, 1f, RecoverySeconds, progress =>
                {
                    // Keep a visible contact frame even if the tween crosses impact between updates.
                    if (Time.frameCount != impactFrame)
                        RenderAttack(animation, 1f, 1f, progress);
                }).SetEase(Ease.Linear))
                .OnKill(ResetAttackOffsets);
        }

        private bool Controls(TokenView view) => view.OwnerActor == 0 ? room.IsDm : view.OwnerActor == room.LocalActor;

        private static Vector2 AttackDirection(TokenView attacker, TokenView target)
        {
            var direction = ((Vector2)(target.transform.position - attacker.transform.position)).normalized;
            return direction == Vector2.zero ? Vector2.right : direction;
        }

        private bool CanAnimateAttack(AttackAnimation animation) => room.CanLeave
            && animation.Attacker && animation.Attacker.isActiveAndEnabled
            && animation.PrimaryTarget && animation.PrimaryTarget.isActiveAndEnabled;

        private void RenderAttack(AttackAnimation animation, float windup, float strike, float recovery)
        {
            if (!CanAnimateAttack(animation))
                return;

            Vector2 pullback = -animation.Direction * (animation.TileSize * 0.25f);
            Vector2 reach = animation.PrimaryTarget.transform.position - animation.Attacker.transform.position;
            Vector2 motion = Vector2.Lerp(pullback * windup, reach, strike) * (1f - EaseOut(recovery));
            Vector2 shake = Shake(recovery);
            animation.Attacker.SetVisualOffset(motion + shake * (animation.TileSize * 0.04f));

            foreach (var reaction in animation.Reactions)
            {
                if (reaction.View && reaction.View.isActiveAndEnabled)
                {
                    var dodge = reaction.Sidestep * (EaseOut(strike) * (1f - EaseOut(recovery)));
                    reaction.View.SetVisualOffset(dodge + shake * reaction.ShakeStrength);
                }
            }

            float cameraHeight = playCamera.orthographicSize * 2f;
            playCamera.transform.localPosition = shake * (cameraHeight * animation.CameraStrength);
        }

        private void ClearAttackFeedback()
        {
            pendingAttack = null;
            attackTween?.Kill();
        }

        private void ResetAttackOffsets()
        {
            attackTween = null;
            var animation = attackAnimation;
            if (animation == null)
                return;
            attackAnimation = null;
            if (animation.Attacker)
                animation.Attacker.SetVisualOffset(Vector2.zero);
            foreach (var reaction in animation.Reactions)
                if (reaction.View)
                    reaction.View.SetVisualOffset(Vector2.zero);
            if (playCamera)
                playCamera.transform.localPosition = Vector3.zero;
        }

        private static float EaseOut(float progress) => progress * (2f - progress);

        private static Vector2 Shake(float progress) =>
            new Vector2(Mathf.Sin(progress * 5f * Mathf.PI), Mathf.Sin(progress * 7f * Mathf.PI) * 0.65f).normalized
            * (1f - progress) * Mathf.Min(1f, progress * 16f);

        private sealed class AttackAnimation
        {
            internal TokenView Attacker;
            internal TokenView PrimaryTarget;
            internal Vector2 Direction;
            internal float TileSize;
            internal float CameraStrength;
            internal readonly List<DefenderReaction> Reactions = new();
        }

        private readonly struct DefenderReaction
        {
            internal readonly TokenView View;
            internal readonly Vector2 Sidestep;
            internal readonly float ShakeStrength;

            internal DefenderReaction(TokenView view, Vector2 sidestep, float shakeStrength)
            {
                View = view;
                Sidestep = sidestep;
                ShakeStrength = shakeStrength;
            }
        }
    }
}
