using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Routes magic card play by MagicType and manages Modification target selection.
/// Mirrors BattleController (summon) and CombatController (combat) patterns.
///
/// Magic Types:
///   Normal       → resolve effect immediately → send to Hell (graveyard)
///   Modification → enter target selection → attach to chosen avatar → continuous buff
///   React        → set in Magic Zone → auto-triggers when conditions are met
///   Land         → place in shared LandMagic zone (replaces existing) → continuous buff
///
/// Additional Systems:
///   Temp Buff    → temporary power boosts that expire at end of turn
///   Land Buff    → continuous power boost to all matching avatars (both players)
///   React Trigger→ auto-activates React cards when enemy avatar is summoned
/// </summary>
public enum MagicPlayState { Idle, SelectingModTarget, AwaitingReactConfirm, AwaitingHellActivation }

public class MagicController : MonoBehaviour
{
    public static MagicController instance;

    // ── STATE ──────────────────────────────────────────────────────
    [Header("Magic State (Read Only)")]
    public MagicPlayState magicState = MagicPlayState.Idle;
    public Card pendingModification;    // The mod card waiting for target selection

    // ── REACT CONFIRMATION STATE ────────────────────────────────────
    private Card pendingReactCard;      // The React card that could trigger
    private Card pendingReactTarget;    // The summoned avatar that triggered it
    private Player pendingReactOwner;   // The player who owns the React card

    // ── HELL ACTIVATION STATE ────────────────────────────────────────
    private Queue<Card> hellActivationQueue = new Queue<Card>();
    private Card pendingHellActivation;     // Current mod card awaiting Hell activation choice
    private Player hellActivationOwner;     // Player who owns the milled card

    // Saved original positions for avatar hover effect
    private Dictionary<Card, Vector3> originalAvatarPositions = new Dictionary<Card, Vector3>();

    // ── TEMP BUFF TRACKING (expires at end of turn) ────────────────
    private struct TempBuff
    {
        public Card avatar;
        public int value;
    }
    private List<TempBuff> tempBuffs = new List<TempBuff>();

    // ── LAND BUFF TRACKING (continuous while land is active) ───────
    // Tracks which avatars have been buffed by the current land card
    private Dictionary<Card, int> landBuffedAvatars = new Dictionary<Card, int>();

    private void Awake()
    {
        instance = this;
    }

    // ════════════════════════════════════════════════════════════════
    //  ENTRY POINT — called from Card.HandleDrop
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Routes a magic card to the correct handler based on its MagicType.
    /// Called after the card is removed from hand.
    /// </summary>
    public void PlayMagicCard(Card magicCard, CardPlacePoint magicZone)
    {
        if (magicCard.magicSO == null)
        {
            Debug.LogWarning($"[MagicController] {magicCard.cardName} has no MagicCardSO!");
            magicZone.AddCard(magicCard);
            return;
        }

        MagicType type = magicCard.magicSO.magicType;
        Debug.Log($"[MagicController] Playing {magicCard.cardName} as {type} magic.");

        switch (type)
        {
            case MagicType.Normal:
                PlayNormalMagic(magicCard, magicZone);
                break;

            case MagicType.Modification:
                PlayModificationMagic(magicCard, magicZone);
                break;

            case MagicType.React:
                PlayReactMagic(magicCard, magicZone);
                break;

            case MagicType.Land:
                PlayLandMagic(magicCard);
                break;

            default:
                // Fallback: treat as Normal
                PlayNormalMagic(magicCard, magicZone);
                break;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  NORMAL MAGIC — resolve effect → send to Hell
    // ════════════════════════════════════════════════════════════════

    private void PlayNormalMagic(Card magicCard, CardPlacePoint magicZone)
    {
        // Briefly add to magic zone (visual feedback)
        magicZone.AddCard(magicCard);

        // Resolve effect
        if (magicCard.magicSO.effect != MagicEffect.None)
        {
            MagicEffectResolver.ResolveEffect(magicCard);
            Debug.Log($"[Magic] {magicCard.cardName} Normal effect resolved: {magicCard.magicSO.effect}");
        }

        // Send to Hell (graveyard) after resolution
        CombatController.instance.SendToHell(magicCard);
        Debug.Log($"[Magic] {magicCard.cardName} → Hell (Normal magic resolved and discarded).");
    }

    // ════════════════════════════════════════════════════════════════
    //  MODIFICATION MAGIC — equip to avatar
    // ════════════════════════════════════════════════════════════════

    private void PlayModificationMagic(Card magicCard, CardPlacePoint magicZone)
    {
        Player cp = GameManager.instance.CurrentPlayerObj;

        // Check if current player has any avatars on field
        if (cp.AvatarsOnField == 0)
        {
            // No valid targets → fizzle: card goes to Hell
            magicZone.AddCard(magicCard);
            CombatController.instance.SendToHell(magicCard);
            UIController.instance.ShowMagicUI(
                $"<color=red>{magicCard.cardName} fizzles!</color>\nNo Avatars on field to equip.");
            Debug.Log($"[Magic] {magicCard.cardName} Modification fizzles — no avatars on field.");
            return;
        }

        // Store the pending modification and enter target selection
        pendingModification = magicCard;

        // Temporarily park the card near the magic zone (not added to any zone yet)
        magicCard.MoveToPoint(
            magicZone.transform.position + new Vector3(0f, 0.5f, 0f),
            Quaternion.identity);

        BeginModTargetSelection();
    }

    /// <summary>Highlight and hover all friendly avatars for target selection.</summary>
    private void BeginModTargetSelection()
    {
        magicState = MagicPlayState.SelectingModTarget;

        // Lift and highlight all friendly avatars
        LiftAvatarsForSelection(true);

        UIController.instance.ShowMagicUI(
            $"<b>Equip: {pendingModification.cardName}</b>\n" +
            $"Select an Avatar to attach this Modification to.\n" +
            $"<i>Right-click to cancel.</i>");

        Debug.Log("[MagicController] Modification target selection started.");
    }

    /// <summary>Called when player clicks one of their avatars during SelectingModTarget.</summary>
    public void HandleAvatarClickForMod(Card clickedAvatar)
    {
        if (magicState != MagicPlayState.SelectingModTarget) return;
        if (pendingModification == null) return;

        // Validate: must be player's avatar on the board
        if (clickedAvatar.cardOwner != GameManager.instance.currentPlayer
            && (hellActivationOwner == null || clickedAvatar.cardOwner != hellActivationOwner.playerId))
        {
            Debug.Log("[MagicController] Not your avatar!");
            return;
        }
        if (clickedAvatar.inHand || clickedAvatar.cardType != CardType.Avatar)
        {
            Debug.Log("[MagicController] Invalid target — must be an Avatar on the board.");
            return;
        }

        // Check symbol restriction if the mod has a targetSymbol
        if (pendingModification.magicSO != null
            && pendingModification.magicSO.targetSymbol != CardSymbol.None
            && clickedAvatar.cardSymbol != pendingModification.magicSO.targetSymbol)
        {
            Debug.Log($"[MagicController] {clickedAvatar.cardName} is [{clickedAvatar.cardSymbol}], mod requires [{pendingModification.magicSO.targetSymbol}]!");
            return;
        }

        // Attach the modification
        AttachModification(pendingModification, clickedAvatar);

        // Clean up selection state
        LiftAvatarsForSelection(false);
        UIController.instance.HideMagicUI();
        pendingModification = null;
        magicState = MagicPlayState.Idle;

        // If there are more Hell activations queued, process them
        if (hellActivationQueue.Count > 0)
            ProcessNextHellActivation();
    }

    /// <summary>Cancel modification target selection — card fizzles to Hell.</summary>
    public void CancelModSelection()
    {
        if (magicState != MagicPlayState.SelectingModTarget) return;

        Debug.Log($"[MagicController] Modification cancelled: {pendingModification?.cardName}");

        // Send the pending mod card to Hell
        if (pendingModification != null)
        {
            CombatController.instance.SendToHell(pendingModification);
        }

        // Lower all avatars and clear highlights
        LiftAvatarsForSelection(false);
        UIController.instance.HideMagicUI();
        pendingModification = null;
        magicState = MagicPlayState.Idle;

        // If there are more Hell activations queued, process them
        if (hellActivationQueue.Count > 0)
            ProcessNextHellActivation();
    }

    // ════════════════════════════════════════════════════════════════
    //  ATTACH MODIFICATION TO AVATAR
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Link a Modification magic card to an avatar.
    /// Applies the buff, tracks the link for later destruction.
    /// </summary>
    private void AttachModification(Card modCard, Card avatar)
    {
        // Create bidirectional link
        modCard.equippedTo = avatar;
        avatar.attachedMods.Add(modCard);

        // Apply the effect and record value for undo
        int value = modCard.magicSO != null ? modCard.magicSO.effectValue : 0;
        modCard.appliedEffectValue = value;

        if (modCard.magicSO != null && modCard.magicSO.effect != MagicEffect.None)
        {
            switch (modCard.magicSO.effect)
            {
                case MagicEffect.PowerBoost:
                case MagicEffect.PowerBoostSymbol:
                    avatar.power += value;
                    break;
                case MagicEffect.PowerReduce:
                    avatar.power = Mathf.Max(0, avatar.power - value);
                    break;
                // Other effects (DrawCards, etc.) resolve once and don't need undo
                default:
                    MagicEffectResolver.ResolveEffect(modCard);
                    break;
            }
            avatar.RefreshPowerDisplay();
        }

        // Position the mod card visually near the avatar
        PositionModOnAvatar(modCard, avatar);

        UIController.instance?.UpdateGameInfo();
        Debug.Log($"[Magic] {modCard.cardName} equipped to {avatar.cardName}. " +
                  $"Mods attached: {avatar.attachedMods.Count}. Power: {avatar.power}");
    }

    /// <summary>Position a modification card visually near its equipped avatar.</summary>
    private void PositionModOnAvatar(Card modCard, Card avatar)
    {
        if (avatar.assignedPlace == null) return;

        Vector3 avatarPos = avatar.assignedPlace.transform.position;
        int modIndex = avatar.attachedMods.IndexOf(modCard);

        // Stack mods slightly behind (Z) and above (Y) the avatar
        Vector3 modOffset = new Vector3(
            0.3f * (modIndex + 1),      // Fan out horizontally
            0.02f * (modIndex + 1),     // Slight vertical stack
            -0.4f                        // Behind the avatar
        );

        modCard.MoveToPoint(avatarPos + modOffset, Quaternion.identity);
    }

    // ════════════════════════════════════════════════════════════════
    //  AVATAR HOVER DURING SELECTION
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Raise or lower all friendly avatars for Modification target selection.
    /// When lift=true: avatars hover 0.3 Y above their zone + green highlight.
    /// When lift=false: restore to original positions + clear highlight.
    /// </summary>
    private void LiftAvatarsForSelection(bool lift)
    {
        Player cp = GameManager.instance.CurrentPlayerObj;

        foreach (var zone in cp.avatarZones)
        {
            if (zone == null || zone.activeCard == null) continue;
            Card avatar = zone.activeCard;

            if (lift)
            {
                // Save original position
                originalAvatarPositions[avatar] = zone.transform.position;

                // Raise avatar 0.3 units above the zone
                Vector3 raised = zone.transform.position + new Vector3(0f, 0.3f, 0f);
                Quaternion rot = avatar.isTapped
                    ? Quaternion.Euler(0f, -90f, 0f)
                    : Quaternion.identity;
                avatar.MoveToPoint(raised, rot);
                avatar.SetReadyHighlight(true);
            }
            else
            {
                // Restore to original zone position
                if (originalAvatarPositions.TryGetValue(avatar, out Vector3 origPos))
                {
                    Quaternion rot = avatar.isTapped
                        ? Quaternion.Euler(0f, -90f, 0f)
                        : Quaternion.identity;
                    avatar.MoveToPoint(origPos, rot);
                }
                avatar.SetReadyHighlight(false);
            }
        }

        if (!lift) originalAvatarPositions.Clear();
    }

    // ════════════════════════════════════════════════════════════════
    //  REACT MAGIC — trigger on enemy summon with confirmation
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Place a React magic card in the Magic Zone.
    /// It will prompt the owner to activate when its trigger condition is met.
    /// </summary>
    private void PlayReactMagic(Card magicCard, CardPlacePoint magicZone)
    {
        magicZone.AddCard(magicCard);
        UIController.instance?.ShowMagicUI(
            $"<b>{magicCard.cardName}</b> set as <color=yellow>React</color> magic.\n" +
            $"Will trigger when an enemy Avatar is summoned.");
        Debug.Log($"[Magic] {magicCard.cardName} set as React magic in Magic Zone.");
    }

    /// <summary>
    /// Called from BattleController.FinalizeSummon() when any avatar is summoned.
    /// Checks for React triggers from the OPPONENT and applies Land buffs.
    /// Uses GameManager.currentPlayer (the summoner) to reliably find the opponent.
    /// </summary>
    public void OnAvatarSummoned(Card summonedAvatar)
    {
        if (summonedAvatar == null) return;

        // 1) Check React triggers from the OPPONENT (the non-summoning player)
        CheckReactTriggers(summonedAvatar);

        // 2) Apply active Land buff to newly summoned avatar (if land is active)
        //    Only if avatar wasn't destroyed by React trigger
        if (summonedAvatar.assignedPlace != null
            && summonedAvatar.assignedPlace.zoneType == ZoneType.Avatar)
        {
            ApplyLandBuffToAvatar(summonedAvatar);
        }
    }

    /// <summary>
    /// Scan the opponent's magic zone for React cards that trigger on summon.
    /// If found, shows a confirmation prompt to the React card's owner.
    /// Uses currentPlayer (the summoner) to determine who the opponent is.
    /// </summary>
    private void CheckReactTriggers(Card summonedAvatar)
    {
        GameManager gm = GameManager.instance;

        // The current player is doing the summoning — the opponent has the React cards
        TurnPlayer summonerPlayer = gm.currentPlayer;
        Player opponent = (summonerPlayer == TurnPlayer.Player1)
            ? gm.player2
            : gm.player1;

        // Find opponent's magic zone
        CardPlacePoint magicZone = FindMagicZone(opponent);
        if (magicZone == null)
        {
            Debug.Log($"[React] No magic zone found for {opponent.playerId}.");
            return;
        }

        Debug.Log($"[React] Checking {opponent.playerId}'s magic zone ({magicZone.activeCards.Count} cards) for React triggers...");

        // Scan for React cards
        for (int i = magicZone.activeCards.Count - 1; i >= 0; i--)
        {
            Card reactCard = magicZone.activeCards[i];
            if (reactCard == null || reactCard.magicSO == null) continue;
            if (reactCard.magicSO.magicType != MagicType.React) continue;

            string ownerName = opponent.playerId == TurnPlayer.Player1 ? "Player 1" : "Player 2";

            // DestroyAvatar React → prompt to activate or keep
            if (reactCard.magicSO.effect == MagicEffect.DestroyAvatar)
            {
                Debug.Log($"[React] {reactCard.cardName} can trigger against {summonedAvatar.cardName}! Asking {opponent.playerId}...");

                pendingReactCard = reactCard;
                pendingReactTarget = summonedAvatar;
                pendingReactOwner = opponent;
                magicState = MagicPlayState.AwaitingReactConfirm;

                UIController.instance?.ShowReactUI(
                    $"<color=yellow>React!</color> <b>{ownerName}</b>\n\n" +
                    $"<b>{reactCard.cardName}</b> can destroy\n" +
                    $"<b>{summonedAvatar.cardName}</b> that was just summoned!\n\n" +
                    $"<i>Activate to destroy it, or Keep for later.</i>");

                break;
            }

            // ReactDiscardThonSoop → discard [Symbol] from hand (cost ≥ summoned) → ธรณีสูบ + destroy
            if (reactCard.magicSO.effect == MagicEffect.ReactDiscardThonSoop)
            {
                // Check if opponent has a matching avatar in hand with sufficient cost
                CardSymbol reqSymbol = reactCard.magicSO.targetSymbol;
                Card discardCandidate = MagicEffectResolver.FindCardInHandBySymbolAndMinCost(
                    opponent, reqSymbol, summonedAvatar.cost);

                if (discardCandidate == null)
                {
                    Debug.Log($"[React] {reactCard.cardName}: No [{reqSymbol}] avatar in hand with cost ≥ {summonedAvatar.cost}. Cannot trigger.");
                    continue; // Check next React card
                }

                Debug.Log($"[React] {reactCard.cardName} can trigger! {discardCandidate.cardName} (cost {discardCandidate.cost}) available to discard.");

                pendingReactCard = reactCard;
                pendingReactTarget = summonedAvatar;
                pendingReactOwner = opponent;
                magicState = MagicPlayState.AwaitingReactConfirm;

                UIController.instance?.ShowReactUI(
                    $"<color=yellow>React!</color> <b>{ownerName}</b>\n\n" +
                    $"<b>{reactCard.cardName}</b> can trigger!\n" +
                    $"Discard <b>{discardCandidate.cardName}</b> [{reqSymbol}] (cost {discardCandidate.cost})\n" +
                    $"→ ธรณีสูบ {reactCard.magicSO.effectValue} cards → Destroy <b>{summonedAvatar.cardName}</b>\n\n" +
                    $"<i>Activate or Keep for later?</i>");

                break;
            }
        }
    }

    /// <summary>
    /// Called when the React card owner clicks "Activate".
    /// Handles both DestroyAvatar and ReactDiscardThonSoop effects.
    /// </summary>
    public void OnReactActivate()
    {
        if (magicState != MagicPlayState.AwaitingReactConfirm) return;
        if (pendingReactCard == null || pendingReactTarget == null) return;

        Card reactCard = pendingReactCard;
        Card target = pendingReactTarget;
        Player reactOwner = pendingReactOwner;

        // Clear state
        pendingReactCard = null;
        pendingReactTarget = null;
        pendingReactOwner = null;
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideReactUI();

        if (reactCard.magicSO.effect == MagicEffect.DestroyAvatar)
        {
            // Simple React: destroy summoned avatar + consume React
            Debug.Log($"[React] ACTIVATED! {reactCard.cardName} destroys {target.cardName}!");
            CombatController.instance.SendToHell(target);
            CombatController.instance.SendToHell(reactCard);
        }
        else if (reactCard.magicSO.effect == MagicEffect.ReactDiscardThonSoop)
        {
            // Complex React: discard [Symbol] from hand → ธรณีสูบ → destroy
            CardSymbol reqSymbol = reactCard.magicSO.targetSymbol;
            int thonSoopValue = reactCard.magicSO.effectValue;

            // Step 1: Find and discard [Symbol] avatar from React owner's hand (cost ≥ target)
            Card toDiscard = MagicEffectResolver.FindCardInHandBySymbolAndMinCost(
                reactOwner, reqSymbol, target.cost);

            if (toDiscard == null)
            {
                // Shouldn't happen (checked in CheckReactTriggers) but handle gracefully
                Debug.LogWarning($"[React] No valid [{reqSymbol}] card to discard! React fizzles.");
                return;
            }

            Debug.Log($"[React] ACTIVATED! {reactCard.cardName}: Discarding {toDiscard.cardName} [{reqSymbol}]...");
            reactOwner.hand.RemoveCardFromHand(toDiscard);
            CombatController.instance.SendToHell(toDiscard);

            // Step 2: ธรณีสูบ — mill N cards from React owner's deck
            var milledCards = reactOwner.deck.MillCards(thonSoopValue);
            Debug.Log($"[React] ธรณีสูบ! Milled {milledCards.Count} card(s) from {reactOwner.playerId}'s deck.");

            // Step 3: Draw N cards (React owner draws)
            GameManager gm = GameManager.instance;
            for (int i = 0; i < thonSoopValue; i++)
            {
                reactOwner.deck.DrawCardToHand();
                if (gm.isGameOver) return;
            }
            Debug.Log($"[React] {reactOwner.playerId} drew {thonSoopValue} card(s).");

            // Step 4: Destroy summoned avatar
            CombatController.instance.SendToHell(target);
            Debug.Log($"[React] {target.cardName} destroyed!");

            // Step 5: Check milled cards for Hell activation
            CheckMilledForHellActivation(milledCards, reactOwner);

            // Step 6: Consume React card
            CombatController.instance.SendToHell(reactCard);
        }

        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>
    /// Called when the React card owner clicks "Keep".
    /// Keeps the React card for a future trigger. Avatar stays on field.
    /// </summary>
    public void OnReactKeep()
    {
        if (magicState != MagicPlayState.AwaitingReactConfirm) return;

        Debug.Log($"[React] KEPT. {pendingReactCard?.cardName} stays in magic zone for later.");

        pendingReactCard = null;
        pendingReactTarget = null;
        pendingReactOwner = null;
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideReactUI();
    }

    /// <summary>Find a player's Magic zone CardPlacePoint.</summary>
    private CardPlacePoint FindMagicZone(Player player)
    {
        // Check if player has a direct reference to their magic zone
        if (player.magicZone != null)
            return player.magicZone;

        // Fallback: search all CardPlacePoints
        foreach (var zone in FindObjectsOfType<CardPlacePoint>())
        {
            if (zone.zoneType == ZoneType.Magic && zone.owner == player.playerId)
                return zone;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════════
    //  LAND MAGIC — shared zone, continuous buff
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Place a Land magic card in the shared LandMagic zone.
    /// If a land already exists, undo its buffs and destroy it first.
    /// Then apply continuous buffs to ALL matching avatars (both players).
    /// </summary>
    private void PlayLandMagic(Card landCard)
    {
        CardPlacePoint landZone = GameManager.instance.landMagicZone;

        if (landZone == null)
        {
            Debug.LogWarning("[MagicController] No LandMagic zone found! Treating as Normal magic.");
            // Fallback: resolve and discard
            if (landCard.magicSO.effect != MagicEffect.None)
                MagicEffectResolver.ResolveEffect(landCard);
            CombatController.instance.SendToHell(landCard);
            return;
        }

        // If a land card already exists, undo its buffs and destroy it
        if (landZone.activeCard != null)
        {
            Card oldLand = landZone.activeCard;
            Debug.Log($"[Magic] New Land magic {landCard.cardName} replaces {oldLand.cardName}!");

            // Undo all buffs from the old land before destroying it
            UndoAllLandBuffs();

            // Clear the zone reference before sending to Hell
            landZone.activeCard = null;
            oldLand.assignedPlace = null;
            CombatController.instance.SendToHell(oldLand);
        }

        // Place new land card in the shared zone
        landZone.activeCard = landCard;
        landCard.assignedPlace = landZone;
        landCard.MoveToPoint(landZone.transform.position, Quaternion.identity);

        // Apply continuous land buff to all matching avatars on field (BOTH players)
        ApplyLandBuffsToAll(landCard);

        UIController.instance?.UpdateGameInfo();
        Debug.Log($"[Magic] Land magic {landCard.cardName} placed in LandMagic zone.");
    }

    /// <summary>
    /// Apply land buff to ALL matching avatars on field (both players).
    /// Called when a new land card is placed.
    /// </summary>
    private void ApplyLandBuffsToAll(Card landCard)
    {
        if (landCard.magicSO == null) return;
        if (landCard.magicSO.effect != MagicEffect.PowerBoostAllSymbol) return;

        GameManager gm = GameManager.instance;
        int value = landCard.magicSO.effectValue;
        CardSymbol sym = landCard.magicSO.targetSymbol;

        // Apply to BOTH players' avatars (Land is a shared zone)
        ApplyLandToPlayerAvatars(gm.player1, sym, value, landCard.cardName);
        ApplyLandToPlayerAvatars(gm.player2, sym, value, landCard.cardName);

        int count = landBuffedAvatars.Count;
        if (count > 0)
            Debug.Log($"[Land] {landCard.cardName}: Buffed {count} [{sym}] avatar(s) by +{value}!");
        else
            Debug.Log($"[Land] {landCard.cardName}: No [{sym}] avatars on field to buff.");
    }

    /// <summary>Apply land buff to all matching avatars of a specific player.</summary>
    private void ApplyLandToPlayerAvatars(Player player, CardSymbol sym, int value, string landName)
    {
        foreach (var zone in player.avatarZones)
        {
            if (zone == null || zone.activeCard == null) continue;
            Card avatar = zone.activeCard;
            if (avatar.cardSymbol == sym)
            {
                avatar.power += value;
                avatar.RefreshPowerDisplay();
                landBuffedAvatars[avatar] = value;
                Debug.Log($"[Land] {landName}: {avatar.cardName} [{sym}] power +{value} → {avatar.power}");
            }
        }
    }

    /// <summary>
    /// Apply active land buff to a single avatar (called when new avatar summoned).
    /// </summary>
    private void ApplyLandBuffToAvatar(Card avatar)
    {
        CardPlacePoint landZone = GameManager.instance.landMagicZone;
        if (landZone == null || landZone.activeCard == null) return;

        Card landCard = landZone.activeCard;
        if (landCard.magicSO == null) return;
        if (landCard.magicSO.effect != MagicEffect.PowerBoostAllSymbol) return;
        if (avatar.cardSymbol != landCard.magicSO.targetSymbol) return;

        // Check if avatar is still alive (wasn't destroyed by React trigger)
        if (avatar.assignedPlace == null || avatar.assignedPlace.zoneType != ZoneType.Avatar)
            return;

        int value = landCard.magicSO.effectValue;
        avatar.power += value;
        avatar.RefreshPowerDisplay();
        landBuffedAvatars[avatar] = value;
        Debug.Log($"[Land] {landCard.cardName}: New summon {avatar.cardName} [{avatar.cardSymbol}] power +{value} → {avatar.power}");
    }

    /// <summary>
    /// Undo all active land buffs. Called when land is destroyed or replaced.
    /// </summary>
    public void UndoAllLandBuffs()
    {
        foreach (var kvp in landBuffedAvatars)
        {
            if (kvp.Key != null)
            {
                kvp.Key.power = Mathf.Max(0, kvp.Key.power - kvp.Value);
                kvp.Key.RefreshPowerDisplay();
                Debug.Log($"[Land] Undo buff: {kvp.Key.cardName} power -{kvp.Value} → {kvp.Key.power}");
            }
        }
        landBuffedAvatars.Clear();
    }

    /// <summary>
    /// Remove a specific avatar from land buff tracking.
    /// Called from CombatController.SendToHell() when an avatar is destroyed.
    /// </summary>
    public void RemoveLandBuff(Card avatar)
    {
        landBuffedAvatars.Remove(avatar);
    }

    // ════════════════════════════════════════════════════════════════
    //  TEMP BUFF SYSTEM — expires at end of turn
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Register a temporary power buff that will be cleaned up at end of turn.
    /// Called from MagicEffectResolver for PowerBoostSymbolTemp effects.
    /// </summary>
    public void RegisterTempBuff(Card avatar, int value)
    {
        tempBuffs.Add(new TempBuff { avatar = avatar, value = value });
        Debug.Log($"[TempBuff] Registered: {avatar.cardName} +{value} (until end of turn)");
    }

    /// <summary>
    /// Remove all temporary buffs. Called from GameManager.SwitchTurn()
    /// at the end of each turn before switching to the next player.
    /// </summary>
    public void CleanupTempBuffs()
    {
        if (tempBuffs.Count == 0) return;

        Debug.Log($"[TempBuff] Cleaning up {tempBuffs.Count} temp buff(s)...");

        foreach (var buff in tempBuffs)
        {
            if (buff.avatar != null)
            {
                buff.avatar.power = Mathf.Max(0, buff.avatar.power - buff.value);
                buff.avatar.RefreshPowerDisplay();
                Debug.Log($"[TempBuff] Expired: {buff.avatar.cardName} -{buff.value} → {buff.avatar.power}");
            }
        }
        tempBuffs.Clear();

        UIController.instance?.UpdateGameInfo();
    }

    // ════════════════════════════════════════════════════════════════
    //  HELL ACTIVATION SYSTEM — activate Modification mods from Hell
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// After ธรณีสูบ (ThonSoop) mills cards, check if any are Modification magic cards
    /// with activateFromHellOnThonSoop = true. If so, prompt the player to activate.
    /// Called from MagicEffectResolver (ThonSoop, DiscardSymbolThonSoop) and OnReactActivate.
    /// </summary>
    public void CheckMilledForHellActivation(System.Collections.Generic.List<Card> milledCards, Player owner)
    {
        if (milledCards == null || milledCards.Count == 0) return;

        // Scan milled cards for Hell-activatable mods
        foreach (Card card in milledCards)
        {
            if (card == null || card.magicSO == null) continue;
            if (card.magicSO.magicType != MagicType.Modification) continue;
            if (!card.magicSO.activateFromHellOnThonSoop) continue;

            hellActivationQueue.Enqueue(card);
            Debug.Log($"[Hell] {card.cardName} was milled by ธรณีสูบ and can activate from Hell!");
        }

        // Start processing the queue
        if (hellActivationQueue.Count > 0)
        {
            hellActivationOwner = owner;
            ProcessNextHellActivation();
        }
    }

    /// <summary>Process the next Hell activation prompt from the queue.</summary>
    private void ProcessNextHellActivation()
    {
        if (hellActivationQueue.Count == 0)
        {
            // All done
            hellActivationOwner = null;
            pendingHellActivation = null;
            return;
        }

        pendingHellActivation = hellActivationQueue.Dequeue();
        magicState = MagicPlayState.AwaitingHellActivation;

        string ownerName = hellActivationOwner.playerId == TurnPlayer.Player1 ? "Player 1" : "Player 2";
        UIController.instance?.ShowReactUI(
            $"<color=orange>Hell Activation!</color> <b>{ownerName}</b>\n\n" +
            $"<b>{pendingHellActivation.cardName}</b> was milled by ธรณีสูบ.\n" +
            $"Effect: {pendingHellActivation.magicSO.effect} +{pendingHellActivation.magicSO.effectValue}\n\n" +
            $"<i>Activate from Hell, or leave it?</i>");

        Debug.Log($"[Hell] Prompting {ownerName} to activate {pendingHellActivation.cardName} from Hell.");
    }

    /// <summary>
    /// Called when player clicks "Activate" for a Hell activation prompt.
    /// Removes the card from Hell zone and enters Modification target selection.
    /// </summary>
    public void OnHellActivate()
    {
        if (magicState != MagicPlayState.AwaitingHellActivation) return;
        if (pendingHellActivation == null) return;

        Card modCard = pendingHellActivation;
        Player owner = hellActivationOwner;
        pendingHellActivation = null;
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideReactUI();

        Debug.Log($"[Hell] ACTIVATED! {modCard.cardName} will be equipped from Hell.");

        // Remove card from Hell zone (it was placed there by SendToHell during MillCards)
        if (modCard.assignedPlace != null)
        {
            if (modCard.assignedPlace.isMultiCardZone)
                modCard.assignedPlace.RemoveCard(modCard);
            else
            {
                modCard.assignedPlace.activeCard = null;
                modCard.assignedPlace = null;
            }
        }

        // Check if owner has avatars on field to equip to
        if (owner.AvatarsOnField == 0)
        {
            Debug.Log($"[Hell] {modCard.cardName}: No avatars on field to equip. Card stays destroyed.");
            // Card is already removed from Hell, send it back
            CombatController.instance.SendToHell(modCard);
            ProcessNextHellActivation();
            return;
        }

        // Enter Modification target selection (same flow as normal Modification)
        pendingModification = modCard;

        // Park the card above the board (visual feedback)
        Vector3 parkPos = owner.avatarZones[0] != null
            ? owner.avatarZones[0].transform.position + new Vector3(0f, 1.0f, 0f)
            : modCard.transform.position + new Vector3(0f, 1.0f, 0f);
        modCard.MoveToPoint(parkPos, Quaternion.identity);

        BeginModTargetSelection();
        // After mod target selection completes, ProcessNextHellActivation will be called
    }

    /// <summary>
    /// Called when player clicks "Keep" (decline) for a Hell activation prompt.
    /// Card stays in Hell. Moves to next item in queue.
    /// </summary>
    public void OnHellKeep()
    {
        if (magicState != MagicPlayState.AwaitingHellActivation) return;

        Debug.Log($"[Hell] DECLINED. {pendingHellActivation?.cardName} stays in Hell.");

        pendingHellActivation = null;
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideReactUI();

        // Process next in queue
        ProcessNextHellActivation();
    }
}
