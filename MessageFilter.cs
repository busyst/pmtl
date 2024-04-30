using System.Net;
using System.Net.Sockets;
enum MessageFilterType
{
    Whitelist,
    Blacklist,
    Random,
}
class MessageFilter(Logger logger,NodeInfo myNode, MessageFilterType filterType) : IDisposable
{
    public NodeInfo myNode = myNode;
    public MessageFilterType filterType = filterType;

    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
    private readonly HashSet<EndPoint> _hashSet = [];
    public bool AddToList(EndPoint item){
        _lock.EnterWriteLock();
        try
        {
            return _hashSet.Add(item);
        }
        finally
        {
            if (_lock.IsWriteLockHeld) _lock.ExitWriteLock();
        }
    }
    public bool Contains(EndPoint item)
    {
        _lock.EnterReadLock();
        try
        {
            return _hashSet.Contains(item);
        }
        finally
        {
            if (_lock.IsReadLockHeld) _lock.ExitReadLock();
        }
    }
    public bool Remove(EndPoint item)
    {
        _lock.EnterWriteLock();
        try
        {
            return _hashSet.Remove(item);
        }
        finally
        {
            if (_lock.IsWriteLockHeld) _lock.ExitWriteLock();
        }
    }
    public bool Filter(EndPoint _ip,Span<byte> bytes)
    {
        var cont = Contains(_ip);
        if(bytes.Length<RouterHeader.HeaderSize){
            logger.Log("Incomplete message");
            return false;
        }
    
        if((filterType == MessageFilterType.Whitelist&&!cont)||
            (filterType == MessageFilterType.Blacklist&&cont) ||
            (filterType == MessageFilterType.Random&&Random.Shared.Next()%2==0)){
            logger.Log(filterType==MessageFilterType.Whitelist?"Not whitelisted ip":filterType == MessageFilterType.Blacklist?"Blacklisted ip":"Unlucky)))");
            return false;
        }

        RouterHeader message = new(bytes);
        if(message.DestinationPort!=myNode.Port){
            logger.Log("Wrong port");
            return false;
        }
        if(message.DestinationID!= myNode.ID){
            logger.Log("Wrong node");
            return false;
        }
        if(message.TotalLength!=bytes.Length){
            logger.Log("Incomplete message");
            return false;
        }
        if(message.Hash!=message.CalculateHash()){
            logger.Log("Message changed without changing hash");
            return false;
        }

        logger.Log("<---("+message.ToString()+")"+" fr:"+_ip);
        return true;
    }
    #region Dispose
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            _lock?.Dispose();
        _hashSet.Clear();
    }
    ~MessageFilter()
    {
        Dispose(false);
    }
    #endregion
}
