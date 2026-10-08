namespace Idara.API.Enums
{
    /// <summary>Ce qu'une proposition de l'assistant fera une fois confirmée.</summary>
    public enum AssistantActionKind
    {
        AddStudent = 0,
        RecordPayment = 1,
        SendReminders = 2,
        // 2026-10-08 — l'assistant remplace le support (§304).
        ResetAccessCode = 3,
        UpdateStudent = 4,
        StudentExit = 5,
        CreateClass = 6,
        RecordAttendance = 7,
        CoranEntry = 8,
        ContactSupport = 9,
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
