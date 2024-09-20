const WebSocket = require('ws');
const { v4: uuidv4 } = require('uuid');
const wss = new WebSocket.Server({ port: 3000 });
const clients = new Map();
let broadcaster = null;

function handleStartBroadcast(ws) {
    broadcaster = ws;
    console.log('Broadcast started by:', ws.clientId);
}

function handleJoinBroadcast(ws) {
    if (broadcaster) {
        console.log('Client joined broadcast:', ws.clientId);
        SendTo(broadcaster.clientId, { id: 'RequestOffer', data: { peerId: ws.clientId } });
    } else {
        console.log('No active broadcast to join');
        SendLocal(ws, { id: 'Error', data: { message: 'No active broadcast to join' } });
    }
}

function handlePeerMessage(ws, data) {
    if (!data.data.targetId) {
        console.error('Target ID missing in peer message');
        return;
    }
    
    if (data.id === "OFFER" || data.id === "ANSWER" || data.id === "CANDIDATE") {
        SendTo(data.data.targetId, {
            id: data.id,
            data: data.data,
            fromId: ws.clientId
        });
    }
}

function SendLocal(ws, data) {
    ws.send(JSON.stringify(data));
}

function SendTo(targetClientId, data) {
    const targetClient = clients.get(targetClientId);
    if (targetClient) {
        targetClient.send(JSON.stringify(data));
    } else {
        console.log('Target client not found:', targetClientId);
    }
}

wss.on('connection', (ws) => {
    const clientId = uuidv4();
    ws.clientId = clientId;
    clients.set(clientId, ws);
    console.log('New client connected:', clientId);
    SendLocal(ws, { id: 'GetId', data: { id: clientId } });

    ws.on('message', (message) => {
        try {
            const data = JSON.parse(message);
            console.log('Received data:', data);
            switch (data.id) {
                case 'Start_Broadcast':
                    handleStartBroadcast(ws);
                    break;
                case 'Join_Broadcast':
                    handleJoinBroadcast(ws);
                    break;
                case 'OFFER':
                case 'ANSWER':
                case 'CANDIDATE':
                    handlePeerMessage(ws, data);
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
        clients.delete(clientId);
        if (broadcaster === ws) {
            broadcaster = null;
        }
    });
});

console.log('WebSocket server is running on ws://localhost:3000');
