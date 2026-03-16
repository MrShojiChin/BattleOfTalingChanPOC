using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// In-game gameplay log system.
/// Collects all major game events and displays them in a scrollable UI panel.
/// Singleton — attach to any GameObject in the scene, or auto-creates itself.
/// </summary>
public class GameplayLogger : MonoBehaviour
{
    public static GameplayLogger instance;

    // ── LOG STORAGE ──────────────────────────────────────────────
    private const int MaxEntries = 150;
    private readonly List<string> _entries = new List<string>();

    /// <summary>Fired whenever a new entry is added. UIController subscribes to this.</summary>
    public event Action OnLogUpdated;

    // ── COLOR TAGS (TMP rich text) ───────────────────────────────
    private const string ColorTurn    = "#FFD700"; // Gold
    private const string ColorCombat  = "#FF6B6B"; // Red
    private const string ColorSummon  = "#66FF66"; // Green
    private const string ColorMagic   = "#CC99FF"; // Purple
    private const string ColorDraw    = "#66CCFF"; // Cyan
    private const string ColorLife    = "#FFFF66"; // Yellow
    private const string ColorGameOver = "#FF0000"; // Bright red

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
    }

    /// <summary>
    /// Ensures GameplayLogger exists in the scene.
    /// Call from any script's Awake/Start if needed.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreate()
    {
        // Will be created by Awake if placed in scene,
        // or can be instantiated here if needed.
    }

    // ════════════════════════════════════════════════════════════════
    //  PUBLIC API
    // ════════════════════════════════════════════════════════════════

    /// <summary>Log a turn/phase change event (gold).</summary>
    public void LogTurn(string message)
        => AddEntry(ColorTurn, message);

    /// <summary>Log a combat event (red).</summary>
    public void LogCombat(string message)
        => AddEntry(ColorCombat, message);

    /// <summary>Log a summon event (green).</summary>
    public void LogSummon(string message)
        => AddEntry(ColorSummon, message);

    /// <summary>Log a magic card event (purple).</summary>
    public void LogMagic(string message)
        => AddEntry(ColorMagic, message);

    /// <summary>Log a draw/deck event (cyan).</summary>
    public void LogDraw(string message)
        => AddEntry(ColorDraw, message);

    /// <summary>Log a LIFE card event (yellow).</summary>
    public void LogLife(string message)
        => AddEntry(ColorLife, message);

    /// <summary>Log a game-over event (bright red, bold).</summary>
    public void LogGameOver(string message)
        => AddEntry(ColorGameOver, message, bold: true);

    /// <summary>Returns all log entries joined with newlines.</summary>
    public string GetFullLog()
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _entries.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(_entries[i]);
        }
        return sb.ToString();
    }

    /// <summary>Clear all log entries.</summary>
    public void ClearLog()
    {
        _entries.Clear();
        OnLogUpdated?.Invoke();
    }

    // ════════════════════════════════════════════════════════════════
    //  INTERNAL
    // ════════════════════════════════════════════════════════════════

    private void AddEntry(string hexColor, string message, bool bold = false)
    {
        // Build turn context prefix
        string prefix = "";
        if (GameManager.instance != null)
        {
            int turn = GameManager.instance.turnNumber;
            string player = GameManager.instance.currentPlayer == TurnPlayer.Player1 ? "P1" : "P2";
            prefix = $"<color=#888888>T{turn} {player}</color> ";
        }

        string text = bold
            ? $"{prefix}<b><color={hexColor}>{message}</color></b>"
            : $"{prefix}<color={hexColor}>{message}</color>";

        _entries.Add(text);

        // Cap entries to prevent memory issues
        while (_entries.Count > MaxEntries)
            _entries.RemoveAt(0);

        OnLogUpdated?.Invoke();
    }
}
