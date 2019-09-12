namespace Ruya.SignalR.Common
{
    public enum Command
    {
        Alert = '!',
        AddToGroup = '+',
        RemoveFromGroup = '-',
        SendDirectMessage = '@',
        SendGroupMessage = '#',
        BroadcastMessage = '*'
    }
}