#nullable enable
using VKR.Models;

namespace VKR.Services;

public static class UserSession
{
    public static AppUser? CurrentUser { get; private set; }

    public static void SignIn(AppUser user) => CurrentUser = user;

    public static void SignOut() => CurrentUser = null;

    public static int? CurrentUserId => CurrentUser?.IdUser;
}
