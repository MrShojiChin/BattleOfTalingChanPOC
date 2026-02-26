using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Resolves magic card effects and LIFE card on-flip effects.
/// Static utility — no MonoBehaviour needed.
///
/// Targeting effects (PowerBoost, PowerReduce, DestroyAvatar) currently
/// apply to a default target (first valid card). Future: add target selection UI.
/// </summary>
public static class MagicEffectResolver
{
    /// <summary>
    /// Resolve a Magic card's effect when played from hand.
    /// Non-targeting effects resolve immediately.
    /// Targeting effects pick the best default target.
    /// </summary>
    public static void ResolveEffect(Card magicCard)
    {
        if (magicCard.magicSO == null) return;

        MagicEffect effect = magicCard.magicSO.effect;
        int value = magicCard.magicSO.effectValue;
        CardSymbol targetSym = magicCard.magicSO.targetSymbol;

        if (effect == MagicEffect.None) return;

        GameManager gm = GameManager.instance;
        Player cp = gm.CurrentPlayerObj;
        Player op = gm.OpponentPlayerObj;

        switch (effect)
        {
            // ── BASIC EFFECTS (existing) ─────────────────────────────

            case MagicEffect.DrawCards:
                for (int i = 0; i < value; i++)
                {
                    cp.deck.DrawCardToHand();
                    if (gm.isGameOver) return;
                }
                Debug.Log($"[Magic] {magicCard.cardName}: Drew {value} card(s).");
                break;

            case MagicEffect.PowerBoost:
                // Boost first friendly avatar on board
                Card friendlyTarget = FindFirstAvatar(cp);
                if (friendlyTarget != null)
                {
                    friendlyTarget.power += value;
                    friendlyTarget.RefreshPowerDisplay();
                    Debug.Log($"[Magic] {magicCard.cardName}: {friendlyTarget.cardName} power +{value} → {friendlyTarget.power}");
                }
                else
                {
                    Debug.Log($"[Magic] {magicCard.cardName}: No friendly avatar to boost!");
                }
                break;

            case MagicEffect.PowerReduce:
                // Reduce first enemy avatar's power
                Card enemyTarget = FindFirstAvatar(op);
                if (enemyTarget != null)
                {
                    enemyTarget.power = Mathf.Max(0, enemyTarget.power - value);
                    enemyTarget.RefreshPowerDisplay();
                    Debug.Log($"[Magic] {magicCard.cardName}: {enemyTarget.cardName} power -{value} → {enemyTarget.power}");
                }
                else
                {
                    Debug.Log($"[Magic] {magicCard.cardName}: No enemy avatar to weaken!");
                }
                break;

            case MagicEffect.DestroyAvatar:
                // Destroy first enemy avatar
                Card destroyTarget = FindFirstAvatar(op);
                if (destroyTarget != null)
                {
                    Debug.Log($"[Magic] {magicCard.cardName}: Destroying {destroyTarget.cardName}!");
                    CombatController.instance.SendToHell(destroyTarget);
                }
                else
                {
                    Debug.Log($"[Magic] {magicCard.cardName}: No enemy avatar to destroy!");
                }
                break;

            case MagicEffect.HealLife:
                // Flip one of your flipped LIFE cards back face-down (heal)
                Card lifeToHeal = FindFirstFlippedLife(cp);
                if (lifeToHeal != null)
                {
                    lifeToHeal.SetFaceDown(true);
                    Debug.Log($"[Magic] {magicCard.cardName}: Healed LIFE card {lifeToHeal.cardName}!");
                }
                else
                {
                    Debug.Log($"[Magic] {magicCard.cardName}: No flipped LIFE cards to heal!");
                }
                break;

            case MagicEffect.DirectLifeHit:
                // Flip one enemy LIFE card face-up (bypass avatar guards)
                Card lifeToHit = FindFirstUnflippedLife(op);
                if (lifeToHit != null)
                {
                    lifeToHit.FlipLifeCard();
                    Debug.Log($"[Magic] {magicCard.cardName}: Direct LIFE hit! Flipped {lifeToHit.cardName}!");
                    gm.CheckWinCondition();
                }
                else
                {
                    Debug.Log($"[Magic] {magicCard.cardName}: No unflipped LIFE cards to hit!");
                }
                break;

            // ── SYMBOL-BASED EFFECTS (Toylaxy TCG) ───────────────────

            case MagicEffect.PowerBoostAllSymbol:
                // +N power to ALL friendly avatars matching targetSymbol
                ResolvePowerBoostAllSymbol(magicCard.cardName, cp, targetSym, value);
                break;

            case MagicEffect.PowerBoostSymbol:
                // +N power to ONE friendly avatar matching targetSymbol
                ResolvePowerBoostSymbol(magicCard.cardName, cp, targetSym, value);
                break;

            case MagicEffect.DiscardSymbolDraw:
                // Discard 1 card matching targetSymbol from hand → draw N
                ResolveDiscardSymbolDraw(magicCard.cardName, cp, targetSym, value);
                break;

            case MagicEffect.ThonSoop:
                // ถล่มสูป: send top N cards from deck to Hell, then draw N
                ResolveThonSoop(magicCard.cardName, cp, value);
                break;

            case MagicEffect.SearchDeck:
                // Search deck for avatar with cost ≤ effectValue, add to hand
                ResolveSearchDeck(magicCard.cardName, cp, value);
                break;

            case MagicEffect.PowerBoostSymbolTemp:
                // +N power to ONE friendly avatar matching targetSymbol (until end of turn)
                ResolvePowerBoostSymbolTemp(magicCard.cardName, cp, targetSym, value);
                break;

            case MagicEffect.DiscardSymbolThonSoop:
                // Discard 1 [Symbol] from hand → ธรณีสูบ (mill N + draw N)
                ResolveDiscardSymbolThonSoop(magicCard.cardName, cp, targetSym, value);
                break;

            case MagicEffect.SearchDeckByName:
                // Search deck for avatar by name prefix + cost ≤ effectValue
                ResolveSearchDeckByName(magicCard.cardName, cp, magicCard.magicSO.searchName, value);
                break;
        }

        UIController.instance?.UpdateGameInfo();
    }

    // ════════════════════════════════════════════════════════════════
    //  SYMBOL-BASED EFFECT RESOLVERS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// +N power to ALL friendly avatars on field matching the target symbol.
    /// e.g. เขาไกรลาส: +1 to all [Giant] avatars
    /// </summary>
    private static void ResolvePowerBoostAllSymbol(string cardName, Player player, CardSymbol symbol, int value)
    {
        int boosted = 0;
        foreach (var zone in player.avatarZones)
        {
            if (zone != null && zone.activeCard != null && zone.activeCard.cardSymbol == symbol)
            {
                zone.activeCard.power += value;
                zone.activeCard.RefreshPowerDisplay();
                boosted++;
                Debug.Log($"[Magic] {cardName}: {zone.activeCard.cardName} [{symbol}] power +{value} → {zone.activeCard.power}");
            }
        }

        if (boosted == 0)
            Debug.Log($"[Magic] {cardName}: No [{symbol}] avatars on field to boost!");
        else
            Debug.Log($"[Magic] {cardName}: Boosted {boosted} [{symbol}] avatar(s) by +{value}!");
    }

    /// <summary>
    /// +N power to ONE friendly avatar on field matching the target symbol.
    /// e.g. ฮ่า ฮ่า ผมได้เลื่อนขั้น: Select 1 [Giant] → +2 power
    /// </summary>
    private static void ResolvePowerBoostSymbol(string cardName, Player player, CardSymbol symbol, int value)
    {
        Card target = FindFirstAvatarBySymbol(player, symbol);
        if (target != null)
        {
            target.power += value;
            target.RefreshPowerDisplay();
            Debug.Log($"[Magic] {cardName}: {target.cardName} [{symbol}] power +{value} → {target.power}");
        }
        else
        {
            Debug.Log($"[Magic] {cardName}: No [{symbol}] avatar on field to boost!");
        }
    }

    /// <summary>
    /// +N power to ONE friendly avatar matching targetSymbol, TEMPORARY (until end of turn).
    /// e.g. Select 1 [God] Avatar; it gains POWER +2 until end of turn.
    /// </summary>
    private static void ResolvePowerBoostSymbolTemp(string cardName, Player player, CardSymbol symbol, int value)
    {
        Card target = FindFirstAvatarBySymbol(player, symbol);
        if (target != null)
        {
            target.power += value;
            target.RefreshPowerDisplay();

            // Register temp buff for end-of-turn cleanup
            if (MagicController.instance != null)
                MagicController.instance.RegisterTempBuff(target, value);

            Debug.Log($"[Magic] {cardName}: {target.cardName} [{symbol}] power +{value} until end of turn → {target.power}");
        }
        else
        {
            Debug.Log($"[Magic] {cardName}: No [{symbol}] avatar on field to boost!");
        }
    }

    /// <summary>
    /// Discard 1 card matching targetSymbol from hand → draw N cards.
    /// e.g. ความยุติธรรมสำหรับนนทก: Discard 1 [Giant] from hand → Draw 2
    /// </summary>
    private static void ResolveDiscardSymbolDraw(string cardName, Player player, CardSymbol symbol, int drawCount)
    {
        // Find a card in hand matching the symbol
        Card toDiscard = FindCardInHandBySymbol(player, symbol);
        if (toDiscard != null)
        {
            // Remove from hand and send to Hell
            player.hand.RemoveCardFromHand(toDiscard);
            CombatController.instance.SendToHell(toDiscard);
            Debug.Log($"[Magic] {cardName}: Discarded {toDiscard.cardName} [{symbol}] from hand.");

            // Draw N cards
            GameManager gm = GameManager.instance;
            for (int i = 0; i < drawCount; i++)
            {
                player.deck.DrawCardToHand();
                if (gm.isGameOver) return;
            }
            Debug.Log($"[Magic] {cardName}: Drew {drawCount} card(s) after discarding [{symbol}].");
        }
        else
        {
            Debug.Log($"[Magic] {cardName}: No [{symbol}] card in hand to discard! Effect fizzles.");
        }
    }

    /// <summary>
    /// ถล่มสูป (Thon Soop): Send top N cards from deck to Hell, then draw N cards.
    /// e.g. กระทะทองแดง: Mill 2 + Draw 2
    /// </summary>
    private static void ResolveThonSoop(string cardName, Player player, int value)
    {
        GameManager gm = GameManager.instance;

        // Mill N cards (send top of deck to Hell)
        var milledCards = player.deck.MillCards(value);
        Debug.Log($"[Magic] {cardName}: ธรณีสูบ! Sent {milledCards.Count} card(s) from deck to Hell.");

        // Draw N cards
        for (int i = 0; i < value; i++)
        {
            player.deck.DrawCardToHand();
            if (gm.isGameOver) return;
        }
        Debug.Log($"[Magic] {cardName}: Drew {value} card(s) after ธรณีสูบ.");

        // Check if any milled cards can activate from Hell
        if (MagicController.instance != null)
            MagicController.instance.CheckMilledForHellActivation(milledCards, player);
    }

    /// <summary>
    /// Search deck for an Avatar with cost ≤ effectValue, add to hand, shuffle deck.
    /// e.g. บัญชีหนังหมา: Search for avatar cost ≤4 → add to hand
    /// </summary>
    private static void ResolveSearchDeck(string cardName, Player player, int maxCost)
    {
        Card found = player.deck.SearchAndDrawAvatar(maxCost);
        if (found != null)
        {
            Debug.Log($"[Magic] {cardName}: Searched deck → found {found.cardName} (cost {found.cost})!");
        }
        else
        {
            Debug.Log($"[Magic] {cardName}: No avatar with cost ≤{maxCost} found in deck!");
        }
    }

    /// <summary>
    /// Discard 1 [Symbol] Avatar from hand → ธรณีสูบ (mill N + draw N).
    /// Combines DiscardSymbol + ThonSoop into one effect.
    /// Cards milled by this are tagged as "ธรณีสูบ" for Hell activation triggers.
    /// </summary>
    private static void ResolveDiscardSymbolThonSoop(string cardName, Player player, CardSymbol symbol, int value)
    {
        // Step 1: Find and discard a [Symbol] card from hand
        Card toDiscard = FindCardInHandBySymbol(player, symbol);
        if (toDiscard == null)
        {
            Debug.Log($"[Magic] {cardName}: No [{symbol}] card in hand to discard! Effect fizzles.");
            return;
        }

        player.hand.RemoveCardFromHand(toDiscard);
        CombatController.instance.SendToHell(toDiscard);
        Debug.Log($"[Magic] {cardName}: Discarded {toDiscard.cardName} [{symbol}] from hand.");

        GameManager gm = GameManager.instance;

        // Step 2: ธรณีสูบ — mill N cards from deck to Hell
        var milledCards = player.deck.MillCards(value);
        Debug.Log($"[Magic] {cardName}: ธรณีสูบ! Sent {milledCards.Count} card(s) from deck to Hell.");

        // Step 3: Draw N cards
        for (int i = 0; i < value; i++)
        {
            player.deck.DrawCardToHand();
            if (gm.isGameOver) return;
        }
        Debug.Log($"[Magic] {cardName}: Drew {value} card(s) after ธรณีสูบ.");

        // Step 4: Check if any milled cards can activate from Hell
        if (MagicController.instance != null)
            MagicController.instance.CheckMilledForHellActivation(milledCards, player);
    }

    /// <summary>
    /// Search deck for an Avatar whose name starts with namePrefix and cost ≤ maxCost.
    /// The found card is added to hand (marked cannotBeTribute) and deck is shuffled.
    /// </summary>
    private static void ResolveSearchDeckByName(string cardName, Player player, string namePrefix, int maxCost)
    {
        if (string.IsNullOrEmpty(namePrefix))
        {
            Debug.LogWarning($"[Magic] {cardName}: searchName is empty! Cannot search by name.");
            return;
        }

        Card found = player.deck.SearchAndDrawAvatarByName(namePrefix, maxCost);
        if (found != null)
        {
            Debug.Log($"[Magic] {cardName}: Searched deck → found '{found.cardName}' (cost {found.cost})! Cannot be used as tribute.");
        }
        else
        {
            Debug.Log($"[Magic] {cardName}: No avatar matching '{namePrefix}' with cost ≤{maxCost} in deck!");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  HELPERS — extended for cost filtering
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Find first card in hand matching symbol AND with cost ≥ minCost.
    /// Used by React effects that require discarding a card with sufficient cost.
    /// </summary>
    public static Card FindCardInHandBySymbolAndMinCost(Player player, CardSymbol symbol, int minCost)
    {
        foreach (Card card in player.hand.heldCards)
        {
            if (card != null && card.cardSymbol == symbol && card.cost >= minCost)
                return card;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════════
    //  UNDO EFFECT (for Modification magic destruction)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reverse a continuous buff/debuff from a Modification magic card.
    /// Called when the modification or its equipped avatar is destroyed.
    /// Only stat-modification effects need undoing — one-time effects (DrawCards, etc.) don't.
    /// </summary>
    public static void UndoEffect(Card modCard, Card avatar)
    {
        if (modCard.magicSO == null || avatar == null) return;

        switch (modCard.magicSO.effect)
        {
            case MagicEffect.PowerBoost:
            case MagicEffect.PowerBoostSymbol:
            case MagicEffect.PowerBoostAllSymbol:
                avatar.power -= modCard.appliedEffectValue;
                break;

            case MagicEffect.PowerReduce:
                avatar.power += modCard.appliedEffectValue;
                break;

            // One-time effects (DrawCards, SearchDeck, ThonSoop, etc.) — no undo needed
            default:
                break;
        }

        avatar.power = Mathf.Max(0, avatar.power); // Prevent negative power
        avatar.RefreshPowerDisplay();
    }

    // ════════════════════════════════════════════════════════════════
    //  LIFE CARD ON-FLIP EFFECTS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Resolve a LIFE card's on-flip effect.
    /// Effects resolve from the LIFE card OWNER's perspective (the defending player).
    /// </summary>
    public static void ResolveLifeFlipEffect(Card lifeCard)
    {
        if (lifeCard.lifeCardSO == null) return;

        LifeCardSO so = lifeCard.lifeCardSO;
        if (so.onFlipEffect == MagicEffect.None) return;

        GameManager gm = GameManager.instance;
        Player lifeOwner = gm.GetPlayer(lifeCard.cardOwner);
        Player attacker = (lifeCard.cardOwner == TurnPlayer.Player1) ? gm.player2 : gm.player1;

        switch (so.onFlipEffect)
        {
            case MagicEffect.DrawCards:
                // LIFE owner draws cards (compensation for getting hit)
                for (int i = 0; i < so.onFlipValue; i++)
                {
                    lifeOwner.deck.DrawCardToHand();
                    if (gm.isGameOver) return;
                }
                Debug.Log($"[LIFE Effect] {lifeCard.cardName}: {lifeCard.cardOwner} drew {so.onFlipValue} card(s).");
                break;

            case MagicEffect.PowerReduce:
                // Weaken ALL of the attacker's avatars
                foreach (var zone in attacker.avatarZones)
                {
                    if (zone != null && zone.activeCard != null)
                    {
                        zone.activeCard.power = Mathf.Max(0, zone.activeCard.power - so.onFlipValue);
                        zone.activeCard.RefreshPowerDisplay();
                    }
                }
                Debug.Log($"[LIFE Effect] {lifeCard.cardName}: All {attacker.playerId} avatars -{so.onFlipValue} power!");
                break;

            case MagicEffect.PowerBoost:
                // Boost ALL of the LIFE owner's avatars
                foreach (var zone in lifeOwner.avatarZones)
                {
                    if (zone != null && zone.activeCard != null)
                    {
                        zone.activeCard.power += so.onFlipValue;
                        zone.activeCard.RefreshPowerDisplay();
                    }
                }
                Debug.Log($"[LIFE Effect] {lifeCard.cardName}: All {lifeOwner.playerId} avatars +{so.onFlipValue} power!");
                break;

            case MagicEffect.HealLife:
                // Heal one of the LIFE owner's other flipped LIFE cards
                Card toHeal = FindFirstFlippedLife(lifeOwner);
                if (toHeal != null && toHeal != lifeCard)
                {
                    toHeal.SetFaceDown(true);
                    Debug.Log($"[LIFE Effect] {lifeCard.cardName}: Healed {toHeal.cardName}!");
                }
                break;

            default:
                break;
        }

        UIController.instance?.UpdateGameInfo();
    }

    // ════════════════════════════════════════════════════════════════
    //  TARGET FINDERS
    // ════════════════════════════════════════════════════════════════

    private static Card FindFirstAvatar(Player player)
    {
        foreach (var zone in player.avatarZones)
        {
            if (zone != null && zone.activeCard != null)
                return zone.activeCard;
        }
        return null;
    }

    /// <summary>Find first avatar on field matching a specific symbol.</summary>
    private static Card FindFirstAvatarBySymbol(Player player, CardSymbol symbol)
    {
        foreach (var zone in player.avatarZones)
        {
            if (zone != null && zone.activeCard != null && zone.activeCard.cardSymbol == symbol)
                return zone.activeCard;
        }
        return null;
    }

    /// <summary>Find first card in hand matching a specific symbol.</summary>
    private static Card FindCardInHandBySymbol(Player player, CardSymbol symbol)
    {
        foreach (Card card in player.hand.heldCards)
        {
            if (card != null && card.cardSymbol == symbol)
                return card;
        }
        return null;
    }

    private static Card FindFirstFlippedLife(Player player)
    {
        foreach (var zone in player.lifeZones)
        {
            if (zone != null && zone.activeCard != null
                && zone.activeCard.isLifeCard && !zone.activeCard.isFaceDown)
                return zone.activeCard;
        }
        return null;
    }

    private static Card FindFirstUnflippedLife(Player player)
    {
        foreach (var zone in player.lifeZones)
        {
            if (zone != null && zone.activeCard != null
                && zone.activeCard.isLifeCard && zone.activeCard.isFaceDown)
                return zone.activeCard;
        }
        return null;
    }
}
