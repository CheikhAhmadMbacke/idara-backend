namespace Idara.API.Enums
{
    /// <summary>
    /// Opérateur Mobile Money utilisé pour un Payment ou un Withdrawal.
    /// MVP Sénégal : Wave + Orange Money uniquement. Free Money / Expresso
    /// volontairement exclus.
    /// </summary>
    public enum PaymentOperator
    {
        Wave = 0,
        Orange = 1
    }
}
