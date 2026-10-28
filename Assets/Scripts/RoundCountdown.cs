using System.Collections;
using UnityEngine;

/// <summary>
/// Runs the "3... 2... 1... GO!" lead-in before a round. While it runs the score and clock sit at their
/// starting values and shooting is locked (ScoreManager isn't active yet); the clock starts on GO.
/// Used both after the hoop is placed and when retrying in the same spot.
/// Added automatically by InstantPlacementController if the scene doesn't already have one.
/// </summary>
public class RoundCountdown : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform headAnchor; // CenterEyeAnchor; falls back to Camera.main

    [Header("Timing")]
    [SerializeField] private int startFrom = 3;
    [SerializeField] private float secondsPerNumber = 1f;
    [SerializeField] private float goDuration = 0.7f;

    [Header("Look")]
    [SerializeField] private float distanceFromHead = 1.3f;
    [SerializeField] private float heightOffset = 0.1f;
    [SerializeField] private float followSpeed = 6f; // eased follow so the number stays in view if the player turns
    [SerializeField] private float fontSize = 260f;
    [SerializeField] private string goText = "GO!";
    [SerializeField] private Color numberColor = Color.white;
    [SerializeField] private Color goColor = new Color(0.2f, 1f, 0.3f, 1f);

    public bool IsCounting { get; private set; }

    private WorldTextLabel label;

    /// <summary>Starts the countdown, then the round. Ignored if one is already running.</summary>
    public void StartCountdown()
    {
        if (IsCounting) return;
        StartCoroutine(CountdownRoutine());
    }

    private IEnumerator CountdownRoutine()
    {
        IsCounting = true;

        // Reset score/clock to their starting values (and put a fresh ball in hand) without starting the clock yet.
        if (ScoreManager.Instance != null) ScoreManager.Instance.PrepareRound();

        if (label == null)
        {
            label = new WorldTextLabel("RoundCountdown", fontSize, new Vector2(800f, 400f));
        }
        label.PlaceInFront(WorldTextLabel.ResolveHead(headAnchor), distanceFromHead, heightOffset);
        label.SetActive(true);

        for (int n = startFrom; n >= 1; n--)
        {
            yield return ShowBeat(n.ToString(), numberColor, secondsPerNumber);
        }

        if (ScoreManager.Instance != null) ScoreManager.Instance.StartGame();
        yield return ShowBeat(goText, goColor, goDuration);

        label.SetActive(false);
        IsCounting = false;
    }

    // One number: starts big and shrinks into place, then fades out over its last 30%.
    private IEnumerator ShowBeat(string text, Color color, float duration)
    {
        label.Text.text = text;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.Clamp01(elapsed / duration);

            float pop = Mathf.Lerp(1.4f, 1f, Mathf.Clamp01(u / 0.25f));
            float alpha = 1f - Mathf.Clamp01((u - 0.7f) / 0.3f);

            label.SetScaleMultiplier(pop);
            label.Text.color = new Color(color.r, color.g, color.b, alpha);
            yield return null;
        }
    }

    private void LateUpdate()
    {
        if (!IsCounting || label == null) return;
        label.FollowHead(WorldTextLabel.ResolveHead(headAnchor), distanceFromHead, heightOffset, followSpeed, Time.deltaTime);
    }

    private void OnDisable()
    {
        IsCounting = false;
        if (label != null) label.SetActive(false);
    }

    private void OnDestroy()
    {
        if (label != null) label.Destroy();
    }
}