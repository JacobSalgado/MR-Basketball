using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// End-of-round flow: when the timer hits zero a panel appears in front of the player.
///  - If the score makes the top 5, the player types a name on an on-screen keyboard.
///  - Then the top-5 table (name, score, date) is shown with a Retry button.
/// The whole panel is built in code at runtime (no prefab to author) and is operated with the
/// right controller's ray + index trigger. It uses plain physics raycasts against button
/// colliders, so no EventSystem or Meta Interaction SDK setup is required.
/// </summary>
public class RoundEndPanel : MonoBehaviour
{
    private enum View { Hidden, NameEntry, Leaderboard }

    [Header("References")]
    [SerializeField] private Transform rayOrigin;      // right controller anchor (same one used for hoop placement)
    [SerializeField] private Transform headAnchor;     // CenterEyeAnchor; falls back to Camera.main if empty
    [SerializeField] private LineRenderer pointerLine; // optional; a simple one is created if empty
    [SerializeField] private InstantPlacementController instantPlacementController; // optional; auto-found. Supplies the controller anchor + ray material if not assigned above

    [Header("Placement")]
    [SerializeField] private float distanceFromHead = 1.1f;
    [SerializeField] private float heightOffset = -0.1f;
    [SerializeField] private float panelScale = 0.001f; // world-space canvas: 1000 canvas units = 1 meter

    [Header("Pointer")]
    [SerializeField] private float maxPointerDistance = 5f;
    [SerializeField] private float inputDelay = 0.5f;   // ignores clicks right after a view appears

    [Header("Name Entry")]
    [SerializeField] private int maxNameLength = 10;

    [Header("Testing")]
    // The keyboard normally only appears when the final score is above 0 and makes the top 5.
    // Turn this on to see the keyboard after every round (zero-point entries are never saved). Turn it off for real play.
    [SerializeField] private bool forceNameEntryForTesting = false;

    [Header("Colors")]
    [SerializeField] private Color backgroundColor = new Color(0.05f, 0.05f, 0.1f, 0.92f);
    [SerializeField] private Color keyColor = new Color(0.18f, 0.2f, 0.32f, 1f);
    [SerializeField] private Color keyHoverColor = new Color(0.25f, 0.6f, 0.9f, 1f);
    [SerializeField] private Color confirmColor = new Color(0.15f, 0.55f, 0.25f, 1f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color highlightColor = new Color(1f, 0.85f, 0.2f, 1f);

    private const float CanvasWidth = 900f;
    private const float CanvasHeight = 660f;

    private GameObject root;
    private GameObject nameEntryView;
    private GameObject leaderboardView;
    private TextMeshProUGUI scoreLabel;
    private TextMeshProUGUI nameLabel;
    private readonly TextMeshProUGUI[] rowLabels = new TextMeshProUGUI[HighScoreStore.MaxEntries];

    private readonly Dictionary<Button, Color> baseColors = new Dictionary<Button, Color>();
    private readonly StringBuilder nameBuffer = new StringBuilder();
    private readonly RaycastHit[] hitBuffer = new RaycastHit[16];

    private View view = View.Hidden;
    private Button hovered;
    private int pendingScore;
    private HighScoreEntry lastAddedEntry;
    private float shownAt;

    private void Start()
    {
        if (instantPlacementController == null)
        {
            instantPlacementController = FindFirstObjectByType<InstantPlacementController>();
        }

        // Reuse the same right-controller anchor the hoop placement ray uses if none was assigned here.
        if (rayOrigin == null && instantPlacementController != null)
        {
            rayOrigin = instantPlacementController.RightControllerAnchor;
        }

        if (rayOrigin == null)
        {
            Debug.LogWarning("RoundEndPanel: no Ray Origin assigned and none could be found — the pointer (and Retry/keyboard clicks) can't work. Drag your right controller anchor into the Ray Origin slot.", this);
        }

        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnGameEnded += HandleGameEnded;
            ScoreManager.Instance.OnGameStarted += Hide;
        }
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnGameEnded -= HandleGameEnded;
            ScoreManager.Instance.OnGameStarted -= Hide;
        }
        if (root != null) Destroy(root);
    }

    // ---------------------------------------------------------------- flow

    private void HandleGameEnded(int finalScore)
    {
        EnsureBuilt();

        pendingScore = finalScore;
        lastAddedEntry = null;
        scoreLabel.text = $"SCORE: {finalScore}";

        PlaceInFrontOfPlayer();
        root.SetActive(true);
        if (pointerLine != null) pointerLine.enabled = true;

        if (forceNameEntryForTesting || HighScoreStore.Qualifies(finalScore)) ShowNameEntry();
        else ShowLeaderboard();
    }

    private void ShowNameEntry()
    {
        SetHovered(null);
        view = View.NameEntry;
        nameBuffer.Clear();
        RefreshNameLabel();
        nameEntryView.SetActive(true);
        leaderboardView.SetActive(false);
        shownAt = Time.time;
    }

    private void ShowLeaderboard()
    {
        SetHovered(null);
        view = View.Leaderboard;
        RefreshRows();
        nameEntryView.SetActive(false);
        leaderboardView.SetActive(true);
        shownAt = Time.time;
    }

    private void Hide()
    {
        SetHovered(null);
        view = View.Hidden;
        if (root != null) root.SetActive(false);
        if (pointerLine != null) pointerLine.enabled = false;
    }

    private void OnKeyPressed(string key)
    {
        if (nameBuffer.Length >= maxNameLength) return;
        nameBuffer.Append(key);
        RefreshNameLabel();
    }

    private void OnDeletePressed()
    {
        if (nameBuffer.Length > 0) nameBuffer.Length--;
        RefreshNameLabel();
    }

    private void OnConfirmPressed()
    {
        // An empty name is stored as "PLAYER" by HighScoreStore. Zero-point rounds (only possible with the
        // testing toggle) are never saved, so they can't push real scores off the table.
        if (pendingScore > 0)
        {
            lastAddedEntry = HighScoreStore.Add(nameBuffer.ToString(), pendingScore);
        }
        ShowLeaderboard();
    }

    private void OnRetryPressed()
    {
        Hide();
        if (ScoreManager.Instance != null) ScoreManager.Instance.StartGame();
    }

    private void RefreshNameLabel()
    {
        nameLabel.text = nameBuffer.Length == 0
            ? "<alpha=#55>TAP LETTERS BELOW"
            : nameBuffer + "_";
    }

    private void RefreshRows()
    {
        IReadOnlyList<HighScoreEntry> entries = HighScoreStore.Entries;
        for (int i = 0; i < rowLabels.Length; i++)
        {
            if (i < entries.Count)
            {
                HighScoreEntry e = entries[i];
                rowLabels[i].text = $"{i + 1}.<pos=12%>{e.name}<pos=52%>{e.score}<pos=70%>{e.date}";
                rowLabels[i].color = e == lastAddedEntry ? highlightColor : textColor;
            }
            else
            {
                rowLabels[i].text = $"{i + 1}.<pos=12%>---";
                rowLabels[i].color = new Color(textColor.r, textColor.g, textColor.b, 0.4f);
            }
        }
    }

    // ---------------------------------------------------------------- pointer

    private void Update()
    {
        if (view == View.Hidden || rayOrigin == null) return;

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);

        // Nearest button collider along the ray (triggers included — button colliders are triggers).
        Button target = null;
        float bestDistance = maxPointerDistance;
        int count = Physics.RaycastNonAlloc(ray, hitBuffer, maxPointerDistance, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < count; i++)
        {
            if (hitBuffer[i].distance >= bestDistance) continue;
            Button candidate = hitBuffer[i].collider.GetComponent<Button>();
            if (candidate == null) continue;
            target = candidate;
            bestDistance = hitBuffer[i].distance;
        }

        SetHovered(target);

        if (pointerLine != null)
        {
            float length = target != null ? bestDistance : Mathf.Min(maxPointerDistance, 3f);
            pointerLine.SetPosition(0, ray.origin);
            pointerLine.SetPosition(1, ray.origin + ray.direction * length);
        }

        if (target != null && Time.time - shownAt >= inputDelay && OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
        {
            target.onClick.Invoke();
        }
    }

    private void SetHovered(Button next)
    {
        if (next == hovered) return;

        if (hovered != null && baseColors.TryGetValue(hovered, out Color previousColor))
        {
            hovered.targetGraphic.color = previousColor;
        }

        hovered = next;
        if (hovered != null) hovered.targetGraphic.color = keyHoverColor;
    }

    // ---------------------------------------------------------------- placement

    private void PlaceInFrontOfPlayer()
    {
        Transform head = headAnchor;
        if (head == null && Camera.main != null) head = Camera.main.transform;
        if (head == null) head = rayOrigin;
        if (head == null) return;

        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        forward.Normalize();

        root.transform.position = head.position + forward * distanceFromHead + Vector3.up * heightOffset;

        // A world-space Canvas is readable from its -Z side, so its forward must point AWAY from the player.
        root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
    }

    // ---------------------------------------------------------------- building the UI

    private void EnsureBuilt()
    {
        if (root != null) return;
        BuildPanel();
        EnsurePointerLine();
    }

    private void BuildPanel()
    {
        root = new GameObject("RoundEndPanel_Canvas", typeof(RectTransform), typeof(Canvas));
        root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        RectTransform rootRect = root.GetComponent<RectTransform>();
        rootRect.sizeDelta = new Vector2(CanvasWidth, CanvasHeight);
        rootRect.localScale = Vector3.one * panelScale;

        GameObject background = new GameObject("Background", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(root.transform, false);
        RectTransform bgRect = background.GetComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;
        Image bgImage = background.GetComponent<Image>();
        bgImage.color = backgroundColor;
        bgImage.raycastTarget = false;

        CreateText(root.transform, "Title", "TIME'S UP!", 56f, TextAlignmentOptions.Center, new Vector2(0f, 275f), new Vector2(800f, 70f));
        scoreLabel = CreateText(root.transform, "Score", "SCORE: 0", 40f, TextAlignmentOptions.Center, new Vector2(0f, 220f), new Vector2(800f, 50f));
        scoreLabel.color = highlightColor;

        BuildNameEntryView();
        BuildLeaderboardView();

        root.SetActive(false);
    }

    private void BuildNameEntryView()
    {
        nameEntryView = CreateContainer("NameEntryView");

        CreateText(nameEntryView.transform, "Prompt", "NEW HIGH SCORE! ENTER YOUR NAME", 30f, TextAlignmentOptions.Center, new Vector2(0f, 170f), new Vector2(800f, 40f));
        nameLabel = CreateText(nameEntryView.transform, "Name", string.Empty, 48f, TextAlignmentOptions.Center, new Vector2(0f, 115f), new Vector2(800f, 60f));
        nameLabel.color = highlightColor;

        const float keySize = 72f;
        const float pitch = 80f;
        string[] rows = { "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM" };

        float y = 30f;
        foreach (string row in rows)
        {
            for (int i = 0; i < row.Length; i++)
            {
                string letter = row[i].ToString();
                float x = (i - (row.Length - 1) * 0.5f) * pitch;
                CreateButton(nameEntryView.transform, letter, new Vector2(x, y), new Vector2(keySize, keySize), keyColor, () => OnKeyPressed(letter));
            }
            y -= pitch;
        }

        // Bottom row: SPACE | DEL | ENTER
        CreateButton(nameEntryView.transform, "SPACE", new Vector2(-188f, y), new Vector2(300f, keySize), keyColor, () => OnKeyPressed(" "));
        CreateButton(nameEntryView.transform, "DEL", new Vector2(50f, y), new Vector2(160f, keySize), keyColor, OnDeletePressed);
        CreateButton(nameEntryView.transform, "ENTER", new Vector2(238f, y), new Vector2(200f, keySize), confirmColor, OnConfirmPressed);
    }

    private void BuildLeaderboardView()
    {
        leaderboardView = CreateContainer("LeaderboardView");

        CreateText(leaderboardView.transform, "Header", "HIGH SCORES", 40f, TextAlignmentOptions.Center, new Vector2(0f, 150f), new Vector2(800f, 50f));

        TextMeshProUGUI columns = CreateText(leaderboardView.transform, "Columns", "RANK<pos=12%>NAME<pos=52%>SCORE<pos=70%>DATE", 24f, TextAlignmentOptions.Left, new Vector2(0f, 100f), new Vector2(780f, 34f));
        columns.color = new Color(textColor.r, textColor.g, textColor.b, 0.6f);

        for (int i = 0; i < rowLabels.Length; i++)
        {
            rowLabels[i] = CreateText(leaderboardView.transform, $"Row{i + 1}", string.Empty, 32f, TextAlignmentOptions.Left, new Vector2(0f, 55f - i * 50f), new Vector2(780f, 44f));
        }

        CreateButton(leaderboardView.transform, "RETRY", new Vector2(0f, -240f), new Vector2(320f, 84f), confirmColor, OnRetryPressed);
    }

    private GameObject CreateContainer(string objectName)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(root.transform, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return go;
    }

    private TextMeshProUGUI CreateText(Transform parent, string objectName, string text, float fontSize, TextAlignmentOptions alignment, Vector2 anchoredPosition, Vector2 size)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = alignment;
        tmp.color = textColor;
        tmp.raycastTarget = false;

        RectTransform rt = tmp.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPosition;
        return tmp;
    }

    private Button CreateButton(Transform parent, string label, Vector2 anchoredPosition, Vector2 size, Color baseColor, UnityAction onClick)
    {
        GameObject go = new GameObject(label + "_Button", typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);

        RectTransform rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = anchoredPosition;

        Image image = go.GetComponent<Image>();
        image.color = baseColor;

        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        button.transition = Selectable.Transition.None; // hover tint is driven by the pointer code above
        button.onClick.AddListener(onClick);

        float fontSize = label.Length > 1 ? size.y * 0.38f : size.y * 0.55f;
        CreateText(go.transform, "Label", label, fontSize, TextAlignmentOptions.Center, Vector2.zero, size);

        // Collider so the controller ray can hit the button.
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(size.x, size.y, 12f);

        baseColors[button] = baseColor;
        return button;
    }

    private void EnsurePointerLine()
    {
        if (pointerLine == null)
        {
            GameObject go = new GameObject("RoundEndPointer");
            go.transform.SetParent(transform, false);
            pointerLine = go.AddComponent<LineRenderer>();

            // Prefer the material the hoop-placement ray already uses — it's known to render on this device.
            // Shader.Find can return null in a build if the shader was stripped, which would leave an invisible line.
            Material rayMaterial = instantPlacementController != null ? instantPlacementController.RayMaterial : null;
            if (rayMaterial != null)
            {
                pointerLine.sharedMaterial = rayMaterial;
            }
            else
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader != null) pointerLine.material = new Material(shader);
                else Debug.LogWarning("RoundEndPanel: couldn't find a material for the pointer line. Assign a LineRenderer in the 'Pointer Line' slot.", this);
            }

            pointerLine.startColor = Color.cyan;
            pointerLine.endColor = Color.cyan;
            pointerLine.startWidth = 0.006f;
            pointerLine.endWidth = 0.006f;
        }

        pointerLine.useWorldSpace = true;
        pointerLine.positionCount = 2;
        pointerLine.enabled = false;
    }
}