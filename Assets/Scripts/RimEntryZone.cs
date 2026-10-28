using UnityEngine;

/// <summary>
/// Thin trigger volume sitting just above the rim opening (inside the hoop prefab).
/// A shot only counts if the ball dropped down through this gate BEFORE reaching the ScoreZone
/// under the net, so a ball that falls short and drops past the front of the hoop can't score.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RimEntryZone : MonoBehaviour
{
    [SerializeField] private string ballTag = "Basketball";

    private Collider gateCollider;

    private void Awake()
    {
        gateCollider = GetComponent<Collider>();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(ballTag)) return;

        // Only a ball moving downward counts as "entering" the hoop from above.
        Rigidbody rb = other.attachedRigidbody;
        if (rb == null || rb.linearVelocity.y >= 0f) return;

        BallContactTracker tracker = rb.GetComponent<BallContactTracker>();
        if (tracker != null) tracker.SetPassedRimEntry(true);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(ballTag)) return;

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null) return;

        BallContactTracker tracker = rb.GetComponent<BallContactTracker>();
        if (tracker == null) return;

        // Leaving through the bottom of the gate while moving down = went through the hoop, keep the flag.
        // Leaving any other way (bounced back up, rolled off sideways) = not a clean entry, clear it.
        bool exitedThroughBottom = rb.linearVelocity.y < 0f && other.bounds.center.y < gateCollider.bounds.center.y;
        if (!exitedThroughBottom)
        {
            tracker.SetPassedRimEntry(false);
        }
    }
}