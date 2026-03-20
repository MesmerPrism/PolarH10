namespace PolarH10.Protocol;

public enum PolarHrvTrackingState
{
    Unavailable = 0,
    WaitingForRr = 1,
    WarmingUp = 2,
    Tracking = 3,
    Stale = 4,
}
