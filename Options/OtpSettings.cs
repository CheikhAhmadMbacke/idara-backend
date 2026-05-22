namespace Idara.API.Options
{
    public class OtpSettings
    {
        public const string SectionName = "Otp";

        /// <summary>Durée de validité en minutes d'un OTP (par défaut : 10 min).</summary>
        public int ExpirationMinutes { get; set; } = 10;
    }
}
