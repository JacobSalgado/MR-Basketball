using System;
using UnityEngine;

/// <summary>
/// Tracks score and a countdown timer for a play session, and persists the
/// best score across sessions via PlayerPrefs. Place on a single persistent
/// GameObject in the scene. Raises events so UI (ScoreUI) can react instead
/// of polling every frame.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    [Header("Game Settings")]
    [SerializeField] private float gameDuration = 60f;
    [SerializeField] private int swishBonus = 1; // extra points awarded for a clean swish over a bank shot

    public int CurrentScore { get; private set; }
    public int HighScore { get; private set; }
    public float TimeRemaining { get; private set; }
    public bool IsGameActive { get; private set; }

    /// <summary>Fired whenever CurrentScore changes.</summary>
    public event Action<int> OnScoreChanged;
    /// <summary>Fired every frame the game is active, with seconds remaining.</summary>
    public event Action<float> OnTimeChanged;
    /// <summary>Fired once when the timer hits zero, with the final score.</summary>
    public event Action<int> OnGameEnded;

    private const string HighScoreKey = "HighScore";

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        HighScore = PlayerPrefs.GetInt(HighScoreKey, 0);
    }

    private void Update()
    {
        if (!IsGameActive) return;

        TimeRemaining -= Time.deltaTime;
        OnTimeChanged?.Invoke(Mathf.Max(TimeRemaining, 0f));

        if (TimeRemaining <= 0f)
        {
            EndGame();
        }
    }

    public void StartGame()
    {
        CurrentScore = 0;
        TimeRemaining = gameDuration;
        IsGameActive = true;

        OnScoreChanged?.Invoke(CurrentScore);
        OnTimeChanged?.Invoke(TimeRemaining);
    }

    public void RegisterScore(bool wasSwish)
    {
        if (!IsGameActive) return;

        int points = 1 + (wasSwish ? swishBonus : 0);
        CurrentScore += points;
        Debug.Log("Register Score called! Current score is: " + CurrentScore);
        OnScoreChanged?.Invoke(CurrentScore);
    }

    private void EndGame()
    {
        IsGameActive = false;
        TimeRemaining = 0f;

        if (CurrentScore > HighScore)
        {
            HighScore = CurrentScore;
            PlayerPrefs.SetInt(HighScoreKey, HighScore);
            PlayerPrefs.Save();
        }

        OnGameEnded?.Invoke(CurrentScore);
    }
}