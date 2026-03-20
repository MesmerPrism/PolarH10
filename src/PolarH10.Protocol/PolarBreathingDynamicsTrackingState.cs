namespace PolarH10.Protocol;

public enum PolarBreathingDynamicsTrackingState
{
    Unavailable = 0,
    WaitingForCalibration = 1,
    WaitingForBreathingTracking = 2,
    Tracking = 3,
    Stale = 4,
}
