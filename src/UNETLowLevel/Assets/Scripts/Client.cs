using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class Player
{
    public string playerName;
    public GameObject avatar;
    public int connectionId;
}

public class Client : MonoBehaviour {

    #region Constants
    private const int MAX_CONNECTIONS = 100;
    #endregion

    #region Global Variables
    private int port = 5701;
    private int hostId;
    private int webHostId;
    private int reliableChannel;
    private int unReliableChannel;
    private int ourClientId;
    private int connectionId;
    private float connectionTime;
    private bool isConnected = false;
    private bool isStarted = false;
    private byte error;
    private string playerName;
    #endregion

    #region Inspector Variables
    public GameObject playerPrefab;
    #endregion

    Vector3 realPosition = Vector3.zero;
    Player p = null;

    #region Lists
    //public List<Player> players = new List<Player>();
    public Dictionary<int, Player> players = new Dictionary<int, Player>();
    #endregion

    public void Connect()
    {
        //Does the player have a name?
        string pName = GameObject.Find("NameInput").GetComponent<InputField>().text;
        if(pName == "")
        {
            //If not then just show an error message and exit from the function
            Debug.Log("You must enter a name!");
            return;
        }

        //Otherwise set the name
        playerName = pName;

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
        hostId = NetworkTransport.AddHost(topo,0);

        //Connect to the host
        connectionId = NetworkTransport.Connect(hostId, "127.0.0.1", port, 0,out error);

        //Set the client ot be connected
        connectionTime = Time.time;
        isConnected = true;
    }

    private void Update()
    {
        #region Receive messages
        //If is not started then dont do anything just exit the function
        if (!isConnected)
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

        switch (recData)
        {
            case NetworkEventType.DataEvent:
                //Recieved a message of type DataEvent, this where everything happens, we have to parse the messages and take action based on it

                //Lets decode the message
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                Debug.Log("Receiving: " + msg);

                //Split the message by the special character, this way we know the types of messages we are receiving
                string[] splitData = msg.Split('|');

                //Check what type of message are we recieving
                switch (splitData[0])
                {
                    case "ASKNAME":
                        OnAskName(splitData);
                        break;
                    case "CNN":
                        SpawnPlayer(splitData[1], int.Parse(splitData[2]));
                        break;
                    case "DC":
                        PlayerDisconnected(int.Parse(splitData[1]));
                        break;
                    case "ASKPOSITION":
                        OnAskPosition(splitData);
                        break;
                    default:
                        Debug.Log("Invalid message: " + msg);
                        break;
                }

                break;
        }
        #endregion
    }

    private void FixedUpdate()
    {
        #region Move the players
        if (p != null && p.connectionId != ourClientId)
        {
            //If the current player is not us then move them
            p.avatar.transform.position = Vector3.Lerp(p.avatar.transform.position, realPosition, 0.1f);
        }
        #endregion
    }


    #region Actions
    private void OnAskPosition(string[] data)
    {
        if (!isStarted)
            return;

        //Update the position of everyone else
        for (int i = 1; i < data.Length; i++)
        {
            string[] d = data[i].Split('%');

            //Prevent the server from updating us
            if (ourClientId != int.Parse(d[0]))
            {
                Vector3 position = Vector3.zero;
                position.x = float.Parse(d[1]);
                position.y = float.Parse(d[2]);
                position.z = float.Parse(d[3]);
                try
                {
                    realPosition = position;
                    p = players[int.Parse(d[0])];
                    //players[int.Parse(d[0])].avatar.transform.position = Vector3.Lerp(t.transform.position, position, 2000*Time.deltaTime);
                    //players[int.Parse(d[0])].avatar.transform.position = position;

                }
                catch (KeyNotFoundException)
                {

                }
                
            }
        }

        //Now send our own position
        Vector3 myPosition = players[ourClientId].avatar.transform.position;
        string positionPacket = "MYPOSITION|" + myPosition.x.ToString() + '|' + myPosition.y.ToString() + '|' + myPosition.z.ToString();
        Send(positionPacket, unReliableChannel);
    }

    /// <summary>
    /// Whenever an ASKNAME header is received then call this method
    /// </summary>
    /// <param name="data">The received data</param>
    private void OnAskName(string[] data)
    {
        //Packet example ASKNAME|3|MUHAND%1|HUSSAM%2|TEMP%3

        //Get the id for the client
        ourClientId = int.Parse(data[1]);

        //Send our name to the server
        Send("NAMEIS|"+playerName, reliableChannel);

        //Create all of the other players
        //Starting at 2 because index 0 of the packet is the header, index 1 is the ID, and starting at index 2 will be the players
        for (int i = 2; i < data.Length-1; i++)
        {
            //Split each data by the special character, which will create 2 fields, the playername and the id
            string[] d = data[i].Split('%');

            //Spawn the player
            SpawnPlayer(d[0],int.Parse(d[1]));
        }
    }
    private void SpawnPlayer(string playerName, int cnnId)
    {
        Debug.Log("Spawned player: " + playerName);

        //Spawn a new prefab everytime someone connnects
        GameObject go = Instantiate(playerPrefab) as GameObject;

        //Is this ours?
        if(cnnId == ourClientId)
        {
            //Add mobility
            go.AddComponent<PlayerMotor>();

            //Remove canvas
            GameObject.Find("Canvas").SetActive(false);

            //Toggle isStarted to true
            isStarted = true;
        }

        //If it's not our then create the player
        Player p = new Player();
        p.avatar = go;
        p.playerName = playerName;
        p.connectionId = cnnId;
        p.avatar.GetComponentInChildren<TextMesh>().text = playerName;
        //Add to the list of players
        players.Add(cnnId,p);
    }
    private void PlayerDisconnected(int cnnId)
    {
        Destroy(players[cnnId].avatar);
        players.Remove(cnnId);
    }
    #endregion

    #region Utility Methods

    private void Send(string message, int channelId)
    {
        Debug.Log("Sending: " + message);

        //Turn string into bytes and prepare it to be sent
        byte[] msg = Encoding.Unicode.GetBytes(message);

        NetworkTransport.Send(hostId, connectionId, channelId, msg, message.Length * sizeof(char), out error);
    }
    #endregion
}
