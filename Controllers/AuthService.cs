namespace ComparadorPrecos.Controllers;

public sealed class AuthService
{
    private const string AdminEmail    = "admin@admin.com";
    private const string AdminPassword = "password";

    public bool ValidateCredentials(string email, string password)
        => email == AdminEmail && password == AdminPassword;
}
