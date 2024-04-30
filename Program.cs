using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

Router router1 = null;
ConsoleMenu menu = new();
menu.AddMenuItem("Crate new router", () => router1 = new Router((ushort)((int)ConsoleMenu.RequestVariable("Enter port","int",true))));
menu.AddMenuItem("Start router", () => {if(router1!=null){router1.Start();}else{System.Console.WriteLine("Router not initialized");}});
menu.AddMenuItem("Ping", () => {if(router1!=null){router1.SendPing((uint)((int)ConsoleMenu.RequestVariable("Enter node id","int",true)),(ushort)((int)ConsoleMenu.RequestVariable("Enter node port","int",true)));}else{System.Console.WriteLine("Router not initialized");}});
menu.AddExitMenuItem("Close menu forever");
menu.Run();

/*
Router router1 = new Router(1337);
Router router2 = new Router(1338);
router1.Start();
router2.Start();


Thread.Sleep(100);
System.Console.WriteLine("-|-|-|-|-|-|-|-|-|-|--|-|--|--|-||--||--||--|-|");
router1.SendPing(0,1338);
router2.SendPing(0,1337);
*/
class Router
{
    protected ushort port;
    public Router(ushort port){
        receiveThread = new Thread(new ThreadStart(RecivingInputLoop));
        operationThread = new Thread(new ThreadStart(RouterLoop));
        this.port = port;
    }
    private readonly ConcurrentQueue<byte[]> messagesToSend = [];
    private readonly Socket _socket = new(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    private IPAddress ip = IPAddress.None;
    private volatile bool Running = false;
    private volatile bool softStop = false;

    private volatile bool RECLOOPSTARTED = false,OPERLOOPSTARTD = false;
    public static IPAddress GetLocalIPAddress() => Dns.GetHostEntry(Dns.GetHostName()).AddressList.First((x) => x.AddressFamily == AddressFamily.InterNetwork);

    protected string localTime = DateTime.Now.ToString("HH:mm:ss");
    private readonly Thread receiveThread,operationThread;
    public void AddMessageToSend(byte[] bytes)
    {
        if(bytes.Length<RouterHeader.HeaderSize)
            return;
        messagesToSend.Enqueue(bytes);
    }
    public ConcurrentDictionary<ulong,(Action<byte[],object?>,object?)> requests = [];
    public void SendPing(uint destinationNode,ushort destinationPort)
    {
        byte[] msg = new byte[RouterHeader.HeaderSize+IMPH.HeaderSize];
        var msgh = new RouterHeader(msg.AsSpan(0, RouterHeader.HeaderSize))
        {
            Version = 1,
            Ttl = 63,
            DestinationID = destinationNode,
            DestinationPort = destinationPort,
            SourcePort = port,
            TotalLength = (ushort)msg.Length,
            MessageID = (ushort)new Random().Next(0, ushort.MaxValue)
        };
        msgh.Hash = msgh.CalculateHash();
        IMPH imph = new(msg.AsSpan(RouterHeader.HeaderSize));
        imph.Make("PING");
        imph.Date = DateTime.UtcNow.Ticks;
        imph.Request = true;

        ulong key = ((ulong)msgh.DestinationID<<32)|((ulong)msgh.DestinationPort<<16)|(ulong)msgh.MessageID;
        if(!requests.TryAdd(key,(PingResolver,imph.Date))){
            System.Console.WriteLine("Error key:"+key+"\nAll keys:\n\t"+string.Join("\n\t",requests.Keys));
        }
        else
            AddMessageToSend(msg);
    }
    private void PingResolver(byte[] data,object? obj)
    {
        if(obj==null)
            throw new Exception("Something wrong happend");
        
        //var msgh = new RouterHeader(data.AsSpan(0, RouterHeader.HeaderSize));
        var imph = new IMPH(data.AsSpan(RouterHeader.HeaderSize));
        
        System.Console.WriteLine($"Ping\nTo them:{new TimeSpan(imph.Date-((long)obj)).TotalMilliseconds}ms\nTo them:{new TimeSpan(DateTime.UtcNow.Ticks-imph.Date).TotalMilliseconds}ms\nComplete{new TimeSpan(DateTime.UtcNow.Ticks-((long)obj)).TotalMilliseconds}ms");
    }
    public void Start()
    {
        if(OPERLOOPSTARTD||RECLOOPSTARTED){
            System.Console.WriteLine("Currently running");
            return;
        }
        ip = GetLocalIPAddress();
        try
        {
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.ReuseAddress, true);
            _socket.Bind(new IPEndPoint(ip, (int)port));

            Running = true;


            receiveThread.Start();
            operationThread.Start();
            do
            {
                Thread.Sleep(1);
            } while(!(OPERLOOPSTARTD&&RECLOOPSTARTED));
            System.Console.WriteLine($"[{ip}:{port}] Started");
        }
        catch (Exception e){
            Console.WriteLine($"Failed to start router: {e}");

            // Cleanup resources
            _socket.Close();
            Running = false;
        }
    }
    private bool PrimaryFilter(byte[] bytes)
    {
        RouterHeader message = new(bytes);
        if(message.DestinationPort!=port){
            System.Console.WriteLine("Wrong port");
            return false;
        }
        if(message.TotalLength!=bytes.Length){
            System.Console.WriteLine("Incomplete message");
            return false;
        }
        if(message.Hash!=message.CalculateHash()){
            System.Console.WriteLine("Wrong hash");
            return false;
        }

        System.Console.WriteLine($"[{ip}:{port}][{localTime}]"+"<---("+message.ToString()+")");
        return true;
    }
    private void RecivingInputLoop()
    {
        byte[] buffer = new byte[short.MaxValue];
        RECLOOPSTARTED = true;
        EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        System.Console.WriteLine($"[{ip}:{port}] Reciving input started");
        while (!softStop)
        {
            int bytesReceived = _socket.Receive(buffer);
            byte[] receivedBytes = buffer.AsSpan(0,bytesReceived).ToArray();
            RouterHeader message = new(receivedBytes);
            if(!PrimaryFilter(receivedBytes)){
                System.Console.WriteLine($"[{ip}:{port}] Bad message");
                continue;
            }
            

            if(bytesReceived>=RouterHeader.HeaderSize+IMPH.HeaderSize)
            {
                IMPH ipmh = new(receivedBytes.AsSpan(RouterHeader.HeaderSize));
                if(ipmh.IsIMPH){
                    var type = Encoding.ASCII.GetString(ipmh.Type);
                    if(!ipmh.Request){
                        ulong key = (message.SourceID<<32)|((ulong)message.SourcePort<<16)|(ulong)message.MessageID;
                        if(requests.TryRemove(key,out var act))
                            act.Item1.Invoke(receivedBytes,act.Item2);
                    }
                    else
                    switch (type)
                    {
                        case "PING":
                            message.ReverseAddress();
                            message.Hash = message.CalculateHash();
                            ipmh.Date = DateTime.UtcNow.Ticks;
                            ipmh.Request = false;
                            AddMessageToSend(receivedBytes);
                            break;
                    }
                }
            }





        }
    }
    private void RouterLoop()
    {
        System.Console.WriteLine($"[{ip}:{port}] Router loop started");
        OPERLOOPSTARTD = true;
        try
        {
            while (!softStop)
            {
                while(messagesToSend.TryDequeue(out var message)){
                    SendMessage(message);
                    Thread.Sleep(1);
                }
                Thread.Sleep(1);
            }
        }
        catch (Exception e){
            Console.WriteLine($"[{ip}:{port}][{localTime}] Unit stopping due to error:\n{e}");
        }
        finally
        {
            // Cleanup resources
            _socket.Close();
            Running = false;
            Console.WriteLine($"[{ip}:{port}][{localTime}] Unit offline ");
        }
    }
    private void SendMessage(byte[] bytes)
    {

        RouterHeader message = new(bytes);
        var _ip = new IPEndPoint(ip,message.DestinationPort);
        int bs = _socket.SendTo(bytes,0,bytes.Length,SocketFlags.None,_ip);
        if(bs!=bytes.Length){
            System.Console.WriteLine($"Error in sending; Sent {bs} bytes insted of {bytes.Length}");
            return;
        }
        System.Console.WriteLine($"[{ip}:{port}][{localTime}]"+"--->("+message.ToString()+")"+" to:"+_ip);
    }

    public void SoftStop() => softStop = Running;
    public void HardStop(){
        try
        {
            softStop = true;
            _socket.Dispose();
        }
        catch (System.Exception e)
        {
            System.Console.WriteLine(e.Message);
        }
        finally{
            System.Console.WriteLine("Hard stop completed");
        }
    }
}