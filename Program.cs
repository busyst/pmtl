using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
/*
Router router1 = null;
ConsoleMenu menu = new();
menu.AddMenuItem("Crate new router", () => router1 = new Router((ushort)((int)ConsoleMenu.RequestVariable("Enter port","int",true))));
menu.AddMenuItem("Start router", () => {if(router1!=null){router1.Start();}else{System.Console.WriteLine("Router not initialized");}});
menu.AddMenuItem("Ping", () => {if(router1!=null){router1.SendPing((uint)((int)ConsoleMenu.RequestVariable("Enter node id","int",true)),(ushort)((int)ConsoleMenu.RequestVariable("Enter node port","int",true)));}else{System.Console.WriteLine("Router not initialized");}});
menu.AddExitMenuItem("Close menu forever");
menu.Run();*/


Router router1 = new(1,1337);
Router router2 = new(2,1338);
router1.Start();
router2.Start();


Thread.Sleep(100);
for (int i = 0; i < 3; i++)
{
    router1.SendPing(2,1338);

}
class Router : IDisposable
{
    public Router(uint nodeID,ushort port){
        myNodeInfo = new NodeInfo(nodeID,port);
        logger = new Logger(myNodeInfo);
        mf = new MessageFilter(logger,myNodeInfo,MessageFilterType.Random);
        inputThread = new Thread(new ThreadStart(ReceiveInputLoop));
        outputThread = new Thread(new ThreadStart(RouterLoop));
    }
    private readonly NodeInfo myNodeInfo;
    private readonly Logger logger;
    private readonly MessageFilter mf;
    private readonly ConcurrentQueue<byte[]> messagesToSend = [];
    public ConcurrentDictionary<ulong,(DateTime dateStamp,Action<byte[],object> Response,Action<object> NoResponse,object arg)> requests = [];
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private IPAddress ip = IPAddress.None;
    private ushort Port => myNodeInfo.Port;
    private readonly Thread inputThread,outputThread;


    private void AddMessageToSend(byte[] bytes)
    {
        if(bytes.Length<RouterHeader.HeaderSize)
            return;
        messagesToSend.Enqueue(bytes);
    }
    public void SendPing(uint destinationNode, ushort destinationPort)
    {
        // Prepare the message
        const int totalSize = RouterHeader.HeaderSize + IMPH.HeaderSize;
        byte[] message = new byte[totalSize];

        RouterHeader header = new(message.AsSpan(0, RouterHeader.HeaderSize))
        {
            Version = 1,
            Ttl = 63,
            DestinationID = destinationNode,
            DestinationPort = destinationPort,
            SourceID = myNodeInfo.ID,
            SourcePort = myNodeInfo.Port,
            TotalLength = (ushort)totalSize,
            MessageID = (ushort)new Random().Next(0, ushort.MaxValue)
        };
        header.Hash = header.CalculateHash();

        IMPH imph = new(message.AsSpan(RouterHeader.HeaderSize));
        imph.Make("PING");
        imph.Date = DateTime.UtcNow.Ticks;
        imph.State = IMPHState.Request;

        // Generate a unique key
        ulong key = GetKey(header.DestinationID,header.DestinationPort,header.MessageID);
        // Track the request
        if (!requests.TryAdd(key, (DateTime.Now, PingResolver, _ => { }, imph.Date)))
        {
            logger.LogError($"Error adding key: {key}\nAll existing keys:\n\t{string.Join("\n\t", requests.Keys)}");
        }
        else
        {
            AddMessageToSend(message);
        }
    }
    private void PingResolver(byte[] data,object obj)
    {
        if(obj==null)
            throw new Exception("Something wrong happened");

        var startTick = (long)obj;
        var theirTick = new IMPH(data.AsSpan(RouterHeader.HeaderSize)).Date;
        var endTick = DateTime.UtcNow.Ticks;
        //var routerHeader = new RouterHeader(data.AsSpan(0, RouterHeader.HeaderSize));
        //return (new DateTime((long)obj),new DateTime(imph.Date),stamp);
        Console.WriteLine($"Ping{{\nTo them:{new TimeSpan(theirTick-startTick).TotalMilliseconds}ms\n"+
                        $"From them:{new TimeSpan(endTick-theirTick).TotalMilliseconds}ms\n"+
                        $"Complete{new TimeSpan(endTick-startTick).TotalMilliseconds}ms\n}}");
    }
    public void Start()
    {
        ip = Dns.GetHostEntry(Dns.GetHostName()).AddressList.First((x) => x.AddressFamily == AddressFamily.InterNetwork);
        try
        {
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
            _socket.Bind(new IPEndPoint(ip, (int)myNodeInfo.Port));


            inputThread.Start();
            outputThread.Start();
            logger.Log($"Started");
        }
        catch (Exception e){
            logger.LogError($"Failed to start router: {e}");

            // Cleanup resources
            _socket.Close();
        }
    }
    public static ulong GetKey(uint ID, ushort Port, ushort MessageID) => ((ulong)ID << 32) | ((ulong)Port << 16) | (ulong)MessageID;
    public static (uint ID, ushort Port, ushort MessageID) GetKeyParts(ulong key) => ((uint)(key >> 32), (ushort)((key >> 16) & 0xFFFF), (ushort)(key & 0xFFFF));
    private void ReceiveInputLoop()
    {
        byte[] buffer = new byte[short.MaxValue];
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        logger.Log($"Receive input loop started");
        while (true)
        {
            int bytesReceived = _socket.ReceiveFrom(buffer,ref remoteEndPoint);
            if(!mf.Filter(remoteEndPoint,buffer.AsSpan(0,bytesReceived))){
                continue;
            }

            byte[] receivedBytes = buffer.AsSpan(0,bytesReceived).ToArray();
            RouterHeader message = new(receivedBytes);
            if(bytesReceived==RouterHeader.HeaderSize+IMPH.HeaderSize)
            {
                IMPH ipmh = new(receivedBytes.AsSpan(RouterHeader.HeaderSize));
                if(ipmh.IsIMPH){
                    var type = Encoding.ASCII.GetString(ipmh.MessageType);
                    if(ipmh.State == IMPHState.Response){
                        ulong key = GetKey(message.SourceID,message.SourcePort,message.MessageID);
                        if(requests.TryRemove(key,out var act))
                            act.Response.Invoke(receivedBytes,act.arg);
                    }
                    else{
                        switch (type)
                        {
                            case "PING":
                                message.ReverseAddress();
                                message.Hash = message.CalculateHash();
                                ipmh.Date = DateTime.UtcNow.Ticks;
                                ipmh.State = IMPHState.Response;
                                AddMessageToSend(receivedBytes);
                                break;
                        }
                    }
                }
            }

        }
    }
    private void RouterLoop()
    {
        logger.Log($"Router loop started");
        try
        {
            while (true)
            {
                while(messagesToSend.TryDequeue(out var message)){
                    SendMessage(message);
                    Thread.Sleep(1);
                }
                Thread.Sleep(1);
                foreach (var x in requests)
                {
                    if(new TimeSpan(DateTime.Now.Ticks-x.Value.dateStamp.Ticks).TotalSeconds>3.0f&&requests.Remove(x.Key,out var val))
                    {
                        var k = GetKeyParts(x.Key);
                        logger.Log($"No response to from [{k.ID}:{k.Port}] about [{k.MessageID}]");
                        x.Value.NoResponse.Invoke(x.Value.arg);
                    }
                }
                Thread.Sleep(1);
            }
        }
        catch (Exception e){
            logger.LogError($"Unit stopping due to error:\n{e}");
        }
        finally
        {
            // Cleanup resources
            _socket.Close();
            logger.Log($"Unit offline");
        }
    }
    private void SendMessage(byte[] bytes)
    {
        RouterHeader message = new(bytes);
        var _ip = new IPEndPoint(ip,message.DestinationPort);
        int bs = _socket.SendTo(bytes,0,bytes.Length,SocketFlags.None,_ip);
        if(bs!=bytes.Length){
            logger.LogError($"Error in sending; Sent {bs} bytes instead of {bytes.Length}");
            return;
        }
        logger.Log("--->("+message.ToString()+")"+" to:"+_ip);
    }

    #region Dispose
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    protected virtual void Dispose(bool disposing)
    {
        if (disposing){

        }
        mf.Dispose();
        _socket.Dispose();
        inputThread.Join();
        outputThread.Join();
    }
    ~Router()
    {
        Dispose(false);
    }
    #endregion
}

class Logger(NodeInfo myNode)
{
    public bool Verbose = true;
    private readonly IPAddress ip = Dns.GetHostEntry(Dns.GetHostName()).AddressList.First((x) => x.AddressFamily == AddressFamily.InterNetwork);
    protected string localTime = DateTime.Now.ToString("HH:mm:ss");
    public NodeInfo myNode = myNode;

    public void Log(string val)
    {
        if(Verbose)
            System.Console.WriteLine($"[{ip}:{myNode.Port}][{localTime}] "+val);
    }
    public void LogError(string val)
    {
        System.Console.WriteLine($"[ERROR][{ip}:{myNode.Port}][{localTime}]"+val);
    }
}