using UnityEngine;

/// <summary>
/// Tracks whether the ball has touched the
/// backboard/rim (tagged "HoopBoard") since it was last fired, so
/// ScoreZone can tell a clean swish apart from a bank/rim shot
/// can reject basketballs that never went through the hoop
/// </summary>

public class BallContactTracker : MonoBehaviour
{
    [SerializeField] private string boardTag = "HoopBoard";

    public bool HasTouchedBoard { get; private set; }
    public bool PassedRimEntry { get; private set; }
    public bool HasScored { get; private set; }

    /*
     * @brief when a new shot is fired, contact from the previous shot doesn't carry over
     */
    public void ResetContact()
    {
        HasTouchedBoard = false;
        PassedRimEntry = false;
        HasScored = false;
    }

    public void SetPassedRimEntry(bool value)
    {
        PassedRimEntry = value;
    }

    public void MarkScored()
    {
        HasScored = true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.CompareTag(boardTag))
        {
            HasTouchedBoard = true;
        }
    }
}
