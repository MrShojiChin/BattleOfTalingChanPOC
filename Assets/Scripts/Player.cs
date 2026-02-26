using UnityEngine;

/// <summary>
/// Represents one player in the game.
/// Each player owns their hand, deck, and all board zones.
///
/// In the scene, you create TWO Player GameObjects:
///   Player1 (with its own HandController + DeckController as children)
///   Player2 (with its own HandController + DeckController as children)
/// </summary>
public class Player : MonoBehaviour
{
    // ── IDENTITY ───────────────────────────────────────────────────
    [Header("Identity")]
    public TurnPlayer playerId;

    // ── CONTROLLERS ────────────────────────────────────────────────
    [Header("Controllers")]
    public HandController hand;
    public DeckController deck;

    // ── BOARD ZONES — Single Card ──────────────────────────────────
    [Header("Board Zones — Avatar (4 slots)")]
    public CardPlacePoint[] avatarZones = new CardPlacePoint[4];

    [Header("Board Zones — Life (5 slots)")]
    public CardPlacePoint[] lifeZones = new CardPlacePoint[5];

    [Header("Board Zones — Other")]
    public CardPlacePoint deckZone;
    public CardPlacePoint constructZone;

    // ── BOARD ZONES — Multi Card ───────────────────────────────────
    [Header("Board Zones — Multi Card")]
    public CardPlacePoint magicZone;
    public CardPlacePoint hellZone;

    // ════════════════════════════════════════════════════════════════
    //  SETUP
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Awake runs BEFORE any Start() — guarantees deck.owner is set
    /// before GameManager.Start() tries to draw cards.
    /// </summary>
    private void Awake()
    {
        // Tell DeckController who owns it (MUST be in Awake, not Start)
        if (deck != null)
            deck.owner = this;
    }

    // ════════════════════════════════════════════════════════════════
    //  HELPER QUERIES
    // ════════════════════════════════════════════════════════════════

    /// <summary>Count how many avatar slots are currently occupied.</summary>
    public int AvatarsOnField
    {
        get
        {
            int count = 0;
            foreach (var zone in avatarZones)
            {
                if (zone != null && zone.activeCard != null)
                    count++;
            }
            return count;
        }
    }

    /// <summary>Count how many LIFE cards have been flipped face-up (damaged).</summary>
    public int LifeCardsFlipped
    {
        get
        {
            int count = 0;
            foreach (var zone in lifeZones)
            {
                if (zone != null && zone.activeCard != null
                    && zone.activeCard.isLifeCard && !zone.activeCard.isFaceDown)
                    count++;
            }
            return count;
        }
    }

    /// <summary>Count how many LIFE cards are still face-down (intact/undamaged).</summary>
    public int UnflippedLifeCards
    {
        get
        {
            int count = 0;
            foreach (var zone in lifeZones)
            {
                if (zone != null && zone.activeCard != null
                    && zone.activeCard.isLifeCard && zone.activeCard.isFaceDown)
                    count++;
            }
            return count;
        }
    }

    /// <summary>True if all 5 LIFE cards are face-up — critical state (สหัส).</summary>
    public bool IsSahat => LifeCardsFlipped >= 5;

    /// <summary>Returns the first empty avatar slot, or null if all 4 are full.</summary>
    public CardPlacePoint GetEmptyAvatarSlot()
    {
        foreach (var zone in avatarZones)
        {
            if (zone != null && zone.activeCard == null)
                return zone;
        }
        return null;
    }
}
