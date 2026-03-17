using UnityEngine;
using System.Collections;
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
public enum MagicPlayState { Idle, SelectingModTarget, AwaitingReactConfirm, AwaitingHellActivation, SelectingDiscardForEffect, SelectingTempBoostTarget, SelectingReactDiscard, SelectingDeckSearch }

public class MagicController : MonoBehaviour
{
    public static MagicController instance;

    // ── STATE ──────────────────────────────────────────────────────
    [Header("Magic State (Read Only)")]
    public MagicPlayState magicState = MagicPlayState.Idle;
    public Card pendingModification;    // The mod card waiting for target selection
    private bool pendingModFromHell;     // True if mod came from Hell activation (cancel → Hell, not hand)

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

    // ── DISCARD-FOR-EFFECT STATE (player picks which card to discard) ──
    private Card pendingDiscardEffectCard;                       // The magic card being played
    private MagicEffect pendingDiscardEffectType;                // DiscardSymbolDraw or DiscardSymbolThonSoop
    private CardSymbol pendingDiscardSymbol;                     // Required symbol
    private int pendingDiscardValue;                             // Effect value
    private List<Card> highlightedForDiscard = new List<Card>(); // Cards highlighted for selection

    // ── REACT DISCARD STATE (player picks which hand card to discard for React) ──
    private Card savedReactCard;            // The React card
    private Card savedReactTarget;          // The summoned avatar to destroy
    private Player savedReactOwner;         // The React card owner
    private List<Card> highlightedForReactDiscard = new List<Card>();


    // ── TEMP BOOST TARGET SELECTION STATE ────────────────────────────
    private Card pendingTempBoostCard;          // The magic card being played
    private CardSymbol pendingTempBoostSymbol;   // Required symbol
    private int pendingTempBoostValue;           // Power boost amount
    private List<Card> highlightedForTempBoost = new List<Card>();

    // ── DECK SEARCH STATE (player picks which card to summon from deck) ──
    private Card pendingDeckSearchCard;   // The magic card that triggered the search

    // ── TEMP BUFF TRACKING (expires at end of turn) ────────────────
    private struct TempBuff
    {
        public Card avatar;
        public int value;
        public Card sourceCard;   // The magic card that created this buff (sent to Hell on cleanup)
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
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogMagic($"{magicCard.cardName} played as {type} magic.");

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

        // Check if this effect needs interactive discard selection
        if (magicCard.magicSO.effect == MagicEffect.DiscardSymbolDraw
            || magicCard.magicSO.effect == MagicEffect.DiscardSymbolThonSoop)
        {
            if (TryBeginDiscardForEffect(magicCard))
                return; // Async — card stays in magic zone until selection completes
            // If TryBegin returned false (no matches), fall through to fizzle → Hell
        }

        // Check if this effect needs interactive avatar target selection
        if (magicCard.magicSO.effect == MagicEffect.PowerBoostSymbolTemp)
        {
            if (TryBeginTempBoostSelection(magicCard))
                return; // Async — card stays in magic zone until target selected
            // If no matching avatars, fall through to fizzle → Hell
        }

        // Check if this effect needs interactive deck search selection
        if (magicCard.magicSO.effect == MagicEffect.SearchDeckByName)
        {
            if (TryBeginDeckSearch(magicCard))
                return; // Async — card stays in magic zone until player picks from panel
            // If no matches in deck, fall through to fizzle → Hell
        }

        // DrawCards with count > 1: use sequential animation
        if (magicCard.magicSO.effect == MagicEffect.DrawCards && magicCard.magicSO.effectValue > 1)
        {
            StartCoroutine(SequentialDrawCards(magicCard));
            return;
        }

        // ThonSoop: mill first, then draw sequentially
        if (magicCard.magicSO.effect == MagicEffect.ThonSoop)
        {
            StartCoroutine(SequentialThonSoop(magicCard));
            return;
        }

        // Resolve effect (single-draw or non-draw effects)
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

        // Check if current player has any valid avatar targets
        CardSymbol reqSym = magicCard.magicSO != null ? magicCard.magicSO.targetSymbol : CardSymbol.None;
        bool hasValidTarget = false;
        foreach (var zone in cp.avatarZones)
        {
            if (zone == null || zone.activeCard == null) continue;
            if (reqSym == CardSymbol.None || zone.activeCard.cardSymbol == reqSym)
            {
                hasValidTarget = true;
                break;
            }
        }

        if (!hasValidTarget)
        {
            // No valid targets → fizzle: card goes to Hell
            magicZone.AddCard(magicCard);
            CombatController.instance.SendToHell(magicCard);
            string symbolNote = reqSym != CardSymbol.None
                ? $"\nNo [{reqSym}] Avatars on field."
                : "\nNo Avatars on field to equip.";
            UIController.instance.ShowMagicUI(
                $"<color=red>{magicCard.cardName} fizzles!</color>{symbolNote}");
            Debug.Log($"[Magic] {magicCard.cardName} Modification fizzles — no valid avatar targets.");
            return;
        }

        // Store the pending modification and enter target selection
        pendingModification = magicCard;
        pendingModFromHell = false;

        // Add the mod card to the magic zone (it stays here even after equipping)
        magicZone.AddCard(magicCard);

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

    /// <summary>Cancel modification target selection — return to hand or Hell.</summary>
    public void CancelModSelection()
    {
        if (magicState != MagicPlayState.SelectingModTarget) return;

        if (pendingModFromHell)
        {
            // Card came from Hell activation — send it back to Hell
            Debug.Log($"[MagicController] Modification cancelled: {pendingModification?.cardName} back to Hell.");
            if (pendingModification != null)
                CombatController.instance.SendToHell(pendingModification);
        }
        else
        {
            // Card came from hand — remove from magic zone and return to hand
            Debug.Log($"[MagicController] Modification cancelled: {pendingModification?.cardName} returned to hand.");
            if (pendingModification != null)
            {
                // Remove from magic zone first
                if (pendingModification.assignedPlace != null && pendingModification.assignedPlace.isMultiCardZone)
                    pendingModification.assignedPlace.RemoveCard(pendingModification);

                Player cp = GameManager.instance.CurrentPlayerObj;
                pendingModification.inHand = true;
                cp.hand.AddCardToHand(pendingModification);
            }
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
    /// Creates a persistent bidirectional link so the buff is undone when the avatar dies.
    /// PowerBoost cards are hidden (not visually placed on the zone).
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

        // Show highlight on both the avatar and the mod card itself
        avatar.RefreshModHighlight();
        modCard.SetModHighlight(true);
        Debug.Log($"[Magic] {modCard.cardName} equipped to {avatar.cardName}. " +
                  $"Mods attached: {avatar.attachedMods.Count}. Power: {avatar.power}");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogMagic($"{modCard.cardName} equipped to {avatar.cardName} (Pw:{avatar.power}).");

        UIController.instance?.UpdateGameInfo();
    }


    // ════════════════════════════════════════════════════════════════
    //  AVATAR HOVER DURING SELECTION
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Raise or lower friendly avatars for Modification target selection.
    /// Only highlights avatars matching the mod's targetSymbol (if set).
    /// When lift=true: avatars hover 0.3 Y above their zone + green highlight.
    /// When lift=false: restore to original positions + clear highlight.
    /// </summary>
    private void LiftAvatarsForSelection(bool lift)
    {
        Player cp = GameManager.instance.CurrentPlayerObj;

        // Determine symbol filter from pending modification
        CardSymbol requiredSymbol = CardSymbol.None;
        if (pendingModification != null && pendingModification.magicSO != null)
            requiredSymbol = pendingModification.magicSO.targetSymbol;

        foreach (var zone in cp.avatarZones)
        {
            if (zone == null || zone.activeCard == null) continue;
            Card avatar = zone.activeCard;

            if (lift)
            {
                // Skip avatars that don't match required symbol
                if (requiredSymbol != CardSymbol.None && avatar.cardSymbol != requiredSymbol)
                    continue;

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
            if (GameplayLogger.instance != null)
                GameplayLogger.instance.LogMagic($"React! {reactCard.cardName} destroys {target.cardName}!");
            CombatController.instance.SendToHell(target);
            CombatController.instance.SendToHell(reactCard);
        }
        else if (reactCard.magicSO.effect == MagicEffect.ReactDiscardThonSoop)
        {
            // Enter hand selection — player picks which [Symbol] card to discard
            BeginReactDiscardSelection(reactCard, target, reactOwner);
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

    // ════════════════════════════════════════════════════════════════
    //  DECK SEARCH — player picks which card to summon from deck panel
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Begin interactive deck search for SearchDeckByName effect.
    /// Shows a panel with all matching avatars; player picks one to summon to hand.
    /// Returns true if matches were found (async flow), false if fizzle.
    /// </summary>
    public bool TryBeginDeckSearch(Card magicCard)
    {
        string namePrefix = magicCard.magicSO.searchName;
        int maxCost = magicCard.magicSO.effectValue;
        Player cp = GameManager.instance.CurrentPlayerObj;

        if (string.IsNullOrEmpty(namePrefix))
        {
            Debug.LogWarning($"[Magic] {magicCard.cardName}: searchName is empty! Cannot search by name.");
            return false;
        }

        // Query deck for all matching avatars (read-only)
        List<AvatarCardSO> matches = cp.deck.FindMatchingAvatarsByName(namePrefix, maxCost);

        if (matches.Count == 0)
        {
            Debug.Log($"[Magic] {magicCard.cardName}: No avatar matching '{namePrefix}' with cost ≤{maxCost} in deck! Effect fizzles.");
            return false;
        }

        // Enter deck search state
        pendingDeckSearchCard = magicCard;
        magicState = MagicPlayState.SelectingDeckSearch;

        // Show the deck search panel
        string title = $"Select a card to summon ({matches.Count} found)";
        UIController.instance?.ShowDeckSearchPanel(matches, title, HandleDeckSearchConfirm);

        Debug.Log($"[Magic] {magicCard.cardName}: Found {matches.Count} avatar(s) matching '{namePrefix}' (cost ≤{maxCost}). Awaiting player selection.");
        return true;
    }

    /// <summary>
    /// Called when the player clicks Summon in the deck search panel.
    /// Removes the selected avatar from deck, adds to hand, sends magic card to Hell.
    /// </summary>
    public void HandleDeckSearchConfirm(AvatarCardSO selectedSO)
    {
        if (pendingDeckSearchCard == null) return;

        Card magicCard = pendingDeckSearchCard;
        Player cp = GameManager.instance.CurrentPlayerObj;

        // Draw the specific avatar from deck → hand (cannotBeTribute, deck shuffled)
        Card drawn = cp.deck.SearchAndDrawSpecificAvatar(selectedSO);
        if (drawn != null)
        {
            Debug.Log($"[Magic] {magicCard.cardName}: Player selected '{drawn.cardName}' from deck → hand.");
        }

        // Send magic card to Hell
        CombatController.instance.SendToHell(magicCard);
        Debug.Log($"[Magic] {magicCard.cardName} → Hell (SearchDeckByName resolved).");

        // Reset state
        pendingDeckSearchCard = null;
        magicState = MagicPlayState.Idle;
    }

    /// <summary>
    /// Cancel the deck search — close panel, return magic card to hand, reset state.
    /// Called when player clicks Close on the deck search panel.
    /// </summary>
    public void CancelDeckSearch()
    {
        if (pendingDeckSearchCard == null) return;

        Card magicCard = pendingDeckSearchCard;
        Player cp = GameManager.instance.CurrentPlayerObj;

        // Remove from magic zone and return to hand
        CardPlacePoint magicZone = cp.magicZone;
        if (magicZone != null && magicZone.activeCards.Contains(magicCard))
            magicZone.activeCards.Remove(magicCard);

        cp.hand.AddCardToHand(magicCard);
        Debug.Log($"[Magic] {magicCard.cardName}: Deck search cancelled → returned to hand.");

        // Reset state
        pendingDeckSearchCard = null;
        magicState = MagicPlayState.Idle;
    }

    // ════════════════════════════════════════════════════════════════
    //  REACT DISCARD SELECTION — player picks which hand card to discard
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Begin interactive hand card selection for ReactDiscardThonSoop.
    /// Highlights matching [Symbol] cards with cost >= summoned avatar's cost.
    /// </summary>
    private void BeginReactDiscardSelection(Card reactCard, Card target, Player reactOwner)
    {
        CardSymbol reqSymbol = reactCard.magicSO.targetSymbol;
        int minCost = target.cost;

        // Find all matching cards in the React owner's hand
        List<Card> matches = new List<Card>();
        foreach (Card c in reactOwner.hand.heldCards)
        {
            if (c != null && c.cardSymbol == reqSymbol && c.cost >= minCost)
                matches.Add(c);
        }

        if (matches.Count == 0)
        {
            // Shouldn't happen (checked in CheckReactTriggers) but handle gracefully
            Debug.LogWarning($"[React] No valid [{reqSymbol}] card to discard! React fizzles.");
            return;
        }

        // Save React context
        savedReactCard = reactCard;
        savedReactTarget = target;
        savedReactOwner = reactOwner;
        magicState = MagicPlayState.SelectingReactDiscard;

        // Highlight matching cards with green glow + raise them
        highlightedForReactDiscard.Clear();
        HandController hc = reactOwner.hand;
        foreach (Card c in matches)
        {
            c.SetReadyHighlight(true);
            highlightedForReactDiscard.Add(c);

            if (hc != null && c.handPosition < hc.cardPositions.Count)
            {
                Vector3 pos = hc.cardPositions[c.handPosition] + new Vector3(0f, 1.5f, 0.5f);
                c.MoveToPoint(pos, hc.minPos.rotation);
            }
        }

        string symbolName = UIController.instance != null
            ? UIController.instance.GetSymbolThaiNamePublic(reqSymbol)
            : reqSymbol.ToString();
        string ownerName = reactOwner.playerId == TurnPlayer.Player1 ? "Player 1" : "Player 2";

        UIController.instance?.ShowMagicUI(
            $"<color=yellow>React!</color> <b>{ownerName}</b>\n" +
            $"<b>{reactCard.cardName}</b>\n" +
            $"เลือกการ์ด [{symbolName}] (cost ≥ {minCost}) ในมือเพื่อทิ้ง\n" +
            $"<i>คลิกซ้ายเลือก — คลิกขวายกเลิก</i>");

        Debug.Log($"[React] Discard selection started: pick 1 [{reqSymbol}] cost ≥ {minCost} ({matches.Count} match(es)).");
    }

    /// <summary>Called when player clicks a hand card during SelectingReactDiscard.</summary>
    public void HandleHandCardClickForReactDiscard(Card clickedCard)
    {
        if (magicState != MagicPlayState.SelectingReactDiscard) return;
        if (!highlightedForReactDiscard.Contains(clickedCard)) return;

        // Clear highlights
        ClearReactDiscardHighlights();

        Card reactCard = savedReactCard;
        Card target = savedReactTarget;
        Player reactOwner = savedReactOwner;
        CardSymbol reqSymbol = reactCard.magicSO.targetSymbol;
        int thonSoopValue = reactCard.magicSO.effectValue;

        // Clear saved state early to prevent double-click
        savedReactCard = null;
        savedReactTarget = null;
        savedReactOwner = null;

        // Step 1: Discard the selected card
        Debug.Log($"[React] ACTIVATED! {reactCard.cardName}: Player chose to discard {clickedCard.cardName} [{reqSymbol}].");
        reactOwner.hand.RemoveCardFromHand(clickedCard);
        CombatController.instance.SendToHell(clickedCard);

        // Steps 2-6 run sequentially via coroutine
        StartCoroutine(DelayedReactThonSoop(reactOwner, reactCard, target, thonSoopValue));
    }

    /// <summary>
    /// Sequenced ReactDiscardThonSoop: discard anim → mill → draw → destroy → cleanup.
    /// </summary>
    private IEnumerator DelayedReactThonSoop(Player reactOwner, Card reactCard, Card target, int thonSoopValue)
    {
        // Wait for discard animation
        yield return new WaitForSeconds(0.5f);

        GameManager gm = GameManager.instance;
        if (gm.isGameOver) yield break;

        // Step 2: ธรณีสูบ — mill N cards (sequentially animated)
        int totalMill = thonSoopValue;
        if (AvatarAbilityController.instance != null)
            totalMill += AvatarAbilityController.GetThonSoopAmplification(reactOwner);
        var milledCards = new List<Card>();
        yield return reactOwner.deck.MillMultipleCards(totalMill, milledCards);
        Debug.Log($"[React] ธรณีสูบ! Milled {milledCards.Count} card(s) (base {thonSoopValue} + amp).");

        // Small pause between mill and draw
        yield return new WaitForSeconds(0.2f);

        if (gm.isGameOver) yield break;

        // Step 3: Draw N cards sequentially
        yield return reactOwner.deck.DrawMultipleCards(thonSoopValue);
        Debug.Log($"[React] {reactOwner.playerId} drew {thonSoopValue} card(s).");

        if (gm.isGameOver) yield break;

        // Step 4: Destroy summoned avatar
        CombatController.instance.SendToHell(target);
        Debug.Log($"[React] {target.cardName} destroyed!");

        // Step 5: Check milled cards for Hell activation
        CheckMilledForHellActivation(milledCards, reactOwner);

        if (AvatarAbilityController.instance != null)
        {
            AvatarAbilityController.instance.CheckMilledForAvatarHellSummon(milledCards, reactOwner);
            AvatarAbilityController.instance.RecalculateAllHellPowerScaling();
        }

        // Step 6: Consume React card
        CombatController.instance.SendToHell(reactCard);

        // Clean up state
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideMagicUI();
        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>Cancel react discard selection — React stays for later.</summary>
    public void CancelReactDiscardSelection()
    {
        if (magicState != MagicPlayState.SelectingReactDiscard) return;

        Debug.Log($"[React] Discard selection cancelled. React stays for later.");

        ClearReactDiscardHighlights();

        savedReactCard = null;
        savedReactTarget = null;
        savedReactOwner = null;
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideMagicUI();
    }

    /// <summary>Check if a card is highlighted for react discard selection.</summary>
    public bool IsHighlightedForReactDiscard(Card card) => highlightedForReactDiscard.Contains(card);

    /// <summary>Remove highlights and lower cards for react discard selection.</summary>
    private void ClearReactDiscardHighlights()
    {
        HandController hc = savedReactOwner?.hand;

        foreach (Card c in highlightedForReactDiscard)
        {
            if (c == null) continue;
            c.SetReadyHighlight(false);

            if (c.inHand && hc != null && c.handPosition < hc.cardPositions.Count)
                c.MoveToPoint(hc.cardPositions[c.handPosition], hc.minPos.rotation);
        }
        highlightedForReactDiscard.Clear();
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

        // Place new land card in the shared zone (rotated 90° horizontal like tapped cards)
        landZone.activeCard = landCard;
        landCard.assignedPlace = landZone;
        landCard.MoveToPoint(landZone.transform.position, Quaternion.Euler(0f, -90f, 0f));

        // Apply continuous land buff to all matching avatars on field (BOTH players)
        ApplyLandBuffsToAll(landCard);

        UIController.instance?.UpdateGameInfo();
        Debug.Log($"[Magic] Land magic {landCard.cardName} placed in LandMagic zone.");
        if (GameplayLogger.instance != null)
            GameplayLogger.instance.LogMagic($"Land magic {landCard.cardName} placed!");
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
    /// </summary>
    public void RegisterTempBuff(Card avatar, int value, Card sourceCard = null)
    {
        tempBuffs.Add(new TempBuff { avatar = avatar, value = value, sourceCard = sourceCard });
        Debug.Log($"[TempBuff] Registered: {avatar.cardName} +{value} (until end of turn)");
    }

    /// <summary>
    /// Remove all temporary buffs. Called from GameManager.SwitchTurn()
    /// at the end of each turn before switching to the next player.
    /// Also sends the source magic cards to Hell.
    /// </summary>
    public void CleanupTempBuffs()
    {
        if (tempBuffs.Count == 0) return;

        Debug.Log($"[TempBuff] Cleaning up {tempBuffs.Count} temp buff(s)...");

        // Collect unique source cards to send to Hell (avoid duplicates)
        HashSet<Card> sourcesToHell = new HashSet<Card>();

        foreach (var buff in tempBuffs)
        {
            if (buff.avatar != null)
            {
                buff.avatar.power = Mathf.Max(0, buff.avatar.power - buff.value);
                buff.avatar.RefreshPowerDisplay();
                Debug.Log($"[TempBuff] Expired: {buff.avatar.cardName} -{buff.value} → {buff.avatar.power}");
            }
            if (buff.sourceCard != null)
                sourcesToHell.Add(buff.sourceCard);
        }
        tempBuffs.Clear();

        // Send source magic cards to Hell
        foreach (Card src in sourcesToHell)
        {
            CombatController.instance.SendToHell(src);
            Debug.Log($"[TempBuff] {src.cardName} → Hell (temp boost expired).");
        }

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
        pendingModFromHell = true;

        // Add the mod card to owner's magic zone (stays there while equipped)
        if (owner.magicZone != null)
            owner.magicZone.AddCard(modCard);

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

    // ════════════════════════════════════════════════════════════════
    //  DISCARD-FOR-EFFECT — player selects which hand card to discard
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Begin interactive discard selection for DiscardSymbolDraw / DiscardSymbolThonSoop.
    /// Returns true if entering selection state (caller should NOT send magic card to Hell yet).
    /// Returns false if no matching cards (effect fizzles, caller handles normally).
    /// </summary>
    public bool TryBeginDiscardForEffect(Card magicCard)
    {
        MagicEffect effect = magicCard.magicSO.effect;
        CardSymbol sym = magicCard.magicSO.targetSymbol;
        int value = magicCard.magicSO.effectValue;
        Player cp = GameManager.instance.CurrentPlayerObj;

        // Find all matching cards in hand
        List<Card> matches = new List<Card>();
        foreach (Card c in cp.hand.heldCards)
        {
            if (c != null && c.cardSymbol == sym)
                matches.Add(c);
        }

        // No matches → fizzle
        if (matches.Count == 0)
        {
            Debug.Log($"[Magic] {magicCard.cardName}: No [{sym}] card in hand to discard! Effect fizzles.");
            return false;
        }

        // Enter selection state
        pendingDiscardEffectCard = magicCard;
        pendingDiscardEffectType = effect;
        pendingDiscardSymbol = sym;
        pendingDiscardValue = value;
        magicState = MagicPlayState.SelectingDiscardForEffect;

        // Highlight matching cards with green glow + raise them
        highlightedForDiscard.Clear();
        foreach (Card c in matches)
        {
            c.SetReadyHighlight(true);
            highlightedForDiscard.Add(c);

            // Raise the card slightly so it stands out
            HandController hc = cp.hand;
            if (hc != null && c.handPosition < hc.cardPositions.Count)
            {
                Vector3 pos = hc.cardPositions[c.handPosition];
                pos += new Vector3(0f, 1.5f, 0.5f);
                c.MoveToPoint(pos, hc.minPos.rotation);
            }
        }

        string symbolName = UIController.instance != null
            ? UIController.instance.GetSymbolThaiNamePublic(sym)
            : sym.ToString();

        UIController.instance?.ShowMagicUI(
            $"<b>{magicCard.cardName}</b>\n" +
            $"เลือกการ์ด [{symbolName}] ในมือเพื่อทิ้ง\n" +
            $"<i>คลิกซ้ายเลือก — คลิกขวายกเลิก</i>");

        Debug.Log($"[Magic] Discard selection started: pick 1 [{sym}] from hand ({matches.Count} match(es)).");
        return true;
    }

    /// <summary>
    /// Called when player clicks a hand card during SelectingDiscardForEffect.
    /// </summary>
    public void HandleHandCardClickForDiscard(Card clickedCard)
    {
        if (magicState != MagicPlayState.SelectingDiscardForEffect) return;
        if (!highlightedForDiscard.Contains(clickedCard)) return;

        // Clear highlights
        ClearDiscardHighlights();

        Player cp = GameManager.instance.CurrentPlayerObj;
        string cardName = pendingDiscardEffectCard?.cardName ?? "?";

        // Remove selected card from hand and send to Hell
        cp.hand.RemoveCardFromHand(clickedCard);
        CombatController.instance.SendToHell(clickedCard);
        Debug.Log($"[Magic] {cardName}: Player chose to discard {clickedCard.cardName} [{pendingDiscardSymbol}].");

        // Complete the effect based on type
        if (pendingDiscardEffectType == MagicEffect.DiscardSymbolDraw)
        {
            // Draw N cards sequentially after discard animation
            Card magicCard = pendingDiscardEffectCard;
            int drawCount = pendingDiscardValue;
            pendingDiscardEffectCard = null; // Prevent double-click

            StartCoroutine(DelayedDiscardDraw(cp, magicCard, cardName, drawCount));
            return; // Coroutine handles cleanup
        }
        else if (pendingDiscardEffectType == MagicEffect.DiscardSymbolThonSoop)
        {
            // ธรณีสูบ: delay so discard animation plays first, then mill + draw
            Card magicCard = pendingDiscardEffectCard;
            int discardValue = pendingDiscardValue;
            pendingDiscardEffectCard = null; // Prevent double-click

            StartCoroutine(DelayedThonSoop(cp, magicCard, cardName, discardValue));
            return; // Coroutine handles cleanup
        }
        else
        {
            FinishDiscardEffect(cardName);
        }
    }

    /// <summary>
    /// Waits for the discard animation to finish, then performs ธรณีสูบ (mill + draw).
    /// </summary>
    private IEnumerator DelayedThonSoop(Player cp, Card magicCard, string cardName, int discardValue)
    {
        // Wait for the discard-to-Hell animation to visually complete
        yield return new WaitForSeconds(0.5f);

        GameManager gm = GameManager.instance;
        if (gm.isGameOver) yield break;

        // ธรณีสูบ: mill cards from deck to Hell (sequentially animated)
        int totalMill = discardValue;
        if (AvatarAbilityController.instance != null)
            totalMill += AvatarAbilityController.GetThonSoopAmplification(cp);

        var milledCards = new List<Card>();
        yield return cp.deck.MillMultipleCards(totalMill, milledCards);
        Debug.Log($"[Magic] {cardName}: ธรณีสูบ! Sent {milledCards.Count} card(s) to Hell (base {discardValue} + amp).");

        // Small pause between mill and draw
        yield return new WaitForSeconds(0.2f);

        if (gm.isGameOver) yield break;

        // Draw N cards sequentially
        yield return cp.deck.DrawMultipleCards(discardValue);
        Debug.Log($"[Magic] {cardName}: Drew {discardValue} card(s) after ธรณีสูบ.");

        if (gm.isGameOver) yield break;

        // Check milled cards for Hell activation
        CheckMilledForHellActivation(milledCards, cp);

        // Check milled cards for avatar Hell summon
        if (AvatarAbilityController.instance != null)
        {
            AvatarAbilityController.instance.CheckMilledForAvatarHellSummon(milledCards, cp);
            AvatarAbilityController.instance.RecalculateAllHellPowerScaling();
        }

        // Send the magic card to Hell
        if (magicCard != null)
        {
            CombatController.instance.SendToHell(magicCard);
            Debug.Log($"[Magic] {cardName} → Hell (Normal magic resolved after discard selection).");
        }

        // Clean up state
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideMagicUI();
        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>
    /// Waits for the discard animation, then draws N cards sequentially.
    /// </summary>
    private IEnumerator DelayedDiscardDraw(Player cp, Card magicCard, string cardName, int drawCount)
    {
        // Wait for the discard-to-Hell animation
        yield return new WaitForSeconds(0.5f);

        GameManager gm = GameManager.instance;
        if (gm.isGameOver) yield break;

        // Draw N cards sequentially
        yield return cp.deck.DrawMultipleCards(drawCount);
        Debug.Log($"[Magic] {cardName}: Drew {drawCount} card(s) after discarding.");

        // Send the magic card to Hell
        if (magicCard != null)
        {
            CombatController.instance.SendToHell(magicCard);
            Debug.Log($"[Magic] {cardName} → Hell (DiscardSymbolDraw resolved).");
        }

        // Clean up state
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideMagicUI();
        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>Finalize discard effect: send magic card to Hell and clean up state.</summary>
    private void FinishDiscardEffect(string cardName)
    {
        if (pendingDiscardEffectCard != null)
        {
            CombatController.instance.SendToHell(pendingDiscardEffectCard);
            Debug.Log($"[Magic] {cardName} → Hell (Normal magic resolved after discard selection).");
        }

        pendingDiscardEffectCard = null;
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideMagicUI();
        UIController.instance?.UpdateGameInfo();
    }

    // ════════════════════════════════════════════════════════════════
    //  SEQUENTIAL NORMAL-MAGIC COROUTINES (DrawCards, ThonSoop)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sequential DrawCards: draw N cards one at a time, then send magic card to Hell.
    /// Used for Normal magic with DrawCards effect when N > 1.
    /// </summary>
    private IEnumerator SequentialDrawCards(Card magicCard)
    {
        Player cp = GameManager.instance.CurrentPlayerObj;
        int count = magicCard.magicSO.effectValue;

        Debug.Log($"[Magic] {magicCard.cardName}: Drawing {count} card(s) sequentially...");
        yield return cp.deck.DrawMultipleCards(count);

        if (!GameManager.instance.isGameOver)
        {
            CombatController.instance.SendToHell(magicCard);
            Debug.Log($"[Magic] {magicCard.cardName} → Hell (DrawCards resolved).");
            UIController.instance?.UpdateGameInfo();
        }
    }

    /// <summary>
    /// Sequential ThonSoop: mill cards to Hell, wait, then draw sequentially, then cleanup.
    /// Used for Normal magic with ThonSoop effect.
    /// </summary>
    private IEnumerator SequentialThonSoop(Card magicCard)
    {
        Player cp = GameManager.instance.CurrentPlayerObj;
        int value = magicCard.magicSO.effectValue;
        string cardName = magicCard.cardName;

        // Ability 4: ThonSoopAmplifier adds extra mill
        int totalMill = value;
        if (AvatarAbilityController.instance != null)
            totalMill += AvatarAbilityController.GetThonSoopAmplification(cp);

        // Mill cards from deck to Hell (sequentially animated)
        var milledCards = new List<Card>();
        yield return cp.deck.MillMultipleCards(totalMill, milledCards);
        Debug.Log($"[Magic] {cardName}: ธรณีสูบ! Sent {milledCards.Count} card(s) to Hell (base {value} + amp).");

        // Small pause between mill and draw
        yield return new WaitForSeconds(0.2f);

        if (GameManager.instance.isGameOver) yield break;

        // Draw N cards sequentially
        yield return cp.deck.DrawMultipleCards(value);

        if (GameManager.instance.isGameOver) yield break;

        Debug.Log($"[Magic] {cardName}: Drew {value} card(s) after ธรณีสูบ.");

        // Check milled cards for Hell activation
        CheckMilledForHellActivation(milledCards, cp);

        // Check milled cards for avatar Hell summon
        if (AvatarAbilityController.instance != null)
        {
            AvatarAbilityController.instance.CheckMilledForAvatarHellSummon(milledCards, cp);
            AvatarAbilityController.instance.RecalculateAllHellPowerScaling();
        }

        // Send magic card to Hell
        CombatController.instance.SendToHell(magicCard);
        Debug.Log($"[Magic] {cardName} → Hell (ThonSoop resolved).");
        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>Cancel discard selection — return magic card to hand.</summary>
    public void CancelDiscardForEffect()
    {
        if (magicState != MagicPlayState.SelectingDiscardForEffect) return;

        Debug.Log($"[Magic] Discard selection cancelled: {pendingDiscardEffectCard?.cardName} returned to hand.");

        ClearDiscardHighlights();

        // Return the magic card to hand (undo the play)
        if (pendingDiscardEffectCard != null)
        {
            Player cp = GameManager.instance.CurrentPlayerObj;

            // Remove from magic zone
            if (pendingDiscardEffectCard.assignedPlace != null
                && pendingDiscardEffectCard.assignedPlace.isMultiCardZone)
            {
                pendingDiscardEffectCard.assignedPlace.RemoveCard(pendingDiscardEffectCard);
            }

            // Add back to hand
            pendingDiscardEffectCard.inHand = true;
            cp.hand.AddCardToHand(pendingDiscardEffectCard);
        }

        pendingDiscardEffectCard = null;
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideMagicUI();
        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>Check if a card is currently highlighted for discard selection.</summary>
    public bool IsHighlightedForDiscard(Card card) => highlightedForDiscard.Contains(card);

    /// <summary>Remove highlights and lower all cards that were highlighted for discard selection.</summary>
    private void ClearDiscardHighlights()
    {
        Player cp = GameManager.instance.CurrentPlayerObj;
        HandController hc = cp.hand;

        foreach (Card c in highlightedForDiscard)
        {
            if (c == null) continue;
            c.SetReadyHighlight(false);

            // Lower card back to hand position
            if (c.inHand && hc != null && c.handPosition < hc.cardPositions.Count)
            {
                c.MoveToPoint(hc.cardPositions[c.handPosition], hc.minPos.rotation);
            }
        }
        highlightedForDiscard.Clear();
    }

    // ════════════════════════════════════════════════════════════════
    //  TEMP BOOST TARGET — player selects which avatar to boost
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Begin interactive avatar selection for PowerBoostSymbolTemp.
    /// Returns true if entering selection (caller should NOT send magic card to Hell).
    /// Returns false if no matching avatars (effect fizzles).
    /// </summary>
    public bool TryBeginTempBoostSelection(Card magicCard)
    {
        CardSymbol sym = magicCard.magicSO.targetSymbol;
        int value = magicCard.magicSO.effectValue;
        Player cp = GameManager.instance.CurrentPlayerObj;

        // Find all matching avatars on field
        List<Card> matches = new List<Card>();
        foreach (var zone in cp.avatarZones)
        {
            if (zone == null || zone.activeCard == null) continue;
            if (zone.activeCard.cardSymbol == sym)
                matches.Add(zone.activeCard);
        }

        // No matches → fizzle
        if (matches.Count == 0)
        {
            Debug.Log($"[Magic] {magicCard.cardName}: No [{sym}] avatar on field. Effect fizzles.");
            return false;
        }

        // Enter selection state
        pendingTempBoostCard = magicCard;
        pendingTempBoostSymbol = sym;
        pendingTempBoostValue = value;
        magicState = MagicPlayState.SelectingTempBoostTarget;

        // Highlight matching avatars with green glow + raise them
        highlightedForTempBoost.Clear();
        foreach (Card avatar in matches)
        {
            originalAvatarPositions[avatar] = avatar.assignedPlace.transform.position;

            Vector3 raised = avatar.assignedPlace.transform.position + new Vector3(0f, 0.3f, 0f);
            Quaternion rot = avatar.isTapped
                ? Quaternion.Euler(0f, -90f, 0f)
                : Quaternion.identity;
            avatar.MoveToPoint(raised, rot);
            avatar.SetReadyHighlight(true);
            highlightedForTempBoost.Add(avatar);
        }

        string symbolName = UIController.instance != null
            ? UIController.instance.GetSymbolThaiNamePublic(sym)
            : sym.ToString();

        UIController.instance?.ShowMagicUI(
            $"<b>{magicCard.cardName}</b>\n" +
            $"เลือก Avatar [{symbolName}] บนสนามเพื่อเพิ่มพลัง +{value}\n" +
            $"(จนจบเทิร์น)\n" +
            $"<i>คลิกซ้ายเลือก — คลิกขวายกเลิก</i>");

        Debug.Log($"[Magic] TempBoost selection started: pick 1 [{sym}] avatar ({matches.Count} match(es)).");
        return true;
    }

    /// <summary>Called when player clicks an avatar during SelectingTempBoostTarget.</summary>
    public void HandleAvatarClickForTempBoost(Card clickedAvatar)
    {
        if (magicState != MagicPlayState.SelectingTempBoostTarget) return;
        if (!highlightedForTempBoost.Contains(clickedAvatar)) return;

        // Clear highlights and lower all avatars
        ClearTempBoostHighlights();

        // Apply the temporary power boost
        clickedAvatar.power += pendingTempBoostValue;
        clickedAvatar.RefreshPowerDisplay();

        // Register temp buff (expires at end of turn) with source card
        RegisterTempBuff(clickedAvatar, pendingTempBoostValue, pendingTempBoostCard);

        Debug.Log($"[Magic] {pendingTempBoostCard.cardName}: {clickedAvatar.cardName} [{pendingTempBoostSymbol}] power +{pendingTempBoostValue} until end of turn → {clickedAvatar.power}");

        // Magic card stays on board (in magic zone) — will go to Hell when buff expires
        pendingTempBoostCard = null;
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideMagicUI();
        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>Cancel temp boost selection — return magic card to hand.</summary>
    public void CancelTempBoostSelection()
    {
        if (magicState != MagicPlayState.SelectingTempBoostTarget) return;

        Debug.Log($"[Magic] TempBoost selection cancelled: {pendingTempBoostCard?.cardName} returned to hand.");

        ClearTempBoostHighlights();

        // Return the magic card to hand
        if (pendingTempBoostCard != null)
        {
            Player cp = GameManager.instance.CurrentPlayerObj;

            if (pendingTempBoostCard.assignedPlace != null
                && pendingTempBoostCard.assignedPlace.isMultiCardZone)
            {
                pendingTempBoostCard.assignedPlace.RemoveCard(pendingTempBoostCard);
            }

            pendingTempBoostCard.inHand = true;
            cp.hand.AddCardToHand(pendingTempBoostCard);
        }

        pendingTempBoostCard = null;
        magicState = MagicPlayState.Idle;
        UIController.instance?.HideMagicUI();
        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>Check if an avatar is highlighted for temp boost selection.</summary>
    public bool IsHighlightedForTempBoost(Card card) => highlightedForTempBoost.Contains(card);

    /// <summary>Remove highlights and lower avatars for temp boost selection.</summary>
    private void ClearTempBoostHighlights()
    {
        foreach (Card avatar in highlightedForTempBoost)
        {
            if (avatar == null) continue;
            avatar.SetReadyHighlight(false);

            if (originalAvatarPositions.TryGetValue(avatar, out Vector3 origPos))
            {
                Quaternion rot = avatar.isTapped
                    ? Quaternion.Euler(0f, -90f, 0f)
                    : Quaternion.identity;
                avatar.MoveToPoint(origPos, rot);
            }
        }
        highlightedForTempBoost.Clear();
        originalAvatarPositions.Clear();
    }
}
