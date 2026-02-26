using UnityEngine;

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
            Debug.Log($"[Combat] {card.cardName} is a LIFE card — can't attack with it.");
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

            // Can only attack LIFE cards when no enemy avatars exist
            if (op.AvatarsOnField > 0)
            {
                Debug.Log("[Combat] Can't attack LIFE cards while enemy has Avatars on field.");
                UIController.instance.ShowCombatUI(
                    "Can't target LIFE cards — defeat all enemy Avatars first!");
                return;
            }

            // Resolve LIFE card attack (flip, no power comparison)
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
            // Attacker wins → defender destroyed
            SendToHell(defender);
            UIController.instance.ShowCombatUI(
                $"<color=green><b>{attacker.cardName}</b> ({atkPower})</color> defeats " +
                $"<color=red>{defender.cardName} ({defPower})</color>!");
        }
        else if (defPower > atkPower)
        {
            // Defender wins → attacker destroyed
            SendToHell(attacker);
            UIController.instance.ShowCombatUI(
                $"<color=red><b>{attacker.cardName}</b> ({atkPower})</color> was defeated by " +
                $"<color=green>{defender.cardName} ({defPower})</color>!");
        }
        else
        {
            // Equal power → both destroyed (unless ลักหัด — TODO)
            SendToHell(attacker);
            SendToHell(defender);
            UIController.instance.ShowCombatUI(
                $"<color=yellow>DRAW!</color> Both {attacker.cardName} and {defender.cardName} " +
                $"destroyed ({atkPower} = {defPower})!");
        }

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

        // Show result
        UIController.instance.ShowCombatUI(
            $"<color=yellow><b>{attacker.cardName}</b> hits LIFE!</color>\n" +
            $"LIFE card revealed: <b>{lifeCard.cardName}</b>\n" +
            $"LIFE remaining: {remaining}/5");

        Debug.Log($"[Combat] LIFE card flipped: {lifeCard.cardName}. {target.playerId} LIFE: {remaining}/5");

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

    private void SendToHell(Card card)
    {
        // Clear board slot
        if (card.assignedPlace != null)
        {
            card.assignedPlace.activeCard = null;
            card.assignedPlace = null;
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
    //  CANCEL
    // ════════════════════════════════════════════════════════════════

    public void CancelAttackSelection()
    {
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
