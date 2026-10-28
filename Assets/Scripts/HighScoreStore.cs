using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

[Serializable]
public class HighScoreEntry
{
    public string name;
    public int score;
    public string date; // yyyy-MM-dd
}

[Serializable]
public class HighScoreData
{
    public List<HighScoreEntry> entries = new List<HighScoreEntry>();
}

/// <summary>
/// Top-N high score table persisted as JSON at Application.persistentDataPath/highscores.json.
/// Plain static class — no GameObject needed.
/// </summary>
public static class HighScoreStore
{
    public const int MaxEntries = 5;
    private const string FileName = "highscores.json";

    private static List<HighScoreEntry> entries;

    private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    /// <summary>Entries sorted best-first. Read this property each time rather than caching the list.</summary>
    public static IReadOnlyList<HighScoreEntry> Entries
    {
        get
        {
            EnsureLoaded();
            return entries;
        }
    }

    public static int TopScore
    {
        get
        {
            EnsureLoaded();
            return entries.Count > 0 ? entries[0].score : 0;
        }
    }

    /// <summary>True if this score would land on the table (and is above zero).</summary>
    public static bool Qualifies(int score)
    {
        EnsureLoaded();
        if (score <= 0) return false;
        return entries.Count < MaxEntries || score > entries[entries.Count - 1].score;
    }

    /// <summary>Adds a score, keeps only the top MaxEntries, saves, and returns the new entry.</summary>
    public static HighScoreEntry Add(string playerName, int score)
    {
        EnsureLoaded();

        var entry = new HighScoreEntry
        {
            name = Sanitize(playerName),
            score = score,
            date = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        };

        entries.Add(entry);
        // OrderByDescending is stable, so on a tie the older entry keeps the higher rank.
        entries = entries.OrderByDescending(e => e.score).Take(MaxEntries).ToList();

        Save();
        return entry;
    }

    private static string Sanitize(string raw)
    {
        string cleaned = (raw ?? string.Empty).Trim().ToUpperInvariant();
        return cleaned.Length == 0 ? "PLAYER" : cleaned;
    }

    private static void EnsureLoaded()
    {
        if (entries != null) return;

        entries = new List<HighScoreEntry>();
        try
        {
            if (File.Exists(FilePath))
            {
                HighScoreData data = JsonUtility.FromJson<HighScoreData>(File.ReadAllText(FilePath));
                if (data != null && data.entries != null)
                {
                    entries = data.entries.OrderByDescending(e => e.score).Take(MaxEntries).ToList();
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"HighScoreStore: could not read {FilePath} ({ex.Message}). Starting with an empty table.");
        }
    }

    private static void Save()
    {
        try
        {
            string json = JsonUtility.ToJson(new HighScoreData { entries = entries }, true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"HighScoreStore: could not write {FilePath} ({ex.Message}).");
        }
    }
}