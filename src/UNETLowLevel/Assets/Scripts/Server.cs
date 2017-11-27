using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

class ServerClient
{
    public int connectionId;
    public string playerName;
    public Vector3 position;
}

public class Server : MonoBehaviour {

    #region Constants
    private const int MAX_CONNECTIONS = 100;
    #endregion

    #region Global Variables
    private int port = 5701;
    private int hostId;
    private int webHostId;
    private int reliableChannel;                        //This channel forces the information to be sent 100%
    private int unReliableChannel;                      //This channel is used if the sent information is okay to be lost sometimes (like position)
    private bool isStarted = false;
    private byte error;
    private float lastMovementUpdate;
    private float movementUpdateRate = 1/20f;           //Every 1/20 seconds ask every player where are they positioned, this can control 'lag'. If number is too large then too much lag, if its too low then is overkill
    #endregion

    #region Lists
    private List<ServerClient> clients = new List<ServerClient>();
    #endregion

    private void Start()
    {
        //Initialize the network
        NetworkTransport.Init();

        //Create a connection configuration
        ConnectionConfig cc = new ConnectionConfig();

        //Declare what type of channels to use for the communications
        reliableChannel = cc.AddChannel(QosType.Reliable);
        unReliableChannel = cc.AddChannel(QosType.Unreliable);

        //Declare a host topology
        HostTopology topo = new HostTopology(cc, MAX_CONNECTIONS);

        //Create hosts ids
        hostId = NetworkTransport.AddHost(topo, port, null);
        webHostId = NetworkTransport.AddWebsocketHost(topo, port, null);

        //Start the server
        isStarted = true;
    }

    private void Update()
    {
        //If is not started then dont do anything just exit the function
        if (!isStarted)
            return;

        //Start listening
        int recHostId;
        int connectionId;
        int channelId;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error;

        NetworkEventType recData = NetworkTransport.Receive(out recHostId, out connectionId, 
            out channelId, recBuffer, bufferSize, out dataSize, out error);

        //Check what message did we receive and based on it take an action
        switch (recData)
        {
            case NetworkEventType.DataEvent:
                //Recieved a message of type DataEvent, this where everything happens, we have to parse the messages and take action based on it

                //Encode the message
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                Debug.Log("Receiving from " + connectionId + ": " + msg);

                //Lets decode the message
                //Split the message by the special character, this way we know the types of messages we are receiving
                string[] splitData = msg.Split('|');

                //Check what type of message are we recieving
                switch (splitData[0])
                {
                    case "NAMEIS":
                        OnNameIs(connectionId, splitData[1]);
                        break;
                    case "MYPOSITION":
                        OnMyPosition(connectionId, float.Parse(splitData[1]), float.Parse(splitData[2]), float.Parse(splitData[3]));
                        break;
                    default:
                        Debug.Log("Invalid message: " + msg);
                        break;
                }

                break;
            case NetworkEventType.ConnectEvent:
                //Recieved a message of ConnectEvent which means someone has connected
                Debug.Log("Player " + connectionId + " has connected");
                OnConnection(connectionId);
                break;
            case NetworkEventType.DisconnectEvent:
                //Recieved a message of DisconnectEvent, which tells us who was disconnected
                Debug.Log("Player " + connectionId + " has disconnected");
                OnDisconnection(connectionId);
                break;
        }

        //Ask players for their current position
        if(Time.time - lastMovementUpdate > movementUpdateRate)
        {
            lastMovementUpdate = Time.time;

            //Send ask position packet to everyone on the unreliable channel
            string positionPacket = "ASKPOSITION|";

            foreach (var client in clients)
            {
                //string p = client.connectionId + '%' + client.position.x.ToString() + '%' + client.position.y.ToString();
                string p = string.Format("{0}%{1}%{2}%{3}|", client.connectionId.ToString(), client.position.x.ToString(), client.position.y.ToString(), client.position.z.ToString());

                //Parse the packet
                positionPacket += p;
            }

            //Format by removing the very last character
            positionPacket = positionPacket.Trim('|');

            //Send the packet
            Send(positionPacket, unReliableChannel, clients);

        }
    }

    #region Actions
    private void OnMyPosition(int cnnId, float x, float y, float z)
    {
        clients.Find(c => c.connectionId == cnnId).position = new Vector3(x, y, z);
    }
    private void OnNameIs(int cnnId, string playerName)
    {
        //Link the name to the connection id
        clients.Find(x=>x.connectionId==cnnId).playerName= playerName;

        //Tell everybody that a new player has connected
        Send("CNN|" + playerName + '|' + cnnId,reliableChannel,clients);
    }
    private void OnConnection(int cnnId)
    {
        //Add the player to a list
        ServerClient c = new ServerClient();
        c.connectionId = cnnId;
        c.playerName = "TEMP";
        clients.Add(c);

        //When the player joins the server, tell him his ID
        //Request his name and send the name of all the other players
        string msg = "ASKNAME|" + cnnId + "|";
        foreach (var player in clients)
            msg += player.playerName + '%' + player.connectionId + "|";

        //Clean the message string
        msg = msg.Trim('|');

        //Packet example ASKNAME|3|MUHAND%1|HUSSAM%2|TEMP%3

        //Send the message
        Send(msg, reliableChannel, cnnId);
    }
    private void OnDisconnection(int cnnId)
    {
        //Remove this player from our client list
        clients.Remove(clients.Find(x => x.connectionId == cnnId));

        //Tell everyone that somebody else has disconnected
        Send("DC|" + cnnId, reliableChannel, clients);
    }
    #endregion

    #region Utility Methods
    private void Send(string message, int channelId, int cnnId)
    {
        List<ServerClient> c = new List<ServerClient>();
        c.Add(clients.Find(x => x.connectionId == cnnId));
        Send(message, channelId, c);
    }

    private void Send(string message, int channelId, List<ServerClient> c)
    {
        Debug.Log("Sending: " + message);

        //Turn string into bytes and prepare it to be sent
        byte[] msg = Encoding.Unicode.GetBytes(message);

        foreach (var client in c)
            NetworkTransport.Send(hostId, client.connectionId, channelId, msg, message.Length * sizeof(char), out error);
    }
    #endregion
}
