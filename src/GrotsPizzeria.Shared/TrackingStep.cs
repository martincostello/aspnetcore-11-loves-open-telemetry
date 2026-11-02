namespace GrotsPizzeria.Shared;

public sealed record TrackingStep(string Status, string Label, bool Completed, bool Reached, DateTimeOffset At);
