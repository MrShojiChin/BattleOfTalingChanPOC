using UnityEngine;
using UnityEditor;

/// <summary>
/// Editor tool: generates a VISUAL prototype of the board layout using Card_Frame.png.
/// Places Quad meshes at every card zone position so the board is visible in Scene/Game view.
///
/// Usage:  Top menu → Tools → Generate Board Layout Prototype
///
/// SAFE TO RE-RUN: Deletes old "BoardLayout" parent first if it exists.
/// This is purely visual — does NOT affect CardPlacePoint collider zones.
/// </summary>
public class BoardLayoutPrototype : Editor
{
    // ── LAYOUT CONSTANTS (same as BoardZoneGenerator) ──────────
    private const float boardY      = 0.005f;   // Slightly below zone colliders (0.01)
    private const float cardWidth   = 1.4f;     // Horizontal spacing between card slots
    private const float cardDepth   = 2.0f;     // Vertical (Z) spacing between rows

    // Row Z positions
    private const float p1AvatarZ   = -3.0f;
    private const float p1MagicZ    = -5.5f;
    private const float p1HandZ     = -8.0f;    // Hand row (visual only)
    private const float centreZ     =  0.0f;
    private const float p2AvatarZ   =  3.0f;
    private const float p2MagicZ    =  5.5f;
    private const float p2HandZ     =  8.0f;    // Hand row (visual only)

    // Side positions
    private const float hellOffsetX     =  4.5f;
    private const float lifeOffsetX     = -4.5f;
    private const float deckOffsetX     = -5.5f;
    private const float constructSpacing =  3.5f;

    // Card frame visual scale (Quad is 1x1 by default, scale to card proportions)
    private static readonly Vector3 cardScale = new Vector3(1.1f, 1.0f, 1.6f);       // Portrait card
    private static readonly Vector3 lifeScale = new Vector3(0.8f, 1.0f, 1.2f);       // Slightly smaller LIFE
    private static readonly Vector3 handScale = new Vector3(0.9f, 1.0f, 1.3f);       // Hand cards (slightly smaller)

    // LIFE card spacing
    private const float lifeSpacing = 2.0f;

    // Colors
    private static readonly Color p1Color   = new Color(0.2f, 0.5f, 1.0f);    // Blue
    private static readonly Color p2Color   = new Color(1.0f, 0.3f, 0.3f);    // Red
    private static readonly Color sharedColor = new Color(1.0f, 0.85f, 0.0f); // Gold

    // Card frame texture path
    private const string cardFramePath = "Assets/_Udemy Card Battler Assets/Art/Card_Frame.png";

    // ════════════════════════════════════════════════════════════════
    //  MENU ITEM
    // ════════════════════════════════════════════════════════════════

    [MenuItem("Tools/Generate Board Layout Prototype")]
    public static void GenerateBoardLayout()
    {
        // Delete old layout if exists
        GameObject old = GameObject.Find("BoardLayout");
        if (old != null)
            Undo.DestroyObjectImmediate(old);

        // Create parent
        GameObject root = new GameObject("BoardLayout");
        Undo.RegisterCreatedObjectUndo(root, "Generate Board Layout Prototype");

        // Load card frame texture
        Texture2D cardFrameTex = AssetDatabase.LoadAssetAtPath<Texture2D>(cardFramePath);
        if (cardFrameTex == null)
        {
            Debug.LogError($"[BoardLayout] Card_Frame.png not found at: {cardFramePath}");
            return;
        }

        // Create material for card frames
        Material frameMat = new Material(Shader.Find("Unlit/Transparent"));
        if (frameMat == null)
            frameMat = new Material(Shader.Find("Sprites/Default"));
        frameMat.mainTexture = cardFrameTex;

        // Create semi-transparent material for hand cards (face-down look)
        Material handMat = new Material(frameMat);
        handMat.color = new Color(0.7f, 0.7f, 0.8f, 0.6f);

        // ── BOARD BACKGROUND ─────────────────────────────────────
        CreateBoardBackground(root);

        // ── PLAYER 1 (bottom) ────────────────────────────────────
        CreateAvatarFrames(root, frameMat, "P1", p1AvatarZ, p1Color);
        CreateMagicFrame(root, frameMat, "P1", p1MagicZ, p1Color);
        CreateHellFrame(root, frameMat, "P1", p1AvatarZ, hellOffsetX, p1Color);
        CreateDeckFrame(root, frameMat, "P1", p1MagicZ, deckOffsetX, p1Color);
        CreateLifeFrames(root, frameMat, "P1", p1AvatarZ, lifeOffsetX, p1Color);
        CreateHandFrames(root, handMat, "P1", p1HandZ, p1Color);

        // ── PLAYER 2 (top, mirrored) ─────────────────────────────
        CreateAvatarFrames(root, frameMat, "P2", p2AvatarZ, p2Color);
        CreateMagicFrame(root, frameMat, "P2", p2MagicZ, p2Color);
        CreateHellFrame(root, frameMat, "P2", p2AvatarZ, -hellOffsetX, p2Color);
        CreateDeckFrame(root, frameMat, "P2", p2MagicZ, -deckOffsetX, p2Color);
        CreateLifeFrames(root, frameMat, "P2", p2AvatarZ, -lifeOffsetX, p2Color);
        CreateHandFrames(root, handMat, "P2", p2HandZ, p2Color);

        // ── CENTRE ROW ───────────────────────────────────────────
        CreateCardFrame(root, frameMat, "P1_Construct",
            new Vector3(-constructSpacing, boardY, centreZ), cardScale, sharedColor);
        CreateCardFrame(root, frameMat, "Land_Magic",
            new Vector3(0f, boardY, centreZ), cardScale, sharedColor);
        CreateCardFrame(root, frameMat, "P2_Construct",
            new Vector3(constructSpacing, boardY, centreZ), cardScale, sharedColor);

        // ── TITLE ────────────────────────────────────────────────
        CreateTitleLabel(root);

        Debug.Log("[BoardLayout] Board layout prototype generated with card frame placeholders.");
        Selection.activeGameObject = root;
    }

    // ════════════════════════════════════════════════════════════════
    //  ZONE GENERATORS
    // ════════════════════════════════════════════════════════════════

    /// <summary>Creates 4 Avatar card frame placeholders in a row.</summary>
    private static void CreateAvatarFrames(GameObject root, Material mat, string prefix, float z, Color labelColor)
    {
        float startX = -((4 - 1) * cardWidth) / 2f;
        for (int i = 0; i < 4; i++)
        {
            string name = $"{prefix}_Avatar_{i + 1}";
            Vector3 pos = new Vector3(startX + cardWidth * i, boardY, z);
            CreateCardFrame(root, mat, name, pos, cardScale, labelColor);
        }
    }

    /// <summary>Creates Magic zone placeholder (single wide frame).</summary>
    private static void CreateMagicFrame(GameObject root, Material mat, string prefix, float z, Color labelColor)
    {
        string name = $"{prefix}_Magic";
        Vector3 pos = new Vector3(0f, boardY, z);
        // Magic zone is wider to indicate it can hold multiple cards
        Vector3 magicScale = new Vector3(4.5f, 1.0f, 1.6f);
        CreateCardFrame(root, mat, name, pos, magicScale, labelColor);
    }

    /// <summary>Creates Hell zone placeholder.</summary>
    private static void CreateHellFrame(GameObject root, Material mat, string prefix, float z, float x, Color labelColor)
    {
        string name = $"{prefix}_Hell";
        Vector3 pos = new Vector3(x, boardY, z);
        CreateCardFrame(root, mat, name, pos, cardScale, labelColor);
    }

    /// <summary>Creates Deck zone placeholder.</summary>
    private static void CreateDeckFrame(GameObject root, Material mat, string prefix, float z, float x, Color labelColor)
    {
        string name = $"{prefix}_Deck";
        Vector3 pos = new Vector3(x, boardY, z);
        CreateCardFrame(root, mat, name, pos, cardScale, labelColor);
    }

    /// <summary>Creates 5 Life zone placeholders in a vertical column.</summary>
    private static void CreateLifeFrames(GameObject root, Material mat, string prefix, float baseZ, float x, Color labelColor)
    {
        float startZ = baseZ - (2 * lifeSpacing);
        for (int i = 0; i < 5; i++)
        {
            string name = $"{prefix}_Life_{i + 1}";
            Vector3 pos = new Vector3(x, boardY, startZ + lifeSpacing * i);
            CreateCardFrame(root, mat, name, pos, lifeScale, labelColor);
        }
    }

    /// <summary>Creates 5 Hand card placeholders in a row (visual only, semi-transparent).</summary>
    private static void CreateHandFrames(GameObject root, Material mat, string prefix, float z, Color labelColor)
    {
        float startX = -((5 - 1) * cardWidth) / 2f;
        for (int i = 0; i < 5; i++)
        {
            string name = $"{prefix}_Hand_{i + 1}";
            Vector3 pos = new Vector3(startX + cardWidth * i, boardY, z);
            CreateCardFrame(root, mat, name, pos, handScale, labelColor);
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  LOW-LEVEL HELPERS
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a single card frame placeholder (Quad + TextMesh label).
    /// The Quad faces upward (rotated 90° on X) so it lies flat on the board.
    /// </summary>
    private static void CreateCardFrame(GameObject root, Material mat, string zoneName,
                                         Vector3 pos, Vector3 scale, Color labelColor)
    {
        // Create Quad (flat card visual)
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = zoneName;
        quad.transform.SetParent(root.transform);
        quad.transform.localPosition = pos;
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Face up
        quad.transform.localScale = scale;

        // Remove collider (we don't need collision on visual placeholders)
        MeshCollider mc = quad.GetComponent<MeshCollider>();
        if (mc != null) DestroyImmediate(mc);

        // Apply material
        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        if (renderer != null && mat != null)
            renderer.sharedMaterial = mat;

        // Add zone label
        CreateLabel(quad, zoneName, labelColor);
    }

    /// <summary>Creates a TextMesh label as a child of the card frame.</summary>
    private static void CreateLabel(GameObject parent, string text, Color color)
    {
        GameObject labelObj = new GameObject("Label");
        labelObj.transform.SetParent(parent.transform);
        // Position label above the card (in parent's local space, Y is forward since parent is rotated)
        labelObj.transform.localPosition = new Vector3(0f, 0f, 0.02f);
        labelObj.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);  // Face camera

        TextMesh tm = labelObj.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 24;
        tm.characterSize = 0.08f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
        tm.fontStyle = FontStyle.Bold;
    }

    /// <summary>Creates the board background plane.</summary>
    private static void CreateBoardBackground(GameObject root)
    {
        GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        bg.name = "BoardBackground";
        bg.transform.SetParent(root.transform);
        bg.transform.localPosition = new Vector3(0f, -0.01f, 0f); // Below everything
        bg.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        bg.transform.localScale = new Vector3(16f, 20f, 1f);      // Wide enough for whole board

        // Remove collider
        MeshCollider mc = bg.GetComponent<MeshCollider>();
        if (mc != null) DestroyImmediate(mc);

        // Dark green board color
        MeshRenderer renderer = bg.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            Material bgMat = new Material(Shader.Find("Unlit/Color"));
            bgMat.color = new Color(0.08f, 0.20f, 0.10f); // Dark green
            renderer.sharedMaterial = bgMat;
        }
    }

    /// <summary>Creates the "Battle of Talingchan" title label above the centre.</summary>
    private static void CreateTitleLabel(GameObject root)
    {
        GameObject titleObj = new GameObject("Title_BattleOfTalingchan");
        titleObj.transform.SetParent(root.transform);
        titleObj.transform.localPosition = new Vector3(0f, 0.02f, centreZ + 1.2f);
        titleObj.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // Face up

        TextMesh tm = titleObj.AddComponent<TextMesh>();
        tm.text = "Battle of Talingchan";
        tm.fontSize = 36;
        tm.characterSize = 0.12f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = sharedColor;
        tm.fontStyle = FontStyle.Bold;
    }
}
