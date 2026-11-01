using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Client.Services;

/// <summary>
/// The menu, loaded once and then kept up to date: the special offers change every 5 minutes, on the wall clock, so
/// when the current ones end the menu is fetched again (and the prices in the cart follow, as that is what will be charged).
/// </summary>
public sealed class MenuState(ApiClient api, CartState cart, TimeProvider time)
{
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _loaded = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _started;

    public event Action? Changed;

    public IReadOnlyList<MenuItem>? Items { get; private set; }

    public ProblemResponse? Problem { get; private set; }

    /// <summary>Completes when the menu has loaded for the first time (or failed to). It carries on refreshing in the background.</summary>
    public Task LoadAsync()
    {
        if (!_started)
        {
            _started = true;
            _ = KeepFreshAsync();
        }

        return _loaded.Task;
    }

    private async Task KeepFreshAsync()
    {
        while (true)
        {
            var result = await api.GetMenuAsync();

            if (result.Value is { } items)
            {
                Items = items;
                Problem = null;
                cart.Refresh(items);
            }
            else if (Items is null)
            {
                Problem = result.Problem;
            }

            _loaded.TrySetResult();
            Changed?.Invoke();

            // Try again soon if that failed; otherwise wait for the offers to end (and stop if there are none, as nothing will change)
            TimeSpan wait;

            if (result.Value is null)
            {
                wait = RetryAfter;
            }
            else if (Items!.Select(i => (DateTimeOffset?)i.Offer?.EndsAt).Min() is { } endsAt)
            {
                // A moment after the period ends, to let the service's clock tick over too
                wait = endsAt - time.GetUtcNow() + TimeSpan.FromMilliseconds(500);

                if (wait < TimeSpan.FromSeconds(1))
                {
                    wait = TimeSpan.FromSeconds(1);
                }
            }
            else
            {
                return;
            }

            await Task.Delay(wait, time);
        }
    }
}
