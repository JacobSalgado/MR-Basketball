using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Spawns a world-space score/timer display above the placed hoop, Pop-A-Shot style, and keeps it
/// updated via ScoreManager's events (no per-frame polling). The timer turns red at 00:00 and the
/// score flashes green on a swish.
/// </summary>
public class ScoreUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InstantPlacementController instantPlacementController;
    [SerializeField] private GameObject scoreUIPrefab; // world-space Canvas prefab — see setup notes

    [Header("Placement")]
    [SerializeField] private Vector3 localOffsetFromHoop = new Vector3(0f, 0.6f, 0f);

    // A World Space Canvas's readable front is its local -Z side. The hoop's local +Z points
    // outward toward the player, so rotating 180° on Y makes the canvas face the player.
    [SerializeField] private Vector3 localRotationOffset = new Vector3(0f, 180f, 0f);

    [Header("Text Layout")]
    // x = toward the viewer's RIGHT (negative = left), y = up, z = out from the wall toward the player.
    [SerializeField] private Vector3 scoreOffset = new Vector3(0.8f, 0.3f, 0.03f);
    [SerializeField] private Vector3 swishOffset = new Vector3(0f, -0.6f, 0.03f); // centered under the hoop
    [SerializeField] private Vector3 timerOffset = new Vector3(0f, 0.75f, 0.03f);
    // Keeps the Swish text visible (with sample text) so you can position it without scoring a swish.
    // Tick it while tuning in Play mode, then untick it for real play.
    [SerializeField] private bool previewSwishLayout = false;

    [Header("Prefab Child Names")]
    [SerializeField] private string scoreTextChildName = "ScoreText";
    [SerializeField] private string timerTextChildName = "TimerText";
    [SerializeField] private string swishTextChildName = "SwishText"; // optional extra label/icon shown on a swish

    [Header("Feedback")]
    [SerializeField] private Color timeUpColor = Color.red;
    [SerializeField] private Color swishColor = new Color(0.2f, 1f, 0.3f, 1f);
    [SerializeField] private float swishFlashDuration = 0.8f;

    private GameObject uiInstance;
    private TMP_Text scoreText;
    private TMP_Text timerText;
    private TMP_Text swishText; // optional
    private Color scoreNormalColor = Color.white;
    private Color timerNormalColor = Color.white;
    private int lastDisplayedSeconds = -1;
    private Coroutine flashRoutine;
    private bool spawned = false;

    private void Update()
    {
        if (instantPlacementController == null) return;

        if (!spawned && instantPlacementController.hoopPlaced)
        {
            SpawnUI();
        }
        else if (spawned && !instantPlacementController.hoopPlaced)
        {
            // The hoop was removed ("MOVE HOOP"); its UI went with it, so reset and wait for the next one.
            DespawnUI();
        }
    }

    private void LateUpdate()
    {
        if (!spawned || instantPlacementController == null) return;

        Transform hoop = instantPlacementController.PlacedHoopTransform;
        if (hoop == null) return;

        PlaceText(scoreText, hoop, scoreOffset);
        PlaceText(timerText, hoop, timerOffset);
        PlaceText(swishText, hoop, swishOffset);

        // Outside a real swish flash, the preview toggle decides whether the Swish text is shown.
        if (swishText != null && flashRoutine == null)
        {
            if (previewSwishLayout && swishText.text != "SWISH +2") swishText.text = "SWISH +2";
            if (swishText.gameObject.activeSelf != previewSwishLayout) swishText.gameObject.SetActive(previewSwishLayout);
        }
    }

    // The viewer faces the wall, so their right is the hoop's local -X and "toward them" is the hoop's +Z (forward).
    private static void PlaceText(TMP_Text text, Transform hoop, Vector3 offset)
    {
        if (text == null) return;
        text.transform.position = hoop.position - hoop.right * offset.x + hoop.up * offset.y + hoop.forward * offset.z;
    }

    // Pivot on the edge nearest the hoop + matching alignment, so each text hugs the hoop whatever its length.
    private static void ConfigureText(TMP_Text text, float pivotX, TextAlignmentOptions alignment)
    {
        if (text == null) return;

        // If the prefab puts these texts inside a Layout Group, the group would keep resetting their
        // position (and grey out Pos X/Y in the inspector). "Ignore Layout" takes them out of its control.
        LayoutElement layout = text.GetComponent<LayoutElement>();
        if (layout == null) layout = text.gameObject.AddComponent<LayoutElement>();
        layout.ignoreLayout = true;

        text.rectTransform.pivot = new Vector2(pivotX, 0.5f);
        text.alignment = alignment;
    }

    private void SpawnUI()
    {
        Transform hoop = instantPlacementController.PlacedHoopTransform;
        if (hoop == null || scoreUIPrefab == null) return;

        // Parented to the hoop so it follows its position and outward-facing rotation.
        uiInstance = Instantiate(scoreUIPrefab, hoop);
        uiInstance.transform.localPosition = localOffsetFromHoop;
        uiInstance.transform.localRotation = Quaternion.Euler(localRotationOffset);

        scoreText = FindChildComponent<TMP_Text>(uiInstance.transform, scoreTextChildName);
        timerText = FindChildComponent<TMP_Text>(uiInstance.transform, timerTextChildName);
        swishText = FindChildComponent<TMP_Text>(uiInstance.transform, swishTextChildName);

        if (scoreText == null) Debug.LogWarning($"ScoreUI: no child named '{scoreTextChildName}' found in the score UI prefab, so the score can't be shown or positioned.", this);
        if (timerText == null) Debug.LogWarning($"ScoreUI: no child named '{timerTextChildName}' found in the score UI prefab, so the timer can't be shown or positioned.", this);
        if (swishText == null) Debug.LogWarning($"ScoreUI: no child named '{swishTextChildName}' found in the score UI prefab. The swish text can't be positioned by Swish Offset until the name matches exactly.", this);

        ConfigureText(scoreText, 0f, TextAlignmentOptions.MidlineLeft);    // right of the hoop, grows away from it
        ConfigureText(swishText, 0.5f, TextAlignmentOptions.Midline);      // centered under the hoop
        ConfigureText(timerText, 0.5f, TextAlignmentOptions.Midline);

        // Remember the prefab's own colors so we can restore them after a flash / new round.
        if (scoreText != null) scoreNormalColor = scoreText.color;
        if (timerText != null) timerNormalColor = timerText.color;
        if (swishText != null) swishText.gameObject.SetActive(false);

        spawned = true;

        if (ScoreManager.Instance != null)
        {
            Subscribe();

            // Prime the display — the countdown has already reset the score/clock by the time this UI exists.
            UpdateScoreText(ScoreManager.Instance.CurrentScore);
            UpdateTimerText(ScoreManager.Instance.TimeRemaining);
        }
    }

    private void DespawnUI()
    {
        Unsubscribe();

        if (flashRoutine != null)
        {
            StopCoroutine(flashRoutine);
            flashRoutine = null;
        }

        if (uiInstance != null) Destroy(uiInstance); // normally already destroyed along with its parent hoop

        uiInstance = null;
        scoreText = null;
        timerText = null;
        swishText = null;
        lastDisplayedSeconds = -1;
        spawned = false;
    }

    private void Subscribe()
    {
        ScoreManager.Instance.OnRoundPrepared += ResetTimerVisuals;
        ScoreManager.Instance.OnScoreChanged += UpdateScoreText;
        ScoreManager.Instance.OnTimeChanged += UpdateTimerText;
        ScoreManager.Instance.OnShotScored += HandleShotScored;
        ScoreManager.Instance.OnGameEnded += HandleGameEnded;
    }

    private void Unsubscribe()
    {
        if (ScoreManager.Instance == null) return;

        ScoreManager.Instance.OnRoundPrepared -= ResetTimerVisuals;
        ScoreManager.Instance.OnScoreChanged -= UpdateScoreText;
        ScoreManager.Instance.OnTimeChanged -= UpdateTimerText;
        ScoreManager.Instance.OnShotScored -= HandleShotScored;
        ScoreManager.Instance.OnGameEnded -= HandleGameEnded;
    }

    /// <summary>New round on the way: forget the last displayed second and put the timer back to its normal color.</summary>
    private void ResetTimerVisuals()
    {
        lastDisplayedSeconds = -1;
        if (timerText != null) timerText.color = timerNormalColor;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        ClearSwishFlash();
    }

    private void UpdateScoreText(int score)
    {
        if (scoreText != null) scoreText.text = score.ToString();
    }

    private void UpdateTimerText(float secondsRemaining)
    {
        if (timerText == null) return;

        int seconds = Mathf.CeilToInt(Mathf.Max(secondsRemaining, 0f));

        // Only touch the TMP text when the displayed second changes (avoids a string alloc every frame).
        if (seconds == lastDisplayedSeconds) return;
        lastDisplayedSeconds = seconds;

        timerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
        timerText.color = seconds <= 0 ? timeUpColor : timerNormalColor;
    }

    private void HandleShotScored(int points, bool wasSwish)
    {
        if (!wasSwish) return;

        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(SwishFlash(points));
    }

    private IEnumerator SwishFlash(int points)
    {
        if (scoreText != null) scoreText.color = swishColor;
        if (swishText != null)
        {
            swishText.text = $"SWISH +{points}";
            swishText.gameObject.SetActive(true);
        }

        yield return new WaitForSeconds(swishFlashDuration);

        ClearSwishFlash();
    }

    private void ClearSwishFlash()
    {
        if (scoreText != null) scoreText.color = scoreNormalColor;
        if (swishText != null) swishText.gameObject.SetActive(false);
        flashRoutine = null;
    }

    private void HandleGameEnded(int finalScore)
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        ClearSwishFlash();

        lastDisplayedSeconds = 0;
        if (timerText != null)
        {
            timerText.text = "00:00";
            timerText.color = timeUpColor;
        }
    }

    private static T FindChildComponent<T>(Transform root, string childName) where T : Component
    {
        Transform direct = root.Find(childName);
        if (direct != null) return direct.GetComponent<T>();

        // Fallback: search all descendants (including inactive) in case the text is nested deeper.
        foreach (var candidate in root.GetComponentsInChildren<T>(true))
        {
            if (candidate.gameObject.name == childName) return candidate;
        }
        return null;
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }
}