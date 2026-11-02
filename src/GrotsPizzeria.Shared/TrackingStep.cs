using System.ComponentModel;

namespace GrotsPizzeria.Shared;

[Description("One step of a delivery.")]
public sealed record TrackingStep(
    [property: Description("The step: the same values as the delivery's status.")] string Status,
    [property: Description("What to call the step.")] string Label,
    [property: Description("The delivery has finished this step.")] bool Completed,
    [property: Description("The delivery is at, or past, this step.")] bool Reached,
    [property: Description("When the step was reached, or is expected to be.")] DateTimeOffset At);
