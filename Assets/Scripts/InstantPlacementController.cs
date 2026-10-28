using System;
using Meta.XR;
using Meta.XR.MRUtilityKit;
using UnityEngine;

public class InstantPlacementController : MonoBehaviour
{
    [Header("Controller/Placement")]
    [SerializeField] private Transform rightControllerAnchor; // origin of the raycast
    [SerializeField] private GameObject hoop; // prefab instantiated when raycast hits surface
    [SerializeField] private float maxPlacementDistance = 5f;

    [Header("Wall Clipping Offset")]
    [SerializeField] private float hoopBackOffset = 0.02f;   // half the hoop's backboard thickness, in meters
    [SerializeField] private float clippingBuffer = 0.005f;  // extra buffer to dodge depth-occlusion noise

    [Header("Raycast Visual")]
    [SerializeField] private LineRenderer lineRenderer;
    [SerializeField] private float maxRayDistance = 5f;
    [SerializeField] private Color validColor = Color.cyan;
    [SerializeField] private Color invalidColor = Color.red;

    [Header("Ghost Preview")]
    [SerializeField, Range(0.05f, 1f)] private float ghostOpacity = 0.35f;
    // Optional: a transparent material (any shader) used for every part of the ghost. Leave empty to
    // auto-convert copies of the hoop's own materials. Assign one if you build to the headset — see notes.
    [SerializeField] private Material ghostMaterial;

    [Header("Prompt / Countdown")]
    [SerializeField] private PlacementPrompt placementPrompt;
    [SerializeField] private RoundCountdown roundCountdown;

    [Header("Raycast Manager")]
    [SerializeField] private EnvironmentRaycastManager raycastManager;

    [HideInInspector]
    public bool hoopPlaced = false;

    // Reference to the actual instantiated hoop in the scene, once placed —
    // other scripts (BallShooting, ScoreUI) read THIS, never the prefab asset,
    // since the prefab's transform has no relation to where the hoop ends up in the room.
    public Transform PlacedHoopTransform { get; private set; }

    // Exposed so the end-of-round panel can reuse the same controller anchor and the same
    // (known-visible) ray material instead of needing its own Inspector wiring.
    public Transform RightControllerAnchor => rightControllerAnchor;
    public Material RayMaterial => lineRenderer != null ? lineRenderer.sharedMaterial : null;

    /// <summary>The countdown runner, so the end-of-round panel can restart a round without placing a new hoop.</summary>
    public RoundCountdown Countdown => roundCountdown;

    /// <summary>Raised right after a hoop is placed (before the countdown begins).</summary>
    public event Action OnHoopPlaced;
    /// <summary>Raised when the placed hoop is removed to let the player pick a new spot.</summary>
    public event Action OnHoopRemoved;

    private GameObject ghostInstance;

    // cached from the last raycast this frame, consumed by TryPlace on trigger press
    private bool hasValidHit = false;
    private RaycastHit cachedHit;
    private MRUKAnchor cachedAnchor;
    private EnvironmentRaycastHit cachedEnvHit; // live-depth hit, used for actual placement

    // Ignores the trigger for a moment after repositioning starts: the same click that pressed
    // "MOVE HOOP" on the end panel would otherwise also count as "place the hoop" in the same frame.
    private float ignoreTriggerUntil = 0f;
    private const float RepositionInputLockSeconds = 0.4f;

    // restricts raycasts to wall surfaces only
    private readonly LabelFilter wallFilter = new LabelFilter(MRUKAnchor.SceneLabels.WALL_FACE);

    private void Awake()
    {
        if (hoop != null)
        {
            ghostInstance = BuildGhost();
        }
        else
        {
            Debug.LogWarning("InstantPlacementController: no Hoop prefab assigned, so there is nothing to preview or place.", this);
        }

        if (placementPrompt == null) placementPrompt = FindOrAdd<PlacementPrompt>();
        if (roundCountdown == null) roundCountdown = FindOrAdd<RoundCountdown>();

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = false;
        }
    }

    private void OnDestroy()
    {
        if (ghostInstance != null) Destroy(ghostInstance);
    }

    private void Update()
    {
        if (hoopPlaced)
        {
            HideVisual();
            return;
        }

        Ray ray = new Ray(
            rightControllerAnchor.position,
            rightControllerAnchor.forward
            );

        // Room raycast confirms this direction is a wall (label filter). This is a
        // scanned/static plane — good for "is this a wall", not pixel-accurate to
        // where the live passthrough occlusion thinks the wall currently is.
        hasValidHit = false;
        var room = MRUK.Instance != null ? MRUK.Instance.GetCurrentRoom() : null;
        if (room != null)
        {
            hasValidHit = room.Raycast(ray, maxPlacementDistance, wallFilter, out cachedHit, out cachedAnchor);
        }

        // Environment (live depth) raycast gives the actual placement point that matches
        // what passthrough occlusion renders against right now — only trust it once the
        // room raycast has already confirmed this direction is a wall.
        bool hasValidEnvHit = false;
        if (hasValidHit && raycastManager != null)
        {
            hasValidEnvHit = raycastManager.Raycast(ray, out cachedEnvHit);
        }

        // Final "can place here" requires both: it's a wall AND live depth confirms a surface
        hasValidHit = hasValidHit && hasValidEnvHit;

        UpdateRayVisual(ray, hasValidHit, cachedEnvHit);

        if (placementPrompt != null) placementPrompt.Show(hasValidHit);

        if (Time.time >= ignoreTriggerUntil && OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
        {
            TryPlace();
        }
    }

    /// <summary>
    /// The one place that decides where the hoop sits for a given wall hit, used by BOTH the ghost
    /// preview and the real placement so what the player sees is exactly what they get.
    /// Offsets outward along the wall normal so the backboard sits flush against the wall instead of
    /// clipping into it (pivot offset + depth-occlusion buffer). Uses the live-depth hit, not the
    /// room-plane hit, since that's what passthrough occlusion actually renders against.
    /// </summary>
    private void ComputePlacementPose(EnvironmentRaycastHit envHit, out Vector3 position, out Quaternion rotation)
    {
        position = envHit.point + envHit.normal * (hoopBackOffset + clippingBuffer);
        rotation = Quaternion.LookRotation(envHit.normal, Vector3.up);
    }

    private void UpdateRayVisual(Ray ray, bool didHit, EnvironmentRaycastHit envHit)
    {
        if (didHit)
        {
            // line from controller to hit point (the accurate live-depth point)
            if (lineRenderer != null)
            {
                lineRenderer.enabled = true;
                lineRenderer.SetPosition(0, ray.origin);
                lineRenderer.SetPosition(1, envHit.point);
                lineRenderer.startColor = validColor;
                lineRenderer.endColor = validColor;
            }

            // Ghost hoop sits exactly where the real one will land.
            if (ghostInstance != null)
            {
                ComputePlacementPose(envHit, out Vector3 ghostPosition, out Quaternion ghostRotation);
                ghostInstance.SetActive(true);
                ghostInstance.transform.SetPositionAndRotation(ghostPosition, ghostRotation);
            }
        }
        else
        {
            // no valid hit - show the ray at max length in "invalid" color, hide the ghost
            if (lineRenderer != null)
            {
                lineRenderer.enabled = true;
                lineRenderer.SetPosition(0, ray.origin);
                lineRenderer.SetPosition(1, ray.origin + ray.direction * maxRayDistance);
                lineRenderer.startColor = invalidColor;
                lineRenderer.endColor = invalidColor;
            }

            if (ghostInstance != null) ghostInstance.SetActive(false);
        }
    }

    /**
     * @brief hides linerenderer, ghost hoop and prompt
     */
    private void HideVisual()
    {
        if (lineRenderer != null) lineRenderer.enabled = false;
        if (ghostInstance != null) ghostInstance.SetActive(false);
        if (placementPrompt != null) placementPrompt.Hide();
    }

    /**
     * @brief looks for proper location to place hoop
     */
    private void TryPlace()
    {
        if (!hasValidHit || hoopPlaced || hoop == null) return;

        ComputePlacementPose(cachedEnvHit, out Vector3 placementPosition, out Quaternion placementRotation);

        var hoopToPlace = Instantiate(hoop);
        hoopToPlace.transform.SetPositionAndRotation(placementPosition, placementRotation);
        PlacedHoopTransform = hoopToPlace.transform;

        // if no MRUK component is present in the scene, add an OVRSpatialAnchor component
        // to the instantiated prefab to anchor it in the physical space and prevent drift
        if (MRUK.Instance?.IsWorldLockActive != true)
        {
            hoopToPlace.AddComponent<OVRSpatialAnchor>();
        }

        hoopPlaced = true;
        HideVisual();
        OnHoopPlaced?.Invoke();

        // The round no longer starts the instant the hoop lands: a 3-2-1 countdown runs first and
        // starts the clock on GO. Falls back to an immediate start if there's no countdown component.
        if (roundCountdown != null)
        {
            roundCountdown.StartCountdown();
        }
        else if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.StartGame();
        }
    }

    /// <summary>
    /// Removes the placed hoop and goes back to the placement ray + ghost preview so the player can
    /// choose a new spot. Call from the end-of-round panel ("MOVE HOOP"). Placing again starts a fresh countdown.
    /// </summary>
    public void BeginRepositioning()
    {
        if (!hoopPlaced) return;

        if (PlacedHoopTransform != null) Destroy(PlacedHoopTransform.gameObject); // its score UI is a child, so it goes too
        PlacedHoopTransform = null;

        hoopPlaced = false;
        hasValidHit = false;
        ignoreTriggerUntil = Time.time + RepositionInputLockSeconds;

        OnHoopRemoved?.Invoke();
    }

    // ---------------------------------------------------------------- ghost

    /// <summary>
    /// Builds a see-through, non-interactive copy of the hoop prefab: scripts, rigidbodies and
    /// colliders are stripped (so it can't score, collide, or block raycasts) and its materials
    /// are made translucent.
    /// </summary>
    private GameObject BuildGhost()
    {
        // Instantiate under an INACTIVE parent so the prefab's Awake/OnEnable (ScoreZone, RimEntryZone...)
        // never run on the ghost; the gameplay components are removed before it is ever activated.
        GameObject holder = new GameObject("GhostHolder");
        holder.SetActive(false);

        GameObject ghost = Instantiate(hoop, holder.transform);
        ghost.name = hoop.name + "_Ghost";

        // Scripts first (they may [RequireComponent] a collider), then physics.
        foreach (MonoBehaviour behaviour in ghost.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(behaviour);
        foreach (Joint joint in ghost.GetComponentsInChildren<Joint>(true)) DestroyImmediate(joint);
        foreach (Rigidbody body in ghost.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(body);
        foreach (Collider col in ghost.GetComponentsInChildren<Collider>(true)) DestroyImmediate(col);

        ApplyGhostLook(ghost);

        ghost.SetActive(false);
        ghost.transform.SetParent(null, false);
        Destroy(holder);

        return ghost;
    }

    private void ApplyGhostLook(GameObject ghost)
    {
        foreach (Renderer rend in ghost.GetComponentsInChildren<Renderer>(true))
        {
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.receiveShadows = false;

            if (ghostMaterial != null)
            {
                Material[] replaced = new Material[rend.sharedMaterials.Length];
                for (int i = 0; i < replaced.Length; i++) replaced[i] = ghostMaterial;
                rend.sharedMaterials = replaced;
                continue;
            }

            // .materials hands back per-renderer COPIES, so the real hoop prefab's materials stay untouched.
            Material[] copies = rend.materials;
            foreach (Material m in copies) MakeTransparent(m, ghostOpacity);
            rend.materials = copies;
        }
    }

    // Best-effort conversion to alpha-blended for URP Lit/Unlit and Built-in Standard. Custom shaders may
    // ignore it — assign a transparent material to "Ghost Material" in that case.
    private static void MakeTransparent(Material m, float alpha)
    {
        if (m.HasProperty("_BaseColor"))
        {
            Color c = m.GetColor("_BaseColor");
            c.a = alpha;
            m.SetColor("_BaseColor", c);
        }
        if (m.HasProperty("_Color"))
        {
            Color c = m.GetColor("_Color");
            c.a = alpha;
            m.SetColor("_Color", c);
        }

        if (m.HasProperty("_Surface")) // URP
        {
            m.SetFloat("_Surface", 1f); // 1 = Transparent
            m.SetFloat("_Blend", 0f);   // 0 = Alpha
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_AlphaClip", 0f);
            m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.DisableKeyword("_ALPHATEST_ON");
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetShaderPassEnabled("ShadowCaster", false);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else if (m.HasProperty("_Mode")) // Built-in Standard
        {
            m.SetFloat("_Mode", 3f); // 3 = Transparent
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.EnableKeyword("_ALPHABLEND_ON");
            m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }

    private T FindOrAdd<T>() where T : Component
    {
        T existing = FindFirstObjectByType<T>();
        return existing != null ? existing : gameObject.AddComponent<T>();
    }
}