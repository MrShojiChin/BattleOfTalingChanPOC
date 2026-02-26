using UnityEngine;
using UnityEngine.InputSystem;

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
public enum CombatState { Idle, SelectingAttacker, SelectingTarget }

public class CombatController : MonoBehaviour
{
    public static CombatController instance;

    // ── STATE ──────────────────────────────────────────────────────
    [Header("Combat State (Read Only)")]
    public CombatState combatState = CombatState.Idle;
    public Card selectedAttacker;

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

        // Detect left-click
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Card lifeCard = RaycastForLifeCard();
            if (lifeCard != null)
            {
                Debug.Log($"[Combat] Direct-detected click on LIFE card '{lifeCard.cardName}'");
                HandleCardClick(lifeCard, false);
            }
        }
        // Detect right-click (for cancel)
        else if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            Card lifeCard = RaycastForLifeCard();
            if (lifeCard != null)
            {
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
            UIController.instance.ShowCombatUI("No enemy targets available!\nPress End Phase \u2192 to continue.");
            combatState = CombatState.Idle;
            Debug.Log("[Combat] No enemy avatars or LIFE cards — Battle Phase has nothing to do.");
            return;
        }

        if (op.AvatarsOnField > 0)
            UIController.instance.ShowCombatUI("Select an Avatar to attack with.");
        else
            UIController.instance.ShowCombatUI("No enemy Avatars — you can attack LIFE cards!\nSelect an Avatar to attack with.");
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
            // Show UI feedback so the player knows the click registered
            if (card.cardOwner != gm.currentPlayer)
                UIController.instance.ShowCombatUI("Select YOUR Avatar first, then click enemy LIFE card!");
            else
                UIController.instance.ShowCombatUI("Can't attack with LIFE cards! Select an Avatar.");
            Debug.Log($"[Combat] {card.cardName} is a LIFE card — can't attack with it. (owner={card.cardOwner}, current={gm.currentPlayer})");
            return;
        }

        // Must be YOUR avatar, on the board
        if (card.cardOwner != gm.currentPlayer)
        {
            UIController.instance.ShowCombatUI("Select YOUR Avatar to attack with first!");
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
            UIController.instance.ShowCombatUI($"{card.cardName} already attacked this turn!");
            return;
        }

        // NOTE: No summon sickness — avatars CAN attack on the turn they're placed.
        // P1's Turn 1 battle skip is handled by GameManager.OnNextPhasePressed().

        // Check if opponent has any targets (avatars OR unflipped LIFE cards)
        Player op = gm.OpponentPlayerObj;
        if (op.AvatarsOnField == 0 && op.UnflippedLifeCards == 0)
        {
            UIController.instance.ShowCombatUI("No enemy targets to attack!");
            Debug.Log("[Combat] No enemy targets — can't select attacker.");
            return;
        }

        // Select this avatar as attacker
        selectedAttacker = card;
        card.SetAttackHighlight(true);
        combatState = CombatState.SelectingTarget;

        // Highlight valid targets
        HighlightValidTargets(true);

        // Different message depending on available targets
        if (op.AvatarsOnField > 0)
        {
            UIController.instance.ShowCombatUI(
                $"<b>{card.cardName}</b> (Power {card.power}) attacks!\nSelect an enemy Avatar as target.");
        }
        else
        {
            UIController.instance.ShowCombatUI(
                $"<b>{card.cardName}</b> attacks!\nNo enemy Avatars — select a LIFE card to attack!");
        }

        Debug.Log($"[Combat] Selected attacker: {card.cardName} (power {card.power})");
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
        if (card.isLifeCard && card.isFaceDown)
        {
            Player op = gm.OpponentPlayerObj;
            Debug.Log($"[Combat] Target is LIFE card '{card.cardName}' (faceDown={card.isFaceDown}). Enemy avatars on field: {op.AvatarsOnField}");

            // Can only attack LIFE cards when no enemy avatars exist
            if (op.AvatarsOnField > 0)
            {
                Debug.Log("[Combat] Can't attack LIFE cards while enemy has Avatars on field.");
                UIController.instance.ShowCombatUI(
                    "Can't target LIFE cards — defeat all enemy Avatars first!");
                return;
            }

            // Resolve LIFE card attack (flip, no power comparison)
            Debug.Log($"[Combat] Resolving LIFE card attack: {selectedAttacker.cardName} → {card.cardName}");
            ResolveLifeCardAttack(selectedAttacker, card);
            return;
        }

        // ── AVATAR TARGETING (normal combat) ──
        if (card.cardType != CardType.Avatar)
        {
            Debug.Log("[Combat] Can only target enemy Avatars or face-down LIFE cards.");
            return;
        }

        // ── RESOLVE COMBAT ──
        ResolveCombat(selectedAttacker, card);
    }

    // ════════════════════════════════════════════════════════════════
    //  STEP 3 — RESOLVE COMBAT
    // ════════════════════════════════════════════════════════════════

    private void ResolveCombat(Card attacker, Card defender)
    {
        int atkPower = attacker.power;
        int defPower = defender.power;

        Debug.Log($"[Combat] {attacker.cardName} (ATK {atkPower}) vs {defender.cardName} (DEF {defPower})");

        // Tap the attacker (นอน — lay down)
        attacker.SetTapped(true);
        attacker.SetAttackHighlight(false);

        // ── SPECIAL CASE: Power 0 vs Power 0 = nothing happens (rulebook) ──
        if (atkPower == 0 && defPower == 0)
        {
            UIController.instance.ShowCombatUI(
                $"Both {attacker.cardName} and {defender.cardName} have 0 Power — nothing happens!");
            Debug.Log("[Combat] Power 0 vs 0 — no destruction.");

            // Reset for next attack
            selectedAttacker = null;
            combatState = CombatState.SelectingAttacker;
            return;
        }

        if (atkPower > defPower)
        {
            // Attacker wins → defender destroyed, attacker takes damage
            SendToHell(defender);
            attacker.power -= defPower;

            if (attacker.power <= 0)
            {
                // Attacker also falls from the damage taken
                SendToHell(attacker);
                UIController.instance.ShowCombatUI(
                    $"<color=green><b>{attacker.cardName}</b> ({atkPower})</color> defeats " +
                    $"<color=red>{defender.cardName} ({defPower})</color>!\n" +
                    $"But <color=red>{attacker.cardName} also falls!</color> (power reduced to 0)");
            }
            else
            {
                attacker.RefreshPowerDisplay();
                UIController.instance.ShowCombatUI(
                    $"<color=green><b>{attacker.cardName}</b> ({atkPower})</color> defeats " +
                    $"<color=red>{defender.cardName} ({defPower})</color>!\n" +
                    $"{attacker.cardName} power: {atkPower} → <b>{attacker.power}</b>");
            }
        }
        else if (defPower > atkPower)
        {
            // Defender wins → attacker destroyed, defender takes damage
            SendToHell(attacker);
            defender.power -= atkPower;

            if (defender.power <= 0)
            {
                // Defender also falls from the damage taken
                SendToHell(defender);
                UIController.instance.ShowCombatUI(
                    $"<color=green>{defender.cardName} ({defPower})</color> defeats " +
                    $"<color=red><b>{attacker.cardName}</b> ({atkPower})</color>!\n" +
                    $"But <color=red>{defender.cardName} also falls!</color> (power reduced to 0)");
            }
            else
            {
                defender.RefreshPowerDisplay();
                UIController.instance.ShowCombatUI(
                    $"<color=green>{defender.cardName} ({defPower})</color> defeats " +
                    $"<color=red><b>{attacker.cardName}</b> ({atkPower})</color>!\n" +
                    $"{defender.cardName} power: {defPower} → <b>{defender.power}</b>");
            }
        }
        else
        {
            // Equal power → both destroyed
            SendToHell(attacker);
            SendToHell(defender);
            UIController.instance.ShowCombatUI(
                $"<color=yellow>DRAW!</color> Both {attacker.cardName} and {defender.cardName} " +
                $"destroyed ({atkPower} = {defPower})!");
        }

        // Clear target highlights and refresh HUD
        HighlightValidTargets(false);
        UIController.instance.UpdateGameInfo();

        // Reset for next attack
        selectedAttacker = null;
        combatState = CombatState.SelectingAttacker;
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

        // Get the target player to check LIFE count
        Player target = GameManager.instance.GetPlayer(lifeCard.cardOwner);
        int remaining = 5 - target.LifeCardsFlipped;

        // Build result message
        string resultMsg =
            $"<color=yellow><b>{attacker.cardName}</b> hits LIFE!</color>\n" +
            $"LIFE card revealed: <b>{lifeCard.cardName}</b>\n" +
            $"LIFE remaining: {remaining}/5";

        // Trigger on-flip effect (if any)
        if (lifeCard.lifeCardSO != null && lifeCard.lifeCardSO.onFlipEffect != MagicEffect.None)
        {
            MagicEffectResolver.ResolveLifeFlipEffect(lifeCard);
            resultMsg += $"\n<color=cyan>LIFE Effect: {lifeCard.lifeCardSO.onFlipEffect}!</color>";
            Debug.Log($"[LIFE] On-flip effect triggered: {lifeCard.lifeCardSO.onFlipEffect} ({lifeCard.lifeCardSO.onFlipValue})");
        }

        UIController.instance.ShowCombatUI(resultMsg);
        Debug.Log($"[Combat] LIFE card flipped: {lifeCard.cardName}. {target.playerId} LIFE: {remaining}/5");

        // Refresh HUD counters
        UIController.instance.UpdateGameInfo();

        // Clear target highlights
        HighlightValidTargets(false);

        // Check win condition (สหัส)
        if (GameManager.instance.CheckWinCondition())
        {
            combatState = CombatState.Idle;
            return;
        }

        // Reset for next attack
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
            var modsToDestroy = new System.Collections.Generic.List<Card>(card.attachedMods);
            card.attachedMods.Clear();
            foreach (Card mod in modsToDestroy)
            {
                // Undo the buff before destroying
                MagicEffectResolver.UndoEffect(mod, card);
                mod.equippedTo = null;
                SendToHell(mod); // Recursive — sends each mod to Hell too
            }
            card.RefreshPowerDisplay();
        }

        // ── If this card IS a modification, unlink from its avatar ──
        if (card.equippedTo != null)
        {
            card.equippedTo.attachedMods.Remove(card);
            card.equippedTo = null;
        }

        // Clear board slot — handle both single-card and multi-card zones
        if (card.assignedPlace != null)
        {
            if (card.assignedPlace.isMultiCardZone)
                card.assignedPlace.RemoveCard(card);
            else
            {
                card.assignedPlace.activeCard = null;
                card.assignedPlace = null;
            }
        }

        // Reset card state
        card.inHand = false;
        card.isTapped = false;
        card.SetAttackHighlight(false);
        card.SetTapped(false);

        // Move to owner's hell zone
        Player cardOwner = GameManager.instance.GetPlayer(card.cardOwner);
        if (cardOwner != null && cardOwner.hellZone != null)
        {
            cardOwner.hellZone.AddCard(card);
            Debug.Log($"[Combat] {card.cardName} → {card.cardOwner}'s Hell Zone.");
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

        if (op.AvatarsOnField > 0)
        {
            // Highlight enemy avatars
            foreach (var zone in op.avatarZones)
            {
                if (zone != null && zone.activeCard != null)
                    zone.activeCard.SetReadyHighlight(on);
            }
        }
        else
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
        UIController.instance.ShowCombatUI("Select an Avatar to attack with.");
    }
}
