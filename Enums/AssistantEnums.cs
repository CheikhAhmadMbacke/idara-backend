namespace Idara.API.Enums
{
    /// <summary>Ce qu'une proposition de l'assistant fera une fois confirmée.</summary>
    public enum AssistantActionKind
    {
        AddStudent = 0,
        RecordPayment = 1,
        SendReminders = 2,
    }

    public enum AssistantActionStatus
    {
        Pending = 0,
        Done = 1,
        Cancelled = 2,
        Failed = 3,
        Expired = 4,
    }
}
