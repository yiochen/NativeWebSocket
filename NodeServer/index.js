const crypto = require("crypto");
const express = require("express");
const { createServer } = require("http");
const WebSocket = require("ws");

const app = express();

const server = createServer(app);
const wss = new WebSocket.Server({ server });

wss.on("connection", function (ws) {
  console.log("client joined.");

  ws.on("message", function (data) {
    if (typeof data === "string") {
      // client sent a string
      console.log("string received from client -> '" + data + "'");
    } else {
      console.log(
        "binary received from client -> " + Array.from(data).join(", ") + ""
      );
    }
    ws.send(data);
  });

  ws.on("close", function () {
    console.log("client left.");
  });
});

server.listen(8080, function () {
  console.log("Listening on http://localhost:8080");
});
