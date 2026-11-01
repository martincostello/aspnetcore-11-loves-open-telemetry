using System.Text.RegularExpressions;

namespace GrotsPizzeria.EndToEndTests;

public sealed class AccountTests(PizzeriaFixture fixture) : PizzeriaTest(fixture)
{
    [Fact]
    public async Task Can_Register_Then_Sign_Out_And_Back_In()
    {
        var username = $"e2e-{Guid.NewGuid():N}"[..16];
        const string Password = "Pizza!Passw0rd";

        await Page.GotoAsync("/register");
        await Page.GetByLabel("Username").FillAsync(username);
        await Page.GetByLabel("Display name").FillAsync("Ada Lovelace");
        await Page.GetByLabel("Password").FillAsync(Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        // Registering signs you in and takes you to the menu
        await Expect(Page).ToHaveURLAsync(new Regex("/menu$"));
        await Expect(Page.GetByTestId("user-chip")).ToContainTextAsync("Ada Lovelace");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
        await Expect(Page.GetByTestId("topbar").GetByRole(AriaRole.Link, new() { Name = "Sign in" })).ToBeVisibleAsync();

        await SignInAsync(username, Password);
        await Expect(Page.GetByTestId("user-chip")).ToContainTextAsync("Ada Lovelace");
    }

    [Fact]
    public async Task Wrong_Password_Is_Rejected()
    {
        await SignInAsync(DemoUsername, "not-the-password");

        await Expect(Page.GetByTestId("error-alert")).ToContainTextAsync("Invalid credentials");
        await Expect(Page).ToHaveURLAsync(new Regex("/login"));
    }

    [Fact]
    public async Task Weak_Passwords_Are_Rejected_By_Identity()
    {
        await Page.GotoAsync("/register");
        await Page.GetByLabel("Username").FillAsync($"e2e-{Guid.NewGuid():N}"[..16]);
        await Page.GetByLabel("Password").FillAsync("password");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create account" }).ClickAsync();

        await Expect(Page.GetByTestId("error-alert")).ToContainTextAsync("Registration failed");
    }

    private static IPageAssertions Expect(IPage page) => Assertions.Expect(page);

    private static ILocatorAssertions Expect(ILocator locator) => Assertions.Expect(locator);
}
