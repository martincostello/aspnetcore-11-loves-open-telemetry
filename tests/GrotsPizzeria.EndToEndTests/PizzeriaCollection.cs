namespace GrotsPizzeria.EndToEndTests;

[CollectionDefinition(Name)]
public sealed class PizzeriaCollection : ICollectionFixture<PizzeriaFixture>
{
    public const string Name = "Grot's Pizzeria";
}
