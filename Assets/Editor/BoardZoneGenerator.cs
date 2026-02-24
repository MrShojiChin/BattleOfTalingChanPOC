using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor tool: generates all 27 CardPlacePoint zones on the board.
///
/// Usage:  Top menu → Tools → Generate Board Zones
///
/// It creates a parent "BoardZones" object with all zones as children.
/// You can move/scale the parent to fit your board, then adjust individual
/// positions as needed.
///
/// SAFE TO RE-RUN: It deletes the old "BoardZones" first if it exists.
/// </summary>
public class BoardZoneGenerator : Editor
{
    // ── LAYOUT CONSTANTS (tweak these to fit your board) ──────────
    //
    // The board is centered at (0, boardY, 0).
    // P1 is on the -Z side (bottom), P2 is on the +Z side (top).

    private const float boardY      = 0.01f;   // Slightly above the table surface
    private const float cardWidth   = 1.4f;    // Horizontal spacing between card slots
    private const float cardDepth   = 2.0f;    // Vertical (Z) spacing between rows

    // Row Z positions (adjust to match your board size)
    private const float p1AvatarZ   = -3.0f;   // P1 Avatar row
    private const float p1MagicZ    = -5.5f;   // P1 Magic row (closer to P1 hand)
    private const float centreZ     =  0.0f;   // Centre row (Construct + Land)
    private const float p2AvatarZ   =  3.0f;   // P2 Avatar row
    private const float p2MagicZ    =  5.5f;   // P2 Magic row (closer to P2 hand)

    // Side positions
    private const float hellOffsetX     =  4.5f;   // Hell zone X offset from centre
    private const float lifeOffsetX     = -4.5f;   // Life zone X offset from centre
    private const float deckOffsetX     = -5.5f;   // Deck X offset
    private const float constructSpacing =  3.5f;  // Distance from centre for Construct zones

    // Collider size
    private static readonly Vector3 singleSlotSize = new Vector3(1.2f, 0.1f, 1.8f);
    private static readonly Vector3 multiZoneSize  = new Vector3(4.0f, 0.1f, 1.8f);  // wider for fan-out

    // ════════════════════════════════════════════════════════════════
    //  MENU ITEM
    // ════════════════════════════════════════════════════════════════

    [MenuItem("Tools/Generate Board Zones")]
    public static void GenerateBoardZones()
    {
        // Delete old BoardZones if it exists
        GameObject old = GameObject.Find("BoardZones");
        if (old != null)
        {
            Undo.DestroyObjectImmediate(old);
        }

        // Create parent
        GameObject root = new GameObject("BoardZones");
        Undo.RegisterCreatedObjectUndo(root, "Generate Board Zones");

        // ── PLAYER 1 (bottom) ────────────────────────────────────
        CreateAvatarSlots(root, TurnPlayer.Player1, p1AvatarZ);
        CreateMagicZone  (root, TurnPlayer.Player1, p1MagicZ);
        CreateHellZone   (root, TurnPlayer.Player1, p1AvatarZ, hellOffsetX);
        CreateDeckZone   (root, TurnPlayer.Player1, p1MagicZ,  deckOffsetX);
        CreateLifeSlots  (root, TurnPlayer.Player1, p1AvatarZ, lifeOffsetX);

        // ── PLAYER 2 (top) ──────────────────────────────────────
        CreateAvatarSlots(root, TurnPlayer.Player2, p2AvatarZ);
        CreateMagicZone  (root, TurnPlayer.Player2, p2MagicZ);
        CreateHellZone   (root, TurnPlayer.Player2, p2AvatarZ, -hellOffsetX);   // Mirrored
        CreateDeckZone   (root, TurnPlayer.Player2, p2MagicZ,  -deckOffsetX);   // Mirrored
        CreateLifeSlots  (root, TurnPlayer.Player2, p2AvatarZ, -lifeOffsetX);   // Mirrored

        // ── CENTRE ROW ───────────────────────────────────────────
        CreateSingleZone(root, "P1_Construct", ZoneType.Construct, TurnPlayer.Player1,
                         new Vector3(-constructSpacing, boardY, centreZ), singleSlotSize);

        CreateSingleZone(root, "Land_Magic", ZoneType.LandMagic, TurnPlayer.Player1,
                         new Vector3(0f, boardY, centreZ), singleSlotSize);

        CreateSingleZone(root, "P2_Construct", ZoneType.Construct, TurnPlayer.Player2,
                         new Vector3(constructSpacing, boardY, centreZ), singleSlotSize);

        Debug.Log("[BoardZoneGenerator] ✅ Created 27 zones under 'BoardZones'. Adjust positions to fit your board.");
        Selection.activeGameObject = root;
    }

    // ════════════════════════════════════════════════════════════════
    //  ZONE CREATORS
    // ════════════════════════════════════════════════════════════════

    /// <summary>Creates 4 Avatar slots in a horizontal row.</summary>
    private static void CreateAvatarSlots(GameObject root, TurnPlayer owner, float z)
    {
        string prefix = owner == TurnPlayer.Player1 ? "P1" : "P2";
        float startX = -((4 - 1) * cardWidth) / 2f;   // Centre the 4 slots

        for (int i = 0; i < 4; i++)
        {
            string name = $"{prefix}_Avatar_{i + 1}";
            Vector3 pos = new Vector3(startX + cardWidth * i, boardY, z);
            CreateSingleZone(root, name, ZoneType.Avatar, owner, pos, singleSlotSize);
        }
    }

    /// <summary>Creates 5 Life slots in a vertical column.</summary>
    private static void CreateLifeSlots(GameObject root, TurnPlayer owner, float baseZ, float x)
    {
        string prefix = owner == TurnPlayer.Player1 ? "P1" : "P2";
        float lifeSpacing = 1.2f;
        float startZ = baseZ - (2 * lifeSpacing);   // Centre 5 slots around the row

        for (int i = 0; i < 5; i++)
        {
            string name = $"{prefix}_Life_{i + 1}";
            Vector3 pos = new Vector3(x, boardY, startZ + lifeSpacing * i);
            Vector3 lifeSize = new Vector3(0.9f, 0.1f, 1.3f);   // Slightly smaller
            CreateSingleZone(root, name, ZoneType.Life, owner, pos, lifeSize);
        }
    }

    /// <summary>Creates 1 Magic zone (multi-card, fan-out).</summary>
    private static void CreateMagicZone(GameObject root, TurnPlayer owner, float z)
    {
        string prefix = owner == TurnPlayer.Player1 ? "P1" : "P2";
        string name = $"{prefix}_Magic";
        Vector3 pos = new Vector3(0f, boardY, z);

        GameObject go = CreateZoneObject(root, name, pos, multiZoneSize);
        CardPlacePoint cpp = go.GetComponent<CardPlacePoint>();
        cpp.zoneType = ZoneType.Magic;
        cpp.owner = owner;
        cpp.stackOffsetX = 0.3f;    // Fan-out
        cpp.stackOffsetY = 0.05f;
    }

    /// <summary>Creates 1 Hell zone (multi-card, pile).</summary>
    private static void CreateHellZone(GameObject root, TurnPlayer owner, float z, float x)
    {
        string prefix = owner == TurnPlayer.Player1 ? "P1" : "P2";
        string name = $"{prefix}_Hell";
        Vector3 pos = new Vector3(x, boardY, z);

        GameObject go = CreateZoneObject(root, name, pos, singleSlotSize);
        CardPlacePoint cpp = go.GetComponent<CardPlacePoint>();
        cpp.zoneType = ZoneType.Hell;
        cpp.owner = owner;
        cpp.stackOffsetX = 0f;      // Pile up, no fan-out
        cpp.stackOffsetY = 0.05f;
    }

    /// <summary>Creates 1 Deck zone.</summary>
    private static void CreateDeckZone(GameObject root, TurnPlayer owner, float z, float x)
    {
        string prefix = owner == TurnPlayer.Player1 ? "P1" : "P2";
        CreateSingleZone(root, $"{prefix}_Deck", ZoneType.Deck, owner,
                         new Vector3(x, boardY, z), singleSlotSize);
    }

    // ════════════════════════════════════════════════════════════════
    //  LOW-LEVEL HELPERS
    // ════════════════════════════════════════════════════════════════

    /// <summary>Creates a single-card zone with CardPlacePoint + BoxCollider.</summary>
    private static void CreateSingleZone(GameObject root, string name, ZoneType type,
                                          TurnPlayer owner, Vector3 pos, Vector3 colliderSize)
    {
        GameObject go = CreateZoneObject(root, name, pos, colliderSize);
        CardPlacePoint cpp = go.GetComponent<CardPlacePoint>();
        cpp.zoneType = type;
        cpp.owner = owner;
    }

    /// <summary>Creates a bare GameObject with BoxCollider + CardPlacePoint, parented to root.</summary>
    private static GameObject CreateZoneObject(GameObject root, string name, Vector3 pos, Vector3 colliderSize)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root.transform);
        go.transform.localPosition = pos;

        // Add collider for Physics.Raycast (card drag detection)
        BoxCollider col = go.AddComponent<BoxCollider>();
        col.size = colliderSize;

        // Add CardPlacePoint (fields set by caller)
        go.AddComponent<CardPlacePoint>();

        // Set the layer to "Placement" if it exists (for whatIsPlacement raycast)
        int placementLayer = LayerMask.NameToLayer("Placement");
        if (placementLayer >= 0)
            go.layer = placementLayer;

        return go;
    }
}
