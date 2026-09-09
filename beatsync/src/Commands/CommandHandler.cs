namespace beatsync;

public class CommandHandler
{
    private readonly Queue<Command> _commandQueue = new();

    public void AddCommand(Command command)
    {
        _commandQueue.Enqueue(command);
    }

    public void ProcessCommands()
    {
        while (_commandQueue.TryDequeue(out Command command))
        {
            switch (command.Type)
            {
                case CommandType.MountLibrary:
                    HandleMountLibrary(command);
                    break;
                case CommandType.MountSyncTarget:
                    HandleMountSyncTarget(command);
                    break;
                case CommandType.SyncLibrary:
                    HandleSyncLibrary(command);
                    break;
            }
        }
    }

    private void HandleMountLibrary(Command command)
    {
        
    }
    
    private void HandleMountSyncTarget(Command command)
    {
        
    }
    
    private void HandleSyncLibrary(Command command)
    {
        
    }
}