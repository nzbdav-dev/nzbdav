namespace NzbWebDAV.Websocket;

public class WebsocketTopic
{
    // Stateful topics
    public static readonly WebsocketTopic UsenetConnections = new("cxs", TopicType.State);
    public static readonly WebsocketTopic SymlinkTaskProgress = new("stp", TopicType.State);
    public static readonly WebsocketTopic CleanupTaskProgress = new("ctp", TopicType.State);
    public static readonly WebsocketTopic QueueItemStatus = new("qs", TopicType.State);
    public static readonly WebsocketTopic QueueItemProgress = new("qp", TopicType.State);
    public static readonly WebsocketTopic HealthItemStatus = new("hs", TopicType.State);
    public static readonly WebsocketTopic HealthItemProgress = new("hp", TopicType.State);

    // Eventful topics
    public static readonly WebsocketTopic QueueItemAdded = new("qa", TopicType.Event);
    public static readonly WebsocketTopic QueueItemRemoved = new("qr", TopicType.Event);
    public static readonly WebsocketTopic HistoryItemAdded = new("ha", TopicType.Event);
    public static readonly WebsocketTopic HistoryItemRemoved = new("hr", TopicType.Event);

    public readonly string Name;
    public readonly TopicType Type;

    private WebsocketTopic(string name, TopicType type)
    {
        Name = name;
        Type = type;
    }

    public enum TopicType
    {
        State,
        Event
    }
}