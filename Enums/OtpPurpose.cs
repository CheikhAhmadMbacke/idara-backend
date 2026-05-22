namespace Idara.API.Enums
{
    /// <summary>
    /// Indique pour quelle action un OTP a été émis. Évite qu'un OTP de
    /// réinitialisation puisse être consommé pour une inscription (ou inversement).
    /// </summary>
    public enum OtpPurpose
    {
        Register = 0,
        ResetPassword = 1
    }
}
