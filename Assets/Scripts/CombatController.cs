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

        // ── Guard: no enemy avatars on field ──
        Player op = GameManager.instance.OpponentPlayerObj;
        if (op.AvatarsOnField == 0)
        {
            // Future: allow LIFE Card targeting. For now, inform player.
            UIController.instance.ShowCombatUI("No enemy avatars on field!\nPress End Phase \u2192 to continue.");
            combatState = CombatState.Idle;
            Debug.Log("[Combat] No enemy avatars — Battle Phase has nothing to do.");
            return;
        }

        UIController.instance.ShowCombatUI("Select an Avatar to attack with.");
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

        // Summon sickness — can't attack on the turn it was placed
        if (card.turnPlaced == gm.turnNumber)
        {
            Debug.Log($"[Combat] {card.cardName} has summon sickness (placed this turn).");
            UIController.instance.ShowCombatUI($"{card.cardName} was just summoned — can't attack yet!");
            return;
        }

        // Check if opponent has any avatars to target
        Player op = gm.OpponentPlayerObj;
        if (op.AvatarsOnField == 0)
        {
            UIController.instance.ShowCombatUI("No enemy avatars to attack!");
            Debug.Log("[Combat] No enemy avatars — can't select attacker.");
            return;
        }

        // Select this avatar as attacker
        selectedAttacker = card;
        card.SetAttackHighlight(true);
        combatState = CombatState.SelectingTarget;
        UIController.instance.ShowCombatUI($"<b>{card.cardName}</b> (Power {card.power}) attacks!\nSelect an enemy Avatar as target.");
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

        // Clicking another of your own cards → switch attacker
        if (card.cardOwner == gm.currentPlayer && !card.inHand && card.cardType == CardType.Avatar)
        {
            // Deselect old attacker, select new one
            selectedAttacker.SetAttackHighlight(false);
            TrySelectAttacker(card);
            return;
        }

        // Must be an ENEMY avatar on the board
        if (card.cardOwner == gm.currentPlayer)
        {
            Debug.Log("[Combat] Can't target your own card.");
            return;
        }
        if (card.inHand) return;
        if (card.cardType != CardType.Avatar)
        {
            Debug.Log("[Combat] Can only target enemy Avatars (for now).");
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
