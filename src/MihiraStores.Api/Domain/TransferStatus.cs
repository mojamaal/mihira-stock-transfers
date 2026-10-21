namespace MihiraStores.Api.Domain;

public enum TransferStatus
{
    Requested,
    Dispatched,
    Received,
    Cancelled,
}

public enum TransferAction
{
    Dispatch,
    Receive,
    Cancel,
}

/// <summary>
/// The single source of truth for the transfer lifecycle:
/// Requested → Dispatched → Received, or Requested → Cancelled. Nothing else is allowed.
/// </summary>
public static class TransferWorkflow
{
    private static readonly Dictionary<TransferAction, (TransferStatus From, TransferStatus To)> Transitions = new()
    {
        [TransferAction.Dispatch] = (TransferStatus.Requested, TransferStatus.Dispatched),
        [TransferAction.Receive] = (TransferStatus.Dispatched, TransferStatus.Received),
        [TransferAction.Cancel] = (TransferStatus.Requested, TransferStatus.Cancelled),
    };

    public static TransferStatus RequiredStatus(TransferAction action) => Transitions[action].From;

    public static TransferStatus ResultingStatus(TransferAction action) => Transitions[action].To;

    public static IReadOnlyList<TransferAction> AllowedActions(TransferStatus status) =>
        Transitions.Where(t => t.Value.From == status).Select(t => t.Key).ToList();

    public static string RejectionMessage(int transferId, TransferAction action, TransferStatus current)
    {
        var (verb, pastTense) = action switch
        {
            TransferAction.Dispatch => ("dispatch", "dispatched"),
            TransferAction.Receive => ("receive", "received"),
            TransferAction.Cancel => ("cancel", "cancelled"),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };

        return $"Cannot {verb} transfer #{transferId}: it is {current}. " +
               $"Only {RequiredStatus(action)} transfers can be {pastTense}.";
    }
}
