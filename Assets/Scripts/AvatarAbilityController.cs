using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Handles all Avatar ability triggers and resolution.
/// Singleton controller following existing BattleController/CombatController patterns.
///
/// Batch 1 Ability hooks:
///   CanSummonFromHand()              — gate for CannotSummonFromHand
///   OnAvatarSummonedFromCost()       — Juti trigger (Hell search, TargetDebuff, TargetBuff, Draw)
///   OnCombatEngagement()             — CombatThonSoop + AttackPowerBoost
///   GetThonSoopAmplification()       — ThonSoopAmplifier
///   CheckMilledForAvatarHellSummon() — HellSummonOnThonSoop
///   RecalculateAllHellPowerScaling() — HellPowerScaling
///
/// Batch 2 Ability hooks:
///   BeginJutiTargetSelection()       — JutiTargetDebuff / JutiTargetBuff target pick
///   HandleAvatarClickForJuti()       — click routing during Juti target selection
///   RecalculateAllAuraBuffs()        — SymbolAura continuous buff
/// </summary>
public class AvatarAbilityController : MonoBehaviour
{
    public static AvatarAbilityController instance;

    // ── HELL SUMMON QUEUE (mirrors MagicController's hellActivationQueue) ──
    private Queue<Card> avatarHellSummonQueue = new Queue<Card>();
    private Card pendingAvatarHellSummon;
    private Player avatarHellSummonOwner;

    /// <summary>Returns true if currently prompting for an avatar Hell summon.</summary>
    public bool IsAwaitingAvatarHellSummon => pendingAvatarHellSummon != null;

    // ── JUTI TARGET SELECTION STATE ──────────────────────────────────
    private bool isSelectingJutiTarget = false;
    private int pendingJutiPowerValue;      // +3 or -4 (signed)
    private bool jutiTargetEnemyOnly;       // true = select enemy, false = select friendly
    private bool jutiTargetBothSides;      // true = can select ANY avatar (buff targets both sides)
    private Player pendingJutiOwner;        // player who summoned the Juti avatar
    private Dictionary<Card, Vector3> originalJutiPositions = new Dictionary<Card, Vector3>();

    /// <summary>Returns true if currently in Juti target selection mode.</summary>
    public bool IsSelectingJutiTarget => isSelectingJutiTarget;

    // ── JUTI HELL SEARCH STATE (player picks which card to summon from Hell) ──
    private bool isSelectingJutiHellSearch = false;
    private Card pendingJutiHellAvatar;     // The avatar that triggered the Juti ability
    private Player pendingJutiHellOwner;    // Player who owns the Juti avatar

    /// <summary>Returns true if currently in Juti Hell search selection mode.</summary>
    public bool IsSelectingJutiHellSearch => isSelectingJutiHellSearch;

    private void Awake()
    {
        instance = this;
    }

    // ════════════════════════════════════════════════════════════════
    //  ABILITY 3 GATE — CannotSummonFromHand
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns false if the avatar has CannotSummonFromHand ability.
    /// Called from Card.OnPointerDown, Card.HandleDrop, BattleController.InitiateSummon.
    /// </summary>
    public static bool CanSummonFromHand(Card avatar)
    {
        if (avatar == null || avatar.cardType != CardType.Avatar) return true;
        return !avatar.HasAbility(AvatarAbility.CannotSummonFromHand);
    }

    // ════════════════════════════════════════════════════════════════
    //  จุติ (Juti): On-Summon from Cost — ALL VARIANTS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called ONLY from BattleController.FinalizeSummon() — not from free place or Hell summon.
    /// Handles all Juti variants: Hell search, Draw, TargetDebuff, TargetBuff.
    /// </summary>
    public void OnAvatarSummonedFromCost(Card summonedAvatar)
    {
        if (summonedAvatar == null) return;

        Player owner = GameManager.instance.GetPlayer(summonedAvatar.cardOwner);
        if (owner == null) return;

        // ── Original Juti (immediate): search Hell for matching avatar ──
        if (summonedAvatar.HasAbility(AvatarAbility.Juti)
            && !string.IsNullOrEmpty(summonedAvatar.jutiSearchPrefix))
        {
            ResolveJutiHellSearch(summonedAvatar, owner);
        }

        // ── JutiDraw: draw N cards sequentially, then continue to TargetDebuff/Buff ──
        if (summonedAvatar.HasAbility(AvatarAbility.JutiDraw)
            && summonedAvatar.jutiDrawCount > 0)
        {
            // Use coroutine so draws animate one-by-one, then remaining abilities run after
            StartCoroutine(JutiDrawThenContinue(summonedAvatar, owner));
            return;
        }

        // If no JutiDraw, proceed to target abilities immediately
        ResolveJutiTargetAbilities(summonedAvatar, owner);
    }

    /// <summary>Draw cards sequentially for JutiDraw, then resolve TargetDebuff/TargetBuff.</summary>
    private IEnumerator JutiDrawThenContinue(Card summonedAvatar, Player owner)
    {
        int count = summonedAvatar.jutiDrawCount;
        Debug.Log($"[JutiDraw] {summonedAvatar.cardName}: Drawing {count} card(s) sequentially...");

        yield return owner.deck.DrawMultipleCards(count);

        Debug.Log($"[JutiDraw] {summonedAvatar.cardName}: Drew {count} card(s).");
        UIController.instance?.UpdateGameInfo();

        if (GameManager.instance.isGameOver) yield break;

        // Continue to target abilities
        ResolveJutiTargetAbilities(summonedAvatar, owner);
    }

    /// <summary>Resolve JutiTargetDebuff and JutiTargetBuff abilities (called after JutiDraw completes).</summary>
    private void ResolveJutiTargetAbilities(Card summonedAvatar, Player owner)
    {
        // ── JutiTargetDebuff (async): select any avatar (own or enemy), apply temp power change ──
        if (summonedAvatar.HasAbility(AvatarAbility.JutiTargetDebuff)
            && summonedAvatar.jutiTargetPowerValue != 0)
        {
            Player opponent = (owner.playerId == TurnPlayer.Player1)
                ? GameManager.instance.player2
                : GameManager.instance.player1;

            // Fizzle only if NO avatars exist on either side
            if (owner.AvatarsOnField > 0 || (opponent != null && opponent.AvatarsOnField > 0))
            {
                BeginJutiTargetSelection(summonedAvatar.jutiTargetPowerValue, true, owner, true);
                return; // Async — waits for player click
            }
            else
            {
                Debug.Log($"[JutiDebuff] {summonedAvatar.cardName}: No avatars on field to target. Fizzles.");
            }
        }

        // ── JutiTargetBuff (async): select ANY avatar (own or opponent), apply temp +power ──
        if (summonedAvatar.HasAbility(AvatarAbility.JutiTargetBuff)
            && summonedAvatar.jutiTargetPowerValue != 0)
        {
            Player opponent = (owner.playerId == TurnPlayer.Player1)
                ? GameManager.instance.player2
                : GameManager.instance.player1;

            // Fizzle only if NO avatars exist on either side
            if (owner.AvatarsOnField > 0 || (opponent != null && opponent.AvatarsOnField > 0))
            {
                BeginJutiTargetSelection(summonedAvatar.jutiTargetPowerValue, false, owner, true);
                return; // Async — waits for player click
            }
            else
            {
                Debug.Log($"[JutiBuff] {summonedAvatar.cardName}: No avatars on field to target. Fizzles.");
            }
        }
    }

    /// <summary>
    /// Juti Hell search: find ALL matching avatars in Hell and show interactive panel.
    /// Player picks which card to summon from Hell to an empty avatar slot.
    /// </summary>
    private void ResolveJutiHellSearch(Card summonedAvatar, Player owner)
    {
        if (owner.hellZone == null) return;

        // Check if there's an empty avatar slot
        CardPlacePoint slot = owner.GetEmptyAvatarSlot();
        if (slot == null)
        {
            Debug.Log($"[Juti] {summonedAvatar.cardName}: No empty avatar slot available.");
            return;
        }

        // Search Hell for ALL Avatars matching the name prefix
        List<Card> matches = new List<Card>();
        for (int i = 0; i < owner.hellZone.activeCards.Count; i++)
        {
            Card card = owner.hellZone.activeCards[i];
            if (card != null && card.cardType == CardType.Avatar
                && card.cardName.StartsWith(summonedAvatar.jutiSearchPrefix, System.StringComparison.Ordinal))
            {
                matches.Add(card);
            }
        }

        if (matches.Count == 0)
        {
            Debug.Log($"[Juti] {summonedAvatar.cardName}: No '{summonedAvatar.jutiSearchPrefix}' avatar in Hell.");
            return;
        }

        // Enter Juti Hell search state
        isSelectingJutiHellSearch = true;
        pendingJutiHellAvatar = summonedAvatar;
        pendingJutiHellOwner = owner;

        // Show the hell search panel
        string title = $"Juti — Select avatar to summon from Hell ({matches.Count} found)";
        UIController.instance?.ShowHellSearchPanel(matches, title, HandleJutiHellSearchConfirm);

        Debug.Log($"[Juti] {summonedAvatar.cardName}: Found {matches.Count} avatar(s) matching '{summonedAvatar.jutiSearchPrefix}' in Hell. Awaiting player selection.");
    }

    /// <summary>
    /// Called when the player clicks Summon in the Juti hell search panel.
    /// Removes selected avatar from Hell and summons it to an empty avatar slot.
    /// </summary>
    public void HandleJutiHellSearchConfirm(Card selectedCard)
    {
        if (!isSelectingJutiHellSearch || pendingJutiHellOwner == null) return;

        Player owner = pendingJutiHellOwner;
        Card summonedAvatar = pendingJutiHellAvatar;

        // Find empty avatar slot
        CardPlacePoint slot = owner.GetEmptyAvatarSlot();
        if (slot == null)
        {
            Debug.Log($"[Juti] {summonedAvatar?.cardName}: No empty avatar slot available.");
            EndJutiHellSearch();
            return;
        }

        // Remove from Hell zone
        if (owner.hellZone != null)
            owner.hellZone.RemoveCard(selectedCard);

        // Place on board
        SummonAvatarToSlot(selectedCard, slot);

        Debug.Log($"[Juti] {summonedAvatar?.cardName} summoned {selectedCard.cardName} from Hell to {slot.name}!");
        UIController.instance?.UpdateGameInfo();

        EndJutiHellSearch();
    }

    /// <summary>Cancel Juti Hell search — close panel, effect fizzles.</summary>
    public void CancelJutiHellSearch()
    {
        if (!isSelectingJutiHellSearch) return;

        Debug.Log($"[Juti] Hell search cancelled. Effect fizzles.");
        EndJutiHellSearch();
    }

    /// <summary>Clean up Juti Hell search state.</summary>
    private void EndJutiHellSearch()
    {
        isSelectingJutiHellSearch = false;
        pendingJutiHellAvatar = null;
        pendingJutiHellOwner = null;
    }

    // ════════════════════════════════════════════════════════════════
    //  JUTI TARGET SELECTION — Pick avatar for temp buff/debuff
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Enter Juti target selection mode. Highlights valid targets and waits for click.
    /// </summary>
    /// <param name="powerValue">Signed power change (negative = debuff, positive = buff)</param>
    /// <param name="targetEnemy">true = highlight enemy avatars, false = highlight friendly</param>
    /// <param name="owner">Player who summoned the Juti avatar</param>
    /// <param name="bothSides">true = can target ANY avatar (own + opponent)</param>
    private void BeginJutiTargetSelection(int powerValue, bool targetEnemy, Player owner, bool bothSides = false)
    {
        isSelectingJutiTarget = true;
        pendingJutiPowerValue = powerValue;
        jutiTargetEnemyOnly = targetEnemy;
        jutiTargetBothSides = bothSides;
        pendingJutiOwner = owner;

        if (bothSides)
        {
            // Highlight BOTH players' avatars
            LiftAvatarsForJutiSelection(owner, true);
            Player opponent = (owner.playerId == TurnPlayer.Player1)
                ? GameManager.instance.player2
                : GameManager.instance.player1;
            LiftAvatarsForJutiSelection(opponent, true);
        }
        else
        {
            // Highlight only one side
            Player targetPlayer;
            if (targetEnemy)
            {
                targetPlayer = (owner.playerId == TurnPlayer.Player1)
                    ? GameManager.instance.player2
                    : GameManager.instance.player1;
            }
            else
            {
                targetPlayer = owner;
            }
            LiftAvatarsForJutiSelection(targetPlayer, true);
        }

        string actionDesc = powerValue < 0
            ? $"<color=red>POWER {powerValue}</color>"
            : $"<color=green>POWER +{powerValue}</color>";
        string sideDesc = bothSides ? "any" : (targetEnemy ? "enemy" : "friendly");

        UIController.instance?.ShowMagicUI(
            $"<b>จุติ Target Selection</b>\n" +
            $"Select {(bothSides ? "any" : (targetEnemy ? "an enemy" : "a friendly"))} Avatar to apply {actionDesc} (until end of turn).\n" +
            $"<i>Right-click to cancel (effect fizzles).</i>");

        Debug.Log($"[Juti] Target selection started: {actionDesc} on {sideDesc} avatar.");
    }

    /// <summary>
    /// Called from Card.OnPointerDown when a card is clicked during Juti target selection.
    /// Validates the target and applies the temp buff/debuff.
    /// </summary>
    public void HandleAvatarClickForJuti(Card clickedAvatar)
    {
        if (!isSelectingJutiTarget) return;
        if (clickedAvatar == null || clickedAvatar.cardType != CardType.Avatar) return;
        if (clickedAvatar.inHand) return;

        // Validate correct side
        if (jutiTargetBothSides)
        {
            // Can target ANY avatar (own or opponent) — no side restriction
        }
        else if (jutiTargetEnemyOnly)
        {
            // Must be opponent's avatar
            if (clickedAvatar.cardOwner == pendingJutiOwner.playerId)
            {
                Debug.Log("[Juti] Must select an ENEMY avatar!");
                return;
            }
        }
        else
        {
            // Must be owner's avatar
            if (clickedAvatar.cardOwner != pendingJutiOwner.playerId)
            {
                Debug.Log("[Juti] Must select a FRIENDLY avatar!");
                return;
            }
        }

        // Apply temp power change
        clickedAvatar.power += pendingJutiPowerValue;
        clickedAvatar.power = Mathf.Max(0, clickedAvatar.power); // Clamp to 0
        clickedAvatar.RefreshPowerDisplay();

        // Register temp buff for end-of-turn cleanup
        // RegisterTempBuff stores value and cleanup does power -= value
        // For debuff (-4): apply power += (-4), register (-4), cleanup: power -= (-4) = power += 4 ✓
        if (MagicController.instance != null)
            MagicController.instance.RegisterTempBuff(clickedAvatar, pendingJutiPowerValue);

        string effectDesc = pendingJutiPowerValue < 0
            ? $"debuffed {pendingJutiPowerValue}"
            : $"buffed +{pendingJutiPowerValue}";
        Debug.Log($"[Juti] {clickedAvatar.cardName} {effectDesc} → power {clickedAvatar.power} (until end of turn)");

        // Clean up selection
        EndJutiTargetSelection();
    }

    /// <summary>Cancel Juti target selection — effect fizzles, no buff/debuff applied.</summary>
    public void CancelJutiSelection()
    {
        if (!isSelectingJutiTarget) return;

        Debug.Log("[Juti] Target selection cancelled. Effect fizzles.");
        EndJutiTargetSelection();
    }

    /// <summary>Clean up Juti target selection state — lower avatars, hide UI.</summary>
    private void EndJutiTargetSelection()
    {
        if (jutiTargetBothSides)
        {
            // Lower BOTH players' avatars
            LiftAvatarsForJutiSelection(pendingJutiOwner, false);
            Player opponent = (pendingJutiOwner.playerId == TurnPlayer.Player1)
                ? GameManager.instance.player2
                : GameManager.instance.player1;
            LiftAvatarsForJutiSelection(opponent, false);
        }
        else
        {
            // Lower only the targeted side
            Player targetPlayer;
            if (jutiTargetEnemyOnly)
            {
                targetPlayer = (pendingJutiOwner.playerId == TurnPlayer.Player1)
                    ? GameManager.instance.player2
                    : GameManager.instance.player1;
            }
            else
            {
                targetPlayer = pendingJutiOwner;
            }
            LiftAvatarsForJutiSelection(targetPlayer, false);
        }

        isSelectingJutiTarget = false;
        pendingJutiPowerValue = 0;
        jutiTargetBothSides = false;
        pendingJutiOwner = null;

        UIController.instance?.HideMagicUI();
        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>
    /// Raise or lower avatars for Juti target selection.
    /// Mirrors MagicController.LiftAvatarsForSelection pattern.
    /// </summary>
    private void LiftAvatarsForJutiSelection(Player targetPlayer, bool lift)
    {
        foreach (var zone in targetPlayer.avatarZones)
        {
            if (zone == null || zone.activeCard == null) continue;
            Card avatar = zone.activeCard;

            if (lift)
            {
                originalJutiPositions[avatar] = zone.transform.position;
                Vector3 raised = zone.transform.position + new Vector3(0f, 0.3f, 0f);
                Quaternion rot = avatar.isTapped
                    ? Quaternion.Euler(0f, -90f, 0f)
                    : Quaternion.identity;
                avatar.MoveToPoint(raised, rot);
                avatar.SetReadyHighlight(true);
            }
            else
            {
                if (originalJutiPositions.TryGetValue(avatar, out Vector3 origPos))
                {
                    Quaternion rot = avatar.isTapped
                        ? Quaternion.Euler(0f, -90f, 0f)
                        : Quaternion.identity;
                    avatar.MoveToPoint(origPos, rot);
                }
                avatar.SetReadyHighlight(false);
            }
        }

        if (!lift) originalJutiPositions.Clear();
    }

    // ════════════════════════════════════════════════════════════════
    //  CombatThonSoop + AttackPowerBoost: Combat trigger
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Called from CombatController when an avatar attacks or is targeted.
    /// Handles CombatThonSoop (attack/defend) and AttackPowerBoost (attack only).
    /// </summary>
    /// <param name="avatar">The avatar engaged in combat.</param>
    /// <param name="isAttacker">true if this avatar is the attacker, false if defender.</param>
    public void OnCombatEngagement(Card avatar, bool isAttacker)
    {
        if (avatar == null) return;

        // ── CombatThonSoop needs sequential animation — run via coroutine ──
        if (avatar.HasAbility(AvatarAbility.CombatThonSoop) && avatar.combatThonSoopMill > 0)
        {
            StartCoroutine(CombatThonSoopSequence(avatar, isAttacker));
            return; // AttackPowerBoost handled inside the coroutine
        }

        // ── AttackPowerBoost: triggers ONLY on attack (no CombatThonSoop) ──
        if (isAttacker && avatar.HasAbility(AvatarAbility.AttackPowerBoost) && avatar.attackPowerBoost > 0)
        {
            int boost = avatar.attackPowerBoost;
            avatar.power += boost;
            avatar.RefreshPowerDisplay();
            if (MagicController.instance != null)
                MagicController.instance.RegisterTempBuff(avatar, boost);
            Debug.Log($"[AttackBoost] {avatar.cardName}: +{boost} power on attack → {avatar.power}");
        }

        UIController.instance?.UpdateGameInfo();
    }

    /// <summary>
    /// Sequenced CombatThonSoop: mill cards one-by-one, then boost, then check Hell, then AttackPowerBoost.
    /// </summary>
    private IEnumerator CombatThonSoopSequence(Card avatar, bool isAttacker)
    {
        Player owner = GameManager.instance.GetPlayer(avatar.cardOwner);
        if (owner == null) yield break;

        int millCount = avatar.combatThonSoopMill;

        // Ability 4: ThonSoopAmplifier adds extra mill
        millCount += GetThonSoopAmplification(owner);

        // Mill cards sequentially (animated)
        var milledCards = new List<Card>();
        yield return owner.deck.MillMultipleCards(millCount, milledCards);
        Debug.Log($"[CombatThonSoop] {avatar.cardName}: ธรณีสูบ {milledCards.Count} card(s).");

        // Apply temp power boost
        int boost = avatar.combatThonSoopPowerBoost;
        if (boost > 0)
        {
            avatar.power += boost;
            avatar.RefreshPowerDisplay();
            if (MagicController.instance != null)
                MagicController.instance.RegisterTempBuff(avatar, boost);
            Debug.Log($"[CombatThonSoop] {avatar.cardName}: +{boost} power until end of turn → {avatar.power}");
        }

        // Check milled cards for existing Hell activation (mods with activateFromHellOnThonSoop)
        if (MagicController.instance != null)
            MagicController.instance.CheckMilledForHellActivation(milledCards, owner);

        // Ability 3: Check milled cards for avatar Hell summon
        CheckMilledForAvatarHellSummon(milledCards, owner);

        // Ability 5: Recalculate Hell scaling (cards entered Hell)
        RecalculateAllHellPowerScaling();

        // ── AttackPowerBoost: triggers ONLY on attack ──
        if (isAttacker && avatar.HasAbility(AvatarAbility.AttackPowerBoost) && avatar.attackPowerBoost > 0)
        {
            int atkBoost = avatar.attackPowerBoost;
            avatar.power += atkBoost;
            avatar.RefreshPowerDisplay();
            if (MagicController.instance != null)
                MagicController.instance.RegisterTempBuff(avatar, atkBoost);
            Debug.Log($"[AttackBoost] {avatar.cardName}: +{atkBoost} power on attack → {avatar.power}");
        }

        UIController.instance?.UpdateGameInfo();
    }

    // ════════════════════════════════════════════════════════════════
    //  ThonSoopAmplifier: +2 additional mill
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns additional mill count from any ThonSoopAmplifier avatars on the player's field.
    /// Each amplifier adds +2 to every ธรณีสูบ effect.
    /// Called from all ThonSoop resolution points.
    /// </summary>
    public static int GetThonSoopAmplification(Player player)
    {
        int extra = 0;
        foreach (var zone in player.avatarZones)
        {
            if (zone != null && zone.activeCard != null
                && zone.activeCard.HasAbility(AvatarAbility.ThonSoopAmplifier))
            {
                extra += 2;
            }
        }
        return extra;
    }

    // ════════════════════════════════════════════════════════════════
    //  HellSummonOnThonSoop: Summon from Hell after mill
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// After ธรณีสูบ mills cards, check if any are avatars with HellSummonOnThonSoop.
    /// If found, queue them for confirmation prompts.
    /// </summary>
    public void CheckMilledForAvatarHellSummon(List<Card> milledCards, Player owner)
    {
        if (milledCards == null || milledCards.Count == 0) return;

        foreach (Card card in milledCards)
        {
            if (card == null || card.cardType != CardType.Avatar) continue;
            if (!card.HasAbility(AvatarAbility.HellSummonOnThonSoop)) continue;

            avatarHellSummonQueue.Enqueue(card);
            Debug.Log($"[HellSummon] {card.cardName} milled by ธรณีสูบ — can summon from Hell!");
        }

        if (avatarHellSummonQueue.Count > 0)
        {
            avatarHellSummonOwner = owner;
            ProcessNextAvatarHellSummon();
        }
    }

    /// <summary>Process the next avatar Hell summon prompt from the queue.</summary>
    private void ProcessNextAvatarHellSummon()
    {
        if (avatarHellSummonQueue.Count == 0)
        {
            avatarHellSummonOwner = null;
            pendingAvatarHellSummon = null;
            return;
        }

        pendingAvatarHellSummon = avatarHellSummonQueue.Dequeue();

        // Check if there's an empty slot
        if (avatarHellSummonOwner.GetEmptyAvatarSlot() == null)
        {
            Debug.Log($"[HellSummon] {pendingAvatarHellSummon.cardName}: No empty avatar slot. Skipping.");
            pendingAvatarHellSummon = null;
            ProcessNextAvatarHellSummon();
            return;
        }

        // Use the shared AwaitingHellActivation state
        if (MagicController.instance != null)
            MagicController.instance.magicState = MagicPlayState.AwaitingHellActivation;

        string ownerName = avatarHellSummonOwner.playerId == TurnPlayer.Player1
            ? "Player 1" : "Player 2";
        UIController.instance?.ShowReactUI(
            $"<color=orange>Hell Summon!</color> <b>{ownerName}</b>\n\n" +
            $"<b>{pendingAvatarHellSummon.cardName}</b> was milled by ธรณีสูบ.\n" +
            $"Power: {pendingAvatarHellSummon.power}\n\n" +
            $"<i>Summon from Hell, or leave it?</i>");

        Debug.Log($"[HellSummon] Prompting {ownerName} to summon {pendingAvatarHellSummon.cardName} from Hell.");
    }

    /// <summary>Called when player clicks "Activate" for an avatar Hell summon prompt.</summary>
    public void OnAvatarHellSummonActivate()
    {
        if (pendingAvatarHellSummon == null) return;

        Card avatar = pendingAvatarHellSummon;
        Player owner = avatarHellSummonOwner;
        pendingAvatarHellSummon = null;

        if (MagicController.instance != null)
            MagicController.instance.magicState = MagicPlayState.Idle;
        UIController.instance?.HideReactUI();

        // Find empty slot
        CardPlacePoint slot = owner.GetEmptyAvatarSlot();
        if (slot == null)
        {
            Debug.Log($"[HellSummon] {avatar.cardName}: No empty slot. Card stays in Hell.");
            ProcessNextAvatarHellSummon();
            return;
        }

        // Remove from Hell zone
        if (avatar.assignedPlace != null && avatar.assignedPlace.isMultiCardZone)
            avatar.assignedPlace.RemoveCard(avatar);
        else if (avatar.assignedPlace != null)
        {
            avatar.assignedPlace.activeCard = null;
            avatar.assignedPlace = null;
        }

        // Place on board
        SummonAvatarToSlot(avatar, slot);

        Debug.Log($"[HellSummon] {avatar.cardName} summoned from Hell to {slot.name}!");
        UIController.instance?.UpdateGameInfo();

        ProcessNextAvatarHellSummon();
    }

    /// <summary>Called when player clicks "Keep" for an avatar Hell summon prompt.</summary>
    public void OnAvatarHellSummonKeep()
    {
        Debug.Log($"[HellSummon] DECLINED. {pendingAvatarHellSummon?.cardName} stays in Hell.");

        pendingAvatarHellSummon = null;

        if (MagicController.instance != null)
            MagicController.instance.magicState = MagicPlayState.Idle;
        UIController.instance?.HideReactUI();

        ProcessNextAvatarHellSummon();
    }

    // ════════════════════════════════════════════════════════════════
    //  HellPowerScaling: +1 per distinct name in Hell
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recalculate HellPowerScaling for ALL avatars on field that have the ability.
    /// Called whenever any card enters or leaves Hell.
    /// </summary>
    public void RecalculateAllHellPowerScaling()
    {
        GameManager gm = GameManager.instance;
        if (gm == null) return;

        RecalculateHellPowerForPlayer(gm.player1);
        RecalculateHellPowerForPlayer(gm.player2);
    }

    private void RecalculateHellPowerForPlayer(Player player)
    {
        if (player == null) return;

        foreach (var zone in player.avatarZones)
        {
            if (zone == null || zone.activeCard == null) continue;
            Card avatar = zone.activeCard;
            if (!avatar.HasAbility(AvatarAbility.HellPowerScaling)) continue;
            if (string.IsNullOrEmpty(avatar.hellScalingNamePrefix)) continue;

            // Remove old bonus
            avatar.power -= avatar.hellScalingBonus;

            // Count distinct names in Hell matching prefix
            int newBonus = CountDistinctNamesInHell(player, avatar.hellScalingNamePrefix);

            // Apply new bonus
            avatar.hellScalingBonus = newBonus;
            avatar.power += newBonus;
            avatar.RefreshPowerDisplay();

            Debug.Log($"[HellScale] {avatar.cardName}: {newBonus} distinct '{avatar.hellScalingNamePrefix}' names in Hell → power {avatar.power}");
        }
    }

    /// <summary>
    /// Count distinct cardNames among Avatar cards in the player's Hell zone
    /// whose name starts with the given prefix.
    /// </summary>
    private int CountDistinctNamesInHell(Player player, string namePrefix)
    {
        if (player.hellZone == null) return 0;

        HashSet<string> distinctNames = new HashSet<string>();
        foreach (Card card in player.hellZone.activeCards)
        {
            if (card != null && card.cardType == CardType.Avatar
                && card.cardName.StartsWith(namePrefix, System.StringComparison.Ordinal))
            {
                distinctNames.Add(card.cardName);
            }
        }
        return distinctNames.Count;
    }

    // ════════════════════════════════════════════════════════════════
    //  SymbolAura: Continuous +power to matching symbol on your side
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Recalculate SymbolAura buffs for ALL avatars on field.
    /// Uses a full recalculation approach (like HellPowerScaling) to avoid stacking errors.
    /// Called whenever any avatar enters or leaves the field.
    /// </summary>
    public void RecalculateAllAuraBuffs()
    {
        GameManager gm = GameManager.instance;
        if (gm == null) return;

        // Phase 1: Remove all existing aura bonuses
        RemoveAllAuraBonuses(gm.player1);
        RemoveAllAuraBonuses(gm.player2);

        // Phase 2: Recompute aura bonuses from all active aura sources
        ComputeAuraBonuses(gm.player1);
        ComputeAuraBonuses(gm.player2);

        // Phase 3: Apply new aura bonuses
        ApplyAllAuraBonuses(gm.player1);
        ApplyAllAuraBonuses(gm.player2);
    }

    /// <summary>Remove existing aura bonus from all avatars of a player.</summary>
    private void RemoveAllAuraBonuses(Player player)
    {
        if (player == null) return;
        foreach (var zone in player.avatarZones)
        {
            if (zone == null || zone.activeCard == null) continue;
            Card avatar = zone.activeCard;
            if (avatar.auraBonus != 0)
            {
                avatar.power -= avatar.auraBonus;
                avatar.auraBonus = 0;
            }
        }
    }

    /// <summary>
    /// Scan all aura sources for a player and accumulate auraBonus on their targets.
    /// Aura only buffs same-side avatars (not opponent), and does NOT buff itself.
    /// </summary>
    private void ComputeAuraBonuses(Player player)
    {
        if (player == null) return;

        // Find all aura sources on this player's field
        foreach (var sourceZone in player.avatarZones)
        {
            if (sourceZone == null || sourceZone.activeCard == null) continue;
            Card source = sourceZone.activeCard;
            if (!source.HasAbility(AvatarAbility.SymbolAura)) continue;
            if (source.auraSymbol == CardSymbol.None || source.auraPowerBoost == 0) continue;

            // Apply aura to all matching avatars on SAME side (not self)
            foreach (var targetZone in player.avatarZones)
            {
                if (targetZone == null || targetZone.activeCard == null) continue;
                Card target = targetZone.activeCard;
                if (target == source) continue; // Don't buff self
                if (target.cardSymbol != source.auraSymbol) continue;

                target.auraBonus += source.auraPowerBoost;
            }
        }
    }

    /// <summary>Apply accumulated aura bonus to all avatars and refresh display.</summary>
    private void ApplyAllAuraBonuses(Player player)
    {
        if (player == null) return;
        foreach (var zone in player.avatarZones)
        {
            if (zone == null || zone.activeCard == null) continue;
            Card avatar = zone.activeCard;
            if (avatar.auraBonus != 0)
            {
                avatar.power += avatar.auraBonus;
                avatar.RefreshPowerDisplay();
                Debug.Log($"[Aura] {avatar.cardName} [{avatar.cardSymbol}]: aura bonus +{avatar.auraBonus} → power {avatar.power}");
            }
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  SHARED HELPER — Summon avatar from Hell to board slot
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Place an avatar card from Hell onto an avatar slot.
    /// Triggers Land buffs and React checks via OnAvatarSummoned.
    /// Recalculates HellPowerScaling and SymbolAura (field changed).
    /// </summary>
    private void SummonAvatarToSlot(Card avatar, CardPlacePoint slot)
    {
        slot.activeCard = avatar;
        avatar.assignedPlace = slot;
        avatar.isSelected = false;
        avatar.inHand = false;
        avatar.isTapped = false;
        avatar.EnableInteraction();
        avatar.MoveToPoint(slot.transform.position, Quaternion.identity);

        if (GameManager.instance != null)
            avatar.turnPlaced = GameManager.instance.turnNumber;

        avatar.RefreshPowerDisplay();

        // Recalculate HellPowerScaling (Hell zone changed — a card left)
        RecalculateAllHellPowerScaling();

        // Recalculate SymbolAura (field changed — new avatar may gain/provide aura)
        RecalculateAllAuraBuffs();

        // Apply land buff and check React triggers for the newly summoned avatar
        if (MagicController.instance != null)
            MagicController.instance.OnAvatarSummoned(avatar);
    }
}
