namespace TickZoom.Api
{
    public enum SimulatorType
    {
        ReceiveDisconnect,
        SendDisconnect,
        SendServerOffline,
        ReceiveServerOffline,
        ServerOfflineReject,
        BlackHole,
        CancelBlackHole,
        SystemOffline,
        RejectSymbol,
        RejectAll,
    }
}