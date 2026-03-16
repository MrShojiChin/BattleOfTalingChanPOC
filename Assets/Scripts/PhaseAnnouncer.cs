using System;
using System.Collections;
using UnityEngine;
using TMPro;

/// <summary>
/// Displays a full-screen "stomp" text animation when a phase changes.
/// The text scales up from large → overshoots → settles, holds, then fades out.
/// Attach to a UI Canvas. Requires a TMP_Text child for the announcement text.
/// </summary>
public class PhaseAnnouncer : MonoBehaviour
{
    public static PhaseAnnouncer instance;

    [Header("UI References")]
    public CanvasGroup canvasGroup;      // For fading the whole overlay
    public TMP_Text   announceText;      // The big phase name text
    public GameObject  overlay;           // The overlay panel (enable/disable)

    [Header("Animation Settings")]
    public float stompDuration  = 0.25f;  // Time for the scale-up stomp
    public float overshootScale = 1.3f;   // How big it overshoots
    public float settleScale    = 1.0f;   // Final resting scale
    public float settleDuration = 0.1f;   // Time to settle from overshoot
    public float holdDuration   = 0.6f;   // How long the text stays visible
    public float fadeOutDuration = 0.3f;  // Fade-out time

    [Header("Visual Style")]
    public Color textColor = Color.white;
    public Color shadowColor = new Color(0, 0, 0, 0.5f);

    private Coroutine currentAnim;

    private void Awake()
    {
        instance = this;
        if (overlay != null)
            overlay.SetActive(false);
    }

    /// <summary>
    /// Show the phase announcement with a stomp animation.
    /// Calls onComplete when the animation finishes (so GameManager can proceed).
    /// </summary>
    public void AnnouncePhase(string phaseName, Action onComplete = null)
    {
        AnnouncePhase(phaseName, textColor, onComplete);
    }

    /// <summary>
    /// Show phase announcement with a specific text color (for player-colored phases).
    /// </summary>
    public void AnnouncePhase(string phaseName, Color color, Action onComplete = null)
    {
        if (currentAnim != null)
            StopCoroutine(currentAnim);

        currentAnim = StartCoroutine(StompAnimation(phaseName, color, onComplete));
    }

    /// <summary>
    /// Show phase announcement and wait for it to complete (for use in coroutines).
    /// Usage: yield return PhaseAnnouncer.instance.AnnouncePhaseCoroutine("Draw Phase");
    /// </summary>
    public Coroutine AnnouncePhaseCoroutine(string phaseName)
    {
        return AnnouncePhaseCoroutine(phaseName, textColor);
    }

    public Coroutine AnnouncePhaseCoroutine(string phaseName, Color color)
    {
        if (currentAnim != null)
            StopCoroutine(currentAnim);

        currentAnim = StartCoroutine(StompAnimation(phaseName, color, null));
        return currentAnim;
    }

    private IEnumerator StompAnimation(string phaseName, Color color, Action onComplete)
    {
        // Setup
        if (overlay != null) overlay.SetActive(true);
        if (announceText != null)
        {
            announceText.text = phaseName;
            announceText.color = color;
        }
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        RectTransform textRect = announceText?.GetComponent<RectTransform>();
        if (textRect == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        // ── Phase 1: Stomp in — scale from 0 → overshoot with easing ──
        float elapsed = 0f;
        while (elapsed < stompDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / stompDuration);
            // Ease-out cubic for a punchy feel
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float scale = Mathf.Lerp(0f, overshootScale, eased);
            textRect.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }
        textRect.localScale = new Vector3(overshootScale, overshootScale, 1f);

        // ── Phase 2: Settle — overshoot → final scale ──
        elapsed = 0f;
        while (elapsed < settleDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / settleDuration);
            // Ease-in-out for a smooth settle
            float eased = t * t * (3f - 2f * t);
            float scale = Mathf.Lerp(overshootScale, settleScale, eased);
            textRect.localScale = new Vector3(scale, scale, 1f);
            yield return null;
        }
        textRect.localScale = new Vector3(settleScale, settleScale, 1f);

        // ── Phase 3: Hold ──
        yield return new WaitForSeconds(holdDuration);

        // ── Phase 4: Fade out ──
        elapsed = 0f;
        while (elapsed < fadeOutDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / fadeOutDuration);
            if (canvasGroup != null) canvasGroup.alpha = 1f - t;
            yield return null;
        }

        // Cleanup
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (overlay != null) overlay.SetActive(false);
        textRect.localScale = Vector3.one;

        currentAnim = null;
        onComplete?.Invoke();
    }
}
