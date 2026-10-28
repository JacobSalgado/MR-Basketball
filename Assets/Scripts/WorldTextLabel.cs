using TMPro;
using UnityEngine;

/// <summary>
/// Small helper that builds a single world-space text label (Canvas + TextMeshProUGUI) in code and
/// can place it in front of the player's head. Used by PlacementPrompt and RoundCountdown.
/// Plain C# class (not a component) — the owner calls Destroy() when it is done with it.
/// </summary>
public class WorldTextLabel
{
    public GameObject Root { get; private set; }
    public TextMeshProUGUI Text { get; private set; }

    private readonly float baseScale;

    public WorldTextLabel(string name, float fontSize, Vector2 canvasSize, float scale = 0.001f, TMP_FontAsset font = null)
    {
        baseScale = scale;

        Root = new GameObject(name, typeof(RectTransform), typeof(Canvas));
        Root.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        RectTransform rect = Root.GetComponent<RectTransform>();
        rect.sizeDelta = canvasSize;
        rect.localScale = Vector3.one * scale; // world-space canvas: 1000 canvas units = 1 meter at the default scale

        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(Root.transform, false);

        Text = textObject.AddComponent<TextMeshProUGUI>();
        if (font != null) Text.font = font; // otherwise the TMP default font is used
        Text.fontSize = fontSize;
        Text.alignment = TextAlignmentOptions.Center;
        Text.raycastTarget = false;

        RectTransform textRect = Text.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Root.SetActive(false);
    }

    public void SetActive(bool active)
    {
        if (Root != null) Root.SetActive(active);
    }

    /// <summary>Scales the label relative to its base size (used for the countdown "pop").</summary>
    public void SetScaleMultiplier(float multiplier)
    {
        if (Root != null) Root.transform.localScale = Vector3.one * (baseScale * multiplier);
    }

    /// <summary>The assigned head anchor, or Camera.main's transform if none was assigned.</summary>
    public static Transform ResolveHead(Transform assigned)
    {
        if (assigned != null) return assigned;
        return Camera.main != null ? Camera.main.transform : null;
    }

    /// <summary>Snaps the label to a point in front of the head (flattened to the horizontal plane), facing the player.</summary>
    public void PlaceInFront(Transform head, float distance, float heightOffset)
    {
        if (head == null || Root == null) return;

        Root.transform.position = head.position + FlatForward(head) * distance + Vector3.up * heightOffset;
        FaceAwayFrom(head);
    }

    /// <summary>Eases the label toward the point in front of the head so it trails behind when the player turns.</summary>
    public void FollowHead(Transform head, float distance, float heightOffset, float speed, float deltaTime)
    {
        if (head == null || Root == null) return;

        Vector3 target = head.position + FlatForward(head) * distance + Vector3.up * heightOffset;
        float t = 1f - Mathf.Exp(-speed * deltaTime);
        Root.transform.position = Vector3.Lerp(Root.transform.position, target, t);
        FaceAwayFrom(head);
    }

    public void Destroy()
    {
        if (Root != null) UnityEngine.Object.Destroy(Root);
    }

    private static Vector3 FlatForward(Transform head)
    {
        Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
        return forward.normalized;
    }

    // A world-space Canvas is readable from its -Z side, so its forward must point AWAY from the viewer.
    private void FaceAwayFrom(Transform head)
    {
        Vector3 away = Root.transform.position - head.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f) return;
        Root.transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
    }
}