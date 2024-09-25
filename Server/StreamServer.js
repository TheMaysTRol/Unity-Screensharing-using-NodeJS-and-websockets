const WebSocket = require('ws');
const { v4: uuidv4 } = require('uuid');

// Constants
const PORT = 3000;

// WebSocket server setup
const wss = new WebSocket.Server({ port: PORT });

// Store for multiple broadcasts
const broadcasts = new Map();

/**
 * Represents a broadcast session
 */
class Broadcast {
  /**
   * @param {string} id - Unique identifier for the broadcast
   * @param {string} owner - Client ID of the broadcast owner
   */
  constructor(id, owner) {
    this.id = id;
    this.owner = owner;
    this.connectedUsers = [];
  }
}

/**
 * Sends a JSON message to a WebSocket client
 * @param {WebSocket} ws - WebSocket connection
 * @param {Object} data - Data to be sent
 */
function sendLocal(ws, data) {
  ws.send(JSON.stringify(data));
}

/**
 * Cleans up a broadcast when the host disconnects
 * @param {string} broadcastId - ID of the broadcast to clean up
 */
function cleanupBroadcast(broadcastId) {
  if (broadcasts.has(broadcastId)) {
    const broadcast = broadcasts.get(broadcastId);
    for (const user of broadcast.connectedUsers) {
      sendLocal(user, { 
        id: 'BroadcastDisconnect', 
        message: 'The host has disconnected. You will be disconnected.' 
      });
      user.connectedBroadcast = null;
      user.isHost = null;
    }
    broadcasts.delete(broadcastId);
    console.log(`Broadcast ${broadcastId} cleaned up.`);
  }
}

/**
 * Handles the start or join of a broadcast
 * @param {WebSocket} ws - WebSocket connection
 * @param {string} broadcastId - ID of the broadcast to start or join
 */
function handleStartBroadcast(ws, broadcastId) {
  if (broadcastId.length <= 0) {
    sendLocal(ws, {
      id: 'BroadcastFatalError',
      message: "Broadcast name can't be empty"
    });
    console.log("Broadcast ID must not be empty");
    return;
  }

  let isHost = false;
  if (broadcasts.has(broadcastId)) {
    console.log(`Joining broadcast: ${broadcastId}`);
  } else {
    console.log(`Hosting broadcast: ${broadcastId}`);
    broadcasts.set(broadcastId, new Broadcast(broadcastId, ws.clientId));
    isHost = true;
  }

  ws.connectedBroadcast = broadcastId;
  ws.isHost = isHost;
  broadcasts.get(broadcastId).connectedUsers.push(ws);

  sendLocal(ws, {
    id: 'JoinBroadcastResult',
    broadcastId: broadcastId,
    isHost: isHost,
    message: "Broadcast is created successfully"
  });
}

/**
 * Streams data to all connected clients in a broadcast except the sender
 * @param {WebSocket} ws - WebSocket connection of the sender
 * @param {Object} data - Data to be streamed
 */
function stream(ws, data) {
  if (ws.connectedBroadcast && broadcasts.has(ws.connectedBroadcast) && ws.isHost) {
    broadcasts.get(ws.connectedBroadcast).connectedUsers.forEach((client) => {
      if (client.clientId !== ws.clientId) {
        sendLocal(client, { id: 'StreamResult', data: data.data });
      }
    });
  }
}

// WebSocket server event handlers
wss.on('connection', (ws) => {
  const clientId = uuidv4();
  ws.clientId = clientId;
  console.log('New client connected:', clientId);
  sendLocal(ws, { id: 'ReceivePlayerId', data: { id: clientId } });

  ws.on('message', (message) => {
    try {
      const data = JSON.parse(message);
      switch (data.id) {
        case 'JoinBroadcast':
          handleStartBroadcast(ws, data.data.roomName);
          break;
        case 'Stream':
          stream(ws, data);
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
        broadcast.connectedUsers = broadcast.connectedUsers.filter(user => user !== ws);
        console.log(`Removed client ${clientId} from broadcast ${connectedBroadcast}`);
        
        if (ws.isHost) {
          cleanupBroadcast(connectedBroadcast);
        }
      }
    }
  });
});

console.log(`WebSocket server is running on ws://localhost:${PORT}`);