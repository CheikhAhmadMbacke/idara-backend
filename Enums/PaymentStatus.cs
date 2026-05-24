namespace Idara.API.Enums
{
    /// <summary>
    /// État d'un Payment côté Idara. Pending = créé en DB, en attente du webhook
    /// SenePay. Transitions terminales (Completed/Failed/Cancelled/Expired) ne
    /// reviennent jamais en arrière.
    /// </summary>
    public enum PaymentStatus
    {
        Pending = 0,
        Completed = 1,
        Failed = 2,
        Cancelled = 3,
        Expired = 4
    }
}
