using UnityEngine;

public class BallShooting : MonoBehaviour
{
    private enum ShotState { InHand, Aiming, InFlight }
    private ShotState state = ShotState.InHand;

    [Header("Controller & Shooting Features")]
    [SerializeField] private Transform rightController; // origin of the spawned basketball
    [SerializeField] private GameObject basketballPrefab; // prefab of basketball

    [Header("Aiming")]
    [SerializeField] private float minLaunchSpeed = 3f;
    [SerializeField] private float maxLaunchSpeed = 9f;
    [SerializeField] private float chargeDeadzone = 0.15f; // stick must exceed this to start (and stay) aiming

    [Header("Trajectory Preview")]
    [SerializeField] private LineRenderer trajectoryLine;
    [SerializeField] private int trajectoryPoints = 30;
    [SerializeField] private float trajectoryTimeStep = 0.05f;
    [SerializeField, Range(0.1f, 1f)] private float trajectoryRevealFraction = 0.5f;

    [Header("Shot Lifecycle")]
    [SerializeField] private float maxFlightTime = 4f;
    [SerializeField] private float outOfPlayHeight = -2f; // relative to spawn height, ball below this is "gone"

    [Header("Physics Feel")]
    [SerializeField] private float ballMass = 0.6f; // kg, roughly a real basketball
    [SerializeField] private float ballDrag = 0.05f;
    [SerializeField] private float ballAngularDrag = 0.3f;
    [SerializeField] private float backspinAmount = 8f; // radians/sec applied at release

    [Header("Instant Placement Controller Reference")]
    [SerializeField] private InstantPlacementController instantPlacementController;

    private bool activeBall = false;
    private GameObject basketballInstance;
    private Rigidbody basketballRb;
    private BallContactTracker basketballContactTracker;

    private float currentSpeed01; // 0-1, how "charged" the shot is
    private float flightTimer;

    private void Awake()
    {
        // spawn once, in the hand, and keep inactive until the hoop is placed
        basketballInstance = Instantiate(basketballPrefab, rightController.position, rightController.rotation, rightController);

        basketballRb = basketballInstance.GetComponent<Rigidbody>();
        basketballRb.isKinematic = true; // no gravity yet - controlled manually while in-hand
        ConfigureBallPhysics(basketballRb);
        basketballInstance.SetActive(false);

        basketballContactTracker = basketballInstance.GetComponent<BallContactTracker>();

        // Must match trajectoryPoints or SetPosition() throws IndexOutOfRangeException
        // once DrawTrajectoryPreview tries to write past the LineRenderer's default count of 2
        if (trajectoryLine != null)
        {
            trajectoryLine.positionCount = trajectoryPoints;
            trajectoryLine.enabled = false;
        }
        if (instantPlacementController != null)
            instantPlacementController.OnHoopRemoved += HandleHoopRemoved;
    }

    private void Start()
    {
        // subscribed in Start so ScoreManager.Instance is guaranteed to be set
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnGameStarted += HandleRoundStarted;
            ScoreManager.Instance.OnGameEnded += HandleRoundEnded;
        }
    }

    private void Update()
    {
        if (instantPlacementController.hoopPlaced && !activeBall)
        {
            basketballInstance.SetActive(true);
            activeBall = true;
        }

        switch (state)
        {
            case ShotState.InHand:
                CheckForChargeStart(); // stick exceeds deadzone -> move to Aiming
                break;
            case ShotState.Aiming:
                HandleAiming(); // read controller pointing direction + stick power, draw trajectory, check release/cancel
                break;
            case ShotState.InFlight:
                HandleFlightTimeout(); // watch for miss (fell out of play / timed out)
                break;
        }
    }

    /// <summary>Retry pressed: put a fresh ball in the hand. (The very first round is handled by Update once the hoop exists.)</summary>
    private void HandleRoundStarted()
    {
        if (!activeBall) return;
        RespawnBall();
    }

    /// <summary>Timer hit zero: stop aiming and hide the ball while the end-of-round panel is up.</summary>
    private void HandleRoundEnded(int finalScore)
    {
        if (!activeBall) return;
        CancelAiming();
        basketballInstance.SetActive(false);
    }

    /// <summary>
    /// A round is being set up (countdown begins): put a fresh ball in the hand. It can't be aimed or fired
    /// until the countdown ends because Update only acts while ScoreManager.IsGameActive.
    /// (The very first round is handled by Update once the hoop exists.)
    /// </summary>
    private void HandleRoundPrepared()
    {
        if (!activeBall) return;
        RespawnBall();
    }

    /// <summary>The hoop was removed so the player can reposition it: reset the ball and hide it until the next hoop is placed.</summary>
    private void HandleHoopRemoved()
    {
        if (!activeBall) return;

        RespawnBall(); // clean slate: kinematic, parented to the hand, tracker reset, state InHand
        basketballInstance.SetActive(false);
        activeBall = false; // Update re-activates the ball when the next hoop is placed
    }


    /// <summary>
    /// Sets mass/drag for realistic weight and switches to Continuous Dynamic
    /// collision detection so a fast-moving ball can't tunnel through the thin
    /// rim collider between physics steps.
    /// </summary>
    private void ConfigureBallPhysics(Rigidbody rb)
    {
        rb.mass = ballMass;
        rb.linearDamping = ballDrag;
        rb.angularDamping = ballAngularDrag;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate; // smooths visual motion between physics steps, important in headset
    }

    /**
     * @brief checks if right stick is enough to charge basketball aim
     */
    private void CheckForChargeStart()
    {
        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
        if (stick.sqrMagnitude > (chargeDeadzone * chargeDeadzone))
        {
            state = ShotState.Aiming;
            trajectoryLine.enabled = true;
        }
    }

    private void HandleAiming()
    {
        // keeps the ball anchored to hand while aiming
        basketballInstance.transform.SetPositionAndRotation(rightController.position, rightController.rotation);

        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);

        // Letting the stick fall back to neutral WITHOUT firing cancels the aim entirely
        // and hides the trajectory line — without this there was no way back to InHand.
        if (stick.sqrMagnitude < (chargeDeadzone * chargeDeadzone))
        {
            CancelAiming();
            return;
        }

        // Stick Y (forward push) drives ONLY power now — direction comes straight from
        // wherever the controller is physically pointed, teleporter-style. Tilt/move your
        // hand to reshape the arc freely instead of it snapping toward the hoop.
        currentSpeed01 = Mathf.Clamp01(stick.y);

        Vector3 launchVelocity = ComputeLaunchVelocity();
        DrawTrajectoryPreview(rightController.position, launchVelocity);

        // Trigger press confirms the shot — separate from the stick, like a teleporter's confirm button
        if (OVRInput.GetDown(OVRInput.RawButton.RIndexTrigger))
        {
            FireShot(launchVelocity);
        }
    }

    private void CancelAiming()
    {
        state = ShotState.InHand;
        trajectoryLine.enabled = false;
    }

    private Vector3 ComputeLaunchVelocity()
    {
        // Direction comes straight from where the controller is physically pointed —
        // this is the free-aim behavior: no clamp, no auto-correction toward the hoop.
        Vector3 launchDir = rightController.forward.normalized;

        float speed = Mathf.Lerp(minLaunchSpeed, maxLaunchSpeed, currentSpeed01);
        return launchDir * speed;
    }

    private void DrawTrajectoryPreview(Vector3 origin, Vector3 initialVelocity)
    {
        // Only the first part of the arc is drawn; the rest is up to the player's judgment.
        int visiblePoints = Mathf.Clamp(Mathf.RoundToInt(trajectoryPoints * trajectoryRevealFraction), 2, trajectoryPoints);
        if (trajectoryLine.positionCount != visiblePoints) trajectoryLine.positionCount = visiblePoints;


        Vector3 gravity = Physics.gravity;
        for (int i = 0; i < trajectoryPoints; i++)
        {
            float t = i * trajectoryTimeStep;
            Vector3 point = origin + initialVelocity * t + 0.5f * gravity * t * t;
            trajectoryLine.SetPosition(i, point);
        }
    }

    private void FireShot(Vector3 launchVelocity)
    {
        // Unparent before enabling physics — otherwise the Rigidbody keeps inheriting
        // the moving controller's transform on top of its own physics motion, causing
        // erratic/compounded flight instead of a clean launch.
        basketballInstance.transform.SetParent(null);

        basketballContactTracker?.ResetContact();

        basketballRb.isKinematic = false;
        basketballRb.linearVelocity = launchVelocity;

        // Real shots carry backspin off the fingertips — affects how the ball deflects
        // on rim/backboard contact, not just how it looks mid-flight.
        Vector3 spinAxis = Vector3.Cross(Vector3.up, launchVelocity.normalized);
        basketballRb.angularVelocity = spinAxis * backspinAmount;

        trajectoryLine.enabled = false;
        flightTimer = 0f;
        state = ShotState.InFlight;
    }

    private void HandleFlightTimeout()
    {
        flightTimer += Time.deltaTime;

        bool fellOutOfPlay = basketballInstance.transform.position.y < rightController.position.y + outOfPlayHeight;
        bool tookTooLong = flightTimer > maxFlightTime;

        if (fellOutOfPlay || tookTooLong)
        {
            RespawnBall();
        }
    }

    private void RespawnBall()
    {
        Destroy(basketballInstance);

        basketballInstance = Instantiate(basketballPrefab, rightController.position, rightController.rotation, rightController);
        basketballRb = basketballInstance.GetComponent<Rigidbody>();
        basketballRb.isKinematic = true; // back to hand-controlled, no gravity until next shot
        ConfigureBallPhysics(basketballRb);
        basketballContactTracker = basketballInstance.GetComponent<BallContactTracker>();

        if (trajectoryLine != null) trajectoryLine.enabled = false;
        state = ShotState.InHand;
    }

    /// <summary>
    /// Call this from ScoreZone the moment the ball passes through the net.
    /// Ends the flight immediately instead of waiting for HandleFlightTimeout,
    /// and reuses the same respawn path as a miss.
    /// </summary>
    public void OnBallResult(bool madeShot)
    {
        if (state != ShotState.InFlight) return;
        RespawnBall();
    }

    private void OnDestroy()
    {
        if (ScoreManager.Instance != null)
        {
            ScoreManager.Instance.OnGameStarted -= HandleRoundStarted;
            ScoreManager.Instance.OnGameEnded -= HandleRoundEnded;
        }
    }
}