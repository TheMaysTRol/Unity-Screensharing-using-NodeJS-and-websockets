const WebSocket = require('ws');
const { v4: uuidv4 } = require('uuid');
const wss = new WebSocket.Server({ port: 3000 });
const broadcasts = new Map();  // Stores multiple broadcasts by broadcastId

class Broadcast {
  constructor(id, owner) {
    this.id = id;
    this.owner = owner;
    this.connectedUsers = [];
  }
}

function SendLocal(ws, data) {
    ws.send(JSON.stringify(data));
}

function cleanupBroadcast(broadcastId) {
    if (broadcasts.has(broadcastId)) {
        const broadcast = broadcasts.get(broadcastId);
        for (const user of broadcast.connectedUsers) {
            SendLocal(user, { id: 'BroadcastDisconnect', message: `The host has disconnected. You will be disconnected.` });
            user.connectedBroadcast = null; // Clear the connected broadcast reference
            user.isHost = null; // Clear the connected broadcast reference
        }
        broadcasts.delete(broadcastId); // Remove the broadcast from memory
        console.log(`Broadcast ${broadcastId} cleaned up.`);
    }
}

function handleStartBroadcast(ws, broadcastId) {
  if(broadcastId.length <=0){
	  SendLocal(ws,{id:'BroadcastFatalError',message:"Broadcastname can't be empty"});
	  console.log("Broadcast ID must not be empty");
	  return;
  }
  isHost  = false;
  if (broadcasts.has(broadcastId)) {
    console.log(`Joining broadcast: ${broadcastId}`);
  }else{
    console.log(`Hosting broadcast: ${broadcastId}`);
	broadcasts.set(broadcastId, new Broadcast(broadcastId, ws.clientId));
	isHost = true;
  }
    ws.connectedBroadcast = broadcastId;
	ws.isHost = isHost;
  	broadcasts.get(broadcastId).connectedUsers.push(ws);
	SendLocal(ws,{id:'JoinBroadcastResult',broadcastId:broadcastId,isHost:isHost,message:"Broadcast is created succesfully"});
}

function Stream(ws,data){
	if (ws.connectedBroadcast && broadcasts.has(ws.connectedBroadcast) && ws.isHost) {
		// Loop through all clients to broadcast the message
        broadcasts.get(ws.connectedBroadcast).connectedUsers.forEach((websock) => {
            if (websock.clientId !== ws.clientId) { // Optional: Avoid sending the message back to the sender
				SendLocal(websock,{id:'StreamResult',data:data.data});
            }
        });
	}
}

wss.on('connection', (ws) => {
    const clientId = uuidv4();
    ws.clientId = clientId;
    console.log('New client connected:', clientId);
    SendLocal(ws, { id: 'ReceivePlayerId', data: { id: clientId } });

    ws.on('message', (message) => {
        try {
            const data = JSON.parse(message);
            switch (data.id) {
                case 'JoinBroadcast':
                    handleStartBroadcast(ws, data.data.roomName)
                    break;
				case 'Stream':
                    Stream(ws,data)
                    break;
                default:
                    console.log('Unknown message type:', data.id);
            }
        } catch (error) {
            console.error('Error processing message:', error);
        }
    });

    ws.on('close', () => {
        console.log('Client disconnected:', clientId);
		const connectedBroadcast = ws.connectedBroadcast;
        if (connectedBroadcast) {
            const broadcast = broadcasts.get(connectedBroadcast);
            if (broadcast) {
                // Remove the user from the broadcast
                broadcast.connectedUsers = broadcast.connectedUsers.filter(user => user !== ws);
                console.log(`Removed client ${clientId} from broadcast ${connectedBroadcast}`);
                
                // If the host disconnects
                if (ws.isHost) {
                    cleanupBroadcast(connectedBroadcast);
                }
            }
        }
    });
});

console.log('WebSocket server is running on ws://localhost:3000');
