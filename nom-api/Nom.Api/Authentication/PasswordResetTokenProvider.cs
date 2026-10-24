using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Nom.Api.Authentication
{
    /// <summary>
    /// Lifespan of a password-reset code, kept separate from email-confirmation tokens so a
    /// reset link really does expire after a short time, as the reset email says.
    /// </summary>
    public class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
    {
        public PasswordResetTokenProviderOptions()
        {
            Name = "PasswordResetDataProtectorTokenProvider";
            TokenLifespan = TimeSpan.FromHours(1);
        }
    }

    /// <summary>
    /// Issues and validates password-reset codes with <see cref="PasswordResetTokenProviderOptions"/>.
    /// </summary>
    public class PasswordResetTokenProvider : DataProtectorTokenProvider<IdentityUser>
    {
        public const string ProviderName = "PasswordReset";

        public PasswordResetTokenProvider(
            IDataProtectionProvider dataProtectionProvider,
            IOptions<PasswordResetTokenProviderOptions> options,
            ILogger<DataProtectorTokenProvider<IdentityUser>> logger)
            : base(dataProtectionProvider, options, logger)
        {
        }
    }

    /// <summary>
    /// Registration for <see cref="PasswordResetTokenProvider"/>.
    /// </summary>
    public static class PasswordResetTokenProviderExtensions
    {
        /// <summary>
        /// Routes Identity's password-reset codes through <see cref="PasswordResetTokenProvider"/>.
        /// </summary>
        public static IdentityBuilder AddPasswordResetTokenProvider(this IdentityBuilder builder)
        {
            builder.Services.Configure<IdentityOptions>(o => o.Tokens.PasswordResetTokenProvider = PasswordResetTokenProvider.ProviderName);
            return builder.AddTokenProvider<PasswordResetTokenProvider>(PasswordResetTokenProvider.ProviderName);
        }
    }
}
