using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;

namespace OptiPowerTools.ScheduledJobsInsights.E2E;

/// <summary>
/// Users beyond the administrator the fixture registers through the UI. Written straight into the
/// stack's ASP.NET Identity tables: the CMS has no other way to create a user without an admin UI that
/// differs between CMS 12 and 13. The password hash comes from Identity's own hasher, so the site's
/// normal login accepts it.
/// </summary>
internal static class Users
{
    /// <summary>
    /// The editor role. The hosts map WebEditors to CmsEditors, which opens the CMS's editing UI but is
    /// not among the roles the insights pages authorize.
    /// </summary>
    private const string EditorRole = "WebEditors";

    public static async Task EnsureEditorAsync(string connectionString)
    {
        var hash = new PasswordHasher<object>().HashPassword(new object(), E2ETarget.AdminPassword);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE Name = @role)
                INSERT AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
                VALUES (CONVERT(nvarchar(450), NEWID()), @role, UPPER(@role), CONVERT(nvarchar(450), NEWID()));

            IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE UserName = @user)
                INSERT AspNetUsers (Id, IsApproved, IsLockedOut, CreationDate, UserName, NormalizedUserName,
                                    Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp,
                                    ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled,
                                    AccessFailedCount)
                VALUES (CONVERT(nvarchar(450), NEWID()), 1, 0, SYSUTCDATETIME(), @user, UPPER(@user),
                        @email, UPPER(@email), 1, @hash, CONVERT(nvarchar(450), NEWID()),
                        CONVERT(nvarchar(450), NEWID()), 0, 0, 0, 0);

            IF NOT EXISTS (SELECT 1 FROM AspNetUserRoles ur JOIN AspNetUsers u ON u.Id = ur.UserId
                           JOIN AspNetRoles r ON r.Id = ur.RoleId WHERE u.UserName = @user AND r.Name = @role)
                INSERT AspNetUserRoles (UserId, RoleId)
                SELECT u.Id, r.Id FROM AspNetUsers u, AspNetRoles r WHERE u.UserName = @user AND r.Name = @role;
            """, connection);
        command.Parameters.AddWithValue("@role", EditorRole);
        command.Parameters.AddWithValue("@user", E2ETarget.EditorUser);
        command.Parameters.AddWithValue("@email", "editor@example.test");
        command.Parameters.AddWithValue("@hash", hash);
        await command.ExecuteNonQueryAsync();
    }
}
