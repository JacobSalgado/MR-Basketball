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
    [SerializeField] private GameObject reticlePrefab;
    [SerializeField] private float maxRayDistance = 5f;
    [SerializeField] private Color validColor = Color.cyan;
    [SerializeField] private Color invalidColor = Color.red;

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

    private GameObject reticleInstance;

    // cached from the last raycast this frame, consumed by TryPlace on trigger press
    private bool hasValidHit = false;
    private RaycastHit cachedHit;
    private MRUKAnchor cachedAnchor;
    private EnvironmentRaycastHit cachedEnvHit; // live-depth hit, used for actual placement

    // restricts raycasts to wall surfaces only
    private readonly LabelFilter wallFilter = new LabelFilter(MRUKAnchor.SceneLabels.WALL_FACE);

    private void Awake()
    {
        if (reticlePrefab != null)
        {
            reticleInstance = Instantiate(reticlePrefab);
            reticleInstance.SetActive(false);
        }

        if (lineRenderer != null)
        {
            lineRenderer.positionCount = 2;
            lineRenderer.enabled = false;
        }
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

        if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
        {
            TryPlace();
        }
    }

    private void UpdateRayVisual(Ray ray, bool didHit, EnvironmentRaycastHit envHit)
    {
        if (didHit)
        {
            // line from controller to hit point (the accurate live-depth point)
            lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, ray.origin);
            lineRenderer.SetPosition(1, envHit.point);
            lineRenderer.startColor = validColor;
            lineRenderer.endColor = validColor;

            // Reticle sits flush on the surface, oriented to its normal — uses envHit so
            // the preview matches exactly where the hoop will actually land.
            if (reticleInstance != null)
            {
                reticleInstance.SetActive(true);
                reticleInstance.transform.SetPositionAndRotation(
                    envHit.point,
                    Quaternion.LookRotation(envHit.normal, Vector3.up)
                    );
            }
        }
        else
        {
            // no valid hit - show the ray at max length in "invalid" color, hide reticle
            lineRenderer.enabled = true;
            lineRenderer.SetPosition(0, ray.origin);
            lineRenderer.SetPosition(1, ray.origin + ray.direction * maxRayDistance);
            lineRenderer.startColor = invalidColor;
            lineRenderer.endColor = invalidColor;

            if (reticleInstance != null)
                reticleInstance.SetActive(false);
        }
    }

    /**
     * @brief hides linerenderer and reticleinstance
     */
    private void HideVisual()
    {
        if (lineRenderer != null) lineRenderer.enabled = false;
        if (reticleInstance != null) reticleInstance.SetActive(false);
    }

    /**
     * @brief looks for proper location to place hoop
     */
    private void TryPlace()
    {
        if (!hasValidHit || hoopPlaced) return;

        // Offset outward along the wall normal so the hoop's backboard sits flush against
        // the wall instead of clipping into it (pivot offset + depth-occlusion buffer).
        // Uses cachedEnvHit (live depth), not the room-plane cachedHit — that's what
        // passthrough occlusion actually renders against, so this is what keeps the
        // hoop from visually sinking into the wall.
        Vector3 placementPosition = cachedEnvHit.point + cachedEnvHit.normal * (hoopBackOffset + clippingBuffer);

        var hoopToPlace = Instantiate(hoop);
        hoopToPlace.transform.SetPositionAndRotation(
            placementPosition,
            Quaternion.LookRotation(cachedEnvHit.normal, Vector3.up)
            );
        PlacedHoopTransform = hoopToPlace.transform;

        // if no MRUK component is present in the scene, add an OVRSpatialAnchor component
        // to the instantiated prefab to anchor it in the physical space and prevent drift
        if (MRUK.Instance?.IsWorldLockActive != true)
        {
            hoopToPlace.AddComponent<OVRSpatialAnchor>();
        }

        hoopPlaced = true;
        HideVisual();

        // Game clock starts the moment the hoop is placed — Pop-A-Shot arcade cabinets
        // start the timer the instant the machine is ready, not on a separate "ready" press.
        ScoreManager.Instance?.StartGame();
    }
}