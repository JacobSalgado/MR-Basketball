using TMPro;
using UnityEngine;

/// <summary>
/// Spawns a world-space score/timer display above the placed hoop, Pop-A-Shot
/// style, and keeps it updated via ScoreManager's events (no per-frame polling).
/// Attach to any persistent GameObject in the scene.
/// </summary>
public class ScoreUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private InstantPlacementController instantPlacementController;
    [SerializeField] private GameObject scoreUIPrefab; // world-space Canvas prefab — see setup notes

    [Header("Placement")]
    [SerializeField] private Vector3 localOffsetFromHoop = new Vector3(0f, 0.3f, 0f);

    // A World Space Canvas's readable front is its local -Z side, not +Z. The hoop's
    // local +Z already points outward toward the player, so a canvas at identity
    // rotation ends up with its readable face pointed INTO the wall — which is exactly
    // what shows up as "backwards/mirrored" text. Rotating 180° on Y flips it to face
    // the player correctly. Exposed here so you can nudge it without touching code if
    // your prefab's Canvas is authored facing a different default direction.
    [SerializeField] private Vector3 localRotationOffset = new Vector3(0f, 180f, 0f);


    [Header("Prefab Child Names")]
    [SerializeField] private string scoreTextChildName = "ScoreText";
    [SerializeField] private string timerTextChildName = "TimerText";

    private GameObject uiInstance;
    private TMP_Text scoreText;
    private TMP_Text timerText;
    private bool spawned = false;

    private void Update()
    {
        if (!spawned && instantPlacementController != null && instantPlacementController.hoopPlaced)
        {
            SpawnUI();
        }
    }

    private void SpawnUI()
    {
        Transform hoop = instantPlacementController.PlacedHoopTransform;
        if (hoop == null || scoreUIPrefab == null) return;

        // Parented to the hoop so it inherits its rotation — the hoop already faces
        // outward from the wall (same normal the Board uses), so the display
        // automatically faces the player without any extra billboard logic.
        uiInstance = Instantiate(scoreUIPrefab, hoop);
        uiInstance.transform.localPosition = localOffsetFromHoop;
        uiInstance.transform.localRotation = Quaternion.Euler(localRotationOffset);

        scoreText = FindChildComponent<TMP_Text>(uiInstance.transform, scoreTextChildName);
        timerText = FindChildComponent<TMP_Text>(uiInstance.transform, timerTextChildName);

        spawned = true;

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged += UpdateScoreText;
            ScoreManager.Instance.OnTimeChanged += UpdateTimerText;
            ScoreManager.Instance.OnGameEnded += HandleGameEnded;

            // Prime the display immediately — StartGame() fires in the same frame
            // TryPlace() places the hoop, so the game may already be running by now.
            UpdateScoreText(ScoreManager.Instance.CurrentScore);
            UpdateTimerText(ScoreManager.Instance.TimeRemaining);
        }
    }

    private void UpdateScoreText(int score)
    {
        if (scoreText != null) scoreText.text = score.ToString();
    }

    private void UpdateTimerText(float secondsRemaining)
    {
        if (timerText == null) return;
        int seconds = Mathf.CeilToInt(Mathf.Max(secondsRemaining, 0f));
        timerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
    }

    private void HandleGameEnded(int finalScore)
    {
        // Placeholder end-state — extend once the rest of the game-over flow exists
        // (e.g. swap to a "GAME OVER" label, show high score, prompt to replay).
        if (timerText != null) timerText.text = "00:00";
    }

    private static T FindChildComponent<T>(Transform root, string childName) where T : Component
    {
        Transform direct = root.Find(childName);
        if (direct != null) return direct.GetComponent<T>();

        // Fallback: search all descendants in case the prefab nests text under
        // additional layout/container objects rather than as a direct child.
        foreach (var candidate in root.GetComponentsInChildren<T>(true))
        {
            if (candidate.gameObject.name == childName) return candidate;
        }
        return null;
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnScoreChanged -= UpdateScoreText;
            ScoreManager.Instance.OnTimeChanged -= UpdateTimerText;
            ScoreManager.Instance.OnGameEnded -= HandleGameEnded;
        }
    }
}