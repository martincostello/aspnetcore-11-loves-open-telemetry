using GrotsPizzeria.Shared;

namespace GrotsPizzeria.Client.Services;

public sealed record CartLine(MenuItem Item, int Quantity)
{
    public decimal Total => Item.CurrentPrice * Quantity;
}

/// <summary>The shopping cart. Lives for the lifetime of the browser tab.</summary>
public sealed class CartState
{
    private readonly List<CartLine> _lines = [];

    public event Action? Changed;

    public IReadOnlyList<CartLine> Lines => _lines;

    public int Count => _lines.Sum(l => l.Quantity);

    public decimal Subtotal => _lines.Sum(l => l.Total);

    public void Add(MenuItem item) => Set(item, (_lines.FirstOrDefault(l => l.Item.Id == item.Id)?.Quantity ?? 0) + 1);

    public void Set(MenuItem item, int quantity)
    {
        var index = _lines.FindIndex(l => l.Item.Id == item.Id);
        quantity = Math.Clamp(quantity, 0, 20);

        if (quantity == 0)
        {
            if (index >= 0)
            {
                _lines.RemoveAt(index);
            }
        }
        else if (index >= 0)
        {
            _lines[index] = new(item, quantity);
        }
        else
        {
            _lines.Add(new(item, quantity));
        }

        Changed?.Invoke();
    }

    /// <summary>Takes the prices from a fresh menu (the special offers change), keeping what is in the cart.</summary>
    public void Refresh(IEnumerable<MenuItem> menu)
    {
        var latest = menu.ToDictionary(i => i.Id);
        var changed = false;

        for (var i = 0; i < _lines.Count; i++)
        {
            if (latest.TryGetValue(_lines[i].Item.Id, out var item) && item != _lines[i].Item)
            {
                _lines[i] = _lines[i] with { Item = item };
                changed = true;
            }
        }

        if (changed)
        {
            Changed?.Invoke();
        }
    }

    public void Clear()
    {
        _lines.Clear();
        Changed?.Invoke();
    }
}
