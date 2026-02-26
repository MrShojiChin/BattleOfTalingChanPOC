using UnityEngine;

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

        if (effect == MagicEffect.None) return;

        GameManager gm = GameManager.instance;
        Player cp = gm.CurrentPlayerObj;
        Player op = gm.OpponentPlayerObj;

        switch (effect)
        {
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

                    // Check win condition
                    gm.CheckWinCondition();
                }
                else
                {
                    Debug.Log($"[Magic] {magicCard.cardName}: No unflipped LIFE cards to hit!");
                }
                break;
        }

        UIController.instance?.UpdateGameInfo();
    }

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
    //  TARGET FINDERS (default targeting — first valid card found)
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
