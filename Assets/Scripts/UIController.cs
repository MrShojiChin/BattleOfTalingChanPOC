using UnityEngine;
using TMPro;

public class UIController : MonoBehaviour
{
    public static UIController instance;

    [Header("Payment UI")]
    public GameObject paymentPanel;           // Parent panel — shown during CostStep & ReadyToPlace
    public TMP_Text playerGemPaidText;        // "Gems Paid: X / Cost: Y"
    public TMP_Text summonStatusText;         // "Drag cards to Hell Point" or "Ready to Place!"

    public void Awake()
    {
        instance = this;
    }

    void Start()
    {
        HidePaymentUI();
    }

    // ── SHOW DURING COST STEP ──────────────────────────────────────
    public void ShowPaymentUI(string avatarName, int currentGem, int requiredGem)
    {
        if (paymentPanel != null)
            paymentPanel.SetActive(true);

        if (playerGemPaidText != null)
            playerGemPaidText.text = $"Gems Paid: {currentGem} / {requiredGem}";

        if (summonStatusText != null)
            summonStatusText.text = $"Summoning <b>{avatarName}</b>\nDrag cards to the Hell Point to pay.";
    }

    // ── SHOW WHEN PAYMENT IS COMPLETE ──────────────────────────────
    public void ShowReadyToPlace(string avatarName)
    {
        if (paymentPanel != null)
            paymentPanel.SetActive(true);

        if (summonStatusText != null)
            summonStatusText.text = $"<color=green><b>{avatarName}</b> is READY!</color>\nDrag it to a board slot.";
    }

    // ── HIDE EVERYTHING ────────────────────────────────────────────
    public void HidePaymentUI()
    {
        if (paymentPanel != null)
            paymentPanel.SetActive(false);

        if (playerGemPaidText != null)
            playerGemPaidText.text = "";

        if (summonStatusText != null)
            summonStatusText.text = "";
    }
}
