using System;

namespace TickZoom.Api
{
    public interface TriggerController
    {
        long AddTrigger(long reference, TriggerData data, TriggerOperation operation, TimeStamp time, Action<long> callback);
        long AddTrigger(long reference, TriggerData data, TriggerOperation operation, double price, Action<long> callback);
        long AddTrigger(long reference, TriggerData data, TriggerOperation operation, long value, Action<long> callback);
        bool RemoveTrigger(long triggerId);
    }
}