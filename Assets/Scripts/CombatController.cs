using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Photon.Pun;

/// <summary>
/// Handles all Battle Phase combat logic.
///
/// Flow:
///   1. Player enters Battle Phase
///   2. Click your Avatar on board → Select Attacker
///   3. Click enemy Avatar → Resolve Combat (power vs power)
///   4. Right-click or click same card → Cancel selection
///   5. Repeat or End Phase
///
/// Separate from BattleController (which handles summoning in Main Phase).
///
/// NOTE: Life cards can't be detected through the EventSystem (their rotated
///       Canvas blocks GraphicRaycaster). Instead, Update() uses a direct
///       world-space proximity check to detect clicks on LIFE cards.
/// </summary>
public enum CombatState { Idle, SelectingAttacker, SelectingTarget, AwaitingLifeReveal }

public class CombatController : MonoBehaviour
{
    public static CombatController instance;

    // ── STATE ──────────────────────────────────────────────────────
    [Header("Combat State (Read Only)")]
    public CombatState combatState = CombatState.Idle;
    public Card selectedAttacker;

    // ── LIFE REVEAL STATE ────────────────────────────────────────
    private Card _pendingRevealLifeCard;       // Life card waiting for reveal panel dismiss
    private Coroutine _lifeRevealAutoClose;    // Auto-dismiss coroutine

    private void Awake()
    {
        instance = this;
    }

    // ════════════════════════════════════════════════════════════════
    //  LIFE CARD CLICK DETECTION (direct raycast — bypasses EventSystem)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// During Battle Phase, detects left-clicks on LIFE cards by casting a ray
    /// from the camera onto the board plane and checking proximity to each LIFE card.
    /// This bypasses Unity's EventSystem which can't detect the rotated cards.
    /// </summary>
    private void Update()
    {
        // Only active during Battle Phase and when combat is in progress
        if (combatState == CombatState.Idle) return;
        GameManager gm = GameManager.instance;
        if (gm == null || gm.isGameOver || !gm.IsBattlePhase()) return;
        if (Mouse.current == null) return;

        // In multiplayer: only process input if it's our turn
        if (PhotonNetwork.IsConnected && !NetworkIdentity.IsMyTurn) return;

        // ── Click-anywhere to dismiss Life Reveal panel ──
        if (combatState == CombatState.AwaitingLifeReveal)
        {
            if (Mouse.current.leftButton.wasPressedThisFrame
                || Mouse.current.rightButton.wasPressedThisFrame)
            {
                if (PhotonNetwork.IsConnected && GameManager.instance != null)
                    GameManager.instance.photonView.RPC("RPC_LifeRevealContinue", RpcTarget.All);
                else
                    OnLifeRevealContinue();
            }
            return; // Block all other combat input while panel is shown
        }

        // Detect left-click
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Card lifeCard = RaycastForLifeCard();
            if (lifeCard != null)
            {
                Debug.Log($"[Combat] Direct-detected click on LIFE card '{lifeCard.cardName}'");
                if (PhotonNetwork.IsConnected && GameManager.instance != null && lifeCard.assignedPlace != null)
                {
                    string zoneName = lifeCard.assignedPlace.gameObject.name;
                    if (combatState == CombatState.SelectingAttacker)
                        GameManager.instance.photonView.RPC("RPC_SelectAttacker", RpcTarget.All, zoneName);
                    else if (combatState == CombatState.SelectingTarget)
                        GameManager.instance.photonView.RPC("RPC_SelectTarget", RpcTarget.All, zoneName);
                }
                else
                {
                    HandleCardClick(lifeCard, false);
                }
            }
        }
        // Detect right-click (for cancel)
        else if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            Card lifeCard = RaycastForLifeCard();
            if (lifeCard != null)
            {
                if (PhotonNetwork.IsConnected && GameManager.instance != null)
                    GameManager.instance.photonView.RPC("RPC_CancelAttack", RpcTarget.All);
                else
                    HandleCardClick(lifeCard, true);
            }
        }
    }

    /// <summary>
    /// Casts a ray from the camera through the mouse position onto the board plane,
    /// then checks if any LIFE card is close enough to count as a click.
    /// Returns the closest LIFE card hit, or null.
    /// </summary>
    private Card RaycastForLifeCard()
    {
        Camera cam = Camera.main;
        if (cam == null) return null;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());

        // Intersect the ray with the board plane (Y ≈ 0)
        // LIFE cards sit at approximately Y = 0 on the board
        Plane boardPlane = new Plane(Vector3.up, Vector3.zero);
        if (!boardPlane.Raycast(ray, out float distance)) return null;

        Vector3 worldClickPos = ray.GetPoint(distance);

        // Check all LIFE cards on both players
        Card closest = null;
        float closestDist = float.MaxValue;
        float clickRadius = 1.2f; // How close the click must be (generous for horizontal cards)

        GameManager gm = GameManager.instance;
        Player[] players = { gm.player1, gm.player2 };

        foreach (Player p in players)
        {
            if (p == null || p.lifeZones == null) continue;
            foreach (CardPlacePoint zone in p.lifeZones)
            {
                if (zone == null || zone.activeCard == null) continue;
                Card card = zone.activeCard;
                if (!card.isLifeCard) continue;

                // Use XZ distance (ignoring Y)
                float dx = worldClickPos.x - card.transform.position.x;
                float dz = worldClickPos.z - card.transform.position.z;
                float dist = Mathf.Sqrt(dx * dx + dz * dz);

                if (dist < clickRadius && dist < closestDist)
                {
                    closestDist = dist;
                    closest = card;
                }
            }
        }

        return closest;
    }

    // ════════════════════════════════════════════════════════════════
    //  PHASE ENTRY / EXIT (called by GameManager)
    // ════════════════════════════════════════════════════════════════

    /// <summary>Called when GameManager enters Battle Phase.</summary>
    public void BeginBattlePhase()
    {
        combatState = CombatState.SelectingAttacker;
        selectedAttacker = null;

        Player op = GameManager.instance.OpponentPlayerObj;

        // ── Guard: no targets at all (no avatars AND no unflipped LIFE cards) ──
        if (op.AvatarsOnField == 0 && op.UnflippedLifeCards == 0)
        {
            if (op.IsSahat)
            {
                // สาหัส! Allow direct hit on life zone — important, keep this message
                UIController.instance.ShowCombatUI(
                    "<color=#FF0000><b>\u0E2A\u0E32\u0E2B\u0E31\u0E2A!</b></color> Enemy's LIFE is exposed!\nSelect an Avatar to deliver the finishing blow!");
                Debug.Log("[Combat] Opponent is สาหัส — direct hit available!");
            }
            else
            {
                combatState = CombatState.Idle;
                Debug.Log("[Combat] No enemy avatars or LIFE cards — Battle Phase has nothing to do.");
                return;
            }
        }
    }

    /// <summary>Called when GameManager leaves Battle Phase.</summary>
    public void EndBattlePhase()
    {
        HighlightValidTargets(false);

        if (selectedAttacker != null)
            selectedAttacker.SetAttackHighlight(false);

        combatState = CombatState.Idle;
        selectedAttacker = null;
        UIController.instance.HideCombatUI();
    }

    // ════════════════════════════════════════════════════════════════
    //  CARD CLICK HANDLER (called by Card.OnPointerDown during Battle)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Routes a card click based on the current combat state.
    /// </summary>
    public void HandleCardClick(Card card, bool isRightClick)
    {
        // Right-click always cancels
        if (isRightClick)
        {
            CancelAttackSelection();
            return;
        }

        switch (combatState)
        {
            case CombatState.SelectingAttacker:
                TrySelectAttacker(card);
                break;
            case CombatState.SelectingTarget:
                TrySelectTarget(card);
                break;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  STEP 1 — SELECT ATTACKER
    // ════════════════════════════════════════════════════════════════

    private void TrySelectAttacker(Card card)
    {
        GameManager gm = GameManager.instance;

        // LIFE cards can never be used as attackers
        if (card.isLifeCard)
        {
            Debug.Log($"[Combat] {card.cardName} is a LIFE card — can't attack with it. (owner={card.cardOwner}, current={gm.currentPlayer})");
            return;
        }

        // Must be YOUR avatar, on the board
        if (card.cardOwner != gm.currentPlayer)
        {
            Debug.Log($"[Combat] {card.cardName} is not your card.");
            return;
        }
        if (card.inHand)
        {
            Debug.Log($"[Combat] {card.cardName} is in hand, not on the board.");
            return;
        }
        if (card.cardType != CardType.Avatar)
        {
            Debug.Log($"[Combat] {card.cardName} is not an Avatar.");
            return;
        }

        // Can't attack if tapped (already attacked this turn)
        if (card.isTapped)
        {
            Debug.Log($"[Combat] {card.cardName} is tapped (นอน) — already attacked.");
            return;
        }

        // NOTE: No summon sickness — avatars CAN attack on the turn they're placed.
        // P1's Turn 1 battle skip is handled by GameManager.OnNextPhasePressed().

        // Check if opponent has any targets (avatars, unflipped LIFE cards, or สาหัส direct hit)
        Player op = gm.OpponentPlayerObj;
        if (op.AvatarsOnField == 0 && op.UnflippedLifeCards == 0 && !op.IsSahat)
        {
            Debug.Log("[Combat] No enemy targets — can't select attacker.");
            return;
        }

        // Select this avatar as attacker
        selectedAttacker = card;
        card.SetAttackHighlight(true);
        combatState = CombatState.SelectingTarget;

        // NOTE: Attacker abilities (AttackPowerBoost, CombatThonSoop) are triggered
        // in TrySelectTarget() when the target is confirmed, NOT here at selection time.
        // This prevents buffs from persisting if the attack is cancelled.

        // Highlight valid targets
        HighlightValidTargets(true);

        Debug.Log($"[Combat] Selected attacker: {card.cardName} (power {card.power})");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogCombat($"{card.cardName} (Pw:{card.power}) selected as attacker.");
    }

    // ════════════════════════════════════════════════════════════════
    //  STEP 2 — SELECT TARGET
    // ════════════════════════════════════════════════════════════════

    private void TrySelectTarget(Card card)
    {
        GameManager gm = GameManager.instance;

        // Clicking your own attacker again → cancel
        if (card == selectedAttacker)
        {
            CancelAttackSelection();
            return;
        }

        // Clicking another of your own avatars → switch attacker
        if (card.cardOwner == gm.currentPlayer && !card.inHand
            && card.cardType == CardType.Avatar && !card.isLifeCard)
        {
            // Deselect old attacker, select new one
            selectedAttacker.SetAttackHighlight(false);
            TrySelectAttacker(card);
            return;
        }

        // Must be an ENEMY card on the board
        if (card.cardOwner == gm.currentPlayer)
        {
            Debug.Log("[Combat] Can't target your own card.");
            return;
        }
        if (card.inHand) return;

        // ── LIFE CARD TARGETING ──
        if (card.isLifeCard)
        {
            Player op = gm.OpponentPlayerObj;
            Debug.Log($"[Combat] Target is LIFE card '{card.cardName}' (faceDown={card.isFaceDown}). Enemy avatars on field: {op.AvatarsOnField}");

            // Power 0 avatars cannot hit LIFE cards
            if (selectedAttacker.power <= 0)
            {
                Debug.Log($"[Combat] {selectedAttacker.cardName} has Power {selectedAttacker.power} — cannot attack LIFE cards.");
                UIController.instance.ShowCombatUI(
                    $"<color=red>{selectedAttacker.cardName}</color> has <b>0 Power</b> — cannot attack LIFE cards!");
                return;
            }

            // Can only attack LIFE cards when no enemy avatars exist
            if (op.AvatarsOnField > 0)
            {
                Debug.Log("[Combat] Can't attack LIFE cards while enemy has Avatars on field.");
                return;
            }

            // ── DIRECT HIT (สาหัส) — face-up life card ──
            if (!card.isFaceDown && op.IsSahat)
            {
                StartCoroutine(EngageAndResolveDirectHit(selectedAttacker, card));
                return;
            }

            // ── Normal LIFE card attack — flip face-down card ──
            if (card.isFaceDown)
            {
                StartCoroutine(EngageAndResolveLifeCardAttack(selectedAttacker, card));
                return;
            }

            // Face-up but not สาหัส — can't target
            Debug.Log("[Combat] Can't target already-flipped LIFE cards.");
            return;
        }

        // ── AVATAR TARGETING (normal combat) ──
        if (card.cardType != CardType.Avatar)
        {
            Debug.Log("[Combat] Can only target enemy Avatars or face-down LIFE cards.");
            return;
        }

        // Trigger abilities then resolve — uses coroutine so power boosts apply before combat
        StartCoroutine(EngageAndResolveCombat(selectedAttacker, card));
    }

    // ════════════════════════════════════════════════════════════════
    //  STEP 2.5 — ENGAGE ABILITIES THEN RESOLVE
    // ════════════════════════════════════════════════════════════════

    private IEnumerator EngageAndResolveCombat(Card attacker, Card defender)
    {
        // Wait for attacker abilities (e.g. CombatThonSoop, AttackPowerBoost)
        if (AvatarAbilityController.instance != null)
            yield return StartCoroutine(AvatarAbilityController.instance.OnCombatEngagementRoutine(attacker, true));

        // Wait for defender abilities
        if (AvatarAbilityController.instance != null)
            yield return StartCoroutine(AvatarAbilityController.instance.OnCombatEngagementRoutine(defender, false));

        ResolveCombat(attacker, defender);
    }

    private IEnumerator EngageAndResolveDirectHit(Card attacker, Card target)
    {
        if (AvatarAbilityController.instance != null)
            yield return StartCoroutine(AvatarAbilityController.instance.OnCombatEngagementRoutine(attacker, true));

        Debug.Log($"[Combat] DIRECT HIT! {attacker.cardName} → {target.cardOwner}'s LIFE zone (สาหัส)!");
        ResolveDirectHit(attacker, target);
    }

    private IEnumerator EngageAndResolveLifeCardAttack(Card attacker, Card target)
    {
        if (AvatarAbilityController.instance != null)
            yield return StartCoroutine(AvatarAbilityController.instance.OnCombatEngagementRoutine(attacker, true));

        Debug.Log($"[Combat] Resolving LIFE card attack: {attacker.cardName} → {target.cardName}");
        ResolveLifeCardAttack(attacker, target);
    }

    // ════════════════════════════════════════════════════════════════
    //  STEP 3 — RESOLVE COMBAT
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// In multiplayer: only MasterClient calculates combat outcome and broadcasts via RPC.
    /// In local/offline mode: resolves directly.
    /// </summary>
    private void ResolveCombat(Card attacker, Card defender)
    {
        if (PhotonNetwork.IsConnected)
        {
            // Only MasterClient determines combat outcome — prevents desync
            if (!PhotonNetwork.IsMasterClient) return;

            int atkPower = attacker.power;
            int defPower = defender.power;
            string atkZone = attacker.assignedPlace != null ? attacker.assignedPlace.gameObject.name : "";
            string defZone = defender.assignedPlace != null ? defender.assignedPlace.gameObject.name : "";

            GameManager.instance.photonView.RPC("RPC_ResolveCombatResult",
                RpcTarget.All, atkZone, defZone, atkPower, defPower);
            return;
        }

        // Local/offline mode: resolve directly
        ExecuteCombatResult(attacker, defender, attacker.power, defender.power);
    }

    /// <summary>
    /// Applies the authoritative combat result on all clients.
    /// Called from RPC_ResolveCombatResult (multiplayer) or ResolveCombat (local).
    /// Power values come from MasterClient to ensure both clients agree.
    /// </summary>
    public void ExecuteCombatResult(Card attacker, Card defender, int atkPower, int defPower)
    {
        // Sync local power to authoritative values from MasterClient
        if (PhotonNetwork.IsConnected)
        {
            attacker.power = atkPower;
            attacker.RefreshPowerDisplay();
            defender.power = defPower;
            defender.RefreshPowerDisplay();
        }

        Debug.Log($"[Combat] {attacker.cardName} (ATK {atkPower}) vs {defender.cardName} (DEF {defPower})");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogCombat($"{attacker.cardName} ({atkPower}) vs {defender.cardName} ({defPower})");

        // Tap the attacker (นอน — lay down)
        attacker.SetTapped(true);
        attacker.SetAttackHighlight(false);

        // ── SPECIAL CASE: Power 0 vs Power 0 = nothing happens (rulebook) ──
        if (atkPower == 0 && defPower == 0)
        {
            UIController.instance.ShowCombatUI(
                $"Both {attacker.cardName} and {defender.cardName} have 0 Power — nothing happens!");
            Debug.Log("[Combat] Power 0 vs 0 — no destruction.");
            if (GameplayLogger.instance != null)
                GameplayLogger.instance.LogCombat("Both have 0 Power — nothing happens.");

            // Reset for next attack
            selectedAttacker = null;
            combatState = CombatState.SelectingAttacker;
            return;
        }

        if (atkPower > defPower)
        {
            // Attacker wins → defender sent directly to hell (no rotation)
            UIController.instance.ShowCombatUI(
                $"<color=green><b>{attacker.cardName}</b> ({atkPower})</color> defeats " +
                $"<color=red>{defender.cardName} ({defPower})</color>!");

            if (GameplayLogger.instance != null)
                GameplayLogger.instance.LogCombat($"{attacker.cardName} defeats {defender.cardName}!");
            combatState = CombatState.Idle;
            HighlightValidTargets(false);
            StartCoroutine(DirectSendToHell(defender));
            return;
        }
        else if (defPower > atkPower)
        {
            // Defender wins → attacker flips face-down then destroyed
            UIController.instance.ShowCombatUI(
                $"<color=green>{defender.cardName} ({defPower})</color> defeats " +
                $"<color=red><b>{attacker.cardName}</b> ({atkPower})</color>!");

            if (GameplayLogger.instance != null)
                GameplayLogger.instance.LogCombat($"{defender.cardName} defeats {attacker.cardName}!");
            combatState = CombatState.Idle;
            HighlightValidTargets(false);
            StartCoroutine(FlipAndSendToHell(attacker));
            return;
        }
        else
        {
            // Equal power → only attacker rotates, then both go to hell
            UIController.instance.ShowCombatUI(
                $"<color=yellow>DRAW!</color> Both {attacker.cardName} and {defender.cardName} " +
                $"destroyed ({atkPower} = {defPower})!");

            if (GameplayLogger.instance != null)
                GameplayLogger.instance.LogCombat($"DRAW! Both {attacker.cardName} and {defender.cardName} destroyed!");
            combatState = CombatState.Idle;
            HighlightValidTargets(false);
            StartCoroutine(DrawRotateAndSendToHell(attacker, defender));
            return;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  STEP 3B — RESOLVE LIFE CARD ATTACK
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolve an attack on a face-down LIFE card.
    /// No power comparison — the LIFE card simply flips face-up (revealed).
    /// Then checks for สหัส (all 5 LIFE flipped = game over).
    /// </summary>
    private void ResolveLifeCardAttack(Card attacker, Card lifeCard)
    {
        Debug.Log($"[Combat] {attacker.cardName} attacks {lifeCard.cardOwner}'s LIFE card!");

        // Tap the attacker (นอน — lay down)
        attacker.SetTapped(true);
        attacker.SetAttackHighlight(false);

        // Flip the LIFE card face-up (reveal it)
        lifeCard.FlipLifeCard();

        // White blink on the attacked LIFE card
        StartCoroutine(BlinkLifeCard(lifeCard, Color.white, 5));

        // Clear target highlights
        HighlightValidTargets(false);

        // ── SHOW LIFE REVEAL PANEL ──
        // Pause combat and show flavor text. Combat resumes when player
        // clicks Continue or after auto-dismiss (5 seconds).
        _pendingRevealLifeCard = lifeCard;
        combatState = CombatState.AwaitingLifeReveal;

        UIController.instance.ShowLifeRevealPanel(lifeCard);

        // Auto-dismiss after 5 seconds
        if (_lifeRevealAutoClose != null)
            StopCoroutine(_lifeRevealAutoClose);
        _lifeRevealAutoClose = StartCoroutine(AutoDismissLifeReveal(5f));

        Debug.Log($"[Combat] LIFE card revealed: {lifeCard.cardName}. Showing flavor text panel.");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogLife($"{attacker.cardName} attacks LIFE! Revealed: {lifeCard.cardName}");
    }

    // ════════════════════════════════════════════════════════════════
    //  STEP 3C — RESOLVE DIRECT HIT (สาหัส finishing blow)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolve a direct hit on a สาหัส player's life zone.
    /// This is the finishing blow — the game ends immediately.
    /// </summary>
    private void ResolveDirectHit(Card attacker, Card lifeCard)
    {
        Debug.Log($"[Combat] DIRECT HIT! {attacker.cardName} attacks {lifeCard.cardOwner}'s LIFE zone!");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogCombat($"DIRECT HIT! {attacker.cardName} delivers the finishing blow!");

        // Tap the attacker
        attacker.SetTapped(true);
        attacker.SetAttackHighlight(false);
        HighlightValidTargets(false);

        // Show direct hit message
        UIController.instance.ShowCombatUI(
            $"<color=#FF0000><b>DIRECT HIT!</b></color>\n" +
            $"{attacker.cardName} delivers the finishing blow!");

        // End combat and the game
        selectedAttacker = null;
        combatState = CombatState.Idle;
        GameManager.instance.EndGameDirectHit(lifeCard.cardOwner);
    }

    /// <summary>
    /// Auto-dismiss the Life Reveal panel after a delay.
    /// </summary>
    private IEnumerator AutoDismissLifeReveal(float delay)
    {
        yield return new WaitForSeconds(delay);
        OnLifeRevealContinue();
    }

    /// <summary>
    /// Called when the player clicks Continue on the Life Reveal panel,
    /// or when the auto-dismiss timer fires.
    /// Queues the delayed draw, resolves effects, and resumes combat.
    /// </summary>
    public void OnLifeRevealContinue()
    {
        // Guard: only process if we're actually awaiting reveal
        if (combatState != CombatState.AwaitingLifeReveal) return;

        // Stop auto-dismiss if player clicked early
        if (_lifeRevealAutoClose != null)
        {
            StopCoroutine(_lifeRevealAutoClose);
            _lifeRevealAutoClose = null;
        }

        // Hide the reveal panel
        UIController.instance.HideLifeRevealPanel();

        Card lifeCard = _pendingRevealLifeCard;
        _pendingRevealLifeCard = null;

        if (lifeCard == null) return;

        // ── QUEUE DELAYED DRAW: "In next Main Phase, draw 1 card" ──
        Player lifeOwner = GameManager.instance.GetPlayer(lifeCard.cardOwner);
        lifeOwner.pendingLifeDraws++;
        Debug.Log($"[LIFE] {lifeOwner.playerId} queued +1 draw for next Main Phase (total pending: {lifeOwner.pendingLifeDraws}).");

        // ── TRIGGER ON-FLIP EFFECT (if any additional effect beyond common draw) ──
        if (lifeCard.lifeCardSO != null && lifeCard.lifeCardSO.onFlipEffect != MagicEffect.None)
        {
            MagicEffectResolver.ResolveLifeFlipEffect(lifeCard);
            Debug.Log($"[LIFE] On-flip effect triggered: {lifeCard.lifeCardSO.onFlipEffect} ({lifeCard.lifeCardSO.onFlipValue})");
        }

        // ── UPDATE UI ──
        int remaining = 5 - lifeOwner.LifeCardsFlipped;
        string resultMsg;
        if (lifeOwner.IsSahat)
        {
            // All 5 LIFE cards flipped — สาหัส!
            resultMsg =
                $"<color=#FFD700><b>LIFE card revealed: {lifeCard.cardName}</b></color>\n" +
                $"<color=#FF0000><b>\u0E2A\u0E32\u0E2B\u0E31\u0E2A!</b> All LIFE cards are exposed!</color>\n" +
                "<color=#00FFFF>\u0E43\u0E19 Main Phase \u0E16\u0E31\u0E14\u0E44\u0E1B \u0E08\u0E31\u0E48\u0E27\u0E01\u0E32\u0E23\u0E4C\u0E14 1 \u0E43\u0E1A</color>";
        }
        else
        {
            resultMsg =
                $"<color=#FFD700><b>LIFE card revealed: {lifeCard.cardName}</b></color>\n" +
                $"LIFE remaining: {remaining}/5\n" +
                "<color=#00FFFF>\u0E43\u0E19 Main Phase \u0E16\u0E31\u0E14\u0E44\u0E1B \u0E08\u0E31\u0E48\u0E27\u0E01\u0E32\u0E23\u0E4C\u0E14 1 \u0E43\u0E1A</color>";
        }
        UIController.instance.ShowCombatUI(resultMsg);
        UIController.instance.UpdateGameInfo();

        // Check win condition (announces สาหัส status but doesn't end game)
        GameManager.instance.CheckWinCondition();

        // สาหัส red blink on all 5 LIFE cards
        if (lifeOwner.IsSahat)
            StartCoroutine(BlinkAllLifeCards(lifeOwner, Color.red, 5));

        // Resume combat for next attack
        selectedAttacker = null;
        combatState = CombatState.SelectingAttacker;
    }

    // ════════════════════════════════════════════════════════════════
    //  COMBAT ANIMATION — rotate loser 90° before sending to hell
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Attacker wins: defender gets a brief red glow flash then sent directly to hell (no rotation).
    /// </summary>
    private IEnumerator DirectSendToHell(Card card)
    {
        card.isAnimating = true;

        float duration = 0.3f;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            card.SetDeathGlow(Color.red, t);
            yield return null;
        }

        card.SetDeathGlow(Color.red, 0f);
        card.isAnimating = false;

        SendToHell(card);

        UIController.instance.UpdateGameInfo();

        selectedAttacker = null;
        combatState = CombatState.SelectingAttacker;
    }

    /// <summary>
    /// Attacker loses: attacker rotates 90° on Y-axis then sent to hell.
    /// </summary>
    private IEnumerator FlipAndSendToHell(Card loser)
    {
        loser.isAnimating = true;

        float duration = 0.4f;
        float elapsed = 0f;

        Quaternion startRot = loser.transform.rotation;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float angle = t * 90f;

            loser.transform.rotation = startRot * Quaternion.Euler(0f, angle, 0f);

            loser.SetDeathGlow(Color.red, t);

            yield return null;
        }

        loser.SetDeathGlow(Color.red, 0f);
        loser.isAnimating = false;

        SendToHell(loser);

        UIController.instance.UpdateGameInfo();

        selectedAttacker = null;
        combatState = CombatState.SelectingAttacker;
    }

    // ════════════════════════════════════════════════════════════════
    //  DRAW ANIMATION — only attacker rotates, then both go to hell
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// On draw (equal power), only the attacking card rotates 90° (Y-axis).
    /// Then both cards are sent to hell.
    /// </summary>
    private IEnumerator DrawRotateAndSendToHell(Card attacker, Card defender)
    {
        attacker.isAnimating = true;

        float duration = 0.5f;
        float elapsed = 0f;

        Quaternion atkStartRot = attacker.transform.rotation;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float angle = t * 90f;

            attacker.transform.rotation = atkStartRot * Quaternion.Euler(0f, angle, 0f);

            attacker.SetDeathGlow(Color.red, t);

            yield return null;
        }

        attacker.SetDeathGlow(Color.red, 0f);
        attacker.isAnimating = false;

        SendToHell(attacker);
        SendToHell(defender);

        UIController.instance.UpdateGameInfo();

        selectedAttacker = null;
        combatState = CombatState.SelectingAttacker;
    }

    // ════════════════════════════════════════════════════════════════
    //  SEND TO HELL (destroy a card from the board)
    // ════════════════════════════════════════════════════════════════

    public void SendToHell(Card card)
    {
        // ── Remove from Land buff tracking if this avatar was land-buffed ──
        if (MagicController.instance != null)
            MagicController.instance.RemoveLandBuff(card);

        // ── Modification cascade: destroy all mods attached to this avatar ──
        if (card.attachedMods != null && card.attachedMods.Count > 0)
        {
            // Clear mod highlight before destroying mods
            card.SetModHighlight(false);

            var modsToDestroy = new System.Collections.Generic.List<Card>(card.attachedMods);
            card.attachedMods.Clear();
            foreach (Card mod in modsToDestroy)
            {
                // Undo the buff before destroying
                MagicEffectResolver.UndoEffect(mod, card);
                mod.equippedTo = null;
                mod.SetModHighlight(false); // Clear mod card's own highlight
                SendToHell(mod); // Recursive — sends each mod to Hell too
            }
            card.RefreshPowerDisplay();
        }

        // ── If this card IS a modification, unlink from its avatar ──
        if (card.equippedTo != null)
        {
            card.equippedTo.attachedMods.Remove(card);
            card.equippedTo.RefreshModHighlight();  // Update highlight (may change color or turn off)
            card.equippedTo = null;
            card.SetModHighlight(false); // Clear this mod card's own highlight
        }

        // Clear board slot — handle both single-card and multi-card zones
        if (card.assignedPlace != null)
        {
            if (card.assignedPlace.isMultiCardZone)
                card.assignedPlace.RemoveCard(card);
            else
            {
                // Only clear activeCard if this card is actually the one registered
                if (card.assignedPlace.activeCard == card)
                    card.assignedPlace.activeCard = null;
                else if (card.assignedPlace.activeCard != null)
                    Debug.LogWarning($"[SendToHell] {card.cardName} was in {card.assignedPlace.name} " +
                                     $"but activeCard is {card.assignedPlace.activeCard.cardName} — possible stacking bug!");
                card.assignedPlace = null;
            }
        }

        // Reset card state
        card.inHand = false;
        card.isTapped = false;
        card.SetAttackHighlight(false);
        card.SetTapped(false);

        // Re-activate hidden mod cards (PowerBoost mods are hidden while attached)
        if (!card.gameObject.activeSelf)
            card.gameObject.SetActive(true);

        // Move to owner's hell zone
        Player cardOwner = GameManager.instance.GetPlayer(card.cardOwner);
        if (cardOwner != null && cardOwner.hellZone != null)
        {
            cardOwner.hellZone.AddCard(card);
            Debug.Log($"[Combat] {card.cardName} → {card.cardOwner}'s Hell Zone.");
            if (GameplayLogger.instance != null)
                GameplayLogger.instance.LogCombat($"{card.cardName} → Hell Zone.");
        }

        // Ability 5: Recalculate HellPowerScaling (a card entered Hell)
        if (AvatarAbilityController.instance != null)
        {
            AvatarAbilityController.instance.RecalculateAllHellPowerScaling();
            AvatarAbilityController.instance.RecalculateAllAuraBuffs();
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  TARGET HIGHLIGHTING
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Highlight or un-highlight all valid enemy targets.
    /// When selecting a target: enemy avatars glow green (ready), or LIFE cards glow green if no avatars.
    /// </summary>
    private void HighlightValidTargets(bool on)
    {
        GameManager gm = GameManager.instance;
        if (gm == null) return;

        Player op = gm.OpponentPlayerObj;

        // Power 0 attackers cannot target LIFE cards — only highlight enemy avatars
        bool canTargetLife = selectedAttacker == null || selectedAttacker.power > 0;

        if (op.AvatarsOnField > 0)
        {
            // Highlight enemy avatars
            foreach (var zone in op.avatarZones)
            {
                if (zone != null && zone.activeCard != null)
                    zone.activeCard.SetReadyHighlight(on);
            }
        }
        else if (canTargetLife && op.IsSahat)
        {
            // สาหัส: highlight ALL life cards (even face-up) for direct hit
            foreach (var zone in op.lifeZones)
            {
                if (zone != null && zone.activeCard != null
                    && zone.activeCard.isLifeCard)
                    zone.activeCard.SetReadyHighlight(on);
            }
        }
        else if (canTargetLife)
        {
            // Highlight unflipped LIFE cards
            foreach (var zone in op.lifeZones)
            {
                if (zone != null && zone.activeCard != null
                    && zone.activeCard.isLifeCard && zone.activeCard.isFaceDown)
                    zone.activeCard.SetReadyHighlight(on);
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  CANCEL
    // ════════════════════════════════════════════════════════════════

    public void CancelAttackSelection()
    {
        // Clear target highlights
        HighlightValidTargets(false);

        if (selectedAttacker != null)
        {
            selectedAttacker.SetAttackHighlight(false);
            Debug.Log($"[Combat] Cancelled attack with {selectedAttacker.cardName}.");
        }

        selectedAttacker = null;
        combatState = CombatState.SelectingAttacker;
        UIController.instance.HideCombatUI();
    }

    // ════════════════════════════════════════════════════════════════
    //  LIFE CARD BLINK EFFECTS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Blink a single LIFE card's glow on/off.
    /// Used when a LIFE card is attacked (white blink).
    /// </summary>
    private IEnumerator BlinkLifeCard(Card card, Color color, int blinks)
    {
        float onTime = 0.15f;
        float offTime = 0.1f;

        for (int i = 0; i < blinks; i++)
        {
            card.SetDeathGlow(color, 0.8f);
            yield return new WaitForSeconds(onTime);
            card.SetDeathGlow(color, 0f);
            yield return new WaitForSeconds(offTime);
        }
    }

    /// <summary>
    /// Blink ALL LIFE cards of a player red.
    /// Used when สาหัส status is reached (all 5 flipped).
    /// </summary>
    private IEnumerator BlinkAllLifeCards(Player player, Color color, int blinks)
    {
        float onTime = 0.2f;
        float offTime = 0.15f;

        // Collect all LIFE cards
        var lifeCards = new List<Card>();
        foreach (var zone in player.lifeZones)
        {
            if (zone != null && zone.activeCard != null && zone.activeCard.isLifeCard)
                lifeCards.Add(zone.activeCard);
        }

        for (int i = 0; i < blinks; i++)
        {
            foreach (var card in lifeCards)
                card.SetDeathGlow(color, 0.9f);
            yield return new WaitForSeconds(onTime);
            foreach (var card in lifeCards)
                card.SetDeathGlow(color, 0f);
            yield return new WaitForSeconds(offTime);
        }
    }
}
