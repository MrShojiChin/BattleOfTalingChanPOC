using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every zone type on the board, matching the rulebook exactly.
/// </summary>
public enum ZoneType
{
    Avatar,     // Up to 4 Avatar cards per player (1 card per slot)
    Magic,      // No limit — cards stack in one area
    Hell,       // Discard pile — cards stack in one area
    Deck,       // Draw pile (1 zone)
    Life,       // 5 face-down LIFE cards (1 card per slot)
    Construct,  // 1 per player, in the centre row
    LandMagic   // 1 shared zone in the centre of the board
}

/// <summary>
/// Marks a zone slot on the 3D board.
///
/// Two modes:
///   Single-card zones (Avatar, Life, Construct, LandMagic, Deck)
///     → uses activeCard (1 card max)
///   Multi-card zones  (Magic, Hell)
///     → uses activeCards list (unlimited stacking)
/// </summary>
public class CardPlacePoint : MonoBehaviour
{
    [Header("Zone Identity")]
    public ZoneType   zoneType;
    public TurnPlayer owner;          // Player1 or Player2 (ignored for LandMagic)

    [Header("Runtime — Single Card Zones")]
    public Card activeCard;           // For Avatar, Life, Construct, LandMagic, Deck

    [Header("Runtime — Multi Card Zones (Magic / Hell)")]
    public List<Card> activeCards = new List<Card>();

    [Header("Stacking")]
    public float stackOffsetY = 0.05f;    // Vertical offset between stacked cards
    public float stackOffsetX = 0.3f;     // Horizontal fan-out for Magic Zone (0 for Hell)

    // ── Zone type queries ────────────────────────────────────────
    public bool isPlayerAvatarPoint => zoneType == ZoneType.Avatar;
    public bool isPlayerMagicPoint  => zoneType == ZoneType.Magic;
    public bool isPlayerHellPoint   => zoneType == ZoneType.Hell;
    public bool isLifePoint         => zoneType == ZoneType.Life;
    public bool isDeckPoint         => zoneType == ZoneType.Deck;
    public bool isConstructPoint    => zoneType == ZoneType.Construct;
    public bool isLandMagicPoint    => zoneType == ZoneType.LandMagic;

    /// <summary>True if this zone stacks multiple cards (Magic, Hell).</summary>
    public bool isMultiCardZone => zoneType == ZoneType.Magic || zoneType == ZoneType.Hell;

    /// <summary>True if this single-card slot is empty and can accept a new card.</summary>
    public bool IsEmpty => isMultiCardZone || activeCard == null;

    /// <summary>True if this slot belongs to the player whose turn it currently is.</summary>
    public bool IsCurrentPlayerZone()
    {
        if (GameManager.instance == null) return true;
        if (zoneType == ZoneType.LandMagic) return true;
        return owner == GameManager.instance.currentPlayer;
    }

    // ════════════════════════════════════════════════════════════════
    //  MULTI-CARD ZONE HELPERS (Magic / Hell)
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Add a card to a multi-card zone. Positions it with a stacking offset.
    /// </summary>
    public void AddCard(Card card)
    {
        activeCards.Add(card);
        card.assignedPlace = this;
        RepositionStack();
    }

    /// <summary>
    /// Remove a card from a multi-card zone. Restacks remaining cards.
    /// </summary>
    public void RemoveCard(Card card)
    {
        activeCards.Remove(card);
        card.assignedPlace = null;
        RepositionStack();
    }

    // ════════════════════════════════════════════════════════════════
    //  DOUBLE-CLICK — Hell Zone opens viewer (New Input System)
    // ════════════════════════════════════════════════════════════════

    private float lastClickTime;
    private const float DoubleClickThreshold = 0.5f;

    private void Update()
    {
        // Only hell zones need click detection
        if (zoneType != ZoneType.Hell) return;
        if (GameManager.instance != null && GameManager.instance.isGameOver) return;

        // Cards stacked on top handle their own double-click via Card.OnPointerDown.
        // This Update() handles clicks on the empty hell zone collider itself.
        if (UnityEngine.InputSystem.Mouse.current == null) return;
        if (!UnityEngine.InputSystem.Mouse.current.leftButton.wasPressedThisFrame) return;

        // RaycastAll — the desktop/board may be in front, so check ALL hits
        Ray ray = Camera.main.ScreenPointToRay(UnityEngine.InputSystem.Mouse.current.position.ReadValue());
        RaycastHit[] hits = Physics.RaycastAll(ray, 100f);
        bool hitThisZone = false;
        foreach (var h in hits)
        {
            if (h.collider.gameObject == gameObject) { hitThisZone = true; break; }
        }

        if (hitThisZone)
        {
            float timeSinceLastClick = Time.unscaledTime - lastClickTime;
            lastClickTime = Time.unscaledTime;

            if (timeSinceLastClick <= DoubleClickThreshold)
            {
                Player zoneOwner = GameManager.instance.GetPlayer(owner);
                UIController.instance.ShowHellZoneViewer(zoneOwner);
            }
        }
    }

    /// <summary>
    /// Reposition all cards in the stack with proper offsets.
    /// Magic Zone: fans out horizontally so you can see each card.
    /// Hell Zone:  stacks vertically in a pile.
    /// </summary>
    private void RepositionStack()
    {
        for (int i = 0; i < activeCards.Count; i++)
        {
            Vector3 offset = new Vector3(stackOffsetX * i, stackOffsetY * i, 0f);
            activeCards[i].MoveToPoint(transform.position + offset, Quaternion.identity);
        }
    }
}
