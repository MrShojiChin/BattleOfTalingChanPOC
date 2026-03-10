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

    // ── LIFE CARD EFFECT STATE ───────────────────────────────────
    /// <summary>
    /// Queued draws from LIFE card flips.
    /// Each flipped LIFE card adds +1. Resolved at next Main Phase.
    /// </summary>
    [HideInInspector] public int pendingLifeDraws = 0;

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

    // ════════════════════════════════════════════════════════════════
    //  RUNTIME ZONE AUTO-DISCOVERY
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// If any Inspector zone refs are null, find them by name in the scene.
    /// Called once at game start, before LIFE cards are dealt.
    /// Only fills nulls — does NOT overwrite valid Inspector assignments.
    /// </summary>
    public void AutoDiscoverZones()
    {
        string prefix = playerId == TurnPlayer.Player1 ? "P1" : "P2";

        // ── LIFE ZONES (5 slots) ──
        for (int i = 0; i < lifeZones.Length; i++)
        {
            if (lifeZones[i] == null)
            {
                string zoneName = $"{prefix}_Life_{i + 1}";
                lifeZones[i] = FindZoneByName(zoneName);
                if (lifeZones[i] != null)
                    Debug.Log($"[Player] {playerId}: Auto-discovered {zoneName}");
                else
                    Debug.LogWarning($"[Player] {playerId}: Could not find zone '{zoneName}' in scene!");
            }
        }

        // ── AVATAR ZONES (4 slots) ──
        for (int i = 0; i < avatarZones.Length; i++)
        {
            if (avatarZones[i] == null)
            {
                string zoneName = $"{prefix}_Avatar_{i + 1}";
                avatarZones[i] = FindZoneByName(zoneName);
                if (avatarZones[i] != null)
                    Debug.Log($"[Player] {playerId}: Auto-discovered {zoneName}");
                else
                    Debug.LogWarning($"[Player] {playerId}: Could not find zone '{zoneName}' in scene!");
            }
        }

        // ── SINGLE ZONES ──
        if (deckZone == null)
        {
            deckZone = FindZoneByName($"{prefix}_Deck");
            if (deckZone != null) Debug.Log($"[Player] {playerId}: Auto-discovered {prefix}_Deck");
        }
        if (constructZone == null)
        {
            constructZone = FindZoneByName($"{prefix}_Construct");
            if (constructZone != null) Debug.Log($"[Player] {playerId}: Auto-discovered {prefix}_Construct");
        }
        if (magicZone == null)
        {
            magicZone = FindZoneByName($"{prefix}_Magic");
            if (magicZone != null) Debug.Log($"[Player] {playerId}: Auto-discovered {prefix}_Magic");
        }
        if (hellZone == null)
        {
            hellZone = FindZoneByName($"{prefix}_Hell");
            if (hellZone != null) Debug.Log($"[Player] {playerId}: Auto-discovered {prefix}_Hell");
        }
    }

    /// <summary>Find a CardPlacePoint in the scene by exact GameObject name.</summary>
    private CardPlacePoint FindZoneByName(string zoneName)
    {
        GameObject go = GameObject.Find(zoneName);
        if (go != null)
        {
            CardPlacePoint cpp = go.GetComponent<CardPlacePoint>();
            if (cpp != null) return cpp;
        }

        // Fallback: search all CardPlacePoints (handles renamed parents)
        foreach (var zone in FindObjectsOfType<CardPlacePoint>())
        {
            if (zone.gameObject.name == zoneName)
                return zone;
        }
        return null;
    }

    // ════════════════════════════════════════════════════════════════
    //  RUNTIME AVATAR ZONE SPACING
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reposition AVATAR zone GameObjects to use the specified spacing.
    /// Calculates center from current positions and redistributes along X-axis.
    /// </summary>
    public void AdjustAvatarZoneSpacing(float spacing = 2.5f)
    {
        int validCount = 0;
        Vector3 centerPos = Vector3.zero;
        for (int i = 0; i < avatarZones.Length; i++)
        {
            if (avatarZones[i] != null)
            {
                centerPos += avatarZones[i].transform.position;
                validCount++;
            }
        }

        if (validCount == 0)
        {
            Debug.LogWarning($"[Player] {playerId}: No AVATAR zones found — cannot adjust spacing.");
            return;
        }

        centerPos /= validCount;

        // Redistribute: 4 zones centered around the midpoint along X-axis
        float startX = centerPos.x - ((avatarZones.Length - 1) * spacing) / 2f;

        for (int i = 0; i < avatarZones.Length; i++)
        {
            if (avatarZones[i] != null)
            {
                Vector3 pos = avatarZones[i].transform.position;
                pos.x = startX + spacing * i;
                avatarZones[i].transform.position = pos;
            }
        }

        Debug.Log($"[Player] {playerId}: AVATAR zone spacing set to {spacing} (center X={centerPos.x:F2})");
    }

    // ════════════════════════════════════════════════════════════════
    //  RUNTIME LIFE ZONE SPACING
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reposition LIFE zone GameObjects to use the specified spacing.
    /// Calculates center from current positions and redistributes along Z-axis.
    /// </summary>
    public void AdjustLifeZoneSpacing(float spacing = 2.0f)
    {
        int validCount = 0;
        Vector3 centerPos = Vector3.zero;
        for (int i = 0; i < lifeZones.Length; i++)
        {
            if (lifeZones[i] != null)
            {
                centerPos += lifeZones[i].transform.position;
                validCount++;
            }
        }

        if (validCount == 0)
        {
            Debug.LogWarning($"[Player] {playerId}: No LIFE zones found — cannot adjust spacing.");
            return;
        }

        centerPos /= validCount;

        // Redistribute: 5 zones centered around the midpoint
        float startZ = centerPos.z - ((lifeZones.Length - 1) * spacing) / 2f;

        for (int i = 0; i < lifeZones.Length; i++)
        {
            if (lifeZones[i] != null)
            {
                Vector3 pos = lifeZones[i].transform.position;
                pos.z = startZ + spacing * i;
                lifeZones[i].transform.position = pos;
            }
        }

        Debug.Log($"[Player] {playerId}: LIFE zone spacing set to {spacing} (center Z={centerPos.z:F2})");
    }
}
