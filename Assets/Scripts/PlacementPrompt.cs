using TMPro;
using UnityEngine;

/// <summary>
/// Floating instruction shown while the player is choosing where to put the hoop. It trails the
/// player's gaze (eased follow, so it isn't rigidly head-locked) and changes with the ray state:
/// "point at a wall" until the ray hits one, then "press the right trigger to place the hoop".
/// Added automatically by InstantPlacementController if the scene doesn't already have one.
/// </summary>
public class PlacementPrompt : MonoBehaviour
{
    [Header("Placement")]
    [SerializeField] private Transform headAnchor; // CenterEyeAnchor; falls back to Camera.main
    [SerializeField] private float distance = 1.3f;
    [SerializeField] private float heightOffset = -0.3f;
    [SerializeField] private float followSpeed = 4f;
    [SerializeField] private float fontSize = 56f;
    [SerializeField] private TMP_FontAsset font; // optional; empty = TMP default font

    [Header("Text")]
    [SerializeField] private string readyText = "PRESS THE RIGHT TRIGGER\nTO PLACE THE HOOP";
    [SerializeField] private string aimText = "POINT AT A WALL";
    [SerializeField] private Color readyColor = Color.cyan;
    [SerializeField] private Color aimColor = new Color(1f, 0.45f, 0.45f, 1f);

    private WorldTextLabel label;
    private bool visible;
    private int shownState = -1; // -1 = nothing yet, 0 = aiming, 1 = ready

    /// <summary>Call every frame during placement. <paramref name="targetIsValid"/> = the ray is on a valid wall.</summary>
    public void Show(bool targetIsValid)
    {
        if (label == null)
        {
            label = new WorldTextLabel("PlacementPrompt", fontSize, new Vector2(1000f, 240f), 0.001f, font);
        }

        if (!visible)
        {
            visible = true;
            label.SetActive(true);
            label.PlaceInFront(WorldTextLabel.ResolveHead(headAnchor), distance, heightOffset);
        }

        int state = targetIsValid ? 1 : 0;
        if (state != shownState)
        {
            shownState = state;
            label.Text.text = targetIsValid ? readyText : aimText;
            label.Text.color = targetIsValid ? readyColor : aimColor;
        }
    }

    public void Hide()
    {
        if (!visible) return;

        visible = false;
        shownState = -1;
        label.SetActive(false);
    }

    private void LateUpdate()
    {
        if (!visible) return;
        label.FollowHead(WorldTextLabel.ResolveHead(headAnchor), distance, heightOffset, followSpeed, Time.deltaTime);
    }

    private void OnDestroy()
    {
        if (label != null) label.Destroy();
    }
}