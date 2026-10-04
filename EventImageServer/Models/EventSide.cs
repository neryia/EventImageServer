namespace EventImageServer.Models
{
    // Which side of the couple a guest (or a table) belongs to. Serialized as
    // "both" / "bride" / "groom". Bride and groom guests are never seated at
    // the same table by Smart Arrange; "Both" can sit anywhere.
    public enum EventSide
    {
        Both = 0,
        Bride = 1,
        Groom = 2
    }
}
